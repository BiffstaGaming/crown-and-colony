using System.Collections.Generic;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The Godot-side persistence bridge for the high-score leaderboard: it reads and writes the leaderboard's own JSON
/// document at <c>user://highscores.json</c>, delegating all ranking/serialization to GameLogic's pure
/// <see cref="HighScoreStore"/>. This is the thin host I/O seam (mirroring how <see cref="GameController"/> wraps
/// <see cref="SaveGame"/> with <see cref="FileAccess"/>) so the store stays engine-free and unit-testable.
/// <para>
/// The file is <b>entirely separate from the game save</b> — recording a score never touches a save and never bumps
/// the game-save version (still v52), exactly as FreeCol keeps high scores in their own file.
/// </para>
/// </summary>
public static class HighScoresService
{
    /// <summary>The leaderboard file, in the Godot user data dir — separate from the game saves.</summary>
    public const string HighScoresPath = "user://highscores.json";

    /// <summary>Loads the leaderboard, descending by score. A missing/unreadable file yields an empty list (never throws).</summary>
    /// <returns>The ranked leaderboard.</returns>
    public static IReadOnlyList<HighScore> Load()
    {
        if (!FileAccess.FileExists(HighScoresPath))
        {
            return new List<HighScore>();
        }
        using var file = FileAccess.Open(HighScoresPath, FileAccess.ModeFlags.Read);
        return file is null ? new List<HighScore>() : HighScoreStore.Load(file.GetAsText());
    }

    /// <summary>
    /// Offers <paramref name="candidate"/> to the saved leaderboard and persists the result if it qualifies — the
    /// host-side counterpart to FreeCol's <c>HighScore.newHighScore</c>. Returns the updated leaderboard and whether
    /// the score made it in; if it did, the file is rewritten.
    /// </summary>
    /// <param name="candidate">The completed-game score to record.</param>
    /// <returns>The (possibly updated) leaderboard and whether <paramref name="candidate"/> was added.</returns>
    public static (IReadOnlyList<HighScore> Scores, bool Added) Record(HighScore candidate)
    {
        (IReadOnlyList<HighScore> scores, bool added) = HighScoreStore.Add(candidate, Load());
        if (added)
        {
            Save(scores);
        }
        return (scores, added);
    }

    /// <summary>Writes the leaderboard to <see cref="HighScoresPath"/> (canonicalised by the store).</summary>
    /// <param name="scores">The leaderboard to persist.</param>
    public static void Save(IReadOnlyList<HighScore> scores)
    {
        using var file = FileAccess.Open(HighScoresPath, FileAccess.ModeFlags.Write);
        file?.StoreString(HighScoreStore.ToJson(scores));
    }
}
