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
