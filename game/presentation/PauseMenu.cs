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
    private const string AboutScenePath = "res://scenes/AboutPanel.tscn";
    private const string HelpScenePath = "res://scenes/HelpPanel.tscn";

    private Control? _overlay;
    private ConfirmationDialog? _quitConfirm;

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
        GetNode<Button>("Panel/VBox/HelpButton").Pressed += OnHelp;
        GetNode<Button>("Panel/VBox/AboutButton").Pressed += OnAbout;
        BuildRetireButton(); // a Retire item, built in code (no scene edit), placed above the Quit choices
        GetNode<Button>("Panel/VBox/QuitToMenuButton").Pressed += OnQuitToMenu;
        GetNode<Button>("Panel/VBox/QuitToDesktopButton").Pressed += OnQuitToDesktop;
        Hide();
    }

    private Button _retireButton = null!;

    /// <summary>
    /// Adds the <b>Retire</b> menu item in code (FreeCol's <c>RetireAction</c>, 86d3fq125): inserted just above the Quit
    /// choices, it confirms then ends the game for the human, recording their high score via
    /// <see cref="GameController.RequestRetire"/>. Built programmatically (the scene is not edited); its enabled state
    /// tracks <see cref="GameController.CanRetire"/> each time the menu opens, so it greys out once the game has ended.
    /// </summary>
    private void BuildRetireButton()
    {
        var vbox = GetNode<VBoxContainer>("Panel/VBox");
        _retireButton = new Button { Name = "RetireButton", Text = "Retire" };
        _retireButton.Pressed += OnRetire;
        vbox.AddChild(_retireButton);
        // Place it directly above the Quit-to-Menu button (FreeCol groups Retire with the end-of-game actions).
        int quitIndex = GetNode<Button>("Panel/VBox/QuitToMenuButton").GetIndex();
        vbox.MoveChild(_retireButton, quitIndex);
    }

    /// <summary>Confirms before retiring (it ends the game — like quitting, an irreversible end-state), then hands off to the host's <see cref="GameController.RequestRetire"/>; the end-game UI takes over and the menu resumes/closes.</summary>
    private void OnRetire() => ConfirmQuit(
        "Retire from the colony? This records your score and ends the game.",
        () =>
        {
            Game.RequestRetire();
            Resume(); // unpause + close the menu; the end-game (game-over) screen now governs the board
        });

    /// <summary>Esc toggles the menu — open it over the paused game, or resume if it is already open (ignored while a sub-overlay is up; use its own Back button).</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_overlay is not null || _quitConfirm is not null || !@event.IsActionPressed("ui_cancel"))
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
        _retireButton.Disabled = !Game.CanRetire; // greyed out once the game has ended (already retired/defeated/won)
        Show();
    }

    /// <summary>Hides the menu and unpauses the game.</summary>
    public void Resume()
    {
        GetTree().Paused = false;
        Hide();
    }

    private void OnSettings() => TrackOverlay(GD.Load<PackedScene>(SettingsScenePath).Instantiate<SettingsScreen>());

    /// <summary>
    /// Opens the save dialog directly over a paused game — the <b>Ctrl+S hotkey path</b> (86d3f0vjg). Pauses the tree
    /// (so the game freezes behind the dialog, exactly as the Save menu item does) without showing the full pause menu;
    /// choosing a slot saves and the dialog closes, then <see cref="Resume"/> unpauses. Mirrors FreeCol's Save action.
    /// </summary>
    public void OpenSave()
    {
        GetTree().Paused = true;
        OnSave();
    }

    /// <summary>
    /// Opens the load dialog directly over a paused game — the <b>Ctrl+O hotkey path</b> (86d3f0vjg). Pauses the tree
    /// without showing the full pause menu; loading a slot resumes the live game. Mirrors FreeCol's Open action.
    /// </summary>
    public void OpenLoad()
    {
        GetTree().Paused = true;
        OnLoad();
    }

    private void OnAbout() => TrackOverlay(GD.Load<PackedScene>(AboutScenePath).Instantiate<AboutPanel>());

    /// <summary>Opens the in-game help / tutorial screen over the paused game; its Back button closes it (the game stays paused).</summary>
    private void OnHelp() => TrackOverlay(GD.Load<PackedScene>(HelpScenePath).Instantiate<HelpPanel>());

    private void OnSave()
    {
        var dialog = OpenDialog();
        dialog.Open(SaveLoadDialog.Mode.Save, path =>
        {
            Game.SaveTo(path);
            InfoPopup popup = InfoPopup.Show(Ui, "Game saved", "Your game has been saved.");
            popup.ProcessMode = ProcessModeEnum.Always; // the game is still paused behind the popup
        });
        // The save dialog leaves the game paused behind it (mirrors the menu path); its Closed handler frees it. When
        // it was opened by the Ctrl+S hotkey (the pause menu itself is hidden), unpause once the dialog closes so play
        // resumes — the menu's own flow keeps the menu visible, so this only matters for the hotkey entry.
        ResumeAfter(dialog);
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
        ResumeAfter(dialog);
    }

    // If the save/load dialog is dismissed (Back, or after a save) while the pause menu itself is hidden — i.e. it was
    // summoned by the Ctrl+S/Ctrl+O hotkey rather than the menu — unpause the game so play resumes. When the menu is
    // visible (the normal Esc → Save flow) the game must stay paused, so this is a no-op in that case.
    private void ResumeAfter(SaveLoadDialog dialog) =>
        dialog.Connect("Closed", Callable.From(() => { if (!Visible) { GetTree().Paused = false; } }));

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

    /// <summary>
    /// Confirms before leaving the in-progress game for the title screen — quitting to the menu discards any unsaved
    /// progress, so we ask first (Cancel is a no-op; the game stays paused with the pause menu still up).
    /// </summary>
    private void OnQuitToMenu() => ConfirmQuit("Quit to the main menu without saving?", QuitToMenu);

    /// <summary>
    /// Confirms before closing the application from within a game — same rationale as <see cref="OnQuitToMenu"/>:
    /// unsaved progress would be lost. Cancel is a no-op.
    /// </summary>
    private void OnQuitToDesktop() => ConfirmQuit("Quit to desktop without saving?", () => GetTree().Quit());

    private void QuitToMenu()
    {
        GetTree().Paused = false; // the menu scene must run un-paused
        GetTree().ChangeSceneToFile(MainMenu.MenuScenePath);
    }

    /// <summary>
    /// Shows a modal "Quit without saving?" confirmation over the paused game; <paramref name="onConfirm"/> runs only
    /// if the player confirms, Cancel just frees the dialog (a no-op). Tracked in <see cref="_quitConfirm"/> so Esc
    /// can't dismiss the pause menu out from under it, and given <c>ProcessMode = Always</c> so it works while the tree
    /// is paused. Reuses Godot's <see cref="ConfirmationDialog"/> (a <c>Window</c>, so it can't share the
    /// <see cref="_overlay"/> <c>Control</c> slot) — same pattern as the in-game land-claim prompt in
    /// <see cref="GameController"/>.
    /// </summary>
    private void ConfirmQuit(string prompt, System.Action onConfirm)
    {
        var dialog = new ConfirmationDialog
        {
            Title = "Quit without saving?",
            DialogText = prompt,
            OkButtonText = "Quit",
            CancelButtonText = "Cancel",
            Theme = ColonyTheme.Get(),
        };
        dialog.ProcessMode = ProcessModeEnum.Always; // the game (and pause menu) stay paused behind it
        dialog.Confirmed += () => { dialog.QueueFree(); _quitConfirm = null; onConfirm(); };
        dialog.Canceled += () => { dialog.QueueFree(); _quitConfirm = null; };
        _quitConfirm = dialog; // park Esc while the prompt is up
        Ui.AddChild(dialog);
        dialog.PopupCentered();
    }
}
