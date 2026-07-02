namespace CrownAndColony.GameLogic.Audio;

/// <summary>
/// The set of <em>contexts</em> that select which background music plays. This lives in the engine-free
/// <c>GameLogic</c> assembly (not the Godot presentation layer) so the context→track mapping
/// (<see cref="MusicTrackCatalog"/>) is plain data that can be unit-tested without the engine. The presentation-layer
/// <c>MusicService</c> resolves a context to one or more looping <c>AudioStream</c>s and plays them on the Music bus
/// (ADR-006: logic decides <em>what</em> mood to cue; presentation decides <em>how</em> it sounds). The current
/// context is derived from game state by the pure <see cref="MusicContextSelector"/>.
/// </summary>
/// <remarks>
/// FreeCol plays a single shuffled "default" playlist as background music across both the menu and gameplay, plus
/// per-nation anthems (resource type <c>"music"</c>). We keep that shared bed for <see cref="Menu"/> and
/// <see cref="InGamePeace"/> (they map to the identical playlist, so menu→game is seamless) and add an
/// <see cref="InGameWar"/> context — cued while the human is at war with another European-side power — as the
/// 86d3fq1wy war-music seam. The enum values are never persisted (the context is re-derived from game state on every
/// refresh), so members can be renamed/extended freely.
/// </remarks>
public enum MusicContext
{
    /// <summary>The main menu (no game loaded). Shares FreeCol's default playlist with <see cref="InGamePeace"/>.</summary>
    Menu,

    /// <summary>In a game, at peace (no war with any non-native power) — FreeCol's <c>sound.music.playlist.default</c> bed.</summary>
    InGamePeace,

    /// <summary>In a game, at war with at least one non-native power (a rival, or the REF after declaring independence).</summary>
    InGameWar,
}
