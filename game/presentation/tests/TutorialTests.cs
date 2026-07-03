using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the guided-intro <b>tutorial</b> (ClickUp <c>86d3fq1h9</c>): the tip card
/// shows the first step on a fresh game, advances when a step's goal is met, hides when the client preference is off, and
/// "Skip tutorial" both hides the card and persists the preference off. Driven through the real <c>main.tscn</c> +
/// <see cref="GameController"/> like <see cref="InputTests"/>, plus a couple of direct <see cref="TutorialService"/> /
/// <see cref="TutorialPanel"/> cases. Presentation-only (ADR-006): the service only reads game state.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialTests
{
    private const ulong Seed = 424242;

    private static TutorialPanel TutorialPanelOf(GameController controller) =>
        controller.GetNode<TutorialPanel>("UI/TutorialPanel");

    private static SettingsService Settings(GameController controller) =>
        controller.GetNodeOrNull<SettingsService>("/root/Settings")!;

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;

    // ── Panel-level cases (drive the code-built card directly, like AdvisorPanelTests) ──────────────────────────────

    [TestCase]
    public async Task ShowStep_RendersTheStepsTitleAndBody_AndReveals()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn"); // any Control host will do
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        var panel = new TutorialPanel();
        host.AddChild(panel); // runs _Ready, building the node tree
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse(); // hidden until handed a step

        var step = TutorialService.DefaultSteps()[0];
        panel.ShowStep(step);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsTrue();
        AssertThat(panel.CurrentKind).IsEqual(TutorialStepKind.Welcome);
        AssertThat(panel.GetNode<Label>($"VBox/{TutorialPanel.TitleName}").Text).IsEqual(step.Title);
        AssertThat(panel.GetNode<Label>($"VBox/{TutorialPanel.BodyName}").Text).IsEqual(step.Body);

        panel.QueueFree();
    }

    [TestCase]
    public async Task GotItAndSkipButtons_RaiseTheirSignals()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        var panel = new TutorialPanel();
        host.AddChild(panel);
        panel.ShowStep(TutorialService.DefaultSteps()[0]);
        await runner.SimulateFrames(1);

        bool gotIt = false, skipped = false;
        panel.GotIt += () => gotIt = true;
        panel.SkipRequested += () => skipped = true;

        panel.GetNode<Button>($"VBox/Buttons/{TutorialPanel.GotItButtonName}").EmitSignal(BaseButton.SignalName.Pressed);
        panel.GetNode<Button>($"VBox/Buttons/{TutorialPanel.SkipButtonName}").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(gotIt).IsTrue();
        AssertThat(skipped).IsTrue();

        panel.QueueFree();
    }

    // ── Service-level cases (pure step logic over the live game) ─────────────────────────────────────────────────────

    [TestCase(Timeout = 60000)]
    public async Task Service_AdvancesThroughTheSequence_AsGoalsAreMet_AndNeverRewinds()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        var service = new TutorialService();

        // The classic start has land colonists on the map, so the Welcome + MoveAshore goals (a land unit is on the map)
        // are already met → the first shown step is FoundColony.
        AssertThat(HasOnMapLandUnit(game)).IsTrue();
        AssertThat(service.Evaluate(game)!.Kind).IsEqual(TutorialStepKind.FoundColony);

        // Found a colony → the FoundColony goal is met; the next step is OpenColony (advances on a colony-open action).
        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval && u.Type.CanFoundColony);
        game.FoundColony(founder);
        AssertThat(service.Evaluate(game)!.Kind).IsEqual(TutorialStepKind.OpenColony);

        // Opening a colony advances to the closing End-Turn step…
        service.NotifyColonyOpened();
        AssertThat(service.Evaluate(game)!.Kind).IsEqual(TutorialStepKind.EndTurn);

        // …and ending a turn completes the tutorial (nothing more to show, and it never rewinds).
        service.NotifyTurnEnded();
        AssertThat(service.Evaluate(game)).IsNull();
        AssertThat(service.IsComplete).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    public async Task Service_DismissCurrent_AdvancesPastAStepEvenWhenItsGoalIsUnmet()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        var service = new TutorialService();
        AssertThat(service.Evaluate(game)!.Kind).IsEqual(TutorialStepKind.FoundColony); // no colony yet

        // "Got it" on the found-colony step advances even though no colony was founded → the OpenColony step.
        service.DismissCurrent();
        AssertThat(service.Evaluate(game)!.Kind).IsEqual(TutorialStepKind.OpenColony);
    }

    // ── Wired-through cases (the controller + panel + setting together) ──────────────────────────────────────────────

    [TestCase(Timeout = 60000)]
    public async Task OnStart_TheTutorialCardShows_TheFirstApplicableStep()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        Settings(controller).SetTutorialHints(true); // ensure enabled (the autoload is shared across the suite)
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        TutorialPanel panel = TutorialPanelOf(controller);
        AssertThat(panel.Visible).IsTrue(); // a fresh game shows the guided-intro card
        // The classic start has land units on the map, so the first *applicable* step is FoundColony (welcome/ashore met).
        AssertThat(panel.CurrentKind).IsEqual(TutorialStepKind.FoundColony);
    }

    [TestCase(Timeout = 60000)]
    public async Task FoundingAColony_AdvancesTheCard_FromFoundColonyToOpenColony()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        Settings(controller).SetTutorialHints(true);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        TutorialPanel panel = TutorialPanelOf(controller);
        AssertThat(panel.CurrentKind).IsEqual(TutorialStepKind.FoundColony);

        // Found a colony, then drive a refresh through the public End-Turn button path (RefreshView re-evaluates).
        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval && u.Type.CanFoundColony);
        game.FoundColony(founder);
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        // The found-colony goal is met → the card advanced. (End Turn also fired NotifyTurnEnded, but that only affects
        // the later End-Turn step; OpenColony still needs a colony-open action, so the card sits on OpenColony.)
        AssertThat(panel.Visible).IsTrue();
        AssertThat(panel.CurrentKind).IsEqual(TutorialStepKind.OpenColony);
    }

    [TestCase(Timeout = 60000)]
    public async Task OpeningAColony_AdvancesTheCard_ToTheEndTurnStep()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        Settings(controller).SetTutorialHints(true);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval && u.Type.CanFoundColony);
        Colony colony = game.FoundColony(founder);

        // Open the colony via the public entry point (fires NotifyColonyOpened), then close it so the card is not hidden
        // behind the full-screen panel, and refresh.
        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);
        controller.GetNode<PanelContainer>("UI/ColonyPanel").Hide();
        await runner.SimulateFrames(2);

        TutorialPanel panel = TutorialPanelOf(controller);
        AssertThat(panel.Visible).IsTrue();
        AssertThat(panel.CurrentKind).IsEqual(TutorialStepKind.EndTurn);
    }

    [TestCase(Timeout = 60000)]
    public async Task DisabledPreference_ShowsNothing()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        Settings(controller).SetTutorialHints(false); // turn the tutorial off before the game starts
        try
        {
            controller.StartNewGame(Seed);
            await runner.SimulateFrames(2);

            AssertThat(TutorialPanelOf(controller).Visible).IsFalse(); // nothing shows while disabled
        }
        finally
        {
            Settings(controller).SetTutorialHints(true); // leave the shared autoload enabled for other cases
        }
    }

    [TestCase(Timeout = 60000)]
    public async Task SkipButton_HidesTheCard_AndPersistsThePreferenceOff()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        SettingsService settings = Settings(controller);
        settings.SetTutorialHints(true);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        TutorialPanel panel = TutorialPanelOf(controller);
        AssertThat(panel.Visible).IsTrue();

        try
        {
            panel.GetNode<Button>($"VBox/Buttons/{TutorialPanel.SkipButtonName}").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(2);

            AssertThat(panel.Visible).IsFalse();          // the card hid
            AssertThat(settings.TutorialHints).IsFalse(); // and the preference flipped off (Skip persists it)

            // Persisted: a fresh service loading from disk reads the off state back.
            var reader = new SettingsService();
            controller.AddChild(reader);
            await runner.SimulateFrames(1);
            AssertThat(reader.TutorialHints).IsFalse();
            reader.QueueFree();
        }
        finally
        {
            settings.SetTutorialHints(true);
            settings.Save(); // restore the default for other suites reading the shared settings.cfg
        }
    }

    // ── Settings-screen toggle ───────────────────────────────────────────────────────────────────────────────────────

    [TestCase(Timeout = 60000)]
    public async Task SettingsScreen_TutorialToggle_IsShown_AndTogglesThePreference()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/SettingsScreen.tscn");
        await runner.SimulateFrames(2);
        var scene = runner.Scene();
        var service = scene.GetNodeOrNull<SettingsService>("/root/Settings")!;
        service.SetTutorialHints(true); // start enabled (shared autoload)

        var check = scene.GetNodeOrNull<CheckButton>("Panel/Scroll/VBox/TutorialRow/TutorialCheck");
        AssertThat(check).IsNotNull();
        AssertThat(check!.ButtonPressed).IsTrue(); // ticked = enabled (the default)

        check.ButtonPressed = false;
        check.EmitSignal(BaseButton.SignalName.Toggled, false);
        await runner.SimulateFrames(1);
        AssertThat(service.TutorialHints).IsFalse(); // un-ticking disabled it

        check.ButtonPressed = true;
        check.EmitSignal(BaseButton.SignalName.Toggled, true);
        await runner.SimulateFrames(1);
        AssertThat(service.TutorialHints).IsTrue(); // re-ticking enabled it again
    }

    private static bool HasOnMapLandUnit(Game game) =>
        game.PlayerUnits.Any(u => u.IsOnMap && !u.Type.IsNaval);
}
