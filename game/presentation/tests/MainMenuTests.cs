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
        MapSource? chosenMap = null;
        dialog.Open((size, land, difficulty, map) =>
        {
            chosenSize = size;
            chosenLand = land;
            chosenDifficulty = difficulty;
            chosenMap = map;
        });

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
        AssertThat(chosenMap).IsEqual(MapSource.Random); // map defaults to the random New World
    }

    [TestCase]
    public async Task NewGameDialog_ForwardsTheChosenNation_ThroughPendingNation()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var dialog = new NewGameDialog();
        menu.AddChild(dialog);
        await runner.SimulateFrames(1); // _Ready builds the controls (incl. the nation dropdown from the ruleset)

        GameController.PendingNation = null; // clean slate (a static survives between tests)
        dialog.Open((_, _, _, _) => { });

        var nationOption = dialog.FindChild("NationOption", recursive: true, owned: false) as OptionButton;
        AssertThat(nationOption).IsNotNull();
        // Index 0 is "No nation (default)"; the selectable European powers follow. Pick the first real nation.
        AssertThat(nationOption!.ItemCount).IsGreater(1);
        nationOption.Select(1);

        var start = dialog.FindChild("StartButton", recursive: true, owned: false) as Button;
        AssertThat(start).IsNotNull();
        start!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The first selectable European nation in the default ruleset is forwarded as the human's chosen nation.
        string firstNation = GameVariants.Default.LoadRuleset().EuropeanNations
            .First(n => n.Selectable && !n.IsRef).Id;
        AssertThat(GameController.PendingNation).IsEqual(firstNation);
        GameController.PendingNation = null; // tidy the static for the next test
    }

    [TestCase]
    public async Task NewGameDialog_DefaultingTheNation_LeavesPendingNationNull()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var dialog = new NewGameDialog();
        menu.AddChild(dialog);
        await runner.SimulateFrames(1);

        GameController.PendingNation = "stale"; // a stale value must be overwritten with null when no nation is picked
        dialog.Open((_, _, _, _) => { });

        // Leave the nation dropdown at its default index 0 ("No nation") and press Start.
        var start = dialog.FindChild("StartButton", recursive: true, owned: false) as Button;
        AssertThat(start).IsNotNull();
        start!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(GameController.PendingNation).IsNull(); // the classic nation-less default
    }

    [TestCase]
    public async Task NewGameDialog_ForwardsTheChosenScenarioVariant_ThroughPendingVariant()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var dialog = new NewGameDialog();
        menu.AddChild(dialog);
        await runner.SimulateFrames(1); // _Ready builds the controls (incl. the scenario dropdown from the registry)

        GameController.PendingVariant = null; // clean slate (a static survives between tests)
        dialog.Open((_, _, _, _) => { });

        var variantOption = dialog.FindChild("VariantOption", recursive: true, owned: false) as OptionButton;
        AssertThat(variantOption).IsNotNull();
        // Every shipped variant is offered, in registry order; the default (Classic) is pre-selected.
        AssertThat(variantOption!.ItemCount).IsEqual(GameVariants.All.Count);
        variantOption.Select(0); // Classic — the only shipped variant today (still exercises the forwarding seam)

        var start = dialog.FindChild("StartButton", recursive: true, owned: false) as Button;
        AssertThat(start).IsNotNull();
        start!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The chosen variant is forwarded as the new game's ruleset source.
        AssertThat(GameController.PendingVariant).IsNotNull();
        AssertThat(GameController.PendingVariant!.Id).IsEqual(GameVariants.All[0].Id);
        GameController.PendingVariant = null; // tidy the static for the next test
    }

    [TestCase]
    public async Task NewGameDialog_ForwardsTheChosenGameOptions_ThroughTheirPendingStatics()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var dialog = new NewGameDialog();
        menu.AddChild(dialog);
        await runner.SimulateFrames(1); // _Ready builds the controls (incl. the game-option checkboxes from the ruleset)

        GameController.PendingVictoryConditions = null;
        GameController.PendingFogOfWar = null;
        GameController.PendingCustomIgnoreBoycott = null;
        dialog.Open((_, _, _, _) => { });

        // The checkboxes start at the spec defaults (REF on / Europeans on / Humans off / fog on / smuggling on).
        var victoryHumans = dialog.FindChild("VictoryHumansCheck", recursive: true, owned: false) as CheckBox;
        var fogOfWar = dialog.FindChild("FogOfWarCheck", recursive: true, owned: false) as CheckBox;
        var customHouse = dialog.FindChild("CustomIgnoreBoycottCheck", recursive: true, owned: false) as CheckBox;
        AssertThat(victoryHumans).IsNotNull();
        AssertThat(fogOfWar).IsNotNull();
        AssertThat(customHouse).IsNotNull();
        AssertThat(fogOfWar!.ButtonPressed).IsTrue();   // default on
        AssertThat(customHouse!.ButtonPressed).IsTrue(); // default on

        // Flip a non-default set: last-human-standing victory ON, fog OFF, custom-house smuggling OFF.
        victoryHumans!.ButtonPressed = true;
        fogOfWar.ButtonPressed = false;
        customHouse.ButtonPressed = false;

        var start = dialog.FindChild("StartButton", recursive: true, owned: false) as Button;
        AssertThat(start).IsNotNull();
        start!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The picks are forwarded onto their statics. (Extract to plain bools: AssertThat on a nullable bool resolves to
        // the object overload, which rejects the boxed primitive — so assert HasValue + Value as plain booleans.)
        AssertThat(GameController.PendingVictoryConditions).IsNotNull();
        bool humansWin = GameController.PendingVictoryConditions!.Value.DefeatHumans;
        AssertThat(humansWin).IsTrue();
        bool fogSet = GameController.PendingFogOfWar.HasValue;
        bool fogValue = GameController.PendingFogOfWar ?? true;
        AssertThat(fogSet).IsTrue();
        AssertThat(fogValue).IsFalse();
        bool smuggleSet = GameController.PendingCustomIgnoreBoycott.HasValue;
        bool smuggleValue = GameController.PendingCustomIgnoreBoycott ?? true;
        AssertThat(smuggleSet).IsTrue();
        AssertThat(smuggleValue).IsFalse();

        // Tidy the statics for the next test.
        GameController.PendingVictoryConditions = null;
        GameController.PendingFogOfWar = null;
        GameController.PendingCustomIgnoreBoycott = null;
    }

    [TestCase]
    public async Task NewGameDialog_SelectingAmerica_ForwardsAmerica_AndDisablesWorldSize()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();

        var dialog = new NewGameDialog();
        menu.AddChild(dialog);
        await runner.SimulateFrames(1); // _Ready builds the controls

        MapSource? chosenMap = null;
        dialog.Open((_, _, _, map) => chosenMap = map);

        var mapOption = dialog.FindChild("MapOption", recursive: true, owned: false) as OptionButton;
        var sizeOption = dialog.FindChild("SizeOption", recursive: true, owned: false) as OptionButton;
        var landOption = dialog.FindChild("LandOption", recursive: true, owned: false) as OptionButton;
        AssertThat(mapOption).IsNotNull();
        AssertThat(sizeOption).IsNotNull();
        AssertThat(landOption).IsNotNull();

        // The size/land dropdowns start enabled (Random) and disable when America (a fixed-size map) is picked.
        AssertThat(sizeOption!.Disabled).IsFalse();
        mapOption!.Select(1); // "America (fixed)"
        mapOption.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
        await runner.SimulateFrames(1);
        AssertThat(sizeOption.Disabled).IsTrue();
        AssertThat(landOption!.Disabled).IsTrue();

        var start = dialog.FindChild("StartButton", recursive: true, owned: false) as Button;
        AssertThat(start).IsNotNull();
        start!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(chosenMap).IsEqual(MapSource.America);
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

    [TestCase]
    public async Task AboutButton_OpensTheAboutScreenAsAnOverlay()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = runner.Scene();

        menu.GetNode<Button>("Panel/VBox/AboutButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(menu.GetChildren().OfType<AboutPanel>().Any()).IsTrue();
    }
}
