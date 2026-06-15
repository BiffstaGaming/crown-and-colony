using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
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

        // Click the unit's tile: it becomes selected (exactly one marker in the unit layer is selected).
        await ClickTile(runner, controller, start);
        int selected = controller.GetNode<Node2D>("MapView/UnitLayer").GetChildren().OfType<UnitMarker>().Count(m => m.Selected);
        AssertThat(selected).IsEqual(1);

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
        // No unit marker remains: the human has no on-map unit, and no rival/brave is in sight near the new colony.
        AssertThat(controller.GetNode<Node2D>("MapView/UnitLayer").GetChildCount()).IsEqual(0);
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

    [TestCase(Timeout = 60000)]
    public async Task NativeRaid_DuringEndTurn_ShowsANoticeInTheStatusBar()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Plant a brave adjacent to the human's starting unit and enrage every native nation, so when the turn
        // ends the brave raids the human — exercising the AI-combat → CombatNotice → status-bar feedback path.
        Position humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position bravePos = humanPos.Neighbours().First(n => Free(game, n));
        string nation = game.NativeSettlements.First().NationTypeId;
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), bravePos, nation);
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm);
        }

        // End the turn via the button (the notice path lives in OnEndTurnPressed, not Game.EndTurn).
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        var label = controller.GetNode<Label>("UI/StatusLabel");
        AssertThat(label.Text.ToLower()).Contains("raid"); // "raided" (native won) or "fought off … raid" (defended)
    }

    [TestCase(Timeout = 60000)]
    public async Task MultipleOwnUnits_AllRender()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Spawn a second human-owned unit next to the first, then refresh: the unit layer draws both (the old
        // single-marker HUD only ever showed the first).
        Position humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position spot = humanPos.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), spot);

        await ClickTile(runner, controller, humanPos); // forces a RefreshView (and selects the first unit)

        AssertThat(controller.GetNode<Node2D>("MapView/UnitLayer").GetChildCount()).IsEqual(2);
    }

    [TestCase(Timeout = 60000)]
    public async Task NonHumanUnit_RendersOnlyWhenInSight_WithAnOwnerRing()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // A brave adjacent to the human is in sight → drawn (with a non-transparent owner ring); the braves that
        // spawned far away near their settlements are out of sight → not drawn. So exactly two markers: the
        // human's own (no ring) and the one visible brave (ring).
        Position humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        string nation = game.NativeSettlements.First().NationTypeId;
        Position nearTile = humanPos.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), nearTile, nation);

        await ClickTile(runner, controller, humanPos); // refresh

        var markers = controller.GetNode<Node2D>("MapView/UnitLayer").GetChildren().OfType<UnitMarker>().ToList();
        AssertThat(markers.Count).IsEqual(2); // the human + the in-sight brave; far braves are not drawn
        AssertThat(markers.Any(m => m.Position == MapView.TileCentre(nearTile) && m.OwnerColor.A > 0f)).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    public async Task ForeignUnit_RendersWithItsNationColour()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Inject a foreign colonial unit next to the human via the save layer (the presentation project can't
        // set Unit.OwnerId directly), reload it into the controller, refresh: it must render with its NATION's
        // colour ring — exercising OwnerColorOf → NormalizeHex → Color.FromString, which the brave tests skip.
        Position humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position spot = humanPos.Neighbours().First(n => Free(game, n));
        SaveGame save = SaveGame.From(game);
        var rival = save.Players!.First(p => !p.IsHuman && p.PlayerType == (int)PlayerType.Colonial);
        int uid = game.Units.Max(u => u.Id) + 1;
        var foreignUnit = new SavedUnit(uid, "model.unit.freeColonist", spot.X, spot.Y, 0, OwnerId: rival.PlayerId);
        Game injected = (save with { Units = save.Units.Append(foreignUnit).ToList() }).Restore(game.Ruleset);
        SetGame(controller, injected);

        runner.SimulateKeyPressed(Key.F5); // refresh (no turn advance, no AI movement)
        await runner.SimulateFrames(2);

        UnitMarker marker = controller.GetNode<Node2D>("MapView/UnitLayer").GetChildren().OfType<UnitMarker>()
            .First(m => m.Position == MapView.TileCentre(spot));
        string nationId = injected.Players.First(p => p.PlayerId == rival.PlayerId).NationId!;
        string hex = injected.Ruleset.EuropeanNations.First(n => n.Id == nationId).Color!;
        string bare = hex.Length >= 2 && hex[0] == '0' && (hex[1] == 'x' || hex[1] == 'X') ? hex[2..] : hex.TrimStart('#');
        Color expected = Color.FromString("#" + bare, Colors.Magenta);
        AssertThat(marker.OwnerColor).IsEqual(expected); // the nation colour — not the fallback red, not the native constant
    }

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, game);

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
