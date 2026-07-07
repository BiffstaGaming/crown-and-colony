using CrownAndColony.GameLogic.App;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The settings / options screen, shown as an overlay by the main menu and the in-game pause menu. It edits the live
/// application settings held by the <see cref="SettingsService"/> autoload — every change applies to the engine
/// immediately; <b>Back</b> saves to disk and emits <see cref="Closed"/> for the host to dismiss it. Same FreeCol map
/// backdrop + parchment/wood framing as the main menu (presentation-only, ADR-006).
/// </summary>
public partial class SettingsScreen : Control
{
    /// <summary>Emitted when the player presses Back (after settings are saved). The host removes the overlay.</summary>
    [Signal]
    public delegate void ClosedEventHandler();

    private const string BackdropPath = "res://assets/freecol/ui/map.jpg";

    /// <summary>The key-rebinding screen, opened from the Key Bindings button as a child overlay.</summary>
    private const string KeyBindingsScenePath = "res://scenes/KeyBindingsScreen.tscn";

    private SettingsService _service = null!;
    private OptionButton _windowMode = null!;
    private OptionButton _mapView = null!; // isometric vs top-down (code-built, no scene edit)
    private CheckButton _vsync = null!;
    private CheckButton _mute = null!;
    private HSlider _master = null!;
    private HSlider _music = null!;
    private HSlider _sfx = null!;
    private Label _masterValue = null!;
    private Label _musicValue = null!;
    private Label _sfxValue = null!;
    private HSlider _uiScale = null!;
    private Label _uiScaleValue = null!;
    private CheckButton _colorblind = null!;
    private SpinBox _autosave = null!;
    private CheckButton _playIntro = null!;
    private bool _populating;

    /// <summary>Builds the look, resolves the settings service, populates the controls, and wires them.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get();
        if (ResourceLoader.Exists(BackdropPath))
        {
            GetNode<TextureRect>("Background").Texture = GD.Load<Texture2D>(BackdropPath);
        }
        GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        if (ColonyArt.ColonyBorder() is { } border)
        {
            GetNode<NinePatchRect>("Border").Texture = border;
        }

        _service = ResolveService();

        _windowMode = GetNode<OptionButton>("Panel/VBox/WindowModeRow/WindowModeOption");
        _windowMode.AddItem("Windowed", (int)WindowMode.Windowed);
        _windowMode.AddItem("Fullscreen", (int)WindowMode.Fullscreen);
        _vsync = GetNode<CheckButton>("Panel/VBox/VSyncRow/VSyncCheck");
        _mute = GetNode<CheckButton>("Panel/VBox/MuteRow/MuteCheck");
        _master = GetNode<HSlider>("Panel/VBox/MasterRow/MasterSlider");
        _music = GetNode<HSlider>("Panel/VBox/MusicRow/MusicSlider");
        _sfx = GetNode<HSlider>("Panel/VBox/SfxRow/SfxSlider");
        _masterValue = GetNode<Label>("Panel/VBox/MasterRow/MasterValue");
        _musicValue = GetNode<Label>("Panel/VBox/MusicRow/MusicValue");
        _sfxValue = GetNode<Label>("Panel/VBox/SfxRow/SfxValue");
        _uiScale = GetNode<HSlider>("Panel/VBox/UiScaleRow/UiScaleSlider");
        _uiScale.MinValue = SettingsModel.MinUiScale;
        _uiScale.MaxValue = SettingsModel.MaxUiScale;
        _uiScaleValue = GetNode<Label>("Panel/VBox/UiScaleRow/UiScaleValue");
        _colorblind = GetNode<CheckButton>("Panel/VBox/ColorblindRow/ColorblindCheck");
        _autosave = GetNode<SpinBox>("Panel/VBox/AutosaveRow/AutosaveSpin");
        _autosave.MinValue = 0;
        _autosave.MaxValue = SettingsModel.MaxAutosavePeriod;

        Populate(_service.Settings);

