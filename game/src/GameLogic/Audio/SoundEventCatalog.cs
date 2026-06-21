using System.Collections.Generic;

namespace CrownAndColony.GameLogic.Audio;

/// <summary>
/// The single source of truth mapping each <see cref="SoundEvent"/> to the Godot resource path of its clip. Kept in the
/// engine-free <c>GameLogic</c> assembly so the mapping is plain data: it can be asserted in a unit test (every event
/// resolves, every clip path is well-formed) without booting the engine. The presentation-layer <c>SoundService</c>
/// loads these paths as <c>AudioStream</c>s and plays them on the SFX bus.
/// </summary>
/// <remarks>
/// Clips are FreeCol's own GPL-v2 sound effects, copied unmodified into <c>game/assets/freecol/sound/</c>
/// (see that folder's <c>PROVENANCE.md</c>). Several FreeCol events deliberately share a clip (load/unload, sell/cash-in)
/// — that reuse is preserved here. Paths use Godot's <c>res://</c> scheme.
/// </remarks>
public static class SoundEventCatalog
{
    /// <summary>The directory (Godot <c>res://</c> path) holding every SFX clip.</summary>
    public const string SoundDir = "res://assets/freecol/sound/";

    private static readonly IReadOnlyDictionary<SoundEvent, string> Clips = new Dictionary<SoundEvent, string>
    {
        [SoundEvent.ColonyFounded] = SoundDir + "colony.ogg",
        [SoundEvent.BuildingComplete] = SoundDir + "building.ogg",
        [SoundEvent.Combat] = SoundDir + "attack.ogg",
        [SoundEvent.CargoMoved] = SoundDir + "load.ogg",
        [SoundEvent.CargoSold] = SoundDir + "sell.ogg",
        [SoundEvent.ShipSunk] = SoundDir + "sunk.ogg",
        [SoundEvent.IllegalMove] = SoundDir + "illegal.ogg",
        [SoundEvent.Alert] = SoundDir + "alert.ogg",
    };

    /// <summary>The resource path of the clip for <paramref name="evt"/>.</summary>
    /// <exception cref="KeyNotFoundException">No clip is registered for <paramref name="evt"/> — a missing-mapping bug.</exception>
    public static string ClipPath(SoundEvent evt) => Clips[evt];

    /// <summary>Tries to resolve the clip path for <paramref name="evt"/>; returns <c>false</c> if none is registered.</summary>
    public static bool TryGetClipPath(SoundEvent evt, out string path) => Clips.TryGetValue(evt, out path!);

    /// <summary>The full event→path mapping (read-only) — used by tests and by the presentation-layer preloader.</summary>
    public static IReadOnlyDictionary<SoundEvent, string> All => Clips;
}
