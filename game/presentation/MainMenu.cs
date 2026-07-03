using CrownAndColony.GameLogic.Audio;
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

    /// <summary>The About / version / license screen, opened from the About button.</summary>
    private const string AboutScenePath = "res://scenes/AboutPanel.tscn";

    /// <summary>The in-game help / tutorial screen, opened from the Help button.</summary>
    private const string HelpScenePath = "res://scenes/HelpPanel.tscn";

    /// <summary>FreeCol's antique New-World map (GPL v2 — see <c>assets/freecol/PROVENANCE.md</c>), the menu backdrop.</summary>
    private const string BackdropPath = "res://assets/freecol/ui/map.jpg";

    /// <summary>Builds the menu's look (theme, backdrop, parchment skin, carved-wood frame) and wires the buttons.</summary>
    public override void _Ready()
    {
        // This screen is the Menu music context (86d3fq1wy). The Music autoload persists across scene changes (e.g.
        // quit-to-menu from the pause menu with the war bed playing), so re-assert Menu here; the service relabels
        // without restarting when the audible playlist is unchanged (Menu shares the in-game peace bed). Lazy lookup:
        // no-op when the autoload is absent (bare headless test scenes).
        GetNodeOrNull<MusicService>("/root/Music")?.SetContext(MusicContext.Menu);

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
        GetNode<Button>("Panel/VBox/HelpButton").Pressed += OnHelp;
        GetNode<Button>("Panel/VBox/AboutButton").Pressed += OnAbout;
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
            // The setup picks now ride the Pending* statics across the scene change; play the optional opening cinematic
            // first (interactive New-Game path only), then boot the game. The cinematic is a presentation-only front step
            // (ADR-006): it changes nothing, and is NOT injected into GameController.StartNewGame — the fast path many L3
            // tests take stays intact. Skipped entirely when the player has turned the intro off.
            StartGameAfterIntro();
        });
    }

    /// <summary>
    /// Plays the skippable opening cinematic (86d3fq1kf) and then boots the game scene — the interactive New-Game
    /// hand-off. When the intro is off (<see cref="ShouldPlayIntro"/> false) this goes straight to the game so nothing
    /// is delayed. Otherwise an <see cref="OpeningCinematic"/> overlay is shown; its <see cref="OpeningCinematic.Finished"/>
    /// signal (fired when it plays through, is clicked past, skipped, or Esc'd) advances to the game exactly once.
    /// </summary>
    private void StartGameAfterIntro()
    {
        if (!ShouldPlayIntro())
        {
            GetTree().ChangeSceneToFile(GameScenePath);
            return;
        }
        var cinematic = new OpeningCinematic { Name = "OpeningCinematic" };
        cinematic.Finished += () => GetTree().ChangeSceneToFile(GameScenePath);
        AddChild(cinematic);
        cinematic.Play();
    }

    /// <summary>
    /// Whether the interactive New-Game path should play the opening cinematic (86d3fq1kf): true when the Settings
    /// autoload's "Play intro" option (<see cref="GameLogic.App.SettingsModel.PlayIntro"/>) is on. When the autoload is
    /// absent (a bare test scene with no <c>/root/Settings</c>) this is false, so nothing is delayed and the game boots
    /// straight away. Exposed <c>internal</c> so the L3 suite can assert the gate decision without firing the scene
    /// change (the project's test-seam convention — the actual navigation stays untested to avoid freeing the scene out
    /// from under the runner, matching <see cref="MainMenuTests"/>).
    /// </summary>
    internal bool ShouldPlayIntro() =>
        GetNodeOrNull<SettingsService>("/root/Settings")?.Settings.PlayIntro ?? false;

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

    /// <summary>Opens the in-game help / tutorial screen as an overlay; removes it again when the player presses Back.</summary>
    private void OnHelp()
    {
        var help = GD.Load<PackedScene>(HelpScenePath).Instantiate<HelpPanel>();
        help.Closed += help.QueueFree;
        AddChild(help);
    }

    /// <summary>Opens the About / version / license screen as an overlay; removes it again when the player presses Back.</summary>
    private void OnAbout()
    {
        var about = GD.Load<PackedScene>(AboutScenePath).Instantiate<AboutPanel>();
        about.Closed += about.QueueFree;
        AddChild(about);
    }

    /// <summary>
    /// Exits the application. No confirmation is shown from the title screen: no game is in progress here, so there is
    /// nothing unsaved to lose. (The in-game quit paths in <see cref="PauseMenu"/> do confirm.)
    /// </summary>
    private void OnQuit() => GetTree().Quit();
}
