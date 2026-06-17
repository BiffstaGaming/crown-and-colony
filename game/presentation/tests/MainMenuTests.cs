using System.Linq;
using System.Threading.Tasks;
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
    public async Task NewGameQuitAndSettings_AreEnabled_LoadGame_StaysDisabled()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        AssertThat(scene.GetNode<Button>("Panel/VBox/NewGameButton").Disabled).IsFalse();
        AssertThat(scene.GetNode<Button>("Panel/VBox/QuitButton").Disabled).IsFalse();
        AssertThat(scene.GetNode<Button>("Panel/VBox/SettingsButton").Disabled).IsFalse(); // wired in Slice B
        // Load Game waits on the save-load dialog UI (ClickUp 86d3c9y5y).
        AssertThat(scene.GetNode<Button>("Panel/VBox/LoadGameButton").Disabled).IsTrue();
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
