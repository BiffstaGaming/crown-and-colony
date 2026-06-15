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
/// Native colony pillage (native-AI follow-up): a hostile brave beside an undefended, pillageable human colony
/// storms it and carries off a slice of one goods stack (FreeCol <c>csPillageColony</c> / <c>PILLAGE_COLONY</c>)
/// — the colony is NOT captured or destroyed (natives can't take colonies). On a repelled raid the brave is
/// slain (dispose-on-combat-loss). All combat/loot draws come from the nation's OWN RNG stream (ADR-009), so the
/// human's stream 0 stays byte-stable. We model the goods-loot outcome only (building/ship/gold/destroy deferred).
/// </summary>
public class NativePillageTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Brave = "model.unit.brave";       // offence 1, model.ability.pillageUnprotectedColony + disposeOnCombatLoss
    private const string Artillery = "model.unit.artillery";
    private const string Tobacco = "model.goods.tobacco";
    private const string Hammers = "model.goods.hammers"; // storable="false" — banked for construction, never lootable (FreeCol getLootableGoodsList)

    /// <summary>A fixed RNG (same NextDouble each call; Next → minimum) — forces a chosen combat band and the first loot stack.</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>The human founds an (undefended) colony stocked with goods; a brave of a real native nation stands beside it.</summary>
    private static (Game game, Colony colony, Unit brave) Stage(ulong seed, int tobacco = 100)
    {
        Game game = Game.New(Classic, seed);
        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony);
        Colony colony = game.FoundColony(founder); // human-owned; undefended (the founder became its population)
        if (tobacco > 0)
        {
            colony.AddGoods(Tobacco, tobacco);
        }

        string nation = game.NativeSettlements.First().NationTypeId;
        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit brave = game.SpawnUnit(Classic.Unit(Brave), adj, nation);
        return (game, colony, brave);
    }

    [Fact]
    public void Pillage_StealsGoods_FromAnUndefendedHumanColony_AndRecordsARaidNotice()
    {
        (Game game, Colony colony, Unit brave) = Stage(seed: 7);

        game.PillageColony(brave, colony.Position, new FixedRandom(0.0)); // forced brave win

        Assert.Equal(50, colony.StoreOf(Tobacco));   // min(100/2, 50) carried off
        Assert.Contains(game.ColonyRaidNotices,
            n => n.AttackerNationId == brave.OwnerNationId && n.ColonyName == colony.Name
                 && n.GoodsId == Tobacco && n.Amount == 50 && n.Position == colony.Position);
        Assert.Equal(colony.OwnerId, game.Colonies.First(c => c.Id == colony.Id).OwnerId); // still the human's — not captured
    }

    [Fact]
    public void RepelledRaid_StealsNothing_AndSlaysTheBrave()
    {
        (Game game, Colony colony, Unit brave) = Stage(seed: 7);
        int braveId = brave.Id;

        game.PillageColony(brave, colony.Position, new FixedRandom(0.99)); // forced brave loss

        Assert.Equal(100, colony.StoreOf(Tobacco));                       // nothing taken
        Assert.Empty(game.ColonyRaidNotices);
        Assert.DoesNotContain(game.Units, u => u.Id == braveId);          // the brave is dispose-on-combat-loss → slain
    }

    [Fact]
    public void CheckPillageColony_RefusesADefendedColony()
    {
        (Game game, Colony colony, Unit brave) = Stage(seed: 7);
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position); // a human defender on the colony tile

        Assert.False(game.CheckPillageColony(brave, colony.Position).Allowed); // unprotected only
    }

    [Fact]
    public void CheckPillageColony_RefusesAColonyWithNothingToLoot()
    {
        (Game game, Colony colony, Unit brave) = Stage(seed: 7, tobacco: 0); // empty warehouse

        Assert.All(colony.Stores.Values, amount => Assert.Equal(0, amount)); // sanity: nothing lootable
        Assert.False(game.CheckPillageColony(brave, colony.Position).Allowed);
    }

    [Fact]
    public void NonStorableGoods_LikeBankedHammers_AreNeverLooted()
    {
        // FreeCol's getLootableGoodsList filters to storable goods; hammers (storable="false") accrue in stores
        // for construction but are never a pillage target. A colony with ONLY hammers can't be pillaged at all,
        // and when it also holds storable goods the loot only ever comes from those — hammers stay untouched.
        (Game game, Colony colony, Unit brave) = Stage(seed: 7, tobacco: 0);
        colony.AddGoods(Hammers, 80);
        Assert.False(game.CheckPillageColony(brave, colony.Position).Allowed); // hammers alone → nothing lootable

        colony.AddGoods(Tobacco, 100);
        game.PillageColony(brave, colony.Position, new FixedRandom(0.0)); // forced win
        Assert.Equal(50, colony.StoreOf(Tobacco));  // tobacco looted…
        Assert.Equal(80, colony.StoreOf(Hammers));  // …hammers untouched
    }

    [Fact]
    public void CheckPillageColony_RefusesAColonialAttacker()
    {
        (Game game, Colony colony, _) = Stage(seed: 7);
        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit artillery = game.SpawnUnit(Classic.Unit(Artillery), adj); // human-owned, not native

        Assert.False(game.CheckPillageColony(artillery, colony.Position).Allowed); // colonial units capture, not pillage
    }

    [Fact]
    public void CheckPillageColony_RefusesANonHumanColony()
    {
        (Game game, Colony colony, Unit brave) = Stage(seed: 7);
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        colony.OwnerId = foreignId; // a rival's colony — natives raid the human only

        Assert.False(game.CheckPillageColony(brave, colony.Position).Allowed);
    }

    [Fact]
    public void AnAlarmedBrave_PillagesAnAdjacentColony_DuringEndTurn()
    {
        // Seed chosen so the brave wins its single pillage roll on turn 1 (brave offence 1 vs colonist defence 1 ≈
        // 0.6 win; a loss would slay the brave instead). The assertion is notice-based (robust to which goods
        // stack the loot pick lands on, since EndTurn production adds food alongside the staged tobacco).
        (Game game, Colony colony, Unit brave) = Stage(seed: 13);
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == brave.OwnerNationId))
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // its nation is now Hateful → it raids
        }

        game.EndTurn();

        Assert.Contains(game.ColonyRaidNotices, n => n.AttackerNationId == brave.OwnerNationId && n.Position == colony.Position);
        Assert.Equal(colony.OwnerId, game.Colonies.First(c => c.Id == colony.Id).OwnerId); // pillaged, not captured
    }

    [Fact]
    public void NativePillage_DoesNotTouchTheHumansStream0()
    {
        // Same seed + same staged colony/brave in both; only one game's natives are enraged. With a winning seed
        // the brave survives and pillages turn after turn — including the colony's FOOD once EndTurn production
        // stocks it (food sorts before tobacco and is a valid loot target) — across a long horizon that spans
        // colony food growth. The raiding game's save diverges yet the human's stream 0 and scoped state stay
        // byte-identical: no native path (combat band or loot pick) draws from stream 0, and stealing food never
        // feeds back into a stream-0 draw (ADR-009). This long horizon is the regression net for any future
        // food→growth→stream-0 coupling the safety review flagged.
        (Game calm, _, _) = Stage(seed: 13);
        (Game raiding, _, Unit brave) = Stage(seed: 13);
        foreach (NativeSettlement s in raiding.NativeSettlements.Where(s => s.NationTypeId == brave.OwnerNationId))
        {
            raiding.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm);
        }

        for (int turn = 0; turn < 40; turn++)
        {
            calm.EndTurn();
            raiding.EndTurn();
        }

        Assert.NotEqual(SaveGame.From(calm).ToJson(), SaveGame.From(raiding).ToJson()); // the raid genuinely diverged…
        Assert.Equal(calm.RandomState, raiding.RandomState);                            // …yet stream 0 is untouched
        Assert.Equal(calm.HumanPlayer.Gold, raiding.HumanPlayer.Gold);
        Assert.Equal(calm.HumanPlayer.Immigration, raiding.HumanPlayer.Immigration);
        Assert.Equal(calm.HumanPlayer.RecruitDock, raiding.HumanPlayer.RecruitDock);
    }
}
