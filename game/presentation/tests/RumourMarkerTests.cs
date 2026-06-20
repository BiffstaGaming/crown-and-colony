using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.World;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the on-map Lost City Rumour marker (`86d3dm9tq`): a marker glyph
/// appears on each explored rumour tile and is fog-gated (none on unexplored tiles). Presentation-only (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RumourMarkerTests
{
    [TestCase]
    public async Task RumourMarker_ShowsOnExploredRumourTiles_NotUnexploredOnes()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);

        Position explored = game.Explored.First();
        Position hidden = game.Map.AllPositions().First(p => !game.IsExplored(p));
        AddRumour(game.Map, explored);
        AddRumour(game.Map, hidden);
        Refresh(controller);
        await runner.SimulateFrames(1);

        var markers = controller.GetNode<Node2D>("MapView/RumourLayer")
            .GetChildren().OfType<RumourMarker>().ToList();
        AssertThat(markers.Any(m => m.Position == MapView.TileCentre(explored))).IsTrue();  // explored → marked
        AssertThat(markers.Any(m => m.Position == MapView.TileCentre(hidden))).IsFalse();   // fogged → hidden
    }

    private static void AddRumour(GameMap map, Position p) =>
        typeof(GameMap).GetMethod("AddRumour", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(map, [p]);

    private static void Refresh(GameController controller) =>
        typeof(GameController).GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
