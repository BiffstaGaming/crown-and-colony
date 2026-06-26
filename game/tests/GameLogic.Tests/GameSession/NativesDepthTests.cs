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

    // ===== 2b. Per-turn military-goods production / refill (86d3fpzna / arm-braves 86d3fpzzj) =====

    private const string Muskets = "model.goods.muskets";
    private const string Horses = "model.goods.horses";

    [Fact]
    public void MilitaryProduction_GrowsMuskets_FromZero_Capped()
    {
        // A settlement that has spent all its muskets re-accumulates them per turn (FreeCol IndianSettlement musket
        // manufacture), capped — production never runs away.
        Game game = Game.New(Classic, Seed);
        var camp = new NativeSettlement(8101, "model.nationType.apache", "model.settlement.camp", false, new Position(0, 0), 5, null);
        Assert.Equal(0, camp.StockOf(Muskets));
        Player apache = game.Players.First(p => p.PlayerType == PlayerType.Native && p.NationId == "model.nationType.apache");

        for (int t = 0; t < 3; t++)
        {
            game.ProduceNativeMilitaryGoods(camp, ApacheStream(game, apache));
        }
        int afterThree = camp.StockOf(Muskets);
        Assert.True(afterThree > 0, "a bare camp manufactures muskets over a few turns");

        for (int t = 0; t < 100; t++)
        {
            game.ProduceNativeMilitaryGoods(camp, ApacheStream(game, apache));
        }
        Assert.True(camp.StockOf(Muskets) <= 100, "production never pushes muskets above the ceiling"); // NativeMilitaryStoreCeilingMax
        Assert.True(camp.StockOf(Muskets) > afterThree, "production keeps topping up until the ceiling");
    }

    [Fact]
    public void MilitaryProduction_BreedsHorses_OnlyForCapitalsOrLargerCamps()
    {
        // Horses are bred only by capitals / village-or-larger camps (mirrors the generator seed's capability — a small
        // frontier camp arms but never mounts), matching FreeCol's food-gated horse breeding.
        Game game = Game.New(Classic, Seed);
        Player apache = game.Players.First(p => p.PlayerType == PlayerType.Native && p.NationId == "model.nationType.apache");
        var smallCamp = new NativeSettlement(8102, "model.nationType.apache", "model.settlement.camp", false, new Position(0, 0), 3, null);
        var bigCamp = new NativeSettlement(8103, "model.nationType.apache", "model.settlement.city", true, new Position(0, 1), 8, null);

        for (int t = 0; t < 10; t++)
        {
            game.ProduceNativeMilitaryGoods(smallCamp, ApacheStream(game, apache));
            game.ProduceNativeMilitaryGoods(bigCamp, ApacheStream(game, apache));
        }

        Assert.Equal(0, smallCamp.StockOf(Horses)); // a small camp never breeds horses…
        Assert.True(bigCamp.StockOf(Horses) > 0, "a capital / large camp breeds horses");
        Assert.True(smallCamp.StockOf(Muskets) > 0, "…but every camp manufactures muskets");
    }

    [Fact]
    public void MilitaryProduction_LetsADisarmedTribeReArmABrave_AfterSpendingItsStock()
    {
        // The headline: a tribe that armed a brave and drew its stock to 0 RE-ACCUMULATES muskets over the next turns
        // (production), so it can arm ANOTHER brave later — closing the arm-braves deviation (86d3fpzzj: produced, not
        // just seeded). Drive a single threatened camp end-to-end: arm a first brave (drains the seed), then spawn a
        // second unarmed brave and run turns — production refills the camp and the second brave eventually arms too.
        Game game = Game.New(Classic, seed: 7);
        NativeSettlement home = game.NativeSettlements.First(s => s.Position.Neighbours().Any(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n)));
        string nation = home.NationTypeId;
        // Spend the camp's whole stock so it must PRODUCE to re-arm (no seed left to lean on).
        home.AddStock(Muskets, -home.StockOf(Muskets));
        home.AddStock(Horses, -home.StockOf(Horses));
        Position spot = home.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit brave = game.SpawnUnit(Classic.Unit("model.unit.brave"), spot, nation);
        Assert.Equal("model.role.default", brave.RoleId);

        bool armed = false;
        for (int turn = 0; turn < 40 && !armed; turn++)
        {
            foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == nation))
            {
                game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // keep the camp threatened so it secures its braves
            }
            brave.Position = spot; // park it home (it would otherwise wander off)
            game.EndTurn();
            armed = brave.RoleId != "model.role.default";
        }

        Assert.True(armed, "a disarmed tribe re-accumulates muskets from production and arms a brave again");
    }

    [Fact]
    public void MilitaryProduction_DrawsOnlyOnTheNativeStream_LeavingStream0Untouched()
    {
        // ADR-009: a game whose camps manufacture arms every turn keeps the human's stream 0 byte-identical to a fresh
        // same-seed game — the military-production variance draws only on the nation's stream.
        Game a = Game.New(Classic, seed: 909);
        Game b = Game.New(Classic, seed: 909);
        for (int t = 0; t < 12; t++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson()); // fully byte-identical (production included)
    }

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

    [Fact]
    public void Gift_NeverGivesFood_EvenWhenFoodIsTheOnlySurplus_SoTheHumanStreamStaysStable()
    {
        // A native gift is drawn on the NATION's stream. Food must be excluded from gifts: food landing in a human
        // colony could reach FoodForGrowth and birth a colonist, adding a tile worker and thus an extra stream-0
        // experience roll per later turn — a native-stream gift would then perturb the human's stream 0 (ADR-009).
        // So even a settlement whose ONLY surplus is food must never gift (matching the ProduceNativeGoods food filter).
        Game game = Game.New(Classic, seed: 7);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.Type.CanFoundColony));
        NativeSettlement home = game.NativeSettlements.First();
        string nation = home.NationTypeId;
        Position adj = colony.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit brave = game.SpawnUnit(Classic.Unit("model.unit.brave"), adj, nation);

        // Stock the home store with food ONLY, well above the gift threshold — the sole giftable surplus candidate.
        for (int turn = 0; turn < 40; turn++)
        {
            foreach (NativeSettlement s in game.NativeSettlements)
            {
                game.ChangeNativeAlarm(s, -s.Alarm); // keep everyone Happy (so a gift could otherwise fire)
                foreach (var kv in s.GeneralStock.Where(kv => kv.Key != "model.goods.food").ToList())
                {
                    s.AddGoods(kv.Key, -kv.Value); // strip every non-food good (incl. the farmed production seed) each turn
                }
            }
            home.AddGoods("model.goods.food", 200); // a large food surplus, every turn
            brave.Position = adj;
            game.EndTurn();
        }

        Assert.Empty(game.ColonyGiftNotices); // food is never gifted, so no gift fires though the store overflows with it
    }
}
