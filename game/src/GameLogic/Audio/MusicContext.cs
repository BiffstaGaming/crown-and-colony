namespace CrownAndColony.GameLogic.Audio;

/// <summary>
/// The set of <em>contexts</em> that select which background music plays. This lives in the engine-free
/// <c>GameLogic</c> assembly (not the Godot presentation layer) so the context→track mapping
/// (<see cref="MusicTrackCatalog"/>) is plain data that can be unit-tested without the engine. The presentation-layer
/// <c>MusicService</c> resolves a context to one or more looping <c>AudioStream</c>s and plays them on the Music bus
/// (ADR-006: logic decides <em>what</em> mood to cue; presentation decides <em>how</em> it sounds).
/// </summary>
/// <remarks>
/// FreeCol plays a single shuffled "default" playlist as background music across both the menu and gameplay, plus
/// per-nation anthems (resource type <c>"music"</c>). We mirror that: one shared <see cref="Background"/> playlist
/// underlies the whole game, and the anthem is a separate per-nation cue (see <see cref="MusicTrackCatalog.TryGetAnthem"/>).
/// </remarks>
public enum MusicContext
{
    /// <summary>The looping background-music playlist (FreeCol <c>sound.music.playlist.default</c>) — used on the main menu and in-game.</summary>
    Background,
}
