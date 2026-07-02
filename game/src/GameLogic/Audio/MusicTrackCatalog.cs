using System.Collections.Generic;

namespace CrownAndColony.GameLogic.Audio;

/// <summary>
/// The single source of truth mapping each music <see cref="MusicContext"/> to the Godot resource paths of its track(s),
/// and each European nation id to its national-anthem track. Kept in the engine-free <c>GameLogic</c> assembly so the
/// mapping is plain data: it can be asserted in a unit test (every context resolves, every path is a well-formed
/// <c>res://</c> <c>.ogg</c> under the music directory, every shipped anthem nation resolves) without booting the engine.
/// The presentation-layer <c>MusicService</c> loads these paths as <c>AudioStream</c>s and plays them, looping, on the
/// Music bus.
/// </summary>
/// <remarks>
/// Tracks are FreeCol's own music, copied unmodified into <c>game/assets/freecol/music/</c> (see that folder's
/// <c>PROVENANCE.md</c>). The background playlist tracks are CC-BY 4.0 (Alexander Zhelanov / Stian Grenborgen); the
/// anthems are part of FreeCol's GPL-v2 package. FreeCol plays one shuffled "default" playlist (<c>sound.music.playlist.default</c>)
/// as background music across menu and gameplay; we mirror a representative subset of it as the shared
/// <see cref="MusicContext.Menu"/>/<see cref="MusicContext.InGamePeace"/> bed, plus an interim
/// <see cref="MusicContext.InGameWar"/> subset (no dedicated war asset exists yet). Paths use Godot's <c>res://</c> scheme.
/// </remarks>
public static class MusicTrackCatalog
{
    /// <summary>The directory (Godot <c>res://</c> path) holding the background-playlist tracks.</summary>
    public const string MusicDir = "res://assets/freecol/music/";

    /// <summary>The directory (Godot <c>res://</c> path) holding the per-nation anthem tracks.</summary>
    public const string AnthemDir = MusicDir + "anthem/";

    // The looping background playlist — a faithful subset of FreeCol's shuffled "default" playlist. MusicService
    // shuffles and cross-cycles these so the menu and the running game share one ambient bed (FreeCol behaviour):
    // Menu and InGamePeace map to this SAME list, which is what lets MusicService relabel the context without
    // restarting the track when the player moves between them.
    private static readonly IReadOnlyList<string> BackgroundPlaylist = new[]
    {
        MusicDir + "el-dorado.ogg",
        MusicDir + "founders.ogg",
        MusicDir + "settlers-routine.ogg",
        MusicDir + "sunrise.ogg",
        MusicDir + "tailwind.ogg",
        MusicDir + "fearless-sailors.ogg",
    };

    // INTERIM war playlist (86d3fq1wy): FreeCol ships no dedicated war/tension track, and this project adds no new
    // assets in this wave — so the war bed is the two most martial-sounding tracks of the existing six. It must stay
    // a DIFFERENT list from BackgroundPlaylist (the L1 audible-switch guard) until a real war track is sourced
    // (asset gap recorded in the audio system doc's TODO + the Asset Register).
    private static readonly IReadOnlyList<string> WarPlaylist = new[]
    {
        MusicDir + "el-dorado.ogg",
        MusicDir + "fearless-sailors.ogg",
    };

    private static readonly IReadOnlyDictionary<MusicContext, IReadOnlyList<string>> Tracks =
        new Dictionary<MusicContext, IReadOnlyList<string>>
        {
            [MusicContext.Menu] = BackgroundPlaylist,
            [MusicContext.InGamePeace] = BackgroundPlaylist, // same list as Menu — the seamless menu→game bed
            [MusicContext.InGameWar] = WarPlaylist,
        };

    // The 8 European powers FreeCol ships an anthem for, keyed by their full nation id (model.nation.<x>). The track
    // file is named after the last id segment, mirroring FreeCol's sound.anthem.model.nation.<x> → anthem/<x>.ogg map.
    private static readonly IReadOnlyDictionary<string, string> Anthems =
        new Dictionary<string, string>
        {
            ["model.nation.dutch"] = AnthemDir + "dutch.ogg",
            ["model.nation.english"] = AnthemDir + "english.ogg",
            ["model.nation.french"] = AnthemDir + "french.ogg",
            ["model.nation.spanish"] = AnthemDir + "spanish.ogg",
            ["model.nation.danish"] = AnthemDir + "danish.ogg",
            ["model.nation.portuguese"] = AnthemDir + "portuguese.ogg",
            ["model.nation.russian"] = AnthemDir + "russian.ogg",
            ["model.nation.swedish"] = AnthemDir + "swedish.ogg",
        };

    /// <summary>The ordered list of track paths for <paramref name="context"/> (the playlist the service loops over).</summary>
    /// <exception cref="KeyNotFoundException">No tracks are registered for <paramref name="context"/> — a missing-mapping bug.</exception>
    public static IReadOnlyList<string> Playlist(MusicContext context) => Tracks[context];

    /// <summary>Tries to resolve the track list for <paramref name="context"/>; returns <c>false</c> if none is registered.</summary>
    public static bool TryGetPlaylist(MusicContext context, out IReadOnlyList<string> playlist) =>
        Tracks.TryGetValue(context, out playlist!);

    /// <summary>
    /// Tries to resolve the national-anthem track for a European <paramref name="nationId"/> (e.g.
    /// <c>model.nation.dutch</c>); returns <c>false</c> for a nation FreeCol ships no anthem for (natives, REF powers,
    /// or an unknown id) — in which case the caller simply plays no anthem.
    /// </summary>
    public static bool TryGetAnthem(string? nationId, out string path)
    {
        if (nationId is not null && Anthems.TryGetValue(nationId, out string? p))
        {
            path = p;
            return true;
        }
        path = string.Empty;
        return false;
    }

    /// <summary>The full context→playlist mapping (read-only) — used by tests and the presentation-layer loader.</summary>
    public static IReadOnlyDictionary<MusicContext, IReadOnlyList<string>> All => Tracks;

    /// <summary>The full nation-id→anthem mapping (read-only) — used by tests and the presentation-layer loader.</summary>
    public static IReadOnlyDictionary<string, string> AnthemsByNation => Anthems;
}
