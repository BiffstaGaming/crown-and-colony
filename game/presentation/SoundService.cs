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
/// <remarks>
/// As a single global hook (rather than per-button wiring) it also gives every <see cref="BaseButton"/> in the scene
/// tree a UI <em>click</em> cue: it watches <see cref="SceneTree.NodeAdded"/> and connects each button's
/// <see cref="BaseButton.Pressed"/> signal to <see cref="PlayUiClick"/> as it enters the tree. The illegal-move
/// <em>buzz</em> is the existing <see cref="SoundEvent.IllegalMove"/> cue, fired by <c>GameController</c> at the
/// "illegal/blocked action" status path. Both ride the SFX bus, so the Sound-Effects volume slider and the master mute
/// apply to UI feedback exactly as they do to event SFX.
/// </remarks>
public partial class SoundService : Node
{
    private const string SfxBus = "SFX";

    // A handful of voices is plenty for UI/event SFX; the oldest is reused if all are busy (round-robin).
    private const int VoiceCount = 6;

    // The logical cue used for a generic UI button click. IllegalMove's clip doubles as the click/deny cue in FreeCol
    // (see SoundEvent.IllegalMove); a dedicated click clip can be mapped here later without touching the wiring.
    private const SoundEvent UiClickEvent = SoundEvent.IllegalMove;

    private readonly List<AudioStreamPlayer> _voices = new();
    private readonly Dictionary<SoundEvent, AudioStream> _streams = new();
    private int _nextVoice;

    /// <summary>
    /// The most recent <see cref="SoundEvent"/> passed to <see cref="Play"/> (<c>null</c> until the first call). Lets a
    /// headless test assert that a game event cued the right sound without depending on real audio output (the CI L3 run
    /// has no audio device). Set even when the clip is missing, so the <em>mapping intent</em> is what is verified.
    /// </summary>
    public SoundEvent? LastPlayed { get; private set; }

    /// <summary>On startup: preload every catalogued clip, spin up the SFX voice pool, and start auto-wiring button clicks.</summary>
    public override void _Ready()
    {
        PreloadClips();

        for (int i = 0; i < VoiceCount; i++)
        {
            var player = new AudioStreamPlayer { Bus = SfxBus };
            AddChild(player);
            _voices.Add(player);
        }

        // One global hook for the UI click cue: every button added anywhere in the tree gets its Pressed signal wired to
        // the click sound, so a single connection covers the whole UI rather than per-button boilerplate (Godot 4 best
        // practice for a global SFX hook). Buttons already present at boot are wired in a deferred sweep below.
        GetTree().NodeAdded += OnNodeAdded;
        CallDeferred(nameof(WireExistingButtons));
    }

    // Connects a button's Pressed signal to the click cue exactly once. Connecting is idempotent here because each node
    // is seen once via NodeAdded (and once in the boot sweep, guarded by IsConnected).
    private void WireButton(BaseButton button)
    {
        var callable = new Callable(this, MethodName.PlayUiClick);
        if (!button.IsConnected(BaseButton.SignalName.Pressed, callable))
        {
            button.Pressed += PlayUiClick;
        }
    }

    private void OnNodeAdded(Node node)
    {
        if (node is BaseButton button)
        {
            WireButton(button);
        }
    }

    // Wires any buttons that already existed before the NodeAdded hook was attached (e.g. an autoload races a menu that
    // was loaded first). Deferred so the first scene is fully built. Cheap, and only runs once.
    private void WireExistingButtons()
    {
        if (GetTree()?.Root is { } root)
        {
            WireButtonsUnder(root);
        }
    }

    private void WireButtonsUnder(Node node)
    {
        if (node is BaseButton button)
        {
            WireButton(button);
        }
        foreach (Node child in node.GetChildren())
        {
            WireButtonsUnder(child);
        }
    }

    /// <summary>Plays the generic UI button-click cue. Wired automatically to every <see cref="BaseButton"/>'s click; safe to call directly too.</summary>
    public void PlayUiClick() => Play(UiClickEvent);

    /// <summary>
    /// Plays the clip mapped to <paramref name="evt"/> on a free voice (round-robin). No-op (with a warning) if the clip
    /// failed to load. Safe to call from any game event; never throws.
    /// </summary>
    public void Play(SoundEvent evt)
    {
        LastPlayed = evt; // record the intent first, so a missing-clip no-op is still observable to tests
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
