using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The pioneer tile-improvement build action (<c>86d3dqr62</c>) — a tooled pioneer spends turns building a
/// road / plow / clear-forest, consuming tools, with the FreeCol effects (road movement, plow yield, forest
/// cleared to its base type + lumber to the owning colony). Cross-checked against FreeCol
/// <c>ServerUnit.csImproveTile</c> and the classic spec improvement types.
/// </summary>
public class PioneerBuildTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Road = TileImprovementType.RoadId;
    private const string Plow = TileImprovementType.PlowId;
    private const string Clear = TileImprovementType.ClearForestId;
    private const string Pioneer = "model.role.pioneer";
    private const string FreeColonist = "model.unit.freeColonist";
    private const string HardyPioneer = "model.unit.hardyPioneer";
    private const string Lumber = "model.goods.lumber";

    /// <summary>
    /// A 3×3 map of one terrain type with a pioneer (a free colonist equipped with tools, unless
    /// <paramref name="unitTypeId"/> overrides) standing on the centre tile, and an optional human colony.
    /// </summary>
    private static Game GameWithPioneer(
        string centreTerrain, int roleCount = 1, string unitTypeId = FreeColonist,
        bool withColony = false, string surroundTerrain = "model.tile.plains")
    {
        string[] terrain =
        [
            surroundTerrain, surroundTerrain, surroundTerrain,
            surroundTerrain, centreTerrain,   surroundTerrain,
            surroundTerrain, surroundTerrain, surroundTerrain,
        ];
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = terrain,
            Units = [new SavedUnit(1, unitTypeId, 1, 1, 3, Role: Pioneer, RoleCount: roleCount)],
            Explored = [],
            // A colony on a corner (Chebyshev 1 from the centre) so it "works"/owns the centre tile.
            Colonies = withColony ? [new SavedColony(2, "Townsville", 0, 0, 1)] : null,
        };
        return save.Restore(Classic);
    }

    private static Unit ThePioneer(Game game) => game.Units.First(u => u.Id == 1);

    // Advances the pioneer's work by running its owner's improvement processing N times (one per turn).
    private static void WorkTurns(Game game, int turns)
    {
        for (int i = 0; i < turns; i++)
        {
            game.ProcessImprovements(game.HumanPlayer);
        }
    }

    // ---- CheckBuildImprovement gating ----

    [Fact]
    public void CheckBuild_Road_AllowedForTooledPioneerOnLand()
    {
        Game game = GameWithPioneer("model.tile.plains");
        Assert.True(game.CheckBuildImprovement(ThePioneer(game), Road).Allowed);
    }

    [Fact]
    public void CheckBuild_DeniedWithoutTools()
    {
        // A pioneer that has run out of tools (count 0) reverts to a colonist — it cannot build.
        Game game = GameWithPioneer("model.tile.plains");
        Unit u = ThePioneer(game);
        u.RoleId = RoleType.DefaultRoleId;
        u.RoleCount = 0;
        Assert.False(game.CheckBuildImprovement(u, Road).Allowed);
    }

    [Fact]
    public void CheckBuild_Plow_DeniedOnForest()
    {
        Game game = GameWithPioneer("model.tile.mixedForest");
        Assert.False(game.CheckBuildImprovement(ThePioneer(game), Plow).Allowed); // plow needs cleared land
        Assert.True(game.CheckBuildImprovement(ThePioneer(game), Clear).Allowed); // but clearing is allowed
    }

    [Fact]
    public void CheckBuild_ClearForest_DeniedOnPlains()
    {
        Game game = GameWithPioneer("model.tile.plains");
        Assert.False(game.CheckBuildImprovement(ThePioneer(game), Clear).Allowed); // nothing to clear
    }

    [Fact]
    public void CheckBuild_DeniedWhenRoadAlreadyPresent()
    {
        Game game = GameWithPioneer("model.tile.plains");
        game.BuildImprovement(ThePioneer(game), Road);
        // Give the pioneer a fresh turn + tools and re-check: the road it just finished blocks a second road.
        Unit u = ThePioneer(game);
        WorkTurns(game, 3); // plains road = 3 work turns
        Assert.True(game.Map.HasImprovement(u.Position, Road));
        u.RoleId = Pioneer;       // re-tool the (now reverted) pioneer for the re-check
        u.RoleCount = 1;
        u.MovementLeft = 3;
        Assert.False(game.CheckBuildImprovement(u, Road).Allowed);
    }

    // ---- BuildImprovement + per-turn work ----

    [Fact]
    public void BuildRoad_TakesTerrainPlusAddWorkTurns_ThenLandsAndSpendsTools()
    {
        Game game = GameWithPioneer("model.tile.plains");
        Unit u = ThePioneer(game);

        game.BuildImprovement(u, Road);
        Assert.True(u.IsImproving);
        Assert.Equal(Game.WorkTurnsToComplete(Classic.Terrain("model.tile.plains"), Classic.Improvement(Road)), u.WorkTurnsLeft);
        Assert.Equal(3, u.WorkTurnsLeft);  // plains basic-work-turns 3 + road add-work-turns 0
        Assert.Equal(0, u.MovementLeft);   // committed for the turn

        WorkTurns(game, 2);
        Assert.True(u.IsImproving);        // not done yet (1 left)
        Assert.Equal(1, u.WorkTurnsLeft);
        Assert.False(game.Map.HasImprovement(u.Position, Road));

        WorkTurns(game, 1);
        Assert.False(u.IsImproving);                          // completed
        Assert.True(game.Map.HasImprovement(u.Position, Road)); // road laid
        Assert.True(u.HasDefaultRole);                        // out of tools → reverted to a colonist
        Assert.Equal(0, u.RoleCount);
    }

    [Fact]
    public void HardyPioneer_WorksTwiceAsFast()
    {
        Game game = GameWithPioneer("model.tile.plains", unitTypeId: HardyPioneer);
        Unit u = ThePioneer(game);

        game.BuildImprovement(u, Road); // 3 work turns
        WorkTurns(game, 1);             // hardy does 2 → 1 left
        Assert.Equal(1, u.WorkTurnsLeft);
        Assert.True(u.IsImproving);
        WorkTurns(game, 1);             // 1 - 2 → done
        Assert.True(game.Map.HasImprovement(u.Position, Road));
    }

    [Fact]
    public void PioneerWithSpareTools_StaysAPioneerAfterBuilding()
    {
        // Count 2 (40 tools) → one road spends one count, leaving a still-tooled pioneer.
        Game game = GameWithPioneer("model.tile.plains", roleCount: 2);
        Unit u = ThePioneer(game);
        game.BuildImprovement(u, Road);
        WorkTurns(game, 3);
        Assert.Equal(Pioneer, u.RoleId);
        Assert.Equal(1, u.RoleCount);
    }

    // ---- Effects ----

    [Fact]
    public void Road_ReducesMovementCostBetweenTwoRoadTiles()
    {
        // Build a road on the pioneer's tile, then place a road on the neighbour via a second pioneer and check
        // a land unit pays the road follow-cost (1) instead of plains (3) stepping road→road.
        Game game = GameWithPioneer("model.tile.plains");
        Unit pioneer = ThePioneer(game);
        game.BuildImprovement(pioneer, Road);
        WorkTurns(game, 3);

        // Lay a road on the eastern neighbour directly via the map (it's the effect under test, not a second build).
        var east = new Position(2, 1);
        game.Map.SetImprovement(east, Classic.Improvement(Road));

        pioneer.MovementLeft = 3; // refresh moves; the (now colonist) unit steps east
        MoveCheck check = game.CheckMove(pioneer, east);
        Assert.True(check.Allowed);
        Assert.Equal(1, check.Cost); // road follow-cost, cheaper than plains (3)
    }

    [Fact]
    public void Plow_AddsOneFarmedYield()
    {
        Game game = GameWithPioneer("model.tile.plains");
        Unit u = ThePioneer(game);
        var tile = u.Position;
        int before = game.TileYieldPotential(tile, "model.goods.grain");

        game.BuildImprovement(u, Plow);
        Assert.Equal(5, u.WorkTurnsLeft); // plains 3 + plow add-work-turns 2
        WorkTurns(game, 5);

        Assert.True(game.Map.HasImprovement(tile, Plow));
        Assert.Equal(before + 1, game.TileYieldPotential(tile, "model.goods.grain"));
    }

    [Fact]
    public void ClearForest_ChangesTerrainAndDeliversLumberToOwningColony()
    {
        // Pioneer on a mixed-forest centre tile, a colony on the (0,0) corner (Chebyshev 1) that owns it.
        Game game = GameWithPioneer("model.tile.mixedForest", withColony: true);
        Unit u = ThePioneer(game);
        var tile = u.Position;
        Colony colony = game.Colonies.First(c => c.Id == 2);
        int lumberBefore = colony.StoreOf(Lumber);

        game.BuildImprovement(u, Clear);
        Assert.Equal(6, u.WorkTurnsLeft); // mixedForest basic-work-turns 4 + clearForest add-work-turns 2
        WorkTurns(game, 6);

        Assert.Equal("model.tile.plains", game.Map.TerrainAt(tile).Id);   // mixedForest → plains
        Assert.False(game.Map.TerrainAt(tile).IsForest);
        Assert.Equal(lumberBefore + 20, colony.StoreOf(Lumber));          // one-off 20 lumber delivered
    }

    [Fact]
    public void ClearForest_PreservesARiverOnTheTile()
    {
        Game game = GameWithPioneer("model.tile.mixedForest");
        Unit u = ThePioneer(game);
        var tile = u.Position;
        game.Map.SetImprovement(tile, Classic.RiverType); // a river runs through the forest

        game.BuildImprovement(u, Clear);
        WorkTurns(game, 6); // mixedForest 4 + clearForest 2

        Assert.Equal("model.tile.plains", game.Map.TerrainAt(tile).Id);
        Assert.True(game.Map.HasRiver(tile)); // the river survives the clearing
    }

    [Fact]
    public void ClearForest_WithNoOwningColony_StillChangesTerrain()
    {
        Game game = GameWithPioneer("model.tile.coniferForest"); // no colony
        Unit u = ThePioneer(game);
        var tile = u.Position;

        game.BuildImprovement(u, Clear);
        WorkTurns(game, Game.WorkTurnsToComplete(Classic.Terrain("model.tile.coniferForest"), Classic.Improvement(Clear)));

        Assert.Equal("model.tile.grassland", game.Map.TerrainAt(tile).Id); // coniferForest → grassland, no crash
    }

    // ---- Cycling + orders ----

    [Fact]
    public void ImprovingUnit_IsSkippedWhenCyclingUnits()
    {
        Game game = GameWithPioneer("model.tile.plains");
        Unit u = ThePioneer(game);
        Assert.Same(u, game.NextUnitToMove(game.HumanPlayer)); // needs orders before building
        game.BuildImprovement(u, Road);
        Assert.Null(game.NextUnitToMove(game.HumanPlayer));    // now busy → skipped
    }

    [Fact]
    public void ClearOrders_CancelsAnInProgressImprovement()
    {
        Game game = GameWithPioneer("model.tile.plains");
        Unit u = ThePioneer(game);
        game.BuildImprovement(u, Road);
        WorkTurns(game, 1);
        game.ClearOrders(u);
        Assert.False(u.IsImproving);
        Assert.Equal(0, u.WorkTurnsLeft);
    }

    // ---- Persistence (save v48) ----

    [Fact]
    public void InProgressImprovement_RoundTripsThroughSave_V48()
    {
        Game game = GameWithPioneer("model.tile.plains");
        Unit u = ThePioneer(game);
        game.BuildImprovement(u, Plow);
        WorkTurns(game, 2); // 5 - 2 = 3 left, still improving

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Unit reloaded = loaded.Units.First(x => x.Id == 1);
        Assert.Equal(Plow, reloaded.WorkImprovementId);
        Assert.Equal(3, reloaded.WorkTurnsLeft);
        Assert.True(reloaded.IsImproving);
    }

    [Fact]
    public void NonImprovingUnit_OmitsTheWorkTokens()
    {
        Game game = GameWithPioneer("model.tile.plains");
        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("WorkImprovement", json);
        Assert.DoesNotContain("WorkTurnsLeft", json);
    }

    [Fact]
    public void SaveVersion_IsCurrent() => Assert.Equal(61, SaveGame.CurrentVersion);
}
