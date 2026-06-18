using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Building units in a colony (FreeCol <c>ServerColony.csBuildUnit</c> + <c>Colony.getNoBuildReason</c>): the
/// construction queue can hold land units — <b>artillery</b> (192 hammers + 40 tools, needs an armory) and the
/// <b>wagon train</b> (40 hammers, the free carpenter's house enables it). A completed unit musters on the colony
/// tile under the colony's owner, costs no colonist, and can be built repeatedly; the wagon train is capped at one
/// per colony. Ships and the free colonist are excluded. No save-format change (a unit id rides the queue strings).
/// </summary>
public class BuildUnitTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Hammers = "model.goods.hammers";
    private const string Tools = "model.goods.tools";
    private const string Artillery = "model.unit.artillery";
    private const string WagonTrain = "model.unit.wagonTrain";
    private const string Armory = "model.building.armory";
    private const string Warehouse = "model.building.warehouse";

    // ---- Ruleset parse: build cost, build-ability scopes, the wagon-train limit ----

    [Fact]
    public void ArtilleryAndWagonTrain_ParseTheirBuildCost()
    {
        Assert.Equal(
            [("model.goods.hammers", 192), ("model.goods.tools", 40)],
            Classic.Unit(Artillery).BuildCostOrEmpty.Select(g => (g.GoodsId, g.Amount)).ToList());
        Assert.Equal(
            [("model.goods.hammers", 40)],
            Classic.Unit(WagonTrain).BuildCostOrEmpty.Select(g => (g.GoodsId, g.Amount)).ToList());
    }

    [Theory]
    [InlineData("model.building.carpenterHouse", WagonTrain)]
    [InlineData("model.building.lumberMill", WagonTrain)]   // extends carpenterHouse → inherits the scope
    [InlineData("model.building.armory", Artillery)]
    [InlineData("model.building.magazine", Artillery)]      // extends armory → inherits the scope
    [InlineData("model.building.arsenal", Artillery)]
    public void BuildAbilityScopes_AreCollectedDownTheExtendsChain(string buildingId, string unitId)
    {
        Assert.Contains(unitId, Classic.Building(buildingId).BuildableUnitTypeIdsOrEmpty);
    }

    [Fact]
    public void ShipyardGrantsTheNavalBuildScope_OthersDoNot()
    {
        Assert.True(Classic.Building("model.building.shipyard").BuildsNavalUnits);
        Assert.False(Classic.Building("model.building.armory").BuildsNavalUnits);
    }

    [Fact]
    public void BuildableUnitTypes_AreTheHammerUnits_NotTheFreeColonist()
    {
        var ids = Classic.BuildableUnitTypes.Select(u => u.Id).ToList();
        Assert.Contains(Artillery, ids);
        Assert.Contains(WagonTrain, ids);
        Assert.DoesNotContain("model.unit.freeColonist", ids); // food cost = the growth threshold, not a build item
    }

    [Fact]
    public void WagonTrain_CarriesItsBuildLimit()
    {
        UnitBuildLimit limit = Assert.IsType<UnitBuildLimit>(Classic.Unit(WagonTrain).BuildLimit);
        Assert.Equal(("lt", "units", "settlements"), (limit.Operator, limit.LeftOperand, limit.RightOperand));
        Assert.Null(Classic.Unit(Artillery).BuildLimit); // artillery is uncapped
    }

    // ---- Build-ability gate ----

    [Fact]
    public void WagonTrain_IsBuildableInAnyColony_TheCarpentersHouseEnablesIt()
    {
        Game game = PlainsColony(out Colony colony);
        Assert.True(game.CheckSetBuild(colony, WagonTrain).Allowed);
        Assert.Contains(game.BuildableUnits(colony), u => u.Id == WagonTrain);
    }

    [Fact]
    public void Artillery_NeedsAnArmory()
    {
        Game game = PlainsColony(out Colony colony);
        MoveCheck refused = game.CheckSetBuild(colony, Artillery);
        Assert.False(refused.Allowed);
        Assert.Contains("armory", refused.Reason);
        Assert.DoesNotContain(game.BuildableUnits(colony), u => u.Id == Artillery);

        colony.AddBuilding(Armory);
        Assert.True(game.CheckSetBuild(colony, Artillery).Allowed);
        Assert.Contains(game.BuildableUnits(colony), u => u.Id == Artillery);
    }

    [Fact]
    public void ShipsAndTheFreeColonist_CannotBeBuilt()
    {
        Game game = PlainsColony(out Colony colony);
        // A shipyard *does* grant the naval build scope — yet ships stay excluded this slice (deferred at
        // ResolveBuildable's IsNaval filter), so the refusal isn't merely "no enabling building".
        colony.AddBuilding("model.building.shipyard");
        Assert.False(game.CheckSetBuild(colony, "model.unit.caravel").Allowed);   // naval — deferred even with a shipyard
        Assert.False(game.CheckSetBuild(colony, "model.unit.freeColonist").Allowed); // food-grown, not built
    }

    [Fact]
    public void AUnit_CanBeQueuedAndRebuiltRepeatedly_UnlikeABuilding()
    {
        Game game = PlainsColony(out Colony colony);
        colony.AddBuilding(Armory);
        game.SetBuild(colony, Artillery);
        Assert.True(game.CheckEnqueueBuild(colony, Artillery).Allowed); // the same unit may be queued again (a building can't)
    }

    // ---- Completion ----

    [Fact]
    public void BuildingArtillery_SpendsTheGoods_MustersOnTheTile_KeepsPopulation()
    {
        Game game = PlainsColony(out Colony colony);
        colony.AddBuilding(Armory);
        colony.AddGoods(Hammers, 192);
        colony.AddGoods(Tools, 40);
        game.SetBuild(colony, Artillery);
        int unitsBefore = game.Units.Count;

        game.EndTurn();

        Unit artillery = Assert.Single(game.Units, u => u.Type.Id == Artillery);
        Assert.Equal(colony.Position, artillery.Position);   // musters on the colony tile
        Assert.Equal(colony.OwnerId, artillery.OwnerId);     // owned by the colony's player (the human, 0)
        Assert.Null(artillery.OwnerNationId);                // a colonial unit, not a native
        Assert.Equal(unitsBefore + 1, game.Units.Count);
        Assert.Equal(0, colony.StoreOf(Hammers));            // spent exactly 192 / 40
        Assert.Equal(0, colony.StoreOf(Tools));
        Assert.Equal(1, colony.Population);                  // building a unit costs no colonist
    }

    [Fact]
    public void Construction_IsAllOrNothing_NoMaterialsSpentUntilAffordable()
    {
        Game game = PlainsColony(out Colony colony);
        colony.AddBuilding(Armory);
        colony.AddGoods(Hammers, 191); // one short
        colony.AddGoods(Tools, 40);
        game.SetBuild(colony, Artillery);

        game.EndTurn();
        Assert.DoesNotContain(game.Units, u => u.Type.Id == Artillery);
        Assert.Equal(191, colony.StoreOf(Hammers)); // nothing spent
        Assert.Equal(40, colony.StoreOf(Tools));

        colony.AddGoods(Hammers, 1); // now exactly enough
        game.EndTurn();
        Assert.Single(game.Units, u => u.Type.Id == Artillery);
        Assert.Equal(0, colony.StoreOf(Hammers));
    }

    [Fact]
    public void SpawnUnit_SetsTheGivenColonialOwner()
    {
        // The internal owner-aware spawn that RunConstruction uses: a built unit takes the colony's owner, not 0.
        Game game = PlainsColony(out Colony colony);
        Unit u = game.SpawnUnit(Classic.Unit(Artillery), colony.Position, ownerId: 7);
        Assert.Equal(7, u.OwnerId);
        Assert.Null(u.OwnerNationId);
    }

    [Fact]
    public void AForeignPowersColony_BuildsArtilleryOwnedByThatPower_NotTheHuman()
    {
        // End-to-end through RunConstruction: a unit built in a non-human colony belongs to that power, proving the
        // build path forwards colony.OwnerId (a regression to the hard-coded human owner 0 would fail here).
        Game game = Game.New(Classic, Seed);
        Unit founder = game.PlayerUnits.First(u => u.Type.CanFoundColony);
        Colony colony = game.FoundColony(founder);
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        colony.OwnerId = foreignId; // hand it to a foreign power, as a capture would
        colony.AddBuilding(Armory);
        colony.AddGoods(Hammers, 192);
        colony.AddGoods(Tools, 40);
        game.SetBuild(colony, Artillery);

        game.EndTurn(); // the foreign power's colony turn builds it

        Unit artillery = Assert.Single(game.Units, u => u.Type.Id == Artillery);
        Assert.Equal(foreignId, artillery.OwnerId); // built under the colony's owner, not 0
        Assert.Null(artillery.OwnerNationId);
    }

    // ---- Queue interaction ----

    [Fact]
    public void ALoneQueuedUnit_RepeatsEveryTurn()
    {
        Game game = PlainsColony(out Colony colony);
        colony.AddBuilding(Armory);
        colony.AddGoods(Hammers, 1000);
        colony.AddGoods(Tools, 1000);
        game.SetBuild(colony, Artillery);

        game.EndTurn();
        game.EndTurn();

        Assert.Equal(2, game.Units.Count(u => u.Type.Id == Artillery)); // built one per turn, queue never emptied
        Assert.Equal(Artillery, colony.CurrentBuild);                   // still set — keeps churning them out
    }

    [Fact]
    public void AUnitFollowedByMore_DoesNotRepeat_TheQueueAdvances()
    {
        Game game = PlainsColony(out Colony colony);
        colony.AddBuilding(Armory);
        colony.AddGoods(Hammers, 1000);
        colony.AddGoods(Tools, 1000);
        colony.SetBuildQueue([Artillery, Warehouse]); // a unit at the front, a building behind it

        game.EndTurn(); // builds the artillery, then advances (does NOT repeat, queue size was 2)
        Assert.Single(game.Units, u => u.Type.Id == Artillery);
        Assert.Equal(Warehouse, colony.CurrentBuild);

        game.EndTurn(); // the warehouse now builds
        Assert.True(colony.HasBuilding(Warehouse));
    }

    [Fact]
    public void AQueuedUnitAtTheFront_DoesNotThrow_AndBuildsBeforeABuildingBehindIt()
    {
        // Regression: the old RunConstruction called Ruleset.Building on the front id, which throws on a unit id.
        Game game = PlainsColony(out Colony colony);
        colony.AddGoods(Hammers, 1000);
        colony.SetBuildQueue([WagonTrain, Warehouse]);

        game.EndTurn(); // a unit id at the front must dispatch, not throw
        Assert.Single(game.Units, u => u.Type.Id == WagonTrain);
        Assert.Equal(Warehouse, colony.CurrentBuild);
    }

    // ---- Wagon-train limit (one per colony) ----

    [Fact]
    public void WagonTrains_AreCappedAtOnePerColony()
    {
        Game game = TwoColonyWorld(out Colony first, out var positions);

        Assert.True(game.CheckSetBuild(first, WagonTrain).Allowed);              // 0 wagons < 2 colonies
        game.SpawnUnit(Classic.Unit(WagonTrain), positions.spare);              // now own 1 wagon train
        Assert.True(game.CheckSetBuild(first, WagonTrain).Allowed);              // 1 < 2 → still allowed
        game.SpawnUnit(Classic.Unit(WagonTrain), positions.spare2);            // now own 2 (== 2 colonies)
        Assert.False(game.CheckSetBuild(first, WagonTrain).Allowed);            // 2 < 2 false → capped
    }

    [Fact]
    public void ALoneWagonTrainOrder_RepeatsUntilTheCap_ThenClears()
    {
        // The REMOVE_EXCEPT_LAST repeat meets the one-per-colony cap: a lone wagon-train order builds one, then the
        // cap (1 < 1 is false) makes the next turn skip it and the queue clears — no runaway wagon-train spam.
        Game game = PlainsColony(out Colony colony); // one colony, the free carpenter's house enables wagon trains
        colony.AddGoods(Hammers, 1000);
        game.SetBuild(colony, WagonTrain);

        game.EndTurn(); // builds wagon #1; lone unit so it stays queued to repeat
        Assert.Single(game.Units, u => u.Type.Id == WagonTrain);
        Assert.Equal(WagonTrain, colony.CurrentBuild);

        game.EndTurn(); // now 1 wagon == 1 colony → capped → the affordable-but-unbuildable front is skipped, queue clears
        Assert.Single(game.Units, u => u.Type.Id == WagonTrain); // still exactly one — the cap held
        Assert.Null(colony.CurrentBuild);
        Assert.Equal(960, colony.StoreOf(Hammers)); // the skipped second build spent nothing
    }

    // ---- Persistence (no version bump) ----

    [Fact]
    public void AUnitInTheQueue_RoundTripsThroughSave_WithNoVersionBump()
    {
        Game game = PlainsColony(out Colony colony);
        colony.AddBuilding(Armory);
        colony.SetBuildQueue([Artillery, WagonTrain, Warehouse]);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal([Artillery, WagonTrain, Warehouse], restored.Colonies[0].BuildQueue);
        Assert.Equal(32, SaveGame.CurrentVersion);
    }

    [Fact]
    public void AnEmptyBuildQueue_PersistsNoQueueTokens()
    {
        // Building units rides the existing v24 queue strings — an idle colony adds no new bytes (byte-stable).
        Game game = PlainsColony(out _);
        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("CurrentBuild", json);   // nothing under construction → omitted
        Assert.DoesNotContain("BuildQueueRest", json); // no queued tail → omitted
    }

    [Fact]
    public void BuildingAUnit_DrawsNoRandomness()
    {
        // Two identically-seeded games: one builds artillery this turn, one has its queue cleared. If construction
        // drew from the RNG their RNG state would diverge after EndTurn — it must not (ADR-009).
        Game building = ArmoryArtilleryGame();
        Game idle = ArmoryArtilleryGame();
        idle.SetBuild(idle.Colonies[0], null); // clear the queue → no construction this turn

        building.EndTurn();
        idle.EndTurn();

        Assert.Single(building.Units, u => u.Type.Id == Artillery); // the building game really did build
        SaveGame b = SaveGame.From(building);
        SaveGame i = SaveGame.From(idle);
        Assert.Equal((i.RandomStateValue, i.RandomIncrement), (b.RandomStateValue, b.RandomIncrement));
    }

    // ---- Fixtures ----

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
            Colonies = [new SavedColony(1, "Forge", 0, 0, 1)],
        }.Restore(Classic);
        colony = game.Colonies[0];
        return game;
    }

    /// <summary>A seeded 1×1 colony with an armory and exactly enough goods to build artillery this turn.</summary>
    private static Game ArmoryArtilleryGame()
    {
        Game game = PlainsColony(out Colony colony);
        colony.AddBuilding(Armory);
        colony.AddGoods(Hammers, 192);
        colony.AddGoods(Tools, 40);
        game.SetBuild(colony, Artillery);
        return game;
    }

    /// <summary>Two colonies (cols 0 and 4 of a 5×1 plains strip) plus two spare land tiles to spawn wagon trains on.</summary>
    private static Game TwoColonyWorld(out Colony first, out (Position spare, Position spare2) positions)
    {
        var game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 5,
            MapHeight = 1,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", 5)],
            Units = [],
            Explored = [0, 1, 2, 3, 4],
            Colonies =
            [
                new SavedColony(1, "Alpha", 0, 0, 1),
                new SavedColony(2, "Beta", 4, 0, 1),
            ],
        }.Restore(Classic);
        first = game.Colonies[0];
        positions = (new Position(2, 0), new Position(3, 0));
        return game;
    }
}
