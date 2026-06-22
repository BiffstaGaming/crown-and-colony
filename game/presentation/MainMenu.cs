using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The game's entry shell — the main-menu / title screen shown before any game starts (it replaces the old
/// boot-straight-into-a-running-game behaviour; <c>project.godot</c>'s <c>run/main_scene</c> now points here). It
/// presents <b>New Game</b> / <b>Load Game</b> / <b>Settings</b> / <b>Quit</b> over FreeCol's antique New-World map
/// backdrop, framed by the same carved-wood border and brown-parchment skin as the colony screen
/// (<see cref="ColonyArt"/> + <see cref="ColonyTheme"/>) so the two screens share one look.
/// </summary>
/// <remarks>
/// Presentation-only (ADR-006): the menu owns no game rules. <b>New Game</b> simply loads the in-game scene, which
/// builds a fresh game exactly as the app did on boot before this screen existed. <b>Load Game</b> opens the save-slot
/// dialog and boots the chosen save; <b>Settings</b> opens the options screen.
/// </remarks>
public partial class MainMenu : Control
{
    /// <summary>The scene loaded when the player starts a new game (the in-game map/controller scene).</summary>
    public const string GameScenePath = "res://scenes/main.tscn";

    /// <summary>This menu's own scene path — for screens that navigate back to it (e.g. the settings screen).</summary>
    public const string MenuScenePath = "res://scenes/MainMenu.tscn";

    /// <summary>The settings / options screen, opened from the Settings button.</summary>
    private const string SettingsScenePath = "res://scenes/SettingsScreen.tscn";

    /// <summary>FreeCol's antique New-World map (GPL v2 — see <c>assets/freecol/PROVENANCE.md</c>), the menu backdrop.</summary>
    private const string BackdropPath = "res://assets/freecol/ui/map.jpg";

    /// <summary>Builds the menu's look (theme, backdrop, parchment skin, carved-wood frame) and wires the buttons.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get(); // cohesive parchment/wood styling cascades to every child

        if (ResourceLoader.Exists(BackdropPath))
        {
            GetNode<TextureRect>("Background").Texture = GD.Load<Texture2D>(BackdropPath);
        }

        GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());

        if (ColonyArt.ColonyBorder() is { } borderTex)
        {
            GetNode<NinePatchRect>("Border").Texture = borderTex;
        }

        GetNode<Button>("Panel/VBox/NewGameButton").Pressed += OnNewGame;
        GetNode<Button>("Panel/VBox/LoadGameButton").Pressed += OnLoadGame;
        GetNode<Button>("Panel/VBox/SettingsButton").Pressed += OnSettings;
        GetNode<Button>("Panel/VBox/QuitButton").Pressed += OnQuit;
    }

    /// <summary>
    /// Opens the new-game options overlay (scenario/variant + map source + world size + land mass + landmass style +
    /// difficulty + nation + the honoured base game options — victory conditions, fog of war, custom-house smuggling);
    /// choosing Start forwards the size/land/difficulty/map picks to the game scene via
    /// <see cref="GameController.PendingWorldSize"/>/<see cref="GameController.PendingLandMass"/>/<see cref="GameController.PendingDifficulty"/>/<see cref="GameController.PendingMapSource"/>
    /// (the variant, nation and game-option picks ride their own <c>GameController.Pending*</c> statics, set by the
    /// dialog) and boots it (which builds a fresh game from those options, defaulting to the shipped Classic random world
    /// if none were changed).
    /// </summary>
    private void OnNewGame()
    {
        var dialog = new NewGameDialog();
        dialog.Closed += dialog.QueueFree;
        AddChild(dialog);
        dialog.Open((size, land, difficulty, mapSource) =>
        {
            GameController.PendingWorldSize = size;
            GameController.PendingLandMass = land;
            GameController.PendingDifficulty = difficulty;
            GameController.PendingMapSource = mapSource;
            GetTree().ChangeSceneToFile(GameScenePath);
        });
    }

    /// <summary>Opens the save-slot dialog; choosing a save boots the game scene loaded from it.</summary>
    private void OnLoadGame()
    {
        var dialog = GD.Load<PackedScene>(SaveLoadDialog.ScenePath).Instantiate<SaveLoadDialog>();
        dialog.Closed += dialog.QueueFree;
        AddChild(dialog);
        dialog.Open(SaveLoadDialog.Mode.Load, path =>
        {
            GameController.PendingLoadPath = path;
            GetTree().ChangeSceneToFile(GameScenePath);
        });
    }

    /// <summary>Opens the settings screen as an overlay; removes it again when the player presses Back.</summary>
    private void OnSettings()
    {
        var settings = GD.Load<PackedScene>(SettingsScenePath).Instantiate<SettingsScreen>();
        settings.Closed += settings.QueueFree;
        AddChild(settings);
    }

    /// <summary>Exits the application.</summary>
    private void OnQuit() => GetTree().Quit();
}
