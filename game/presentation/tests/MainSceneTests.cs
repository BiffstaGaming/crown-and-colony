using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md): drive the real main scene inside the
/// Godot runtime and assert the UI wiring reaches the game logic.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MainSceneTests
{
    [TestCase]
    public async Task MainScene_Loads_AndShowsTurn1()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var label = runner.Scene().GetNode<Label>("UI/StatusLabel");
        AssertThat(label.Text).Contains("Turn 1");
    }

    [TestCase]
    public async Task EndTurnButton_AdvancesTurn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var button = runner.Scene().GetNode<Button>("UI/EndTurnButton");
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var label = runner.Scene().GetNode<Label>("UI/StatusLabel");
        AssertThat(label.Text).Contains("Turn 2");
    }

    [TestCase]
    public async Task CalendarHud_ShowsTheDate_AndAdvancesWithTheTurn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var calendar = runner.Scene().GetNode<Label>("UI/CalendarPanel/CalendarLabel");
        // Turn 1 is 1492 in the classic calendar (a bare year — seasons begin at 1600), shown in the
        // dedicated HUD readout, not just the dev status string.
        AssertThat(calendar.Text).IsEqual("1492");

        runner.Scene().GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(calendar.Text).IsEqual("1493"); // the readout follows Game.CalendarLabel as turns pass
    }

    [TestCase]
    public async Task ColonyPanel_OpensWithColonyDetails_AndCloses()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Found a colony through the game API, then open its panel.
        var game = (CrownAndColony.GameLogic.GameSession.Game)controller
            .GetType()
            .GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(controller)!;
        var colony = game.FoundColony(game.Units[0]);
        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/ColonyPanel/VBox/ColonyTitle").Text)
            .IsEqual(colony.Name);
        AssertThat(controller.GetNode<Label>("UI/ColonyPanel/VBox/ColonyInfo").Text)
            .Contains("Population: 1");

        controller.GetNode<Button>("UI/ColonyPanel/VBox/CloseButton")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase]
    public async Task NewGame_AppliesAChosenNonDefaultOptionSet_ToTheStartedGame()
    {
        // L3 (86d3e4bu0): the NewGameDialog forwards the player's option picks through the GameController.Pending*
        // statics; this drives the REAL consume-and-apply path (the private NewGame()) and asserts a NON-DEFAULT set
        // reaches the started game's ruleset — the new-game options actually take effect, not just get collected.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // A deliberately non-default option set, every value flipped away from the byte-identical default:
        //   variant   = Classic (explicit — only shipped variant; still exercises the PendingVariant → _variant seam)
        //   victory   = REF off / Europeans off / Humans ON (default is on / on / off)
        //   fog of war = OFF        (default on)
        //   custom-house smuggling = OFF (default on)
        GameController.PendingVariant = GameVariants.ClassicAmerica;
        GameController.PendingVictoryConditions = (DefeatRef: false, DefeatEuropeans: false, DefeatHumans: true);
        GameController.PendingFogOfWar = false;
        GameController.PendingCustomIgnoreBoycott = false;

        // Invoke the production NewGame() (private — the same method the N hotkey / Game Over → New Game button call),
        // which consumes the statics, applies them to the freshly-loaded ruleset, and starts the game.
        typeof(GameController)
            .GetMethod("NewGame", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);
        await runner.SimulateFrames(1);

        var game = (CrownAndColony.GameLogic.GameSession.Game)typeof(GameController)
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

        // The chosen option set is reflected on the started game's ruleset (the win checks + the game-option bundle).
        AssertThat(game.Ruleset.VictoryDefeatRef).IsFalse();
        AssertThat(game.Ruleset.VictoryDefeatEuropeans).IsFalse();
        AssertThat(game.Ruleset.VictoryDefeatHumans).IsTrue();
        AssertThat(game.Ruleset.GameOptions.FogOfWar).IsFalse();
        AssertThat(game.Ruleset.GameOptions.CustomIgnoreBoycott).IsFalse();

        // The statics were consumed (cleared) so a later default new game isn't surprised by a stale pick.
        AssertThat(GameController.PendingVictoryConditions).IsNull();
        AssertThat(GameController.PendingFogOfWar).IsNull();
        AssertThat(GameController.PendingCustomIgnoreBoycott).IsNull();
        AssertThat(GameController.PendingVariant).IsNull();
    }

    [TestCase]
    public async Task NewGame_WithNoOptionPicks_StartsTheByteIdenticalDefault()
    {
        // The companion to the non-default test: with NO Pending* picks, the started game's ruleset carries the spec
        // defaults (REF on / Europeans on / Humans off / fog on / smuggling on) — proving the dialog's defaults map to
        // the byte-identical default game (ADR-009).
        GameController.PendingVariant = null;
        GameController.PendingVictoryConditions = null;
        GameController.PendingFogOfWar = null;
        GameController.PendingCustomIgnoreBoycott = null;

        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        var game = (CrownAndColony.GameLogic.GameSession.Game)typeof(GameController)
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

        AssertThat(game.Ruleset.VictoryDefeatRef).IsTrue();
        AssertThat(game.Ruleset.VictoryDefeatEuropeans).IsTrue();
        AssertThat(game.Ruleset.VictoryDefeatHumans).IsFalse();
        AssertThat(game.Ruleset.GameOptions.FogOfWar).IsTrue();
        AssertThat(game.Ruleset.GameOptions.CustomIgnoreBoycott).IsTrue();
    }

    [TestCase]
    public async Task UnitMarker_SitsOnUnitTile()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var controller = (GameController)runner.Scene();
        var layer = controller.GetNode<Node2D>("MapView/UnitLayer");
        int count = layer.GetChildCount();
        AssertThat(count).IsEqual(3); // turn 1: the human's starting roster (pioneer + soldier + caravel), no rivals/braves in sight

        // Every marker must sit at the centre of some diamond: isometric centres land on multiples of half the tile width/height.
        for (int i = 0; i < count; i++)
        {
            Vector2 pos = ((UnitMarker)layer.GetChild(i)).Position;
            AssertThat(pos.X % (MapView.TileW / 2f)).IsEqual(0f);
            AssertThat(pos.Y % (MapView.TileH / 2f)).IsEqual(0f);
        }
    }
}
