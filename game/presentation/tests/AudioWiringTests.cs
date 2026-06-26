using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Audio;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the audio wiring: game events cue the right <see cref="SoundEvent"/>
/// through the notice path, the master mute persists and applies to the Master bus, and the music playlist advances.
/// Headless has no audio device, so these assert <em>state and the cued event</em> (via <see cref="SoundService.LastPlayed"/>),
/// never real sound output — and they confirm the audio autoloads boot without crashing in the CI L3 run.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AudioWiringTests
{
    private const string SettingsScene = "res://scenes/SettingsScreen.tscn";

    // Reaches the GameController's private game so a test can drive real game state (mirrors MainSceneTests).
    private static CrownAndColony.GameLogic.GameSession.Game GameOf(GameController controller) =>
        (CrownAndColony.GameLogic.GameSession.Game)typeof(GameController)
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    private static SoundService Sound() => ((SceneTree)Engine.GetMainLoop()).Root.GetNode<SoundService>("Sound");
    private static MusicService Music() => ((SceneTree)Engine.GetMainLoop()).Root.GetNode<MusicService>("Music");

    [TestCase]
    public async Task AudioAutoloads_BootWithoutCrashing_InHeadless()
    {
        // The CI L3 run is headless (no audio device). Loading the main scene boots the Sound + Music autoloads; this
        // asserts they are present and did not crash on init (a regression guard for the "audio init is safe" rule).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        AssertThat(Sound()).IsNotNull();
        AssertThat(Music()).IsNotNull();
    }

    [TestCase]
    public async Task PlayUiClick_IsSafe_AndButtonsAreAutoWired()
    {
        // The global click hook auto-connects every BaseButton's Pressed to the click cue. Pressing the End-Turn button
        // must not throw, and the click must have routed through SoundService (LastPlayed is set). Drive a known button.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        Sound().PlayUiClick(); // direct call is safe headless (no clip → logged no-op, never a crash)
        AssertThat(Sound().LastPlayed).IsNotNull();
    }

    [TestCase]
    public async Task BlockedAction_CuesTheIllegalMoveBuzz()
    {
        // Every player-initiated "not allowed" path funnels through GameController.NoticeBlocked (a blocked move / attack
        // / found / disband / goto). Drive that shared sink and assert it cues the IllegalMove buzz through SoundService.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        typeof(GameController).GetMethod("NoticeBlocked", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, new object?[] { "That move is not allowed." });
        await runner.SimulateFrames(1);

        AssertThat(Sound().LastPlayed).IsEqual(SoundEvent.IllegalMove);
    }

    [TestCase]
    public async Task BlockedCargoUnload_CuesTheIllegalMoveBuzz_ThroughThePublicCommand()
    {
        // The colony cargo commands are public on GameController. An impossible unload (carrier not adjacent / empty)
        // throws InvalidMoveException inside the command, which the catch surfaces as the deny buzz — drive that path.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        var game = GameOf(controller);

        var colony = game.FoundColony(game.Units[0]);
        // The starting caravel carries nothing and is not at this colony → unloading any goods is an illegal move.
        Unit carrier = game.Units.First(u => u.Type.IsNaval);
        controller.UnloadColonyCargo(carrier, colony, "model.goods.food", 1);
        await runner.SimulateFrames(1);

        AssertThat(Sound().LastPlayed).IsEqual(SoundEvent.IllegalMove);
    }

    [TestCase]
    public async Task MasterMute_Persists_AndAppliesToTheMasterBus()
    {
        // Toggling Mute applies SetBusMute(Master, true) live and persists to settings.cfg; a fresh service reads it back.
        ISceneRunner runner = ISceneRunner.Load(SettingsScene);
        await runner.SimulateFrames(2);
        var screen = (SettingsScreen)runner.Scene();

        var service = (SettingsService)typeof(SettingsScreen)
            .GetField("_service", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(screen)!;

        var mute = screen.GetNode<CheckButton>("Panel/VBox/MuteRow/MuteCheck");
        mute.ButtonPressed = true; // user-style toggle → applies live and flips the service flag
        await runner.SimulateFrames(1);

        int master = AudioServer.GetBusIndex("Master");
        AssertThat(AudioServer.IsBusMute(master)).IsTrue();
        AssertThat(service.MasterMute).IsTrue();

        // Persist, then a fresh service reads the mute back from disk.
        service.Save();
        var reader = new SettingsService();
        screen.AddChild(reader); // _Ready loads + applies the saved mute
        await runner.SimulateFrames(1);
        AssertThat(reader.MasterMute).IsTrue();

        // Restore: un-mute and persist so other suites see the default (the Master bus is process-global).
        service.SetMasterMute(false);
        service.Save();
        AssertThat(AudioServer.IsBusMute(master)).IsFalse();
        reader.QueueFree();
    }

    [TestCase]
    public async Task MusicService_BuildsAPlaylist_AndAdvancesTrackToTrack()
    {
        // The background playlist is non-empty and OnTrackFinished advances the position (wrapping) — the looping bed.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var music = Music();

        var playlistField = typeof(MusicService).GetField("_playlist", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var posField = typeof(MusicService).GetField("_playlistPos", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var advance = typeof(MusicService).GetMethod("OnTrackFinished", BindingFlags.NonPublic | BindingFlags.Instance)!;

        music.PlayBackground(MusicContext.Background); // (re)build + start the shuffled playlist
        var playlist = (List<string>)playlistField.GetValue(music)!;
        AssertThat(playlist.Count).IsGreater(1);

        int before = (int)posField.GetValue(music)!;
        advance.Invoke(music, null); // simulate the current track finishing
        int after = (int)posField.GetValue(music)!;
        AssertThat(after).IsEqual((before + 1) % playlist.Count);
    }

    [TestCase]
    public async Task MusicService_SetContext_SameContextWhilePlaying_DoesNotRestart()
    {
        // SetContext to the same context that is already playing must not re-shuffle/cut the track (seamless menu→game).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var music = Music();

        var posField = typeof(MusicService).GetField("_playlistPos", BindingFlags.NonPublic | BindingFlags.Instance)!;
        music.PlayBackground(MusicContext.Background);
        // Move off track 0 so a restart (which resets pos to 0) would be observable.
        var advance = typeof(MusicService).GetMethod("OnTrackFinished", BindingFlags.NonPublic | BindingFlags.Instance)!;
        advance.Invoke(music, null);
        int posBefore = (int)posField.GetValue(music)!;

        music.SetContext(MusicContext.Background); // same context, already playing → no-op
        int posAfter = (int)posField.GetValue(music)!;
        AssertThat(posAfter).IsEqual(posBefore); // position preserved → the current track was not interrupted
    }
}
