using System.Linq;
using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md): drive the in-game pause menu inside the Godot runtime — it starts hidden
/// with the game running, Open/Resume pause and unpause the tree, the Settings button opens the settings overlay and
/// its Back closes it, and Quit to Main Menu targets a valid menu scene.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PauseMenuTests
{
    private const string GameScene = "res://scenes/main.tscn";

    [TestCase]
    public async Task PauseMenu_StartsHidden_WithTheGameUnpaused()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var pause = runner.Scene().GetNode<PauseMenu>("UI/PauseMenu");

        AssertThat(pause.Visible).IsFalse();
        AssertThat(pause.GetTree().Paused).IsFalse();
    }

    [TestCase]
    public async Task Open_PausesAndShows_Resume_Unpauses()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var pause = runner.Scene().GetNode<PauseMenu>("UI/PauseMenu");

        pause.Open();
        await runner.SimulateFrames(1);
        AssertThat(pause.Visible).IsTrue();
        AssertThat(pause.GetTree().Paused).IsTrue();

        pause.Resume();
        await runner.SimulateFrames(1);
        AssertThat(pause.Visible).IsFalse();
        AssertThat(pause.GetTree().Paused).IsFalse();
    }

    [TestCase]
    public async Task ResumeButton_ClosesTheMenu_AndUnpauses()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var pause = runner.Scene().GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        pause.GetNode<Button>("Panel/VBox/ResumeButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(pause.Visible).IsFalse();
        AssertThat(pause.GetTree().Paused).IsFalse();
    }

    [TestCase]
    public async Task SettingsButton_OpensTheSettingsOverlay_BackClosesIt()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var pause = runner.Scene().GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        pause.GetNode<Button>("Panel/VBox/SettingsButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var settings = pause.GetParent().GetChildren().OfType<SettingsScreen>().FirstOrDefault();
        AssertThat(settings).IsNotNull();

        settings!.GetNode<Button>("Panel/VBox/BackButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);
        AssertThat(pause.GetParent().GetChildren().OfType<SettingsScreen>().Any()).IsFalse();

        pause.Resume(); // leave the shared tree unpaused for the next test
    }

    [TestCase]
    public async Task SaveButton_OpensTheSaveLoadDialog()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var pause = runner.Scene().GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        pause.GetNode<Button>("Panel/VBox/SaveButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        // The dialog is added to the UI layer (the pause menu's parent), over the paused game.
        AssertThat(pause.GetParent().GetChildren().OfType<SaveLoadDialog>().Any()).IsTrue();

        pause.Resume(); // leave the shared tree unpaused
    }

    [TestCase]
    public async Task QuitToMenuButton_IsWired_ToAValidMenuScene()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var button = runner.Scene().GetNode<Button>("UI/PauseMenu/Panel/VBox/QuitToMenuButton");

        // The button is connected; its destination loads to the main menu. (We assert wiring + a valid target rather
        // than firing ChangeSceneToFile, which would free the scene out from under the runner.)
        AssertThat(button.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count).IsGreater(0);
        AssertThat(ResourceLoader.Exists(MainMenu.MenuScenePath)).IsTrue();
        var menu = GD.Load<PackedScene>(MainMenu.MenuScenePath).Instantiate();
        AssertThat(menu).IsInstanceOf<MainMenu>();
        menu.Free();
    }

    [TestCase]
    public async Task AboutButton_OpensTheAboutOverlay_BackClosesIt()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var pause = runner.Scene().GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        pause.GetNode<Button>("Panel/VBox/AboutButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var about = pause.GetParent().GetChildren().OfType<AboutPanel>().FirstOrDefault();
        AssertThat(about).IsNotNull();

        about!.GetNode<Button>("Panel/VBox/BackButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);
        AssertThat(pause.GetParent().GetChildren().OfType<AboutPanel>().Any()).IsFalse();

        pause.Resume(); // leave the shared tree unpaused for the next test
    }

    [TestCase]
    public async Task QuitToDesktop_ShowsAConfirmation_AndCancelKeepsTheGame()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var pause = runner.Scene().GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        // Pressing Quit to Desktop must NOT exit immediately — it raises a confirmation dialog first.
        pause.GetNode<Button>("Panel/VBox/QuitToDesktopButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var confirm = pause.GetParent().GetChildren().OfType<ConfirmationDialog>().FirstOrDefault();
        AssertThat(confirm).IsNotNull();

        // Cancel is a no-op: the dialog goes away and the game is still paused with the pause menu up.
        confirm!.EmitSignal(AcceptDialog.SignalName.Canceled);
        await runner.SimulateFrames(2);
        AssertThat(pause.GetParent().GetChildren().OfType<ConfirmationDialog>().Any()).IsFalse();
        AssertThat(pause.Visible).IsTrue();
        AssertThat(pause.GetTree().Paused).IsTrue();

        pause.Resume(); // leave the shared tree unpaused for the next test
    }

    [TestCase]
    public async Task QuitToMenu_ShowsAConfirmation_AndCancelKeepsTheGame()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var pause = runner.Scene().GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        pause.GetNode<Button>("Panel/VBox/QuitToMenuButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var confirm = pause.GetParent().GetChildren().OfType<ConfirmationDialog>().FirstOrDefault();
        AssertThat(confirm).IsNotNull();

        confirm!.EmitSignal(AcceptDialog.SignalName.Canceled);
        await runner.SimulateFrames(2);
        AssertThat(pause.GetParent().GetChildren().OfType<ConfirmationDialog>().Any()).IsFalse();
        AssertThat(pause.Visible).IsTrue();         // still on the pause menu
        AssertThat(pause.GetTree().Paused).IsTrue(); // game still paused (we did not change scene)

        pause.Resume(); // leave the shared tree unpaused for the next test
    }
}
