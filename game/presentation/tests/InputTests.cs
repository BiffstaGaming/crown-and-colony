using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 input tests (docs/TESTING.md): simulated mouse/keyboard driving the real
/// scene — closes the "click-to-move and hotkeys untested" debt from the
/// units-movement and save-load system docs.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InputTests
{
    private const ulong Seed = 424242;

    [TestCase(Timeout = 60000)]
    public async Task ClickUnit_Selects_ThenClickAdjacentTile_Moves()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        Game game = GameOf(controller);
        Unit unit = game.Units[0];
        Position start = unit.Position;

        // Click the unit's tile: it becomes selected.
        await ClickTile(runner, controller, start);
        var marker = controller.GetNode<UnitMarker>("MapView/UnitMarker");
        AssertThat(marker.Selected).IsTrue();

        // Click a legal neighbouring tile: the unit walks there.
        Position target = start.Neighbours().First(n => game.CheckMove(unit, n).Allowed);
        await ClickTile(runner, controller, target);
        AssertThat(unit.Position).IsEqual(target);
    }

    [TestCase(Timeout = 60000)]
    public async Task PressingN_StartsAFreshGame()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game before = GameOf(controller);
        before.EndTurn();
        AssertThat(before.Turn).IsEqual(2);

        runner.SimulateKeyPressed(Key.N);
        await runner.SimulateFrames(2);

        Game after = GameOf(controller);
        AssertThat(ReferenceEquals(before, after)).IsFalse();
        AssertThat(after.Turn).IsEqual(1);
    }

    [TestCase(Timeout = 60000)]
    public async Task PressingB_WithSelectedUnit_FoundsColony()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        await ClickTile(runner, controller, game.Units[0].Position); // select
        runner.SimulateKeyPressed(Key.B);
        await runner.SimulateFrames(2);

        AssertThat(game.Colonies.Count).IsEqual(1);
        AssertThat(game.PlayerUnits.Count()).IsEqual(0); // founder settled (native braves remain)
        // The HUD unit marker must hide — it must never show a native brave as the player's unit.
        AssertThat(controller.GetNode<UnitMarker>("MapView/UnitMarker").Visible).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    public async Task QuickSaveF5_ThenF9_RestoresTheTurn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        runner.SimulateKeyPressed(Key.F5); // save at turn 1
        await runner.SimulateFrames(2);
        GameOf(controller).EndTurn();
        GameOf(controller).EndTurn();
        AssertThat(GameOf(controller).Turn).IsEqual(3);

        runner.SimulateKeyPressed(Key.F9); // load
        await runner.SimulateFrames(2);
        AssertThat(GameOf(controller).Turn).IsEqual(1);
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickingAnEnemy_WithSelectedUnit_Attacks()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Plant a native brave next to the human's start (on-screen for the click) and a human artillery
        // (offence 7, no role needed) adjacent to it, then click the brave to attack.
        string nation = game.Units.First(u => u.IsNative).OwnerNationId!;
        Position human = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position bravePos = human.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), bravePos, nation);
        Position artPos = bravePos.Neighbours().First(n => n != human && Free(game, n));
        Unit artillery = game.SpawnUnit(game.Ruleset.Unit("model.unit.artillery"), artPos);
        int artId = artillery.Id;

        await ClickTile(runner, controller, artPos);    // select the artillery
        await ClickTile(runner, controller, bravePos);  // click the brave → attack (not a move)

        // The attack resolved: the attacker spent its turn — it's gone (slain/demoted-away) or present with
        // 0 movement. A rejected move would have left it on its tile with full movement.
        Unit? after = game.Units.FirstOrDefault(u => u.Id == artId);
        AssertThat(after == null || after.MovementLeft == 0).IsTrue();
    }

    private static bool Free(Game game, Position n) =>
        game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
        && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
        && !game.Units.Any(u => u.IsOnMap && u.Position == n);

    /// <summary>Left-clicks the window position corresponding to a map tile (camera-aware, zoom 1).</summary>
    private static async Task ClickTile(ISceneRunner runner, GameController controller, Position tile)
    {
        var camera = controller.GetNode<Camera2D>("Camera");
        Vector2 viewportSize = controller.GetViewport().GetVisibleRect().Size;
        Vector2 screen = MapView.TileCentre(tile) - camera.Position + viewportSize / 2f;

        runner.SetMousePos(screen);
        await runner.SimulateFrames(1);
        runner.SimulateMouseButtonPressed(MouseButton.Left);
        await runner.SimulateFrames(2);
    }

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
