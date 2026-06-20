using System.Linq;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md): drive the real main-menu scene inside the Godot runtime and assert the
/// menu shell — the title, the four buttons and their enabled/disabled state, the applied FreeCol art/theme, and the
/// New Game → game-scene navigation wiring.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MainMenuTests
{
    private const string MenuScene = "res://scenes/MainMenu.tscn";

    [TestCase]
    public async Task MainMenu_ShowsTitleAndAllFourButtons()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        AssertThat(scene.GetNode<Label>("Panel/VBox/Title").Text).IsEqual("Crown & Colony");
        AssertThat(scene.GetNode<Button>("Panel/VBox/NewGameButton").Text).IsEqual("New Game");
        AssertThat(scene.GetNode<Button>("Panel/VBox/LoadGameButton").Text).IsEqual("Load Game");
        AssertThat(scene.GetNode<Button>("Panel/VBox/SettingsButton").Text).IsEqual("Settings");
        AssertThat(scene.GetNode<Button>("Panel/VBox/QuitButton").Text).IsEqual("Quit");
    }

    [TestCase]
    public async Task AllFourButtons_AreEnabled()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        AssertThat(scene.GetNode<Button>("Panel/VBox/NewGameButton").Disabled).IsFalse();
        AssertThat(scene.GetNode<Button>("Panel/VBox/LoadGameButton").Disabled).IsFalse(); // wired in Slice F
        AssertThat(scene.GetNode<Button>("Panel/VBox/SettingsButton").Disabled).IsFalse();
        AssertThat(scene.GetNode<Button>("Panel/VBox/QuitButton").Disabled).IsFalse();
    }

    [TestCase]
    public async Task LoadGameButton_OpensTheSaveLoadDialog()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = runner.Scene();

        menu.GetNode<Button>("Panel/VBox/LoadGameButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(menu.GetChildren().OfType<SaveLoadDialog>().Any()).IsTrue();
    }

    [TestCase]
    public async Task Theme_Parchment_AndBorderArt_AreApplied()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var scene = (Control)runner.Scene();

        // ColonyTheme cascades from the root; the parchment skin overrides the panel; backdrop + carved-wood art load.
        AssertThat(scene.Theme).IsNotNull();
        AssertThat(scene.GetNode<PanelContainer>("Panel").HasThemeStyleboxOverride("panel")).IsTrue();
        AssertThat(scene.GetNode<TextureRect>("Background").Texture).IsNotNull();
        AssertThat(scene.GetNode<NinePatchRect>("Border").Texture).IsNotNull();
    }

    [TestCase]
    public async Task NewGameButton_IsWired_ToTheGameScene()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var button = runner.Scene().GetNode<Button>("Panel/VBox/NewGameButton");

        // The button is connected, and its destination scene loads to the in-game controller. (We assert the wiring +
        // a valid target rather than firing ChangeSceneToFile, which would free the scene out from under the runner.)
        AssertThat(button.GetSignalConnectionList(BaseButton.SignalName.Pressed).Count).IsGreater(0);
        AssertThat(ResourceLoader.Exists(MainMenu.GameScenePath)).IsTrue();
        var instance = GD.Load<PackedScene>(MainMenu.GameScenePath).Instantiate();
        AssertThat(instance).IsInstanceOf<GameController>();
        instance.Free();
    }

    [TestCase]
    public async Task NewGameButton_OpensTheNewGameOptionsDialog()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = runner.Scene();

        menu.GetNode<Button>("Panel/VBox/NewGameButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(menu.GetChildren().OfType<NewGameDialog>().Any()).IsTrue();
    }

    [TestCase]
    public async Task NewGameDialog_ForwardsTheChosenWorldSizeLandMassAndDifficulty()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var dialog = new NewGameDialog();
        menu.AddChild(dialog);
        await runner.SimulateFrames(1); // _Ready builds the controls

        WorldSize? chosenSize = null;
        LandMass? chosenLand = null;
        DifficultyLevel? chosenDifficulty = null;
        dialog.Open((size, land, difficulty) => { chosenSize = size; chosenLand = land; chosenDifficulty = difficulty; });

        var sizeOption = dialog.FindChild("SizeOption", recursive: true, owned: false) as OptionButton;
        var landOption = dialog.FindChild("LandOption", recursive: true, owned: false) as OptionButton;
        var difficultyOption = dialog.FindChild("DifficultyOption", recursive: true, owned: false) as OptionButton;
        AssertThat(sizeOption).IsNotNull();
        AssertThat(landOption).IsNotNull();
        AssertThat(difficultyOption).IsNotNull();
        sizeOption!.Select(2); // "Large"
        landOption!.Select(2); // "Dense"
        difficultyOption!.Select(4); // "Viceroy" (veryHard)

        var start = dialog.FindChild("StartButton", recursive: true, owned: false) as Button;
        AssertThat(start).IsNotNull();
        start!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(chosenSize).IsNotNull();
        AssertThat(chosenSize!.Name).IsEqual(WorldSizeOptions.Sizes[2].Name);
        AssertThat(chosenLand!.Name).IsEqual(WorldSizeOptions.LandMasses[2].Name);
        AssertThat(chosenDifficulty).IsNotNull();
        AssertThat(chosenDifficulty!.Id).IsEqual(DifficultyLevels.All[4].Id);
    }

    [TestCase]
    public async Task SettingsButton_OpensTheSettingsScreenAsAnOverlay()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = runner.Scene();

        menu.GetNode<Button>("Panel/VBox/SettingsButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(menu.GetChildren().OfType<SettingsScreen>().Any()).IsTrue();
    }
}
