using System.Linq;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.App;
using CrownAndColony.GameLogic.Specification;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the skippable opening cinematic (86d3fq1kf): the new-game intro sequence
/// shown on the <b>interactive</b> New-Game path (main menu → New-Game dialog → cinematic → game). Two halves:
/// <list type="bullet">
/// <item>the <see cref="OpeningCinematic"/> component in isolation — it shows a beat, and Skip / Esc raise
/// <see cref="OpeningCinematic.Finished"/> exactly once (so the host proceeds into the game and the player is never
/// trapped);</item>
/// <item>the <see cref="MainMenu"/> wiring — confirming New-Game with the "Play intro" setting <b>on</b> shows the
/// cinematic (and does <em>not</em> jump straight to the game), while <b>off</b> skips it. The scene change to the game
/// is asserted by wiring/absence rather than fired, matching <c>MainMenuTests</c> (firing it would free the scene out
/// from under the runner).</item>
/// </list>
/// Presentation-only (ADR-006): the cinematic threads no game state and is deliberately absent from
/// <see cref="GameController.StartNewGame"/>, so the L3 fast path (and the goldens that capture the menu / call
/// StartNewGame directly) are untouched.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class OpeningCinematicTests
{
    private const string MenuScene = "res://scenes/MainMenu.tscn";

    /// <summary>
    /// Clears the cross-scene New-Game statics the dialog's Start leaves set (the MainMenu wiring tests fire Start, which
    /// populates them) so they never leak into another suite's "default" game — mirrors <c>NewGameBridgeTests</c>'
    /// clean-slate discipline, scoped to the picks this suite could touch.
    /// </summary>
    private static void ResetPendingStatics()
    {
        GameController.PendingWorldSize = null;
        GameController.PendingLandMass = null;
        GameController.PendingDifficulty = null;
        GameController.PendingMapSource = null;
        GameController.PendingLandStyle = null;
        GameController.PendingNation = null;
        GameController.PendingVictoryConditions = null;
        GameController.PendingFogOfWar = null;
        GameController.PendingCustomIgnoreBoycott = null;
        GameController.PendingVariant = null;
        NewGameDialog.PendingRivalCount = null;
        NewGameDialog.PendingStartYear = null;
        NewGameDialog.PendingMapOptions = null;
        NewGameDialog.PendingRumourNumber = null;
        NewGameDialog.PendingNationalAdvantages = null;
        NewGameDialog.PendingDifficultyOverrides = null;
        NewGameDialog.PendingImportedMap = null;
    }

    [BeforeTest]
    public void CleanSlateBefore() => ResetPendingStatics();

    [AfterTest]
    public void CleanSlateAfter() => ResetPendingStatics();

    /// <summary>
    /// Sets the live "Play intro" client option (through the Settings autoload) so the MainMenu wiring tests can drive
    /// both gate states. Mutates the model field <b>directly</b> rather than through <c>UpdateAndApply</c>: PlayIntro has
    /// no engine effect (it is read only by the menu), and calling <c>Apply()</c> would re-push window-mode / UI-scale to
    /// the shared process — which could disturb the root content scale the later map goldens depend on. Returns the prior
    /// value so the test restores it.
    /// </summary>
    private static bool SetPlayIntro(Node anyNode, bool value)
    {
        var service = anyNode.GetNode<SettingsService>("/root/Settings");
        bool prior = service.Settings.PlayIntro;
        service.Settings.PlayIntro = value;
        return prior;
    }

    // ── The OpeningCinematic component in isolation ─────────────────────────────────────────────────────────────────

    [TestCase]
    public async Task Play_ShowsTheFirstBeat()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var cinematic = new OpeningCinematic();
        menu.AddChild(cinematic);
        await runner.SimulateFrames(1); // _Ready builds the surface
        cinematic.Play();
        await runner.SimulateFrames(1);

        // The cinematic is visible and its beat label carries the first narrative beat's text (non-empty).
        AssertThat(cinematic.Visible).IsTrue();
        var beat = cinematic.FindChild("BeatLabel", recursive: true, owned: false) as Label;
        AssertThat(beat).IsNotNull();
        AssertThat(beat!.Text.Length).IsGreater(0);

        cinematic.QueueFree();
    }

    [TestCase]
    public async Task SetBeats_MakesTheIntroVariantAware()
    {
        // The bug this fixes: the American 1492 story played for every variant. With the Australian Federation's beats
        // injected before Play(), the first panel is the 1788 First-Fleet scene, NOT the classic 1492 charter — proving
        // the host's per-variant beats reach the cinematic (MainMenu passes PendingVariant.OpeningBeats).
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var cinematic = new OpeningCinematic();
        menu.AddChild(cinematic);
        await runner.SimulateFrames(1);
        cinematic.SetBeats(GameVariants.Australia.OpeningBeats);
        cinematic.Play();
        await runner.SimulateFrames(1);

        var beat = cinematic.FindChild("BeatLabel", recursive: true, owned: false) as Label;
        AssertThat(beat).IsNotNull();
        AssertThat(beat!.Text).IsEqual(GameVariants.Australia.OpeningBeats[0]);
        AssertThat(beat.Text).Contains("1788");
        AssertThat(beat.Text).NotContains("1492"); // never the classic American opening under the Australia variant

        cinematic.QueueFree();
    }

    [TestCase]
    public async Task Skip_RaisesFinished_Once()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var cinematic = new OpeningCinematic();
        menu.AddChild(cinematic);
        await runner.SimulateFrames(1);

        int finished = 0;
        cinematic.Finished += () => finished++;
        cinematic.Play();
        await runner.SimulateFrames(1);

        cinematic.Skip();
        cinematic.Skip(); // a second skip must not raise Finished again (one-shot)
        await runner.SimulateFrames(1);

        AssertThat(finished).IsEqual(1);
        cinematic.QueueFree();
    }

    [TestCase]
    public async Task EscKey_SkipsIntoTheGame()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var cinematic = new OpeningCinematic();
        menu.AddChild(cinematic);
        await runner.SimulateFrames(1);

        bool finished = false;
        cinematic.Finished += () => finished = true;
        cinematic.Play();
        await runner.SimulateFrames(1);

        // Esc = Godot's ui_cancel action; the cinematic's _UnhandledKeyInput ends it immediately.
        cinematic._UnhandledKeyInput(new InputEventAction { Action = "ui_cancel", Pressed = true });
        await runner.SimulateFrames(1);

        AssertThat(finished).IsTrue();
        cinematic.QueueFree();
    }

    [TestCase]
    public async Task ClickingThroughEveryBeat_EndsTheCinematic()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var cinematic = new OpeningCinematic();
        menu.AddChild(cinematic);
        await runner.SimulateFrames(1);

        bool finished = false;
        cinematic.Finished += () => finished = true;
        cinematic.Play();
        await runner.SimulateFrames(1);

        // A left-click advances one beat; enough clicks walk past the last beat and end the cinematic. (More clicks than
        // there could ever be beats, so the test never depends on the exact beat count — the one-shot guard absorbs extras.)
        for (int i = 0; i < 12 && !finished; i++)
        {
            cinematic._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            await runner.SimulateFrames(1);
        }

        AssertThat(finished).IsTrue();
        cinematic.QueueFree();
    }

    // ── The MainMenu interactive New-Game wiring ────────────────────────────────────────────────────────────────────
    //
    // The wiring is asserted through the internal ShouldPlayIntro() gate and (on the intro-on path) the cinematic child
    // that Start produces — NOT by firing the actual scene change. Firing ChangeSceneToFile would swap the shared root
    // scene under the runner and contaminate every later suite (map goldens etc.); the project convention (see
    // MainMenuTests.NewGameButton_IsWired_ToTheGameScene) is to assert the wiring/target, never the navigation.

    [TestCase]
    public async Task ConfirmingNewGame_WithPlayIntroOn_ShowsTheCinematic()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();
        bool prior = SetPlayIntro(menu, true);

        // Drive the real New-Game dialog to Start; the menu's onStart shows the cinematic. With the intro ON, no scene
        // change fires yet (that waits on the cinematic's Finished), so asserting the child is safe under the runner.
        menu.GetNode<Button>("Panel/VBox/NewGameButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var realDialog = menu.GetChildren().OfType<NewGameDialog>().Last();
        var start = realDialog.FindChild("StartButton", recursive: true, owned: false) as Button;
        AssertThat(start).IsNotNull();
        start!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var cinematic = menu.GetChildren().OfType<OpeningCinematic>().FirstOrDefault();
        AssertThat(cinematic).IsNotNull();
        AssertThat(cinematic!.Visible).IsTrue();
        // The cinematic is the ONLY thing added — no game scene has been booted (Finished hasn't fired), so the menu
        // scene is still the runner's scene (a cheap proof the scene change is gated on the cinematic, not immediate).
        AssertThat(runner.Scene()).IsEqual(menu);

        cinematic.QueueFree();
        SetPlayIntro(menu, prior);
    }

    [TestCase]
    public async Task PlayIntroGate_FollowsTheSetting()
    {
        // The intro-off path calls ChangeSceneToFile immediately, which would swap the shared root scene and break later
        // suites — so it is asserted through the gate decision (ShouldPlayIntro), hermetically, rather than by firing it.
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (MainMenu)runner.Scene();
        bool prior = SetPlayIntro(menu, false);

        AssertThat(menu.ShouldPlayIntro()).IsFalse(); // off → no cinematic, straight to the game

        SetPlayIntro(menu, true);
        AssertThat(menu.ShouldPlayIntro()).IsTrue();  // on → the cinematic plays first

        SetPlayIntro(menu, prior);
    }
}
