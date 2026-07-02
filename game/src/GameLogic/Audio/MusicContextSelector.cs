using System.Linq;
using CrownAndColony.GameLogic.GameSession;

namespace CrownAndColony.GameLogic.Audio;

/// <summary>
/// Derives the <see cref="MusicContext"/> the background music should play from the current game state — the single,
/// engine-free rule for "what mood is the game in?" (86d3fq1wy). Pure and RNG-free (ADR-009): a read-only fold over
/// <see cref="Player.Stances"/>, so it is L1-testable and safe to call on every view refresh. The presentation layer
/// (<c>GameController.RefreshMusicContext</c> / <c>MainMenu</c>) feeds the result to <c>MusicService.SetContext</c>,
/// which only audibly changes the music when the resolved playlist actually differs.
/// </summary>
public static class MusicContextSelector
{
    /// <summary>
    /// The music context for <paramref name="game"/>: <see cref="MusicContext.Menu"/> when no game is loaded
    /// (<c>null</c>), <see cref="MusicContext.InGameWar"/> while the human holds <see cref="Stance.War"/> toward any
    /// <b>non-native</b> player (a colonial rival, or the REF between declaring independence and winning it — the REF
    /// player object outlives the war, so existence is deliberately not the predicate), else
    /// <see cref="MusicContext.InGamePeace"/> (covers Uncontacted/Peace/CeaseFire/Alliance).
    /// </summary>
    /// <remarks>
    /// Native wars deliberately do <b>not</b> flip the war music: a tribe's WAR stance is tracked in the game's
    /// transient native-stance channels (<c>Game.NativeStanceToward</c>), never written into
    /// <see cref="Player.Stances"/> — and even if a native-keyed entry ever appeared there (e.g. a hand-edited save),
    /// the <see cref="PlayerType.Native"/> filter keeps the war bed reserved for European-side wars (FreeCol scores
    /// native raids with SFX, not a soundtrack change).
    /// </remarks>
    public static MusicContext For(Game? game)
    {
        if (game is null)
        {
            return MusicContext.Menu;
        }
        Player human = game.HumanPlayer;
        bool atWar = human.Stances.Any(kv =>
            kv.Value == Stance.War
            && game.Players.FirstOrDefault(p => p.PlayerId == kv.Key) is { } other
            && other.PlayerType != PlayerType.Native);
        return atWar ? MusicContext.InGameWar : MusicContext.InGamePeace;
    }
}
