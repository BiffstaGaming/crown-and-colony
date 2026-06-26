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

    /// <summary>Forces a brave win and the LAST loot option (the gold option, which sorts after the goods stacks), with a max plunder draw.</summary>
    private sealed class GoldOptionRandom : IGameRandom
    {
        public int Next(int maxExclusive) => maxExclusive - 1; // the last loot option = gold; also the top of the plunder range
        public int Next(int minInclusive, int maxExclusive) => maxExclusive - 1;
        public double NextDouble() => 0.0;                     // force a brave win
        public RandomState SaveState() => new(0, 0);
    }

    /// <summary>Forces a brave win and a chosen pillage-option index (FreeCol order: buildings, then ships, then goods, then gold).</summary>
    private sealed class OptionRandom(int option) : IGameRandom
    {
        public int Next(int maxExclusive) => option;            // pick the option at this index in the pillage choice
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => 0.0;                      // force a brave win
        public RandomState SaveState() => new(0, 0);
    }

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>
    /// Moves every human ship out of <paramref name="colony"/>'s port (the adjacent water tiles) to a far water tile,
    /// so the pillage option set is just the colony's goods/gold — the human start parks ships near the coast, which
    /// would otherwise be a valid (and index-0) "sink a ship in port" pillage option. Ship-sink is exercised by its
    /// own dedicated test.
    /// </summary>
    private static void ClearPort(Game game, Colony colony)
    {
        var port = colony.Position.Neighbours().ToHashSet();
        Position far = game.Map.AllPositions()
            .First(p => game.Map.TerrainAt(p).IsWater && !port.Contains(p)
                && !colony.Position.IsAdjacentTo(p) && p != colony.Position
                && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        foreach (Unit ship in game.PlayerUnits.Where(u => u.IsOnMap && u.Type.IsNaval && port.Contains(u.Position)).ToList())
        {
            ship.Position = far;
        }
    }

    /// <summary>The human founds an (undefended) colony stocked with goods; a brave of a real native nation stands beside it. Docked ships are cleared from the port so the loot options are goods/gold only.</summary>
    private static (Game game, Colony colony, Unit brave) Stage(ulong seed, int tobacco = 100)
    {
        Game game = Game.New(Classic, seed);
        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony);
        founder.RoleId = RoleType.DefaultRoleId; // found a clean colony — the starting soldier/pioneer's banked muskets/tools aren't part of these pillage scenarios
        founder.RoleCount = 0;
        Colony colony = game.FoundColony(founder); // human-owned; undefended (the founder became its population)
        ClearPort(game, colony);                    // park starting ships away so a raid can't pick "sink a ship in port"
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
    public void Pillage_CanStealGold_WhenTheLootPickLandsOnTheGoldOption()
    {
        // FreeCol's pillage choice has an extra "steal gold" option after the goods stacks (csPillageColony:
        // max(1, colony.getPlunder/5)). With one goods stack + a human purse, the gold option is the last index;
        // GoldOptionRandom picks it and takes the top of the plunder range.
        (Game game, Colony colony, Unit brave) = Stage(seed: 7); // tobacco 100 → one goods stack
        game.HumanPlayer.Gold = 1000;
        int goldBefore = game.HumanPlayer.Gold;

        game.PillageColony(brave, colony.Position, new GoldOptionRandom());

        Assert.True(game.HumanPlayer.Gold < goldBefore);             // gold was stolen…
        Assert.Equal(100, colony.StoreOf(Tobacco));                 // …and the goods left untouched (one option per raid)
        ColonyRaidNotice notice = Assert.Single(game.ColonyRaidNotices);
        Assert.Null(notice.GoodsId);                                // a gold raid carries a null goods id
        Assert.True(notice.Amount >= 1);                            // at least 1 gold (FreeCol max(1, …))
        Assert.Equal(goldBefore - game.HumanPlayer.Gold, notice.Amount); // the notice reports exactly what was taken
    }

    [Fact]
    public void Pillage_HasNoGoldOption_WhenTheOwnerIsBroke()
    {
        // With the owner broke, canBePlundered is false → no gold option; the "last" pick falls on the goods stack.
        (Game game, Colony colony, Unit brave) = Stage(seed: 7);
        game.HumanPlayer.Gold = 0;

        game.PillageColony(brave, colony.Position, new GoldOptionRandom()); // would pick gold if it existed

        Assert.Equal(50, colony.StoreOf(Tobacco));                  // goods stolen instead (no gold option)
        ColonyRaidNotice notice = Assert.Single(game.ColonyRaidNotices);
        Assert.Equal(Tobacco, notice.GoodsId);                      // a goods raid, not gold
        Assert.Equal(0, game.HumanPlayer.Gold);                     // no gold to take
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
        game.HumanPlayer.Gold = 0; // …and a broke owner (else the gold option alone makes it pillageable — canBePlundered)

        Assert.All(colony.Stores.Values, amount => Assert.Equal(0, amount)); // sanity: nothing lootable
        Assert.Empty(BurnableBuildings(game, colony));                       // …no burnable building (only free base buildings)
        Assert.False(game.CheckPillageColony(brave, colony.Position).Allowed);
    }

    /// <summary>The colony's burnable buildings, mirroring the game's predicate (a building that cost goods or upgrades from another).</summary>
    private static System.Collections.Generic.List<string> BurnableBuildings(Game game, Colony colony) =>
        colony.Buildings.Where(b => Classic.Building(b).BuildCost.Count > 0 || Classic.Building(b).UpgradesFrom is not null).ToList();

    [Fact]
    public void NonStorableGoods_LikeBankedHammers_AreNeverLooted()
    {
        // FreeCol's getLootableGoodsList filters to storable goods; hammers (storable="false") accrue in stores
        // for construction but are never a pillage target. A colony with ONLY hammers can't be pillaged at all,
        // and when it also holds storable goods the loot only ever comes from those — hammers stay untouched.
        (Game game, Colony colony, Unit brave) = Stage(seed: 7, tobacco: 0);
        game.HumanPlayer.Gold = 0; // isolate the goods gate (no gold option, no docked ships after ClearPort)
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
            s.AddStock("model.goods.muskets", -s.StockOf("model.goods.muskets")); // keep the brave unarmed so the seed-13 pillage roll stands…
            s.AddStock("model.goods.horses", -s.StockOf("model.goods.horses"));   // …(the generator now seeds arms — 86d3e49gq) — this test is about pillage, not arming
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
        // the brave survives and pillages turn after turn — goods (incl. the colony's FOOD once EndTurn production
        // stocks it) AND sometimes gold (the loot pick's extra option) — across a long horizon. The raiding game's
        // save diverges yet the human's RNG cursor (stream 0) stays byte-identical: no native path (combat band,
        // loot pick, or the gold-plunder draw) touches stream 0 (ADR-009). The human's GOLD may now legitimately
        // diverge (gold-steal debits it), but its immigration/dock — which no pillage path touches — stay identical,
        // and crucially no stolen gold/goods feeds back into a stream-0 draw.
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
        Assert.Equal(calm.HumanPlayer.Immigration, raiding.HumanPlayer.Immigration);    // pillage touches neither immigration…
        Assert.Equal(calm.HumanPlayer.RecruitDock, raiding.HumanPlayer.RecruitDock);    // …nor the recruit dock (only gold/goods)
    }

    [Fact]
    public void Pillage_CanBurnABuilding_DowngradingAnUpgradedOne()
    {
        // FreeCol csPillageColony's first option block is the burnable buildings (csDamageBuilding). A warehouse
        // (upgrades from the depot) is burnable; burning it DOWNGRADES it back to a depot rather than razing it.
        // With no goods stocked and a broke owner, the building is the only option — index 0 (OptionRandom 0).
        (Game game, Colony colony, Unit brave) = Stage(seed: 7, tobacco: 0);
        game.HumanPlayer.Gold = 0;
        colony.ReplaceBuilding("model.building.depot", "model.building.warehouse"); // upgrade the free depot to a (burnable) warehouse

        game.PillageColony(brave, colony.Position, new OptionRandom(0));

        Assert.DoesNotContain("model.building.warehouse", colony.Buildings); // burned…
        Assert.Contains("model.building.depot", colony.Buildings);           // …downgraded back to the depot
        ColonyRaidNotice notice = Assert.Single(game.ColonyRaidNotices);
        Assert.Equal(PillageKind.Building, notice.Kind);
        Assert.Equal("model.building.warehouse", notice.BuildingId);
    }

    [Fact]
    public void Pillage_CanRazeABaseBuilding_ThatHasNoPredecessor()
    {
        // A base building that COST goods to build but has no predecessor (the stockade) is razed outright, not
        // downgraded (FreeCol csDamageBuilding: destroy when upgradesFrom == null). It's the only burnable building
        // here (free base houses are spared), so it is the index-0 option.
        (Game game, Colony colony, Unit brave) = Stage(seed: 7, tobacco: 0);
        game.HumanPlayer.Gold = 0;
        colony.AddBuilding("model.building.stockade"); // costs hammers, no upgrades-from → razed on a burn
        // (a stockade also blocks pillage via canBePillaged in FreeCol, but our CheckPillageColony gates on a garrison;
        //  this unit-level test drives PillageColony directly to exercise the raze path.)

        game.PillageColony(brave, colony.Position, new OptionRandom(0));

        Assert.DoesNotContain("model.building.stockade", colony.Buildings); // razed
        ColonyRaidNotice notice = Assert.Single(game.ColonyRaidNotices);
        Assert.Equal(PillageKind.Building, notice.Kind);
        Assert.Equal("model.building.stockade", notice.BuildingId);
    }

    [Fact]
    public void Pillage_CanSinkAShipInPort()
    {
        // FreeCol csPillageColony's second option block is the ships in the colony's port (csSinkShipAttack). A human
        // ship with nowhere to repair (no drydock colony, but a colonial player always has Europe → it is DAMAGED and
        // limps off) sits on a port tile. With no burnable buildings and no goods, the ship is the first option.
        (Game game, Colony colony, Unit brave) = Stage(seed: 7, tobacco: 0);
        game.HumanPlayer.Gold = 0;
        Position water = colony.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater);
        Unit ship = game.SpawnUnit(Classic.Unit("model.unit.caravel"), water); // human-owned naval unit in port
        int shipId = ship.Id;

        game.PillageColony(brave, colony.Position, new OptionRandom(0));

        Unit afterShip = game.Units.First(u => u.Id == shipId);
        Assert.True(afterShip.IsUnderRepair);                  // damaged → limps off to repair (the colonial owner has Europe)
        ColonyRaidNotice notice = Assert.Single(game.ColonyRaidNotices);
        Assert.Equal(PillageKind.Ship, notice.Kind);
    }
}
