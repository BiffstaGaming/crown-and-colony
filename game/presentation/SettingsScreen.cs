using CrownAndColony.GameLogic.App;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The settings / options screen, reached from the main menu's Settings button. It edits the live application
/// settings held by the <see cref="SettingsService"/> autoload — every change applies to the engine immediately;
/// <b>Back</b> saves to disk and returns to the menu. Same FreeCol map backdrop + parchment/wood framing as the main
/// menu (presentation-only, ADR-006).
/// </summary>
public partial class SettingsScreen : Control
{
    private const string BackdropPath = "res://assets/freecol/ui/map.jpg";

    private SettingsService _service = null!;
    private OptionButton _windowMode = null!;
    private CheckButton _vsync = null!;
    private HSlider _master = null!;
    private HSlider _music = null!;
    private HSlider _sfx = null!;
    private Label _masterValue = null!;
    private Label _musicValue = null!;
    private Label _sfxValue = null!;
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
        _master = GetNode<HSlider>("Panel/VBox/MasterRow/MasterSlider");
        _music = GetNode<HSlider>("Panel/VBox/MusicRow/MusicSlider");
        _sfx = GetNode<HSlider>("Panel/VBox/SfxRow/SfxSlider");
        _masterValue = GetNode<Label>("Panel/VBox/MasterRow/MasterValue");
        _musicValue = GetNode<Label>("Panel/VBox/MusicRow/MusicValue");
        _sfxValue = GetNode<Label>("Panel/VBox/SfxRow/SfxValue");

        Populate(_service.Settings);

        _windowMode.ItemSelected += OnWindowMode;
        _vsync.Toggled += OnVSync;
        _master.ValueChanged += v => OnVolume(s => s.MasterVolume = (float)v, v, _masterValue);
        _music.ValueChanged += v => OnVolume(s => s.MusicVolume = (float)v, v, _musicValue);
        _sfx.ValueChanged += v => OnVolume(s => s.SfxVolume = (float)v, v, _sfxValue);
        GetNode<Button>("Panel/VBox/BackButton").Pressed += OnBack;
    }

    private void Populate(SettingsModel s)
    {
        _populating = true; // suppress the change handlers while we set control values programmatically
        _windowMode.Selected = (int)s.WindowMode;
        _vsync.ButtonPressed = s.VSync;
        _master.Value = s.MasterVolume;
        _music.Value = s.MusicVolume;
        _sfx.Value = s.SfxVolume;
        _masterValue.Text = Percent(s.MasterVolume);
        _musicValue.Text = Percent(s.MusicVolume);
        _sfxValue.Text = Percent(s.SfxVolume);
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

    private void OnVolume(System.Action<SettingsModel> set, double value, Label valueLabel)
    {
        valueLabel.Text = Percent((float)value);
        if (_populating)
        {
            return;
        }
        _service.UpdateAndApply(set);
    }

    /// <summary>Persists the settings and returns to the main menu.</summary>
    private void OnBack()
    {
        _service.Save();
        GetTree().ChangeSceneToFile(MainMenu.MenuScenePath);
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
