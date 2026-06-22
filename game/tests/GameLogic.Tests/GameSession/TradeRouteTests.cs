using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Trade routes (86d3c9rq1): a player defines a named ring of colony stops (each listing goods to load), assigns a
/// carrier to it, and the carrier auto-hauls each turn — delivering what a stop doesn't want, loading what it does,
/// then moving to the next stop. Reuses the carrier-haulage seam (Load/UnloadToColony). Save v43, omit-when-default.
/// </summary>
public class TradeRouteTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Sugar = "model.goods.sugar";

    /// <summary>A 3-tile plains strip: colony Alpha (0,0) holding 100 sugar, colony Beta (2,0), a human wagon train on Alpha.</summary>
    private static Game TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.plains", "model.tile.plains"],
            Units = [new SavedUnit(1, "model.unit.wagonTrain", 0, 0, 12)],
            Explored = [0, 1, 2],
            Colonies =
            [
                new SavedColony(1, "Alpha", 0, 0, 1, new Dictionary<string, int> { [Sugar] = 100 }),
                new SavedColony(2, "Beta", 2, 0, 1),
            ],
        };
        Game game = save.Restore(Classic);
        wagon = game.Units[0];
        alpha = game.Colonies[0];
        beta = game.Colonies[1];
        return game;
    }

    [Fact]
    public void CreateTradeRoute_RejectsAStopThatIsNotThePlayersColony()
    {
        Game game = TwoColonyStrip(out _, out Colony alpha, out _);
        Assert.Throws<InvalidMoveException>(() => game.CreateTradeRoute(game.HumanPlayer, "Bogus",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(999, [])])); // 999 is no colony
    }

    [Fact]
    public void AssignTradeRoute_IsCarrierOnly()
    {
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]);

        Unit colonist = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), new Position(1, 0)); // space 0 → not a carrier
        Assert.False(game.CheckAssignTradeRoute(colonist, route.Id).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.AssignTradeRoute(colonist, route.Id));

        game.AssignTradeRoute(wagon, route.Id); // a wagon train is a carrier
        Assert.Equal(route.Id, wagon.TradeRouteId);
        game.ClearTradeRoute(wagon);
        Assert.Null(wagon.TradeRouteId);
    }

    [Fact]
    public void AnAssignedCarrier_HaulsGoodsAlongItsRoute()
    {
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Sugar run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]); // load at Alpha, deliver at Beta
        game.AssignTradeRoute(wagon, route.Id);

        for (int turn = 0; turn < 5; turn++)
        {
            game.EndTurn();
        }

        Assert.True(beta.StoreOf(Sugar) > 0, "the carrier should have hauled sugar from Alpha to Beta");
        Assert.Equal(0, alpha.StoreOf(Sugar)); // picked up at the source
    }

    [Fact]
    public void ARouteLessGame_OmitsTheV43Tokens_AndStaysCurrent()
    {
        string json = SaveGame.From(Game.New(Classic, seed: 7)).ToJson();
        Assert.DoesNotContain("\"TradeRoutes\"", json);
        Assert.DoesNotContain("\"TradeRouteId\"", json);
        Assert.DoesNotContain("\"TradeRouteStop\"", json);
        Assert.DoesNotContain("NextTradeRouteId", json); // omit-when-default (counter still 1) → byte-identical to v44
        Assert.Equal(55, SaveGame.CurrentVersion);
    }

    [Fact]
    public void Routes_AndAssignment_SurviveSaveRoundTrip()
    {
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Sugar run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]);
        game.AssignTradeRoute(wagon, route.Id);
        game.EndTurn(); // serves Alpha + advances the stop index to 1

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        TradeRoute restored = Assert.Single(loaded.HumanPlayer.TradeRoutes);
        Assert.Equal("Sugar run", restored.Name);
        Assert.Equal(2, restored.Stops.Count);
        Assert.Equal([Sugar], restored.Stops[0].LoadGoodsIds);
        Unit loadedWagon = loaded.Units.Single(u => u.Id == wagon.Id);
        Assert.Equal(route.Id, loadedWagon.TradeRouteId);
        Assert.Equal(wagon.TradeRouteStopIndex, loadedWagon.TradeRouteStopIndex); // mid-route position preserved
    }

    [Fact]
    public void RemoveTradeRoute_DeletesTheRoute_AndUnassignsItsCarrier()
    {
        Game game = TwoColonyStrip(out Unit wagon, out Colony alpha, out Colony beta);
        TradeRoute route = game.CreateTradeRoute(game.HumanPlayer, "Run",
            [new TradeRouteStop(alpha.Id, [Sugar]), new TradeRouteStop(beta.Id, [])]);
        game.AssignTradeRoute(wagon, route.Id);

        game.RemoveTradeRoute(game.HumanPlayer, route.Id);

        Assert.Empty(game.HumanPlayer.TradeRoutes); // the route is gone
        Assert.Null(wagon.TradeRouteId);            // its carrier was un-assigned
        game.RemoveTradeRoute(game.HumanPlayer, 999); // unknown route → no-op, no throw
    }

    [Fact]
    public void NextTradeRouteId_SurvivesSaveLoad_SoIdsAreNotReusedAfterDeleting()
    {
        Game game = TwoColonyStrip(out _, out Colony alpha, out Colony beta);
        List<TradeRouteStop> Stops() => [new(alpha.Id, [Sugar]), new(beta.Id, [])];
        game.CreateTradeRoute(game.HumanPlayer, "A", Stops()); // id 1
        game.CreateTradeRoute(game.HumanPlayer, "B", Stops()); // id 2
        TradeRoute c = game.CreateTradeRoute(game.HumanPlayer, "C", Stops()); // id 3 → counter now 4
        game.RemoveTradeRoute(game.HumanPlayer, c.Id);         // delete the highest id

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        // Pre-fix the counter re-derived to max(1,2)+1 = 3 and the next route REUSED id 3; now it's preserved at 4.
        TradeRoute next = loaded.CreateTradeRoute(loaded.HumanPlayer, "D", Stops());
        Assert.Equal(4, next.Id);
        Assert.Equal([1, 2, 4], loaded.HumanPlayer.TradeRoutes.Select(r => r.Id)); // A, B, D — all distinct, no reuse
    }

    [Fact]
    public void APreV45Save_WithoutTheCounter_FallsBackToMaxIdPlusOne()
    {
        Game game = TwoColonyStrip(out _, out Colony alpha, out Colony beta);
        List<TradeRouteStop> Stops() => [new(alpha.Id, [Sugar]), new(beta.Id, [])];
        game.CreateTradeRoute(game.HumanPlayer, "A", Stops()); // id 1
        game.CreateTradeRoute(game.HumanPlayer, "B", Stops()); // id 2

        // Simulate a pre-v45 save: the NextTradeRouteId field is absent (null) on every player.
        SaveGame save = SaveGame.From(game);
        save = save with { Players = save.Players!.Select(p => p with { NextTradeRouteId = null }).ToList() };
        Game loaded = save.Restore(Classic);

        // No persisted counter → fall back to max(1,2)+1 = 3 (the legacy behaviour, preserved for old saves).
        Assert.Equal(3, loaded.CreateTradeRoute(loaded.HumanPlayer, "C", Stops()).Id);
    }
}
