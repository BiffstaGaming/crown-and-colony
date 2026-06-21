using System.Collections.Generic;
using CrownAndColony.GameLogic.Audio;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Global autoload (<c>/root/Sound</c>) that plays one-shot sound effects for game events. Game code asks for a logical
/// <see cref="SoundEvent"/> (e.g. <see cref="SoundEvent.ColonyFounded"/>); this service resolves it to a clip via the
/// engine-free <see cref="SoundEventCatalog"/>, and plays it on the <c>SFX</c> audio bus so the settings volume slider
/// applies. It keeps a small pool of <see cref="AudioStreamPlayer"/> voices so overlapping cues don't cut each other off
/// (ADR-006: logic decides <em>what</em> to cue; this layer decides <em>how</em> it sounds).
/// </summary>
public partial class SoundService : Node
{
    private const string SfxBus = "SFX";

    // A handful of voices is plenty for UI/event SFX; the oldest is reused if all are busy (round-robin).
    private const int VoiceCount = 6;

    private readonly List<AudioStreamPlayer> _voices = new();
    private readonly Dictionary<SoundEvent, AudioStream> _streams = new();
    private int _nextVoice;

    /// <summary>On startup: preload every catalogued clip and spin up the voice pool routed to the SFX bus.</summary>
    public override void _Ready()
    {
        PreloadClips();

        for (int i = 0; i < VoiceCount; i++)
        {
            var player = new AudioStreamPlayer { Bus = SfxBus };
            AddChild(player);
            _voices.Add(player);
        }
    }

    /// <summary>
    /// Plays the clip mapped to <paramref name="evt"/> on a free voice (round-robin). No-op (with a warning) if the clip
    /// failed to load. Safe to call from any game event; never throws.
    /// </summary>
    public void Play(SoundEvent evt)
    {
        if (!_streams.TryGetValue(evt, out AudioStream? stream) || stream is null)
        {
            GD.PushWarning($"SoundService: no loaded clip for {evt}; skipping.");
            return;
        }

        AudioStreamPlayer voice = _voices[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _voices.Count;
        voice.Stream = stream;
        voice.Play();
    }

    // Loads every clip in the catalog once at startup. A missing file logs a warning but never crashes the game —
    // a muted event is always preferable to a hard failure (ADR-006).
    private void PreloadClips()
    {
        foreach (KeyValuePair<SoundEvent, string> entry in SoundEventCatalog.All)
        {
            var stream = ResourceLoader.Load(entry.Value) as AudioStream;
            if (stream is null)
            {
                GD.PushWarning($"SoundService: could not load clip '{entry.Value}' for {entry.Key}.");
                continue;
            }
            _streams[entry.Key] = stream;
        }
    }
}
