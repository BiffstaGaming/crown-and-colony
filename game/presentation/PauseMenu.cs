using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The in-game pause overlay, summoned with Esc and shown over a paused game: <b>Resume</b>, <b>Settings</b>,
/// <b>Quit to Main Menu</b>, <b>Quit to Desktop</b>. It lives in the game scene's UI layer, hidden until summoned.
/// While open it pauses the scene tree (<c>GetTree().Paused</c>) and keeps processing input itself
/// (<c>ProcessMode = Always</c>, set in the scene) so the game underneath is frozen while the menu still responds.
/// </summary>
/// <remarks>
/// Presentation-only (ADR-006): it owns no game rules. Same parchment/wood look as the main menu; <b>Settings</b>
/// reuses <see cref="SettingsScreen"/> as an overlay so the (paused) game is preserved underneath.
/// </remarks>
public partial class PauseMenu : Control
{
    private const string SettingsScenePath = "res://scenes/SettingsScreen.tscn";

    private SettingsScreen? _settings;

    /// <summary>Builds the look and wires the buttons; starts hidden (the game runs un-paused until Esc).</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get();
        GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        if (ColonyArt.ColonyBorder() is { } border)
        {
            GetNode<NinePatchRect>("Border").Texture = border;
        }
        GetNode<Button>("Panel/VBox/ResumeButton").Pressed += Resume;
        GetNode<Button>("Panel/VBox/SettingsButton").Pressed += OpenSettings;
        GetNode<Button>("Panel/VBox/QuitToMenuButton").Pressed += QuitToMenu;
        GetNode<Button>("Panel/VBox/QuitToDesktopButton").Pressed += () => GetTree().Quit();
        Hide();
    }

    /// <summary>Esc toggles the menu — open it over the paused game, or resume if it is already open (ignored while the settings overlay is up; use its Back button).</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_settings is not null || !@event.IsActionPressed("ui_cancel"))
        {
            return;
        }
        if (Visible)
        {
            Resume();
        }
        else
        {
            Open();
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Shows the menu and pauses the game.</summary>
    public void Open()
    {
        GetTree().Paused = true;
        Show();
    }

    /// <summary>Hides the menu and unpauses the game.</summary>
    public void Resume()
    {
        GetTree().Paused = false;
        Hide();
    }

    private void OpenSettings()
    {
        _settings = GD.Load<PackedScene>(SettingsScenePath).Instantiate<SettingsScreen>();
        _settings.ProcessMode = ProcessModeEnum.Always; // keep responding while the tree is paused
        _settings.Closed += CloseSettings;
        AddChild(_settings); // drawn on top of the pause panel; the game stays paused underneath
    }

    private void CloseSettings()
    {
        _settings?.QueueFree();
        _settings = null;
    }

    private void QuitToMenu()
    {
        GetTree().Paused = false; // the menu scene must run un-paused
        GetTree().ChangeSceneToFile(MainMenu.MenuScenePath);
    }
}
