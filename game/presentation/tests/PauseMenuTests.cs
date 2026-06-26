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
    public async Task HelpButton_OpensTheHelpOverlay_BackClosesIt()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var pause = runner.Scene().GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        pause.GetNode<Button>("Panel/VBox/HelpButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var help = pause.GetParent().GetChildren().OfType<HelpPanel>().FirstOrDefault();
        AssertThat(help).IsNotNull();

        help!.GetNode<Button>("Panel/VBox/BackButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);
        AssertThat(pause.GetParent().GetChildren().OfType<HelpPanel>().Any()).IsFalse();

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

    [TestCase]
    public async Task QuitToMenu_CleanGame_ShowsThePlainConfirm_NoSaveButton()
    {
        // 86d3fq1v8: a clean game (no unsaved changes) gets the plain two-button "quit without saving?" confirm.
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        AssertThat(controller.HasUnsavedChanges).IsFalse(); // a fresh game starts clean (StartGame → MarkClean)
        var pause = controller.GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        pause.GetNode<Button>("Panel/VBox/QuitToMenuButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var confirm = pause.GetParent().GetChildren().OfType<ConfirmationDialog>().FirstOrDefault();
        AssertThat(confirm).IsNotNull();
        AssertThat(confirm!.Title).IsEqual("Quit without saving?"); // the plain confirm, not the unsaved-aware prompt

        confirm.EmitSignal(AcceptDialog.SignalName.Canceled);
        await runner.SimulateFrames(2);
        pause.Resume(); // leave the shared tree unpaused for the next test
    }

    [TestCase]
    public async Task QuitToMenu_DirtyGame_ShowsTheUnsavedPrompt_WithASaveButton()
    {
        // 86d3fq1v8: an unsaved game raises the "Unsaved changes" prompt with a Save button (Save / Quit anyway / Cancel).
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        controller.MarkDirty(); // simulate a state-mutating command this session
        AssertThat(controller.HasUnsavedChanges).IsTrue();
        var pause = controller.GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        pause.GetNode<Button>("Panel/VBox/QuitToMenuButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var confirm = pause.GetParent().GetChildren().OfType<ConfirmationDialog>().FirstOrDefault();
        AssertThat(confirm).IsNotNull();
        AssertThat(confirm!.Title).IsEqual("Unsaved changes"); // the unsaved-aware prompt
        AssertThat(confirm.OkButtonText).IsEqual("Quit anyway");
        // The extra Save button is present on the dialog (added via AcceptDialog.AddButton, lives in its internal HBox).
        var save = confirm.FindChild("QuitSaveButton", recursive: true, owned: false) as Button;
        AssertThat(save).IsNotNull();
        AssertThat(save!.Text).IsEqual("Save");

        // Cancel keeps the (still dirty) game paused with the pause menu up.
        confirm.EmitSignal(AcceptDialog.SignalName.Canceled);
        await runner.SimulateFrames(2);
        AssertThat(pause.GetParent().GetChildren().OfType<ConfirmationDialog>().Any()).IsFalse();
        AssertThat(pause.Visible).IsTrue();
        AssertThat(pause.GetTree().Paused).IsTrue();
        AssertThat(controller.HasUnsavedChanges).IsTrue(); // Cancel changed nothing

        pause.Resume(); // leave the shared tree unpaused for the next test
    }

    [TestCase]
    public async Task QuitToDesktop_DirtyGame_SaveButton_OpensTheSaveDialog()
    {
        // 86d3fq1v8: pressing Save on the unsaved prompt opens the save-slot dialog (the save-then-quit path's first step).
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        controller.MarkDirty();
        var pause = controller.GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        pause.GetNode<Button>("Panel/VBox/QuitToDesktopButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var confirm = pause.GetParent().GetChildren().OfType<ConfirmationDialog>().FirstOrDefault();
        AssertThat(confirm).IsNotNull();

        var save = (Button)confirm!.FindChild("QuitSaveButton", recursive: true, owned: false);
        save.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        // The save dialog opened (the desktop quit did NOT fire — we never confirmed) and the prompt is gone.
        AssertThat(pause.GetParent().GetChildren().OfType<SaveLoadDialog>().Any()).IsTrue();
        AssertThat(pause.GetParent().GetChildren().OfType<ConfirmationDialog>().Any()).IsFalse();

        // Close the save dialog without saving (Back) and resume, to leave the shared tree clean for the next test.
        var dialog = pause.GetParent().GetChildren().OfType<SaveLoadDialog>().First();
        dialog.EmitSignal("Closed");
        await runner.SimulateFrames(2);
        pause.Resume();
    }

    [TestCase]
    public async Task RetireButton_Appears_ConfirmsAndEndsTheGame()
    {
        // 86d3fq125: the pause menu offers a Retire item (built in code). It confirms first; confirming forwards to the
        // host (GameController.RequestRetire) which records the score and ends the game — the End Turn button disables.
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        var pause = controller.GetNode<PauseMenu>("UI/PauseMenu");
        pause.Open();
        await runner.SimulateFrames(1);

        var retire = pause.GetNodeOrNull<Button>("Panel/VBox/RetireButton");
        AssertThat(retire).IsNotNull();          // the affordance is present
        AssertThat(retire!.Disabled).IsFalse();  // an active game → retire is available
        AssertThat(controller.CanRetire).IsTrue();

        retire.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        var confirm = pause.GetParent().GetChildren().OfType<ConfirmationDialog>().FirstOrDefault();
        AssertThat(confirm).IsNotNull();         // it confirms before ending the game

        confirm!.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);

        // The human has retired — the game is over (the End Turn button is disabled and relabelled).
        AssertThat(controller.CanRetire).IsFalse();
        AssertThat(controller.GetNode<Button>("UI/EndTurnButton").Disabled).IsTrue();
        AssertThat(pause.GetTree().Paused).IsFalse(); // retiring resumed the tree (the end-game UI governs now)
    }
}
