using System;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The in-game pause overlay, summoned with Esc and shown over a paused game: <b>Resume</b>, <b>Save Game</b>,
/// <b>Load Game</b>, <b>Settings</b>, <b>Quit to Main Menu</b>, <b>Quit to Desktop</b>. It lives in the game scene's UI
/// layer, hidden until summoned. While open it pauses the scene tree (<c>GetTree().Paused</c>) and keeps processing
/// input itself (<c>ProcessMode = Always</c>, set in the scene) so the game underneath is frozen while the menu still
/// responds.
/// </summary>
/// <remarks>
/// Presentation-only (ADR-006): it owns no game rules. Same parchment/wood look as the main menu. <b>Settings</b> and
/// <b>Save/Load</b> open <see cref="SettingsScreen"/> / <see cref="SaveLoadDialog"/> as overlays (the game stays
/// paused underneath); save/load delegate to the host <see cref="GameController"/>, with an <see cref="InfoPopup"/>
/// confirming the result.
/// </remarks>
public partial class PauseMenu : Control
{
    private const string SettingsScenePath = "res://scenes/SettingsScreen.tscn";

    private Control? _overlay;

    /// <summary>The host game controller (this menu's scene root) and the UI layer it lives in.</summary>
    private GameController Game => (GameController)Owner;
    private CanvasLayer Ui => (CanvasLayer)GetParent();

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
        GetNode<Button>("Panel/VBox/SaveButton").Pressed += OnSave;
        GetNode<Button>("Panel/VBox/LoadButton").Pressed += OnLoad;
        GetNode<Button>("Panel/VBox/SettingsButton").Pressed += OnSettings;
        GetNode<Button>("Panel/VBox/QuitToMenuButton").Pressed += QuitToMenu;
        GetNode<Button>("Panel/VBox/QuitToDesktopButton").Pressed += () => GetTree().Quit();
        Hide();
    }

    /// <summary>Esc toggles the menu — open it over the paused game, or resume if it is already open (ignored while a sub-overlay is up; use its own Back button).</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_overlay is not null || !@event.IsActionPressed("ui_cancel"))
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

    private void OnSettings() => TrackOverlay(GD.Load<PackedScene>(SettingsScenePath).Instantiate<SettingsScreen>());

    private void OnSave()
    {
        var dialog = OpenDialog();
        dialog.Open(SaveLoadDialog.Mode.Save, path =>
        {
            Game.SaveTo(path);
            InfoPopup popup = InfoPopup.Show(Ui, "Game saved", "Your game has been saved.");
            popup.ProcessMode = ProcessModeEnum.Always; // the game is still paused behind the popup
        });
    }

    private void OnLoad()
    {
        var dialog = OpenDialog();
        dialog.Open(SaveLoadDialog.Mode.Load, path =>
        {
            Game.LoadFrom(path);
            Resume(); // unpause + hide the pause menu; the loaded game is now live
            InfoPopup.Show(Ui, "Game loaded", "Your saved game has been loaded.");
        });
    }

    private SaveLoadDialog OpenDialog()
    {
        var dialog = GD.Load<PackedScene>(SaveLoadDialog.ScenePath).Instantiate<SaveLoadDialog>();
        TrackOverlay(dialog); // added to the UI layer; its _Ready runs here so Open() can resolve its nodes
        return dialog;
    }

    // Shows a sub-overlay (settings or save/load) over the paused game: keeps it processing while paused, frees it on
    // its Closed signal, and parks Esc until it is gone so Esc can't dismiss the pause menu out from under it.
    private void TrackOverlay(Control overlay)
    {
        overlay.ProcessMode = ProcessModeEnum.Always;
        overlay.Connect("Closed", Callable.From(() => { overlay.QueueFree(); _overlay = null; }));
        _overlay = overlay;
        Ui.AddChild(overlay);
    }

    private void QuitToMenu()
    {
        GetTree().Paused = false; // the menu scene must run un-paused
        GetTree().ChangeSceneToFile(MainMenu.MenuScenePath);
    }
}
