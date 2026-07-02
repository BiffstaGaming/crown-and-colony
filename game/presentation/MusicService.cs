using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Audio;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Global autoload (<c>/root/Music</c>) that plays looping background music — and per-nation anthems — on the
/// <c>Music</c> audio bus. Game/UI code asks for a logical <see cref="MusicContext"/> (e.g.
/// <see cref="MusicContext.InGamePeace"/>) or a nation anthem; this service resolves it to clip paths via the engine-free
/// <see cref="MusicTrackCatalog"/> and streams them on the Music bus so the settings Music-volume slider applies
/// (ADR-006: logic decides <em>what</em> mood to cue; this layer decides <em>how</em> it sounds).
/// </summary>
/// <remarks>
/// One <see cref="AudioStreamPlayer"/> carries the music. The background context is a shuffled playlist that
/// cross-cycles forever (when one track finishes, the next plays), mirroring FreeCol's looping default playlist. An
/// anthem is a one-shot interruption: it plays once, then the background playlist resumes. Missing clips warn and are
/// skipped rather than crashing — silence always beats a hard failure (ADR-006).
/// </remarks>
public partial class MusicService : Node
{
    private const string MusicBus = "Music";

    private AudioStreamPlayer _player = null!;

    // The active background playlist (resolved track paths) and our position in the shuffled order.
    private readonly List<string> _playlist = new();
    private int _playlistPos;

    // The context the background playlist is currently playing — so SetContext can skip a redundant restart (which would
    // re-shuffle and cut the current track) when the mood has not actually changed (e.g. menu → in-game, same playlist).
    // The autoload boots on the main menu (project.godot run/main_scene), so the initial context is Menu.
    private MusicContext _context = MusicContext.Menu;

    // True while a one-shot anthem is playing; when it finishes we fall back to the background playlist.
    private bool _anthemPlaying;

    // The deterministic-enough cycle uses Godot's RNG only for menu/ambience shuffling — it has no gameplay effect, so
    // it is intentionally outside the seeded game RNG (ADR-009 concerns simulation determinism, not cosmetic audio order).
    private readonly RandomNumberGenerator _rng = new();

    /// <summary>On startup: create the Music-bus player, wire the track-finished handler, and begin the background playlist.</summary>
    public override void _Ready()
    {
        _rng.Randomize();
        _player = new AudioStreamPlayer { Bus = MusicBus };
        _player.Finished += OnTrackFinished;
        AddChild(_player);

        PlayBackground();
    }

    /// <summary>The context whose playlist is currently loaded — read by game code and L3 tests to verify the wiring.</summary>
    public MusicContext CurrentContext => _context;

    /// <summary>
    /// Starts (or restarts) the looping background playlist for <paramref name="context"/> (default
    /// <see cref="MusicContext.Menu"/>, the boot screen's context). Re-shuffles the order each time it is started.
    /// No-op if the context has no tracks. Call this on the main menu and when a game begins; it keeps looping until
    /// replaced.
    /// </summary>
    public void PlayBackground(MusicContext context = MusicContext.Menu)
    {
        _anthemPlaying = false;
        _context = context;
        BuildShuffledPlaylist(context);
        if (_playlist.Count == 0)
        {
            return;
        }
        _playlistPos = 0;
        PlayCurrentPlaylistTrack();
    }

    /// <summary>
    /// Switches the background music to match a game state — the menu vs in-game-at-peace vs the war context
    /// (86d3fq1wy). Restarts the playlist (re-shuffled) only when the <em>audible</em> playlist actually changes:
    /// a same-context call is a no-op, and a context whose catalog playlist is identical to the current one (Menu ↔
    /// <see cref="MusicContext.InGamePeace"/> share FreeCol's single background bed) is <em>relabelled</em> without
    /// interrupting the current track. Call this from <c>GameController</c>/<c>MainMenu</c> as the game state changes.
    /// </summary>
    /// <remarks>
    /// Accepted edge: if a one-shot anthem is mid-play when the playlist genuinely changes (e.g. war breaks out during
    /// the game-start anthem), the anthem is cut in favour of the new bed — war music trumps ceremony.
    /// </remarks>
    public void SetContext(MusicContext context)
    {
        if (context == _context && _player.Playing)
        {
            return; // same mood, music already running → leave the current track alone
        }
        if (_player.Playing
            && MusicTrackCatalog.TryGetPlaylist(context, out IReadOnlyList<string> next)
            && MusicTrackCatalog.TryGetPlaylist(_context, out IReadOnlyList<string> current)
            && next.SequenceEqual(current))
        {
            _context = context; // identical playlist → relabel only, keep the current track playing (seamless menu→game)
            return;
        }
        PlayBackground(context);
    }

    /// <summary>
    /// Plays the national anthem for <paramref name="nationId"/> once (e.g. on declaring independence or starting a
    /// game), then resumes the background playlist when it ends. No-op (background keeps playing) if FreeCol ships no
    /// anthem for that nation — natives, REF powers, or an unknown id.
    /// </summary>
    public void PlayAnthem(string? nationId)
    {
        if (!MusicTrackCatalog.TryGetAnthem(nationId, out string path))
        {
            return; // no anthem for this nation — leave the background music running
        }

        var stream = ResourceLoader.Load(path) as AudioStream;
        if (stream is null)
        {
            GD.PushWarning($"MusicService: could not load anthem '{path}' for {nationId}.");
            return;
        }

        _anthemPlaying = true;
        _player.Stream = stream;
        _player.Play();
    }

    /// <summary>Stops all music (e.g. for a cutscene). The background playlist resumes on the next <see cref="PlayBackground"/>.</summary>
    public void Stop()
    {
        _anthemPlaying = false;
        _player.Stop();
    }

    // When the current stream finishes: if it was a one-shot anthem, return to the background playlist; otherwise
    // advance to the next playlist track (wrapping), so the background bed loops forever.
    private void OnTrackFinished()
    {
        if (_anthemPlaying)
        {
            _anthemPlaying = false;
            PlayCurrentPlaylistTrack(); // resume where the background playlist left off
            return;
        }

        if (_playlist.Count == 0)
        {
            return;
        }
        _playlistPos = (_playlistPos + 1) % _playlist.Count;
        PlayCurrentPlaylistTrack();
    }

    private void PlayCurrentPlaylistTrack()
    {
        if (_playlist.Count == 0)
        {
            return;
        }

        var stream = ResourceLoader.Load(_playlist[_playlistPos]) as AudioStream;
        if (stream is null)
        {
            GD.PushWarning($"MusicService: could not load track '{_playlist[_playlistPos]}'; skipping.");
            // Skip the bad track so a single missing file can't stall the whole playlist.
            _playlistPos = (_playlistPos + 1) % _playlist.Count;
            if (_playlistPos != 0) // guard against an all-missing playlist looping forever
            {
                PlayCurrentPlaylistTrack();
            }
            return;
        }

        _player.Stream = stream;
        _player.Play();
    }

    // Copy the catalog's playlist and Fisher–Yates shuffle it (cosmetic ordering only — see _rng note).
    private void BuildShuffledPlaylist(MusicContext context)
    {
        _playlist.Clear();
        if (!MusicTrackCatalog.TryGetPlaylist(context, out IReadOnlyList<string> tracks))
        {
            return;
        }
        _playlist.AddRange(tracks);
        for (int i = _playlist.Count - 1; i > 0; i--)
        {
            int j = (int)(_rng.Randi() % (uint)(i + 1));
            (_playlist[i], _playlist[j]) = (_playlist[j], _playlist[i]);
        }
    }
}