        _windowMode.ItemSelected += OnWindowMode;
        _vsync.Toggled += OnVSync;
        _mute.Toggled += OnMute;
        _master.ValueChanged += v => OnVolume(s => s.MasterVolume = (float)v, v, _masterValue);
        _music.ValueChanged += v => OnVolume(s => s.MusicVolume = (float)v, v, _musicValue);
        _sfx.ValueChanged += v => OnVolume(s => s.SfxVolume = (float)v, v, _sfxValue);
        _uiScale.ValueChanged += OnUiScale;
        _colorblind.Toggled += OnColorblind;
        _autosave.ValueChanged += OnAutosavePeriod;
        BuildTutorialToggle();  // the guided-intro tutorial-hints preference (86d3fq1h9), built in code (no scene edit)
        BuildPlayIntroToggle(); // the opening-cinematic toggle (86d3fq1kf), built in code (no scene edit)
        BuildMapViewOption();   // the map view: isometric diamonds vs top-down squares, built in code (no scene edit)
        BuildMessagePopupToggles(); // the per-category popup-vs-silent message preference (86d3fq1tc), built in code (no scene edit)
        GetNode<Button>("Panel/VBox/KeyBindingsButton").Pressed += OnKeyBindings;
        GetNode<Button>("Panel/VBox/BackButton").Pressed += OnBack;

