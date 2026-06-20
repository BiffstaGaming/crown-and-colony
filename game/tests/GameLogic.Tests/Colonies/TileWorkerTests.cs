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
        // Ocean fishing is legal.
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
