using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Units;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 tests for the trade-route management screen (86d3c9rrd): creating a route from two colonies, assigning a
/// carrier to it, and deleting it — all via the real UI controls, driving the shipped trade-route GameLogic.
/// State is injected (a constructed <see cref="Game"/>) so the seam is exercised deterministically.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TradeRoutePanelTests
{
    [TestCase(Timeout = 60000)]
    public async Task CreateRouteButton_CreatesARoute()
    {
        (ISceneRunner runner, GameController controller, Game game) = await OpenTradeRoutes(TwoColonies());
        AssertThat(game.HumanPlayer.TradeRoutes.Count).IsEqual(0);

        Button create = FindButton(controller, "CreateRoute")!;
        AssertThat(create).IsNotNull();
        create.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.HumanPlayer.TradeRoutes.Count).IsEqual(1); // Alpha → Beta (the default ends)
    }

    [TestCase(Timeout = 60000)]
    public async Task AssignDropdown_AssignsACarrierToARoute()
    {
        (ISceneRunner runner, GameController controller, Game game) = await OpenTradeRoutes(TwoColonies(), PreCreateRoute);
        int routeId = game.HumanPlayer.TradeRoutes[0].Id;
        Unit wagon = game.Units.First(u => u.Type.IsCarrier);

        OptionButton assign = FindOption(controller, $"Assign_{routeId}")!;
        AssertThat(assign).IsNotNull();
        assign.EmitSignal(OptionButton.SignalName.ItemSelected, 1L); // the first carrier
        await runner.SimulateFrames(1);

        AssertThat(wagon.TradeRouteId == routeId).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    public async Task DeleteButton_RemovesARoute()
    {
        (ISceneRunner runner, GameController controller, Game game) = await OpenTradeRoutes(TwoColonies(), PreCreateRoute);
        int routeId = game.HumanPlayer.TradeRoutes[0].Id;

        Button del = FindButton(controller, $"Delete_{routeId}")!;
        AssertThat(del).IsNotNull();
        del.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.HumanPlayer.TradeRoutes.Count).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task CreateRoute_WithEuropeAsTheToStop_BuildsAColonyToEuropeRoute()
    {
        (ISceneRunner runner, GameController controller, Game game) = await OpenTradeRoutes(CoastalPort());
        AssertThat(game.HumanPlayer.TradeRoutes.Count).IsEqual(0);

        // Pick Europe as the "To" stop (it's the item after the single colony → index 1) and load sugar at the From colony.
        OptionButton to = FindOption(controller, "ToColony")!;
        AssertThat(to).IsNotNull();
        int europeIndex = to.ItemCount - 1; // Europe is appended last
        AssertThat(to.GetItemText(europeIndex)).IsEqual("Europe");
        to.Select(europeIndex);
        to.EmitSignal(OptionButton.SignalName.ItemSelected, (long)europeIndex);
        await runner.SimulateFrames(1);

        Button create = FindButton(controller, "CreateRoute")!;
        AssertThat(create).IsNotNull();
        create.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.HumanPlayer.TradeRoutes.Count).IsEqual(1);
        TradeRoute route = game.HumanPlayer.TradeRoutes[0];
        AssertThat(route.Stops.Count).IsEqual(2);
        AssertThat(route.Stops[1].IsEurope).IsTrue(); // the second stop is Europe (sentinel ColonyId 0)

        // Assign the galleon to the route through the per-route dropdown.
        int routeId = route.Id;
        Unit galleon = game.Units.First(u => u.Type.IsCarrier);
        OptionButton assign = FindOption(controller, $"Assign_{routeId}")!;
        AssertThat(assign).IsNotNull();
        assign.EmitSignal(OptionButton.SignalName.ItemSelected, 1L); // the first (only) carrier
        await runner.SimulateFrames(1);
        AssertThat(galleon.TradeRouteId == routeId).IsTrue();

        // The route reads back as "… → Europe" in the panel's route label.
        Label routeLabel = Panel(controller).FindChildren("*", recursive: true, owned: false)
            .OfType<Label>().First(l => l.Text.Contains("→"));
        AssertThat(routeLabel.Text).Contains("→ Europe");
    }

    private static void PreCreateRoute(Game g)
    {
        Colony a = g.Colonies[0];
        Colony b = g.Colonies[1];
        g.CreateTradeRoute(g.HumanPlayer, "R", [new TradeRouteStop(a.Id, []), new TradeRouteStop(b.Id, [])]);
    }

    /// <summary>A 3×1 coastal strip — port colony Alpha at (0,0), ocean, high seas — with a human galleon beside the port.</summary>
    private static SaveGame CoastalPort() => new()
    {
        Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
        MapWidth = 3, MapHeight = 1,
        Terrain = ["model.tile.plains", "model.tile.ocean", "model.tile.highSeas"],
        Units = [new SavedUnit(1, "model.unit.galleon", 1, 0, 18)],
        Explored = [0, 1, 2],
        Colonies = [new SavedColony(1, "Alpha", 0, 0, 1)],
    };

    /// <summary>A 3-tile plains strip with two human colonies (Alpha, Beta) and a human wagon train.</summary>
    private static SaveGame TwoColonies() => new()
    {
        Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
        MapWidth = 3, MapHeight = 1,
        Terrain = ["model.tile.plains", "model.tile.plains", "model.tile.plains"],
        Units = [new SavedUnit(1, "model.unit.wagonTrain", 0, 0, 12)],
        Explored = [0, 1, 2],
        Colonies = [new SavedColony(1, "Alpha", 0, 0, 1), new SavedColony(2, "Beta", 2, 0, 1)],
    };

    private static async Task<(ISceneRunner, GameController, Game)> OpenTradeRoutes(SaveGame state, Action<Game>? setup = null)
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);

        Game game = state.Restore(GameLogic.Specification.Ruleset.LoadClassic());
        setup?.Invoke(game);
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, game);
        controller.OpenTradeRoutePanel();
        await runner.SimulateFrames(1);
        return (runner, controller, game);
    }

    private static Button? FindButton(GameController controller, string namePrefix) =>
        Panel(controller).FindChildren("*", recursive: true, owned: false)
            .OfType<Button>()
            .FirstOrDefault(b => b.Name.ToString().StartsWith(namePrefix));

    private static OptionButton? FindOption(GameController controller, string name) =>
        Panel(controller).FindChild(name, recursive: true, owned: false) as OptionButton;

    private static PanelContainer Panel(GameController controller) =>
        controller.GetNode<PanelContainer>("UI/TradeRoutePanel");
}
