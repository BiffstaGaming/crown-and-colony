using System.Threading.Tasks;
using CrownAndColony.GameLogic.App;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L4 visual goldens for the menu/UI screens (docs/TESTING.md) — now feasible because the UI font is bundled
/// (`ColonyTheme` uses Cardo, SIL OFL), so text rendering is consistent rather than platform-default. A slightly
/// looser tolerance than the map goldens absorbs cross-platform font antialiasing. Regenerate with `GOLDEN_UPDATE=1`.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MenuGoldenTests
{
    private static readonly Vector2I CaptureSize = new(1024, 600);

    // Text frames vary a little more across platforms (font rasterisation) than the map-tile goldens do.
    private const double TextTolerance = 0.02;

    [TestCase(Timeout = 60000)]
    public async Task MainMenu_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        var scene = (Control)runner.Scene();
        scene.GetWindow().Size = CaptureSize;
        await runner.SimulateFrames(5);

        GoldenAssert.Assert("main-menu", scene.GetViewport().GetTexture().GetImage(), TextTolerance);
    }

    [TestCase(Timeout = 60000)]
    public async Task SettingsScreen_MatchesGolden()
    {
        ResetSettingsToDefaults(); // the screen mirrors the live settings — pin them so the golden is deterministic
        ISceneRunner runner = ISceneRunner.Load("res://scenes/SettingsScreen.tscn");
        var scene = (Control)runner.Scene();
        scene.GetWindow().Size = CaptureSize;
        await runner.SimulateFrames(5);

        GoldenAssert.Assert("settings-screen", scene.GetViewport().GetTexture().GetImage(), TextTolerance);
    }

    [TestCase(Timeout = 60000)]
    public async Task PauseMenu_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.GetWindow().Size = CaptureSize;
        controller.StartNewGame(424242); // same deterministic world as the map goldens, dimmed behind the panel
        await runner.SimulateFrames(3);

        var pause = controller.GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(4);
        Image actual = controller.GetViewport().GetTexture().GetImage();
        pause.Resume(); // unpause before asserting, so a failure can't leave the shared tree paused

        GoldenAssert.Assert("pause-menu", actual, TextTolerance);
    }

    [TestCase(Timeout = 60000)]
    public async Task NewGameDialog_MatchesGolden()
    {
        // The New-Game setup overlay opened over the menu (86d3jy0rn — Chris's playtest: the tall option list was
        // cut off + unframed). Instantiate it directly (OnNewGame is private) and open it with a no-op onStart.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        var scene = (Control)runner.Scene();
        scene.GetWindow().Size = CaptureSize;
        await runner.SimulateFrames(3);

        var dialog = new NewGameDialog();
        scene.AddChild(dialog);
        dialog.Open((_, _, _, _) => { });
        await runner.SimulateFrames(6);

        GoldenAssert.Assert("new-game-dialog", scene.GetViewport().GetTexture().GetImage(), TextTolerance);
    }

    [TestCase(Timeout = 60000)]
    public async Task AboutPanel_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/AboutPanel.tscn");
        var scene = (Control)runner.Scene();
        scene.GetWindow().Size = CaptureSize;
        await runner.SimulateFrames(5);

        GoldenAssert.Assert("about-panel", scene.GetViewport().GetTexture().GetImage(), TextTolerance);
    }

    [TestCase(Timeout = 60000)]
    public async Task HelpPanel_MatchesGolden()
    {
        // The standalone help screen (goal + core loops + controls reference). Baseline generated on the CI Linux
        // renderer via the GOLDEN_UPDATE=1 workflow_dispatch (86d3f69e9), so a Windows dev host's font/GPU raster
        // differences no longer block adoption. Functional behaviour is covered by HelpPanelTests.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/HelpPanel.tscn");
        var scene = (Control)runner.Scene();
        scene.GetWindow().Size = CaptureSize;
        await runner.SimulateFrames(5);

        GoldenAssert.Assert("help-panel", scene.GetViewport().GetTexture().GetImage(), TextTolerance);
    }

    [TestCase(Timeout = 60000)]
    public async Task InfoPopup_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        var scene = (Control)runner.Scene();
        scene.GetWindow().Size = CaptureSize;
        await runner.SimulateFrames(3);

        InfoPopup.Show(scene, "Notice", "This is an example event message shown to the player.");
        await runner.SimulateFrames(3);

        GoldenAssert.Assert("info-popup", scene.GetViewport().GetTexture().GetImage(), TextTolerance);
    }

    // Forces the settings autoload back to its default values (other tests may have changed/persisted them), so the
    // settings-screen golden always captures the default control positions.
    private static void ResetSettingsToDefaults()
    {
        if (Engine.GetMainLoop() is SceneTree tree
            && tree.Root.GetNodeOrNull<SettingsService>("Settings") is { } service)
        {
            var defaults = new SettingsModel();
            service.UpdateAndApply(s =>
            {
                s.WindowMode = defaults.WindowMode;
                s.VSync = defaults.VSync;
                s.MasterVolume = defaults.MasterVolume;
                s.MusicVolume = defaults.MusicVolume;
                s.SfxVolume = defaults.SfxVolume;
            });
        }
    }
}