        WrapContentInScroll(); // last: the settings list is taller than the panel — scroll it + pin the border (86d3jy0rn)
    }

    /// <summary>
    /// Fixes two things Chris hit in playtest (86d3jy0rn): the settings list is taller than the panel, which made the
    /// <see cref="PanelContainer"/> <b>grow past the fixed wood Border</b> (the frame no longer lined up) and <b>overflow a
    /// short window</b> so Key Bindings/Back were cut off and you had to resize the window to reach them. This reparents
    /// the content <see cref="VBoxContainer"/> into a <see cref="ScrollContainer"/> capped to the viewport height — so the
    /// panel now stays a fixed size (nothing cut off; the list scrolls when it doesn't fit) — and keeps the wood
    /// <c>Border</c> exactly on the panel's rect whenever it lays out, so the frame lines up at any window size. Run
    /// <b>last</b> in <see cref="_Ready"/> so every earlier <c>Panel/VBox/…</c> lookup still resolves; after this the VBox
    /// lives at <c>Panel/Scroll/VBox</c> (the L3 tests address it there).
    /// </summary>
    private void WrapContentInScroll()
    {
        var panel = GetNode<PanelContainer>("Panel");
        var vbox = GetNode<VBoxContainer>("Panel/VBox");
        var border = GetNode<NinePatchRect>("Border");

        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, // vertical scroll only
        };
        panel.RemoveChild(vbox);
        panel.AddChild(scroll);
        scroll.AddChild(vbox);
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        scroll.SizeFlagsVertical = SizeFlags.ExpandFill; // the scroll fills the panel; the VBox keeps its content height so it scrolls

        // Cap the panel to the *current* viewport height (less a margin) so it can never overflow the window — the scene's
        // panel offset was 660px (taller than a 600px window). Recomputed on every resize (not just once at load) so the
        // panel keeps fitting when Chris drags the window smaller as well as larger; the ScrollContainer supplies the
        // scrollbar whenever the list is taller than the fit.
        void FitPanel()
        {
            float half = Mathf.Max(220f, (GetViewportRect().Size.Y - 40f) / 2f);
            panel.OffsetTop = -half;
            panel.OffsetBottom = half;
            scroll.CustomMinimumSize = new Vector2(510, half * 2f - 28f); // fill the panel minus its parchment padding
        }

        // Keep the wood Border exactly on the panel's *rendered* rect (not its offset rect — a PanelContainer grows past its
        // offsets to fit its content, so an offset copy would leave the frame narrower than the panel and clip the labels).
        // Snap the border to the panel's live GlobalPosition/Size; re-run deferred after every fit so the panel has settled.
        void SyncBorder()
        {
            border.SetAnchorsPreset(LayoutPreset.TopLeft);
            border.GlobalPosition = panel.GlobalPosition;
            border.Size = panel.Size;
        }
        void OnResize() { FitPanel(); Callable.From(SyncBorder).CallDeferred(); }
        GetViewport().SizeChanged += OnResize; // keep fitting + re-framing as the window resizes
        FitPanel();
        Callable.From(SyncBorder).CallDeferred(); // frame once the first layout has settled
    }

    /// <summary>
    /// Builds the <b>Tutorial hints</b> toggle in code (no scene edit, like the message-popup toggles): a single
    /// <see cref="CheckButton"/> in a labelled row, inserted just above the Messages section. Ticked when the guided-intro
    /// tutorial is enabled (the default); un-ticking turns it off (the same client preference the card's "Skip tutorial"
    /// button flips). Toggling mutates <see cref="SettingsService.TutorialHints"/> through the service; Back persists it
    /// like every other setting. Presentation-only (ADR-006): a client preference, no game rule. The row is named
    /// <c>TutorialRow</c> and the check <c>TutorialCheck</c> so the L3 test can find them by a stable path.
    /// </summary>
    private void BuildTutorialToggle()
    {
        var vbox = GetNode<VBoxContainer>("Panel/VBox");
        int insertAt = GetNode<Button>("Panel/VBox/KeyBindingsButton").GetIndex();

        var row = new HBoxContainer { Name = "TutorialRow" };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label { Text = "Tutorial hints", CustomMinimumSize = new Vector2(160, 0) });
        var check = new CheckButton
        {
            Name = "TutorialCheck",
            ButtonPressed = _service.TutorialHints, // ticked = enabled (the default)
        };
        check.Toggled += OnTutorialHints;
        row.AddChild(check);
        vbox.AddChild(row);
        vbox.MoveChild(row, insertAt); // above the Messages header + Key Bindings button
    }

    // The tutorial-hints toggle: on = enabled, off = disabled. A client preference on the service (no engine effect to
    // apply); Back persists it like every other setting.
    private void OnTutorialHints(bool enabled)
    {
        if (_populating)
        {
            return;
        }
        _service.SetTutorialHints(enabled);
    }

    /// <summary>
    /// Builds the <b>Play intro</b> toggle in code (no scene edit, like the message-popup toggles): a labelled
    /// <see cref="CheckButton"/> placed in the <b>Game</b> section just under the autosave row (above Key Bindings). It
    /// is ticked when the opening cinematic (86d3fq1kf) plays on a new game (the default) and un-ticked to skip it. The
    /// wrapper row is named <c>PlayIntroRow</c> and the check <c>PlayIntroCheck</c> so the L3 test can address it by a
    /// stable path. Toggling it mutates the live <see cref="SettingsModel.PlayIntro"/> option through the service (Back
    /// persists it like every other setting). Presentation-only (ADR-006): a client preference, no game rule.
    /// </summary>
    private void BuildPlayIntroToggle()
    {
        var vbox = GetNode<VBoxContainer>("Panel/VBox");
        int insertAt = GetNode<Button>("Panel/VBox/KeyBindingsButton").GetIndex();

        var row = new HBoxContainer { Name = "PlayIntroRow" };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Label
        {
            Text = "Play opening intro",
            CustomMinimumSize = new Vector2(220, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        _playIntro = new CheckButton { Name = "PlayIntroCheck", ButtonPressed = _service.Settings.PlayIntro };
        _playIntro.Toggled += OnPlayIntro;
        row.AddChild(_playIntro);
        vbox.AddChild(row);
        vbox.MoveChild(row, insertAt); // directly under the autosave row, above the Key Bindings button
    }

    // The opening-cinematic toggle: on = play the intro for a new game, off = skip straight into the game. No engine
    // effect to apply (it is read by the main menu when starting a new game), so a plain set-mutate suffices; Back persists.
    private void OnPlayIntro(bool on)
    {
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(s => s.PlayIntro = on);
    }

    /// <summary>
    /// Builds the "Map view" dropdown in code (no scene edit): isometric diamonds (default) vs top-down squares. A client
    /// display preference applied to <c>MapView.TopDown</c> via the service; a running game redraws on the service's
    /// <c>Applied</c> signal. Presentation-only (ADR-006) — no game rule, no save.
    /// </summary>
    private void BuildMapViewOption()
    {
        var vbox = GetNode<VBoxContainer>("Panel/VBox");
        int insertAt = GetNode<Button>("Panel/VBox/KeyBindingsButton").GetIndex();

        var row = new HBoxContainer { Name = "MapViewRow" };
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Label
        {
            Text = "Map view",
            CustomMinimumSize = new Vector2(220, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        _mapView = new OptionButton { Name = "MapViewOption" };
        _mapView.AddItem("Isometric (diamond)", (int)MapViewStyle.Isometric);
        _mapView.AddItem("Top-down (square)", (int)MapViewStyle.TopDown);
        _mapView.Selected = (int)_service.Settings.MapViewStyle;
        _mapView.ItemSelected += OnMapView;
        row.AddChild(_mapView);
        vbox.AddChild(row);
        vbox.MoveChild(row, insertAt); // above the Key Bindings button, alongside the other display options
    }

    // The map-view dropdown: isometric vs top-down. UpdateAndApply pushes MapView.TopDown + fires the service's Applied
    // signal, so a running game redraws immediately (GameController re-syncs + re-centres). Back persists like the rest.
    private void OnMapView(long index)
    {
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(s => s.MapViewStyle = (MapViewStyle)(int)index);
    }

    // Display order + label for each message category's popup toggle (matches the MessageLogPanel filter order).
    private static readonly (MessageCategory Category, string Label)[] PopupCategories =
    {
        (MessageCategory.Combat, "Combat"),
        (MessageCategory.Diplomacy, "Diplomacy"),
        (MessageCategory.Economy, "Economy"),
        (MessageCategory.Natives, "Natives"),
        (MessageCategory.Monarch, "Monarch"),
        (MessageCategory.Colony, "Colony"),
    };

    /// <summary>
    /// Builds the <b>Messages</b> section in code (no scene edit, like the pause menu's Retire item): a section header and
    /// one <see cref="CheckButton"/> per <see cref="MessageCategory"/>, laid out in a compact two-column
    /// <see cref="GridContainer"/> (so the six toggles add only ~three rows of height) and inserted just above the Key
    /// Bindings button. Each box is ticked when that category <b>pops up</b> the end-of-turn message panel (the default)
    /// and un-ticked when it is <b>silenced</b> (logged only — it still appears in the re-openable message log, it just
    /// doesn't pop). Toggling a box mutates the live <see cref="SettingsModel.SilencedMessageCategories"/> client option
    /// through the service (Back persists it like every other setting). Presentation-only (ADR-006): a client preference,
    /// no game rule. Each category's <c>CheckButton</c> sits in a wrapper named <c>MessagePopupRow_{category}</c> so the
    /// L3 test can find it by a stable path.
    /// </summary>
    private void BuildMessagePopupToggles()
    {
        var vbox = GetNode<VBoxContainer>("Panel/VBox");
        int insertAt = GetNode<Button>("Panel/VBox/KeyBindingsButton").GetIndex();

        var header = new Label { Name = "MessagesHeader", Text = "Message pop-ups (off = log only)" };
        header.ThemeTypeVariation = "SectionHeader";
        vbox.AddChild(header);
        vbox.MoveChild(header, insertAt);

        // A 2-column grid of (label+check) cells → two categories per visual row, six categories in three rows.
        var grid = new GridContainer { Name = "MessagePopupGrid", Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach ((MessageCategory category, string label) in PopupCategories)
        {
            // Wrap each label+check pair so the test path MessagePopupRow_{category}/PopupCheck stays stable.
            var cell = new HBoxContainer { Name = $"MessagePopupRow_{category}" };
            cell.AddThemeConstantOverride("separation", 8);
            cell.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) });
            var check = new CheckButton
            {
                Name = "PopupCheck",
                ButtonPressed = !_service.Settings.SilencedMessageCategories.Contains(category), // ticked = pops up
            };
            MessageCategory captured = category;
            check.Toggled += pressed => OnMessagePopupToggled(captured, pressed);
            cell.AddChild(check);
            grid.AddChild(cell);
        }
        vbox.AddChild(grid);
        vbox.MoveChild(grid, insertAt + 1); // directly under the header, above Key Bindings
    }

    // A category's popup box toggled: ticked = pop up (remove from the silenced set), un-ticked = silence (log only). The
    // service clamps + (on Back) persists; no engine effect to apply, so a plain set-mutate via UpdateAndApply suffices.
    private void OnMessagePopupToggled(MessageCategory category, bool popUp)
    {
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(s =>
        {
            if (popUp)
            {
                s.SilencedMessageCategories.Remove(category);
            }
            else
            {
                s.SilencedMessageCategories.Add(category);
            }
        });
    }

    /// <summary>Opens the key-rebinding screen as a child overlay; removes it again when the player presses Back (which persists any rebinds).</summary>
    private void OnKeyBindings()
    {
        var screen = GD.Load<PackedScene>(KeyBindingsScenePath).Instantiate<KeyBindingsScreen>();
        screen.ProcessMode = ProcessModeEnum.Always; // works even if shown over a paused game (the pause-menu path)
        screen.Closed += screen.QueueFree;
        AddChild(screen);
    }

    private void Populate(SettingsModel s)
    {
        _populating = true; // suppress the change handlers while we set control values programmatically
        _windowMode.Selected = (int)s.WindowMode;
        if (_mapView is not null) // built after the first Populate (in _Ready); guarded so a re-show reflects the current value
        {
            _mapView.Selected = (int)s.MapViewStyle;
        }
        _vsync.ButtonPressed = s.VSync;
        _mute.ButtonPressed = _service.MasterMute; // mute is presentation state on the service, not the model
        _master.Value = s.MasterVolume;
        _music.Value = s.MusicVolume;
        _sfx.Value = s.SfxVolume;
        _masterValue.Text = Percent(s.MasterVolume);
        _musicValue.Text = Percent(s.MusicVolume);
        _sfxValue.Text = Percent(s.SfxVolume);
        _uiScale.Value = s.UiScale;
        _uiScaleValue.Text = Percent(s.UiScale);
        _colorblind.ButtonPressed = s.ColorblindMode;
        _autosave.Value = s.AutosavePeriod;
        _populating = false;
    }

    private void OnWindowMode(long index)
    {
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(s => s.WindowMode = (WindowMode)(int)index);
    }

    private void OnVSync(bool on)
    {
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(s => s.VSync = on);
    }

    // The master mute is presentation state on the service (not the engine-free SettingsModel): it silences the Master
    // bus live without disturbing the saved volume sliders. Back persists it like every other setting.
    private void OnMute(bool on)
    {
        if (_populating)
        {
            return;
        }
        _service.SetMasterMute(on);
    }

    private void OnVolume(System.Action<SettingsModel> set, double value, Label valueLabel)
    {
        valueLabel.Text = Percent((float)value);
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(set);
    }

    private void OnUiScale(double value)
    {
        _uiScaleValue.Text = Percent((float)value);
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(s => s.UiScale = (float)value); // applies the root content-scale live
    }

    private void OnColorblind(bool on)
    {
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(s => s.ColorblindMode = on); // swaps the map palette via AccessibilityPalette
        QueueRedraw();
    }

    // The autosave period (turns; 0 = off). No engine effect to apply — it is read at turn end by the GameController —
    // so we just store it on the live model (UpdateAndApply also clamps it); Back persists it like every other setting.
    private void OnAutosavePeriod(double value)
    {
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(s => s.AutosavePeriod = (int)value);
    }

    /// <summary>Persists the settings and asks the host to dismiss the screen (via the <see cref="Closed"/> signal).</summary>
    private void OnBack()
    {
        _service.Save();
        EmitSignal(SignalName.Closed);
    }

    private static string Percent(float linear) => $"{Mathf.RoundToInt(linear * 100f)}%";

    // Prefer the global autoload; fall back to a transient instance (added as a child so its _Ready loads + applies)
    // so the screen still works if it is ever shown without the autoload present.
    private SettingsService ResolveService()
    {
        if (GetNodeOrNull<SettingsService>("/root/Settings") is { } autoload)
        {
            return autoload;
        }
        var transient = new SettingsService { Name = "SettingsFallback" };
        AddChild(transient);
        return transient;
    }
}
