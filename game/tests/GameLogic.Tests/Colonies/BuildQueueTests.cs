using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Ordered build queue (<c>86d3c9nxe</c>, FreeCol <c>Colony.buildQueue</c> + <c>csNextBuildable</c>): a colony
/// holds an ordered list of buildables; completing the front item auto-advances to the next, skipping any that
/// became invalid. <c>CurrentBuild</c> is the front. The tail persists (save v24, omitted for a ≤1-item queue).
/// </summary>
public class BuildQueueTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Hammers = "model.goods.hammers";
    private const string Tools = "model.goods.tools";
    private const string Warehouse = "model.building.warehouse";
    private const string WarehouseExpansion = "model.building.warehouseExpansion";
    private const string Depot = "model.building.depot";

    private static Game PlainsColony(out Colony colony)
    {
        var game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [0],
            Colonies = [new SavedColony(1, "Buildville", 0, 0, 1)],
        }.Restore(Classic);
        colony = game.Colonies[0];
        return game;
    }

    [Fact]
    public void CurrentBuild_IsTheFrontOfTheQueue()
    {
        Game game = PlainsColony(out Colony colony);
        game.SetBuild(colony, Warehouse);
        game.EnqueueBuild(colony, WarehouseExpansion);

        Assert.Equal([Warehouse, WarehouseExpansion], colony.BuildQueue);
        Assert.Equal(Warehouse, colony.CurrentBuild);
    }

    [Fact]
    public void CompletingTheFront_AutoAdvancesToTheNext()
    {
        Game game = PlainsColony(out Colony colony);
        colony.AddGoods(Hammers, 200); // covers warehouse (80) + expansion (80)
        colony.AddGoods(Tools, 20);    // the expansion also needs tools
        game.SetBuild(colony, Warehouse);
        game.EnqueueBuild(colony, WarehouseExpansion);

        game.EndTurn(); // builds the warehouse, advances to the expansion
        Assert.True(colony.HasBuilding(Warehouse));
        Assert.False(colony.HasBuilding(Depot)); // the warehouse upgraded the depot
        Assert.Equal(WarehouseExpansion, colony.CurrentBuild);

        game.EndTurn(); // builds the expansion, queue empties
        Assert.True(colony.HasBuilding(WarehouseExpansion));
        Assert.Null(colony.CurrentBuild);
    }

    [Fact]
    public void AnInvalidQueuedItem_IsSkippedWithoutSpendingMaterials()
    {
        Game game = PlainsColony(out Colony colony);
        // Queue the warehouse twice via the model, then build it the first time → the second is now invalid.
        colony.SetBuildQueue([Warehouse, Warehouse]);
        colony.AddGoods(Hammers, 160); // enough for two — but the second is redundant once the first is built

        game.EndTurn(); // builds warehouse #1, advances to #2 (one build per turn)
        Assert.True(colony.HasBuilding(Warehouse));
        Assert.Equal([Warehouse], colony.BuildQueue);
        Assert.Equal(80, colony.StoreOf(Hammers));

        // FreeCol re-validates the front item only once it is affordable: #2 is now affordable AND already built,
        // so it is dropped without spending its 80 hammers.
        game.EndTurn();
        Assert.Empty(colony.BuildQueue);
        Assert.Equal(80, colony.StoreOf(Hammers)); // the skip spent nothing
    }

    [Fact]
    public void ATransientlyUnbuildableItem_IsKeptWhileUnaffordable_NotDropped()
    {
        // Regression (FreeCol csNextBuildable runs only when the front item is affordable): a building queued
        // before the colony can build it — here a stockade (needs population 3) at population 1 with no hammers —
        // must NOT be silently dropped on idle turns. It waits, exactly as FreeCol does.
        Game game = PlainsColony(out Colony colony);
        colony.SetBuildQueue(["model.building.stockade"]); // requires population 3; colony is pop 1, produces no hammers

        game.EndTurn();
        game.EndTurn();

        Assert.Equal(["model.building.stockade"], colony.BuildQueue); // still queued, not lost
    }

    [Fact]
    public void EnqueueBuild_RejectsAlreadyBuiltOrQueuedOrCostless()
    {
        Game game = PlainsColony(out Colony colony);
        game.EnqueueBuild(colony, Warehouse);

        Assert.False(game.CheckEnqueueBuild(colony, Warehouse).Allowed);                 // already queued
        Assert.False(game.CheckEnqueueBuild(colony, Depot).Allowed);                     // already built (free base)
        Assert.False(game.CheckEnqueueBuild(colony, "model.building.townHall").Allowed);  // costless free building
    }

    [Fact]
    public void SetBuild_ReplacesTheWholeQueue()
    {
        Game game = PlainsColony(out Colony colony);
        colony.SetBuildQueue([Warehouse, WarehouseExpansion]);

        game.SetBuild(colony, Warehouse); // a single set replaces the queue
        Assert.Equal([Warehouse], colony.BuildQueue);

        game.SetBuild(colony, null); // null clears it
        Assert.Empty(colony.BuildQueue);
        Assert.Null(colony.CurrentBuild);
    }

    [Fact]
    public void MultiItemQueue_RoundTripsThroughSaveLoad()
    {
        Game game = PlainsColony(out Colony colony);
        colony.SetBuildQueue([Warehouse, WarehouseExpansion]);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal([Warehouse, WarehouseExpansion], restored.Colonies[0].BuildQueue);
        Assert.Equal(35, SaveGame.CurrentVersion);
    }

    [Fact]
    public void ASingleItemQueue_OmitsTheTailToken()
    {
        Game game = PlainsColony(out Colony colony);
        colony.SetBuildQueue([Warehouse]);
        Assert.DoesNotContain("BuildQueueRest", SaveGame.From(game).ToJson()); // ≤1-item queue stays byte-identical to v23
    }
}
