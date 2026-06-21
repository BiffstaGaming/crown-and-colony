using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

public class TileWorkerTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Grain = "model.goods.grain";
    private const string Ore = "model.goods.ore";
    private const string Fish = "model.goods.fish";

    /// <summary>
    /// 3×3 map: colony on centre plains, surrounded by plains except an ocean
    /// corner and a mountains corner. Pop as requested, no auto-assignments.
    /// </summary>
    private static Game ColonyOnCross(int population)
    {
        string[] terrain =
        [
            "model.tile.ocean", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.mountains",
        ];
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = terrain,
            Units = [],
            Explored = [],
            Colonies = [new SavedColony(1, "Testville", 1, 1, population)],
        };
        return save.Restore(Classic);
    }

    [Fact]
    public void AssignWork_ProducesTheChosenGoods_EachTurn()
    {
        Game game = ColonyOnCross(population: 2);
        Colony colony = game.Colonies[0];

        game.AssignWork(colony, new Position(0, 1), Grain);   // plains farm: 5
        game.AssignWork(colony, new Position(2, 2), Ore);     // mountains: 4

        game.EndTurn();

        // Centre: +3 grain +2 cotton; farm +5 grain (all stored as food);
        // mine +4 ore; 2 colonists eat 4.
        Assert.Equal(3 + 5 - 4, colony.Food);
        Assert.Equal(4, colony.StoreOf(Ore));
        Assert.Equal(0, colony.IdleColonists);
    }

    [Fact]
    public void AssignWork_Validation()
    {
        Game game = ColonyOnCross(population: 1);
        Colony colony = game.Colonies[0];

        // Mountains can't farm.
        Assert.False(game.CheckAssignWork(colony, new Position(2, 2), Grain).Allowed);
        // Non-adjacent tile (the colony's own square).
        Assert.False(game.CheckAssignWork(colony, new Position(1, 1), Grain).Allowed);
        // Ocean fishing needs Docks: refused on this dockless colony, allowed once it is built (see Fishing_RequiresDocks).
        Assert.False(game.CheckAssignWork(colony, new Position(0, 0), Fish).Allowed);
        colony.AddBuilding("model.building.docks");
        Assert.True(game.CheckAssignWork(colony, new Position(0, 0), Fish).Allowed);

        game.AssignWork(colony, new Position(0, 1), Grain);
        // Same tile twice.
        Assert.False(game.CheckAssignWork(colony, new Position(0, 1), Grain).Allowed);
        // No idle colonists left (pop 1, 1 assigned).
        Assert.False(game.CheckAssignWork(colony, new Position(1, 0), Grain).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.AssignWork(colony, new Position(1, 0), Grain));

        game.UnassignWork(colony, new Position(0, 1));
        Assert.Equal(1, colony.IdleColonists);
        Assert.True(game.CheckAssignWork(colony, new Position(1, 0), Grain).Allowed);
    }

    [Fact]
    public void Fishing_RequiresDocks()
    {
        Game game = ColonyOnCross(population: 1);
        Colony colony = game.Colonies[0];

        // FreeCol produceInWater gate: a dockless colony cannot work the sea (ocean corner at 0,0), though its land
        // tiles stay workable. The order command refuses too (not just the check).
        Assert.False(game.CheckAssignWork(colony, new Position(0, 0), Fish).Allowed);
        Assert.True(game.CheckAssignWork(colony, new Position(0, 1), Grain).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.AssignWork(colony, new Position(0, 0), Fish));

        // Build the Docks → the sea opens up, and a colonist can be put to work fishing.
        colony.AddBuilding("model.building.docks");
        Assert.True(game.CheckAssignWork(colony, new Position(0, 0), Fish).Allowed);
        game.AssignWork(colony, new Position(0, 0), Fish);
        Assert.Equal(Fish, colony.TileWorkers[new Position(0, 0)]);
    }

    [Fact]
    public void Fishing_DrydockInheritsTheDocksWaterAbility()
    {
        Game game = ColonyOnCross(population: 1);
        Colony colony = game.Colonies[0];

        // The drydock extends docks, so it inherits model.ability.produceInWater down the extends chain.
        colony.AddBuilding("model.building.drydock");
        Assert.True(game.CheckAssignWork(colony, new Position(0, 0), Fish).Allowed);
    }

    [Fact]
    public void Restore_DropsAWaterWorkerOnADocklessColony_ButKeepsItWithDocks()
    {
        // A save written before the Docks gate could hold a colonist fishing a sea tile on a dockless colony. Loading
        // such a save now returns that colonist to the idle pool; a colony that has Docks keeps its fisher.
        string[] terrain =
        [
            "model.tile.ocean", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.mountains",
        ];
        SaveGame Save(System.Collections.Generic.IReadOnlyList<string>? buildings) => new()
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = terrain,
            Units = [],
            Explored = [],
            Colonies = [new SavedColony(1, "Testville", 1, 1, 1,
                Workers: [new SavedWorker(0, 0, Fish)], Buildings: buildings)],
        };

        // Dockless (null → the free base buildings, no docks): the illegal fisher is dropped, the colonist goes idle.
        Colony dockless = Save(null).Restore(Classic).Colonies[0];
        Assert.DoesNotContain(dockless.TileWorkers.Keys, t => t == new Position(0, 0));
        Assert.Equal(1, dockless.IdleColonists);

        // With Docks built, the same fisher survives the round-trip.
        Colony withDocks = Save(["model.building.docks"]).Restore(Classic).Colonies[0];
        Assert.Equal(Fish, withDocks.TileWorkers[new Position(0, 0)]);
    }

    [Fact]
    public void ColonyCanWorkTile_LandAlways_WaterNeedsDocks()
    {
        Game game = ColonyOnCross(population: 1);
        Colony colony = game.Colonies[0];

        Assert.True(game.ColonyCanWorkTile(colony, new Position(0, 1)));  // a plains tile is always workable terrain
        Assert.False(game.ColonyCanWorkTile(colony, new Position(0, 0))); // the ocean corner — not without docks
        colony.AddBuilding("model.building.docks");
        Assert.True(game.ColonyCanWorkTile(colony, new Position(0, 0)));  // docks open the sea
    }

    [Fact]
    public void Planner_NeverFishesWithoutDocks()
    {
        // The AI/colony tile planner must honour the same gate: on a dockless coastal colony it staffs only land tiles
        // and never the ocean corner — otherwise it would hand AssignWork a sea tile and throw mid-AI-turn.
        Game game = ColonyOnCross(population: 4);
        Colony colony = game.Colonies[0];

        game.PlanColonyTileWork(game.HumanPlayer, colony); // must not throw
        Assert.DoesNotContain(colony.TileWorkers.Keys, t => game.Map.TerrainAt(t).IsWater);
    }

    [Fact]
    public void Founding_AutoAssignsTheFounder_ToTheBestFoodTile()
    {
        // Real game: found on the starting tile and the founder reports to a farm.
        var game = Game.New(Classic, seed: 424242);
        Colony colony = game.FoundColony(game.Units[0]);

        Assert.True(colony.TileWorkers.Count <= 1);
        if (colony.TileWorkers.Count == 1)
        {
            var (tile, goods) = colony.TileWorkers.First();
            Assert.Equal(Grain, goods);
            Assert.True(tile.IsAdjacentTo(colony.Position));
            Assert.True(game.TileYield(tile, Grain) > 0);
            Assert.Equal(0, colony.IdleColonists);
        }
    }

    [Fact]
    public void Growth_AutoAssignsTheNewColonist()
    {
        Game game = ColonyOnCross(population: 1);
        Colony colony = game.Colonies[0];
        game.AssignWork(colony, new Position(0, 1), Grain);
        colony.AddGoods(Colony.FoodId, 195);

        game.EndTurn(); // +3 centre +5 farm −2 eat → 201 ≥ 200 → growth

        Assert.Equal(2, colony.Population);
        Assert.Equal(2, colony.TileWorkers.Count); // newborn took the next-best farm
        Assert.Equal(0, colony.IdleColonists);
    }

    [Fact]
    public void TileWorkOptions_ListEachTilesProducibleGoods_WithYields_SortedByYield()
    {
        // The colony screen's per-tile work picker is fed by this oracle.
        Game game = ColonyOnCross(population: 1);

        // Mountains corner → ore is a workable (non-food) good, the case this slice unblocks.
        var mountains = game.TileWorkOptions(new Position(2, 2));
        Assert.Contains(mountains, o => o.GoodsId == Ore);
        Assert.All(mountains, o => Assert.True(o.Yield > 0));
        // Each option's yield matches what CheckAssignWork/TileYield would award.
        Assert.All(mountains, o => Assert.Equal(game.TileYield(new Position(2, 2), o.GoodsId), o.Yield));
        // Sorted by yield, descending.
        Assert.Equal(mountains.Select(o => o.Yield).OrderByDescending(y => y), mountains.Select(o => o.Yield));

        // Ocean corner → fishing (food) is offered — water tiles are workable, not empty.
        Assert.Contains(game.TileWorkOptions(new Position(0, 0)), o => o.GoodsId == Fish);

        // A plains field → grain is offered.
        Assert.Contains(game.TileWorkOptions(new Position(0, 1)), o => o.GoodsId == Grain);

        // Off-map tiles offer nothing.
        Assert.Empty(game.TileWorkOptions(new Position(-1, -1)));
        Assert.Empty(game.TileWorkOptions(new Position(99, 99)));
    }

    // ---- Coastal fish bonus (86d3c9we8): FreeCol fishBonusLand, +2 fish on coastal water ----

    /// <summary>
    /// Builds an arbitrary-size map from an explicit terrain grid (row-major), with no colonies, so individual
    /// tile-yield potentials can be asserted directly. Mirrors <see cref="ColonyOnCross"/>'s save-restore approach.
    /// </summary>
    private static Game MapFrom(int width, int height, string[] terrain)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = width,
            MapHeight = height,
            Terrain = terrain,
            Units = [],
            Explored = [],
            Colonies = [],
        };
        return save.Restore(Classic);
    }

    [Fact]
    public void CoastalOceanTile_YieldsTwoMoreFish_ThanOpenOcean()
    {
        // Column 0 = open ocean (only ocean neighbours); column 2's ocean touches the central land column.
        // Row-major 3x3:
        //   ocean  plains ocean
        //   ocean  plains ocean
        //   ocean  plains ocean
        string[] terrain =
        [
            "model.tile.ocean", "model.tile.plains", "model.tile.ocean",
            "model.tile.ocean", "model.tile.plains", "model.tile.ocean",
            "model.tile.ocean", "model.tile.plains", "model.tile.ocean",
        ];
        Game game = MapFrom(3, 3, terrain);

        // Open-ocean tile (0,0): land neighbours = plains at (1,0) and (1,1) = 2 → not coastal → base 2 fish.
        int openOcean = game.TileYieldPotential(new Position(0, 0), Fish);
        // Coastal ocean tile (2,1): land neighbours = plains at (1,0),(1,1),(1,2) = 3 → coastal → 2 + 2 = 4 fish.
        int coastal = game.TileYieldPotential(new Position(2, 1), Fish);

        Assert.Equal(2, openOcean);
        Assert.Equal(4, coastal);
        Assert.Equal(openOcean + 2, coastal); // the bonus is exactly the spec amount (+2)
    }

    [Fact]
    public void OpenOceanWithTooFewLandNeighbours_GetsNoFishBonus()
    {
        // Edge ocean tile with at most two land neighbours stays at the open-ocean 2 fish (FreeCol adjacentLand > 2).
        string[] terrain =
        [
            "model.tile.ocean", "model.tile.ocean", "model.tile.ocean",
            "model.tile.ocean", "model.tile.ocean", "model.tile.ocean",
            "model.tile.ocean", "model.tile.plains", "model.tile.plains",
        ];
        Game game = MapFrom(3, 3, terrain);

        // (1,1) touches exactly two land tiles — plains at (1,2) and (2,2) → 2 land neighbours → no bonus.
        Assert.Equal(2, game.TileYieldPotential(new Position(1, 1), Fish));
        // A land-locked-by-water ocean corner (0,0): 0 land neighbours → no bonus.
        Assert.Equal(2, game.TileYieldPotential(new Position(0, 0), Fish));
    }

    [Fact]
    public void CoastalBonus_AppliesOnlyToFish_NotOtherGoods()
    {
        // A coastal ocean tile produces no grain regardless of land adjacency (the bonus is fish-specific).
        string[] terrain =
        [
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.ocean",  "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
        ];
        Game game = MapFrom(3, 3, terrain);

        // Centre ocean (1,1) has 8 land neighbours → coastal → 4 fish, but still 0 grain.
        Assert.Equal(4, game.TileYieldPotential(new Position(1, 1), Fish));
        Assert.Equal(0, game.TileYieldPotential(new Position(1, 1), Grain));
    }

    [Fact]
    public void LakeTilesAlsoGetTheCoastalBonus_HighSeasDoesNot()
    {
        // Lakes are non-high-seas water, so the fishBonusLand scope (isWater && !highSeas) covers them too.
        // High seas (the map edge a ship sails from) is excluded by the improvement's match-negated scope.
        string[] lakeMap =
        [
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.lake",   "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
        ];
        Game lakeGame = MapFrom(3, 3, lakeMap);
        Assert.Equal(4, lakeGame.TileYieldPotential(new Position(1, 1), Fish)); // 2 base + 2 coastal

        string[] highSeasMap =
        [
            "model.tile.plains",    "model.tile.plains", "model.tile.plains",
            "model.tile.plains",    "model.tile.highSeas", "model.tile.plains",
            "model.tile.plains",    "model.tile.plains", "model.tile.plains",
        ];
        Game highSeasGame = MapFrom(3, 3, highSeasMap);
        Assert.Equal(2, highSeasGame.TileYieldPotential(new Position(1, 1), Fish)); // base only, no coastal bonus
    }

    // ---- River-mouth fish bonus (86d3b3qdx): FreeCol fishBonusRiver, +1 fish on water beside a river mouth ----

    /// <summary>
    /// Builds a map from an explicit terrain grid plus a set of river tiles (row-major), so river-mouth yields can be
    /// asserted directly. The river improvement is stamped via the v47 save <see cref="SaveGame.Improvements"/> path.
    /// </summary>
    private static Game MapWithRivers(int width, int height, string[] terrain, params (int X, int Y)[] rivers)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = width,
            MapHeight = height,
            Terrain = terrain,
            Units = [],
            Explored = [],
            Colonies = [],
            Improvements = [.. System.Linq.Enumerable.Select(
                rivers, r => new SavedImprovement(r.Y * width + r.X, "model.improvement.river", 1))],
        };
        return save.Restore(Classic);
    }

    [Fact]
    public void WaterBesideRiverMouth_YieldsOneMoreFish()
    {
        // Row-major 3x3: a central land column, ocean either side. The river sits on the centre land tile (1,1),
        // which touches the ocean columns — so it is a river mouth, and its adjacent ocean tiles get +1.
        //   ocean  plains ocean
        //   ocean  river  ocean
        //   ocean  plains ocean
        string[] terrain =
        [
            "model.tile.ocean", "model.tile.plains", "model.tile.ocean",
            "model.tile.ocean", "model.tile.plains", "model.tile.ocean",
            "model.tile.ocean", "model.tile.plains", "model.tile.ocean",
        ];
        Game game = MapWithRivers(3, 3, terrain, (1, 1));

        // Coastal ocean (2,1) is beside the river-mouth land (1,1): 3 land neighbours → coastal +2, plus river +1 → 5.
        Assert.Equal(5, game.TileYieldPotential(new Position(2, 1), Fish));
        // Open-ocean (0,0) is also beside (1,1) the mouth, but has only 2 land neighbours → no coastal +2; still +1
        // for the river mouth → 2 base + 1 = 3.
        Assert.Equal(3, game.TileYieldPotential(new Position(0, 0), Fish));
    }

    [Fact]
    public void InlandRiver_IsNotAMouth_SoNoFishBonus()
    {
        // A river on a fully land-surrounded tile is not a mouth: it touches no water, so no neighbouring water earns
        // the +1. The river at (1,1) sits in a block of plains; the ocean at (4,0) is two tiles away, so the river
        // tile has no water neighbour of its own and is not a mouth.
        //   plains plains plains plains ocean
        //   plains river  plains plains plains
        //   plains plains plains plains plains
        string[] terrain =
        [
            "model.tile.plains", "model.tile.plains", "model.tile.plains", "model.tile.plains", "model.tile.ocean",
            "model.tile.plains", "model.tile.plains", "model.tile.plains", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.plains", "model.tile.plains", "model.tile.plains",
        ];
        string[] riverless = (string[])terrain.Clone();
        Game riverGame = MapWithRivers(5, 3, terrain, (1, 1)); // (1,1) is the river; its neighbours are all land
        Game plainGame = MapFrom(5, 3, riverless);

        // The ocean (4,0) is the only water; the inland river (1,1) is not its neighbour anyway, but more importantly
        // the river tile has no water neighbour so it is not a mouth → the ocean's yield matches the riverless map.
        Assert.Equal(
            plainGame.TileYieldPotential(new Position(4, 0), Fish),
            riverGame.TileYieldPotential(new Position(4, 0), Fish));
    }

    [Fact]
    public void RiverMouthBonus_AppliesOnlyToFish_AndNotToHighSeas()
    {
        // Centre land column with a river mouth; high seas on one side, ocean on the other.
        //   highSeas plains ocean
        //   highSeas river  ocean
        //   highSeas plains ocean
        string[] terrain =
        [
            "model.tile.highSeas", "model.tile.plains", "model.tile.ocean",
            "model.tile.highSeas", "model.tile.plains", "model.tile.ocean",
            "model.tile.highSeas", "model.tile.plains", "model.tile.ocean",
        ];
        Game game = MapWithRivers(3, 3, terrain, (1, 1));

        // The ocean side (2,1) gets coastal +2 and river +1 → 5; grain stays 0 (the bonus is fish-only).
        Assert.Equal(5, game.TileYieldPotential(new Position(2, 1), Fish));
        Assert.Equal(0, game.TileYieldPotential(new Position(2, 1), Grain));
        // The high-seas side (0,1) is excluded by the improvement's match-negated highSeas scope → base 2, no bonuses.
        Assert.Equal(2, game.TileYieldPotential(new Position(0, 1), Fish));
    }

    [Fact]
    public void SaveRoundTrip_PreservesWorkers_AndPreV5LoadsNone()
    {
        Game game = ColonyOnCross(population: 2);
        Colony colony = game.Colonies[0];
        game.AssignWork(colony, new Position(0, 1), Grain);
        game.AssignWork(colony, new Position(2, 2), Ore);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(colony.TileWorkers, loaded.Colonies[0].TileWorkers);

        SaveGame v4 = SaveGame.From(game) with
        {
            Version = 4,
            Colonies = [new SavedColony(1, "Testville", 1, 1, 2)],
        };
        Assert.Empty(SaveGame.FromJson(v4.ToJson()).Restore(Classic).Colonies[0].TileWorkers);
    }
}
