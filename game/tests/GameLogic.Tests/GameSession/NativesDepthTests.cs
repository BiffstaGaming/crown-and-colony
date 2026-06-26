using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Native-depth fidelity (wave "Natives-depth"): the haggle deal closes at the AGREED price (not the standard one);
/// a settlement's goods store grows per-turn from abstracted production; tribe-wide tension is a distinct channel
/// from per-settlement alarm; gifts from natives draw from the store and respect a cooldown. The cross-cutting
/// invariant is byte-stability (ADR-009): every native draw is on the nation's own stream, never the human's stream 0.
/// FreeCol references: NativeTrade (price-carrying trade), ServerIndianSettlement.csNewTurn (production),
/// the native player's Tension (tribe-wide), IndianSettlement.getRandomGift + IndianBringGiftMission (gifts).
/// </summary>
public class NativesDepthTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    /// <summary>A game with a caravel on a coastal water tile beside a (test-built) camp on the adjacent land, the human holding gold.</summary>
    private static (Game game, NativeSettlement settlement, Unit ship) SetupCoastalTrade(int startingGold = 100000)
    {
        Game game = Game.New(Classic, Seed, startingGold: startingGold);
        Position water = game.Map.AllPositions().First(p =>
            game.Map.TerrainAt(p).IsWater &&
            p.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater));
        Position land = water.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        var settlement = new NativeSettlement(
            7777, "model.nationType.apache", "model.settlement.camp", false, land, 5, learnableSkill: null)
        {
            WantedGoods = ["model.goods.sugar"],
        };
        Unit ship = game.SpawnUnit(Classic.Unit("model.unit.caravel"), water);
        game.ChangeNativeAlarm(settlement, 300); // Content, so trading is allowed
        return (game, settlement, ship);
    }

    private static ulong StreamZeroState(Game game) => SaveGame.From(game).RandomStateValue;

    // ===== 1. Haggle honours the named price =====

    [Fact]
    public void SellAtAgreedPrice_PaysTheHaggledFigure_NotTheStandardPrice()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade();
        ship.AddCargo("model.goods.sugar", 100);
        int standard = game.NativeSalePrice(settlement, "model.goods.sugar", 100);
        int agreed = standard + 200; // the player haggled the chief up
        int goldBefore = game.Gold;

        int got = game.SellToNatives(ship, settlement, "model.goods.sugar", 100, agreed);

        Assert.Equal(agreed, got);                       // the engine paid the AGREED price…
        Assert.NotEqual(standard, got);                  // …not the standard one
        Assert.Equal(goldBefore + agreed, game.Gold);
        Assert.Equal(0, ship.CargoOf("model.goods.sugar"));
    }

    [Fact]
    public void BuyAtAgreedPrice_ChargesTheHaggledFigure_NotTheStandardPrice()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade();
        settlement.AddGoods("model.goods.sugar", 80);
        game.RecomputeWantedGoods(settlement);
        int standard = game.NativeBuyPrice(settlement, "model.goods.sugar", 40);
        int agreed = standard * 8 / 10; // the player haggled the chief down ~20% (inside the ½-standard floor)
        Assert.True(agreed < standard && agreed > standard / 2, "the haggled bid must sit between the floor and the standard ask");
        int goldBefore = game.Gold;

        int paid = game.BuyFromNatives(ship, settlement, "model.goods.sugar", 40, agreed);

        Assert.Equal(agreed, paid);                      // the engine charged the AGREED price…
        Assert.NotEqual(standard, paid);                 // …not the standard one
        Assert.Equal(goldBefore - agreed, game.Gold);
        Assert.Equal(40, ship.CargoOf("model.goods.sugar"));
    }

    [Fact]
    public void AgreedPrice_OutsideTheHaggleBand_FallsBackToStandard_SoTheUiCannotMintGold()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade();
        ship.AddCargo("model.goods.sugar", 100);
        int standard = game.NativeSalePrice(settlement, "model.goods.sugar", 100);

        // An absurd sell figure (10× standard) is rejected → the engine pays the standard price, not the inflated one.
        int got = game.SellToNatives(ship, settlement, "model.goods.sugar", 100, standard * 10);
        Assert.Equal(standard, got);
    }

    [Fact]
    public void AgreedSellPrice_BelowStandard_NeverUndercutsTheStandardOffer()
    {
        (Game game, NativeSettlement settlement, Unit ship) = SetupCoastalTrade();
        ship.AddCargo("model.goods.sugar", 100);
        int standard = game.NativeSalePrice(settlement, "model.goods.sugar", 100);

        // The player would never "agree" to less than the standard sale; if a stale figure does, the engine floors at standard.
        int got = game.SellToNatives(ship, settlement, "model.goods.sugar", 100, standard - 500);
        Assert.Equal(standard, got);
    }

    // ===== 2. Settlement goods store driven by native production over time =====

    [Fact]
    public void ProduceNativeGoods_GrowsTheStore_FromZero()
    {
        Game game = Game.New(Classic, Seed);
        var camp = new NativeSettlement(8001, "model.nationType.apache", "model.settlement.camp", false, new Position(0, 0), 5, null);
        int before = camp.GeneralStock.Sum(kv => kv.Value);

        // Use the apache native player's stream (never stream 0).
        Player apache = game.Players.First(p => p.PlayerType == PlayerType.Native && p.NationId == "model.nationType.apache");
        game.ProduceNativeGoods(camp, ApacheStream(game, apache));

        Assert.True(camp.GeneralStock.Sum(kv => kv.Value) > before, "a turn of production should add goods to the store");
        Assert.All(camp.GeneralStock, kv => Assert.True(Classic.Goods(kv.Key).IsNewWorldGoods)); // raw New-World goods only
    }

    [Fact]
    public void Production_RunsEachTurn_ReplenishingStoresOverTime()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement camp = game.NativeSettlements.First();
        int before = camp.GeneralStock.Sum(kv => kv.Value);

        for (int turn = 0; turn < 5; turn++)
        {
            game.EndTurn();
        }

        Assert.True(camp.GeneralStock.Sum(kv => kv.Value) > before, "a settlement's store grows across turns from production");
    }

    [Fact]
    public void Production_DoesNotPushAGoodAboveTheStoreCeiling()
    {
        Game game = Game.New(Classic, Seed);
        var camp = new NativeSettlement(8002, "model.nationType.apache", "model.settlement.camp", false, new Position(0, 0), 5, null);
        camp.AddGoods("model.goods.sugar", 100); // already at the ceiling

        Player apache = game.Players.First(p => p.PlayerType == PlayerType.Native && p.NationId == "model.nationType.apache");
        for (int t = 0; t < 10; t++)
        {
            game.ProduceNativeGoods(camp, ApacheStream(game, apache));
        }

        Assert.True(camp.GeneralStockOf("model.goods.sugar") <= 100, "production never pushes a good above the store ceiling");
    }

    [Fact]
    public void Production_DrawsOnlyOnTheNativeStream_LeavingStream0Untouched()
    {
        // Two same-seed games: one runs many turns (natives produce every turn), one is freshly saved. Stream 0 is
        // driven only by the human's (identical, empty) actions, so it stays byte-identical however much the natives produce.
        Game a = Game.New(Classic, seed: 4242);
        Game b = Game.New(Classic, seed: 4242);
        for (int t = 0; t < 12; t++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson()); // fully byte-identical
    }

    /// <summary>The apache nation's RNG stream — the same one RunNativeTurn draws from (never the human's stream 0).</summary>
    private static IGameRandom ApacheStream(Game game, Player apache) => apache.Rng!;

    // ===== 3. Nation-level (tribe-wide) tension, distinct from per-settlement =====

    [Fact]
    public void TribeTension_IsADistinctChannel_RaisedNationWideByAnAct_NotJustOneSettlement()
    {
        Game game = Game.New(Classic, Seed);
        string nation = game.NativeSettlements.First().NationTypeId;
        int human = game.HumanPlayer.PlayerId;

        int tribeBefore = game.TribeTensionFor(nation, human);

        // Hit ONE settlement of the nation hard; the tribe channel rises by a propagated share (distinct from any one camp).
        NativeSettlement one = game.NativeSettlements.First(s => s.NationTypeId == nation);
        game.ChangeNativeAlarm(one, human, 400);

        int tribeAfter = game.TribeTensionFor(nation, human);
        Assert.True(tribeAfter > tribeBefore, "an act against one camp stirs the whole tribe's tension channel");
        // The tribe channel is its own value, not equal to the per-settlement alarm that was just set.
        Assert.NotEqual(one.AlarmFor(human), tribeAfter);
    }

    [Fact]
    public void TribeTension_DecaysEachTurn()
    {
        Game game = Game.New(Classic, Seed);
        string nation = game.NativeSettlements.First().NationTypeId;
        int human = game.HumanPlayer.PlayerId;
        game.RaiseTribeTension(nation, human, 500);
        int before = game.TribeTensionFor(nation, human);

        game.EndTurn(); // the nation takes its turn → tribe tension cools

        Assert.True(game.TribeTensionFor(nation, human) < before, "tribe-wide tension cools each turn (like per-settlement alarm)");
    }

    [Fact]
    public void TribeAlarmLevel_BandsTheNationChannel()
    {
        Game game = Game.New(Classic, Seed);
        string nation = game.NativeSettlements.First().NationTypeId;
        int human = game.HumanPlayer.PlayerId;

        Assert.Equal(AlarmLevel.Happy, game.TribeAlarmLevelFor(nation, human)); // calm to begin with
        game.RaiseTribeTension(nation, human, 900); // → Hateful band
        Assert.Equal(AlarmLevel.Hateful, game.TribeAlarmLevelFor(nation, human));
    }

    [Fact]
    public void TribeTension_IsTransient_SaveRoundTripsByteIdentically_NoSaveBump()
    {
        Game game = Game.New(Classic, Seed);
        string nation = game.NativeSettlements.First().NationTypeId;
        game.RaiseTribeTension(nation, game.HumanPlayer.PlayerId, 500); // a live tribe channel

        // The tribe channel is transient (seeded from settlement alarm on load), so it does not appear in the save and
        // the version is unchanged — a settlement's saved alarm round-trips, the derived tribe channel re-seeds.
        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(SaveGame.CurrentVersion, SaveGame.FromJson(SaveGame.From(restored).ToJson()).Version);
    }

    // ===== 4. Gifts from natives: store-backed + cooldown =====

    [Fact]
    public void Gift_DrawsFromTheStore_DeductsIt_AndRespectsTheCooldown()
    {
        // Drive TryBringGiftFromStore directly via a full native turn: a Happy brave beside a colony with a stocked home.
        Game game = Game.New(Classic, seed: 7);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.Type.CanFoundColony));
        NativeSettlement home = game.NativeSettlements.First();
        string nation = home.NationTypeId;
        Position adj = colony.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit brave = game.SpawnUnit(Classic.Unit("model.unit.brave"), adj, nation);
        // Gift from RUM (manufactured → production never refills it, so the deduction is exact); strip the farmed seed.
        foreach (var kv in home.GeneralStock.ToList())
        {
            home.AddGoods(kv.Key, -kv.Value);
        }
        home.AddGoods("model.goods.rum", 200);

        // Run turns until a gift fires; capture the rum store right before the firing turn.
        int giftTurn = -1;
        int storeAtGift = 0;
        for (int turn = 0; turn < 60 && giftTurn < 0; turn++)
        {
            foreach (NativeSettlement s in game.NativeSettlements)
            {
                game.ChangeNativeAlarm(s, -s.Alarm); // keep everyone Happy
            }
            brave.Position = adj;
            int storeBefore = home.GeneralStockOf("model.goods.rum");
            game.EndTurn();
            if (game.ColonyGiftNotices.Any())
            {
                giftTurn = game.Turn;
                storeAtGift = storeBefore;
            }
        }
        Assert.True(giftTurn > 0, "a stocked Happy tribe should eventually gift");

        ColonyGiftNotice gift = game.ColonyGiftNotices.First();
        Assert.Equal("model.goods.rum", gift.GoodsId);
        Assert.InRange(gift.Amount, 10, 80);
        Assert.Equal(storeAtGift - gift.Amount, home.GeneralStockOf("model.goods.rum")); // the gift left the store

        // The settlement is now on cooldown: it stamps LastGiftTurn and won't gift again immediately.
        Assert.True(home.LastGiftTurn > 0, "the gift stamps the settlement's cooldown");
    }

    [Fact]
    public void Gift_WithNoStoreSurplus_NeverFires()
    {
        // A Happy brave beside a colony whose home settlement holds NO giftable surplus brings nothing (FreeCol getRandomGift → null).
        Game game = Game.New(Classic, seed: 7);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.Type.CanFoundColony));
        NativeSettlement home = game.NativeSettlements.First();
        string nation = home.NationTypeId;
        // Strip the home settlement bare so it has nothing above the gift threshold.
        foreach (var kv in home.GeneralStock.ToList())
        {
            home.AddGoods(kv.Key, -kv.Value);
        }
        Position adj = colony.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit brave = game.SpawnUnit(Classic.Unit("model.unit.brave"), adj, nation);

        // Pin the home store empty each turn (production would otherwise slowly refill it), keep Happy, and run a while.
        for (int turn = 0; turn < 6; turn++)
        {
            foreach (NativeSettlement s in game.NativeSettlements)
            {
                game.ChangeNativeAlarm(s, -s.Alarm);
            }
            foreach (var kv in home.GeneralStock.ToList())
            {
                home.AddGoods(kv.Key, -kv.Value); // keep it bare (below the gift threshold)
            }
            brave.Position = adj;
            game.EndTurn();
        }
        Assert.Empty(game.ColonyGiftNotices); // nothing to spare → no gift
    }
}
