using System.Collections.Generic;
using CrownAndColony.GameLogic.Audio;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Global autoload (<c>/root/Music</c>) that plays looping background music — and per-nation anthems — on the
/// <c>Music</c> audio bus. Game/UI code asks for a logical <see cref="MusicContext"/> (e.g.
/// <see cref="MusicContext.Background"/>) or a nation anthem; this service resolves it to clip paths via the engine-free
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

    /// <summary>
    /// Starts (or restarts) the looping background playlist for <paramref name="context"/> (default
    /// <see cref="MusicContext.Background"/>). Re-shuffles the order each time it is started. No-op if the context has no
    /// tracks. Call this on the main menu and when a game begins; it keeps looping until replaced.
    /// </summary>
    public void PlayBackground(MusicContext context = MusicContext.Background)
    {
        _anthemPlaying = false;
        BuildShuffledPlaylist(context);
        if (_playlist.Count == 0)
        {
            return;
        }
        _playlistPos = 0;
        PlayCurrentPlaylistTrack();
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
