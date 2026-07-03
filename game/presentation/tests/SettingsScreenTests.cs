using System.Threading.Tasks;
using CrownAndColony.GameLogic.App;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md): drive the real settings screen and the settings autoload inside the Godot
/// runtime — the controls render and populate, changing a volume applies to its audio bus, and settings persist to
/// (and reload from) <c>user://settings.cfg</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SettingsScreenTests
{
    private const string SettingsScene = "res://scenes/SettingsScreen.tscn";

    [TestCase]
    public async Task SettingsScreen_ShowsTitleAndControls()
    {
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        AssertThat(scene.GetNode<Label>("Panel/VBox/Title").Text).IsEqual("Settings");
        AssertThat(scene.GetNode<OptionButton>("Panel/VBox/WindowModeRow/WindowModeOption").ItemCount).IsEqual(2);
        AssertThat(scene.GetNode<CheckButton>("Panel/VBox/VSyncRow/VSyncCheck")).IsNotNull();
        AssertThat(scene.GetNode<HSlider>("Panel/VBox/MasterRow/MasterSlider")).IsNotNull();
        AssertThat(scene.GetNode<HSlider>("Panel/VBox/MusicRow/MusicSlider")).IsNotNull();
        AssertThat(scene.GetNode<HSlider>("Panel/VBox/SfxRow/SfxSlider")).IsNotNull();
        AssertThat(scene.GetNode<Button>("Panel/VBox/BackButton").Text).IsEqual("Back");
    }

    [TestCase]
    public async Task ChangingMasterSlider_AppliesToMasterBus_AndUpdatesItsLabel()
    {
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        var slider = scene.GetNode<HSlider>("Panel/VBox/MasterRow/MasterSlider");
        slider.Value = 0.5; // user-style change → applies live to the Master bus
        await runner.SimulateFrames(1);

        int master = AudioServer.GetBusIndex("Master");
        AssertThat(Mathf.IsEqualApprox(AudioServer.GetBusVolumeDb(master), Mathf.LinearToDb(0.5f))).IsTrue();
        AssertThat(scene.GetNode<Label>("Panel/VBox/MasterRow/MasterValue").Text).IsEqual("50%");
    }

    [TestCase]
    public async Task AudioBuses_Music_AndSfx_Exist()
    {
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2); // the autoload (or the screen's fallback service) creates these on _Ready

        AssertThat(AudioServer.GetBusIndex("Music")).IsGreater(0);
        AssertThat(AudioServer.GetBusIndex("SFX")).IsGreater(0);
    }

    [TestCase]
    public async Task SettingsScreen_HasAKeyBindingsButton()
    {
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        var button = scene.GetNode<Button>("Panel/VBox/KeyBindingsButton");
        AssertThat(button.Text).IsEqual("Key Bindings…");
    }

    [TestCase]
    public async Task KeyBindingsScreen_ListsActions_AndShowsTheirCurrentKeys()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/KeyBindingsScreen.tscn");
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        AssertThat(scene.GetNode<Label>("Panel/VBox/Title").Text).IsEqual("Key Bindings");
        // One row per rebindable action; the End-Turn row shows its default key (Enter).
        var endTurnValue = scene.GetNode<Label>("Panel/VBox/Scroll/Rows/Row_end_turn/Value");
        AssertThat(endTurnValue.Text).IsEqual("Enter");
        var centerValue = scene.GetNode<Label>("Panel/VBox/Scroll/Rows/Row_center_unit/Value");
        AssertThat(centerValue.Text).IsEqual("Ctrl+C");
    }

    [TestCase]
    public async Task KeyBindingsScreen_CaptureAndBack_PersistsTheOverride_AndReloads()
    {
        // 86d3f0wjj: clicking Rebind then pressing a key records the override; Back persists it to settings.cfg's
        // [keybindings] section, and a fresh load reads it back. Rebind quick_save (F5 → F6) so this doesn't disturb the
        // turn-advancing keys other suites rely on; restore the InputMap default in the finally.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/KeyBindingsScreen.tscn");
        await runner.SimulateFrames(2);
        var screen = (KeyBindingsScreen)runner.Scene();
        try
        {
            // Enter listening mode for quick_save, then press F6.
            screen.GetNode<Button>("Panel/VBox/Scroll/Rows/Row_quick_save/Rebind").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);
            runner.SimulateKeyPressed(Key.F6);
            await runner.SimulateFrames(1);

            // The row's value updated to the captured key.
            AssertThat(screen.GetNode<Label>("Panel/VBox/Scroll/Rows/Row_quick_save/Value").Text).IsEqual("F6");

            // Back persists; a fresh load from disk reads the override back.
            screen.GetNode<Button>("Panel/VBox/Buttons/BackButton").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            KeyBindingsModel reloaded = KeyBindingsService.Load();
            AssertThat(reloaded.HasOverride("quick_save")).IsTrue();
            AssertThat(reloaded.ChordFor("quick_save")).IsEqual(new KeyBindingsModel.KeyChord((long)Key.F6, false));
        }
        finally
        {
            // Restore: reset the override on disk + in the InputMap so other suites see the F5 default.
            var restore = KeyBindingsService.Load();
            KeyBindingsService.Rebind(restore, "quick_save", KeyBindingsModel.DefaultFor("quick_save"));
            KeyBindingsService.Save(restore);
        }
    }

    [TestCase]
    public async Task MessagePopupToggles_AreShown_OneCheckPerCategory()
    {
        // 86d3fq1tc: the settings screen carries a per-category "pop up" toggle row (built in code, no scene edit).
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        AssertThat(scene.GetNodeOrNull<Label>("Panel/VBox/MessagesHeader")).IsNotNull();
        // One (label+check) cell per MessageCategory, in the compact grid; spot-check the Diplomacy and Combat cells.
        AssertThat(scene.GetNodeOrNull<CheckButton>("Panel/VBox/MessagePopupGrid/MessagePopupRow_Diplomacy/PopupCheck")).IsNotNull();
        AssertThat(scene.GetNodeOrNull<CheckButton>("Panel/VBox/MessagePopupGrid/MessagePopupRow_Combat/PopupCheck")).IsNotNull();
    }

    [TestCase]
    public async Task UntickingAMessagePopupToggle_SilencesThatCategory_OnTheLiveSettings()
    {
        // Un-ticking a category's "pop up" box adds it to SilencedMessageCategories (logged-only); re-ticking removes it.
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();
        var service = scene.GetNodeOrNull<SettingsService>("/root/Settings");
        AssertThat(service).IsNotNull();
        service!.Settings.SilencedMessageCategories.Clear(); // start clean (the autoload is shared across the suite)

        var box = scene.GetNode<CheckButton>("Panel/VBox/MessagePopupGrid/MessagePopupRow_Economy/PopupCheck");
        AssertThat(box.ButtonPressed).IsTrue(); // ticked = pops up (the default)

        box.ButtonPressed = false;
        box.EmitSignal(BaseButton.SignalName.Toggled, false);
        await runner.SimulateFrames(1);
        AssertThat(service.Settings.SilencedMessageCategories.Contains(MessageCategory.Economy)).IsTrue(); // now silenced

        box.ButtonPressed = true;
        box.EmitSignal(BaseButton.SignalName.Toggled, true);
        await runner.SimulateFrames(1);
        AssertThat(service.Settings.SilencedMessageCategories.Contains(MessageCategory.Economy)).IsFalse(); // pops up again

        service.Settings.SilencedMessageCategories.Clear(); // leave it clean for other suites
    }

    // ── 86d3f62r5: the Game-section controls beyond the Master slider ────────────────────────────────────────────────

    [TestCase]
    public async Task ChangingAutosaveSpin_StoresThePeriodOnTheLiveSettings()
    {
        // 86d3f62r5: the autosave-period SpinBox was reachable but undriven by L3 (only the Master slider was). Driving
        // it stores the new period on the live settings model (read at turn end by the GameController).
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();
        SettingsService service = ServiceOf(scene);
        const int DefaultPeriod = 1; // SettingsModel's shipped default (SaveLoadTests.EndingATurn_WritesTheAutosave relies on it)
        try
        {
            var spin = scene.GetNode<SpinBox>("Panel/VBox/AutosaveRow/AutosaveSpin");
            spin.Value = 5;
            spin.EmitSignal(Godot.Range.SignalName.ValueChanged, 5.0); // user-style change → applies to the live model
            await runner.SimulateFrames(1);

            AssertThat(service.Settings.AutosavePeriod).IsEqual(5); // the period stored on the live settings
        }
        finally
        {
            // Restore the DEFAULT period on the shared autoload AND persist it, so neither the in-memory autoload nor the
            // on-disk settings.cfg leaks a non-default period into SaveLoadTests.EndingATurn_WritesTheAutosave (which
            // needs period 1 so a single turn-end writes the autosave). Restoring to the constant (not the pre-read
            // value) also self-heals a settings.cfg an earlier run may have left at 5.
            service.UpdateAndApply(s => s.AutosavePeriod = DefaultPeriod);
            service.Save();
        }
    }

    [TestCase]
    public async Task ChangingUiScaleSlider_AppliesTheScale_AndUpdatesItsLabel()
    {
        // 86d3f62r5: the UI-scale HSlider was reachable but undriven. Driving it stores + applies the new root
        // content-scale factor and updates the percent readout.
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();
        SettingsService service = ServiceOf(scene);

        var slider = scene.GetNode<HSlider>("Panel/VBox/UiScaleRow/UiScaleSlider");
        slider.Value = 1.5; // within [MinUiScale, MaxUiScale]
        slider.EmitSignal(Godot.Range.SignalName.ValueChanged, 1.5);
        await runner.SimulateFrames(1);

        AssertThat(Mathf.IsEqualApprox(service.Settings.UiScale, 1.5f)).IsTrue(); // stored + applied
        AssertThat(scene.GetNode<Label>("Panel/VBox/UiScaleRow/UiScaleValue").Text).IsEqual("150%");

        // Restore the default so this process-global content scale doesn't bleed into sibling scene suites.
        service.UpdateAndApply(s => s.UiScale = 1.0f);
    }

    [TestCase]
    public async Task TogglingColorblindCheck_FlipsTheAccessibilityPalette()
    {
        // 86d3f62r5: the colourblind CheckButton was reachable but undriven. Toggling it swaps the map palette via
        // AccessibilityPalette (the static flag the markers/minimap read on redraw).
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();
        SettingsService service = ServiceOf(scene);
        bool before = AccessibilityPalette.ColorblindMode;
        try
        {
            var check = scene.GetNode<CheckButton>("Panel/VBox/ColorblindRow/ColorblindCheck");
            check.ButtonPressed = true;
            check.EmitSignal(BaseButton.SignalName.Toggled, true);
            await runner.SimulateFrames(1);
            AssertThat(service.Settings.ColorblindMode).IsTrue();           // stored on the live model
            AssertThat(AccessibilityPalette.ColorblindMode).IsTrue();       // and applied to the palette flag

            check.ButtonPressed = false;
            check.EmitSignal(BaseButton.SignalName.Toggled, false);
            await runner.SimulateFrames(1);
            AssertThat(AccessibilityPalette.ColorblindMode).IsFalse();      // toggled back off
        }
        finally
        {
            // Restore whatever the palette flag was, so this process-global toggle can't bleed into sibling suites.
            service.UpdateAndApply(s => s.ColorblindMode = before);
        }
    }

    /// <summary>The <see cref="SettingsService"/> the screen actually uses (its private <c>_service</c> field — the autoload or the transient fallback), by reflection.</summary>
    private static SettingsService ServiceOf(Node scene) =>
        (SettingsService)scene.GetType()
            .GetField("_service", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(scene)!;

    [TestCase]
    public async Task Service_SaveThenLoad_RoundTripsThroughDisk()
    {
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(1);
        var host = runner.Scene();

        var writer = new SettingsService();
        host.AddChild(writer);
        await runner.SimulateFrames(1);
        writer.UpdateAndApply(s =>
        {
            s.MasterVolume = 0.33f;
            s.WindowMode = WindowMode.Fullscreen;
            s.VSync = false;
        });
        writer.Save();

        var reader = new SettingsService();
        host.AddChild(reader); // _Ready loads the just-saved file from disk
        await runner.SimulateFrames(1);

        AssertThat(reader.Settings.WindowMode).IsEqual(WindowMode.Fullscreen);
        AssertThat(reader.Settings.VSync).IsFalse();
        AssertThat(Mathf.IsEqualApprox(reader.Settings.MasterVolume, 0.33f)).IsTrue();

        writer.QueueFree();
        reader.QueueFree();
    }
}
