using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrownAndColony.GameLogic.GameSession;

namespace CrownAndColony.GameLogic.Persistence;

/// <summary>
/// The persisted high-score leaderboard — a faithful port of FreeCol's static high-score machinery on
/// <c>HighScore</c> (<c>checkHighScore</c> / <c>tidyScores</c> / <c>loadHighScores</c> / <c>saveHighScores</c>) and the
/// server's <c>HighScores</c> file. It keeps the best completed games sorted by score, capped at
/// <see cref="MaxEntries"/> (FreeCol's <c>NUMBER_OF_HIGH_SCORES = 10</c>).
/// <para>
/// <b>Stored entirely separately from the game save.</b> Exactly as FreeCol writes high scores to their own file
/// rather than into a saved game, this serializes to its own JSON document (the presentation layer writes it to
/// <c>user://highscores.json</c>). Nothing here touches the game-save format, so the game save version is unchanged
/// (v52). A pure value layer: serialization in/out is plain string ⇄ list, leaving file I/O to the Godot host (which
/// keeps this engine-free and unit-testable, matching the <see cref="SaveGame"/> split).
/// </para>
/// </summary>
public static class HighScoreStore
{
    /// <summary>The number of entries the leaderboard keeps (FreeCol <c>HighScore.NUMBER_OF_HIGH_SCORES</c>).</summary>
    public const int MaxEntries = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Persist enums (ScoreLevel) by name, so the file is human-readable and stable across enum reordering.
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Whether <paramref name="candidate"/> belongs in <paramref name="scores"/>, and where — a port of FreeCol's
    /// <c>checkHighScore</c>. Returns the index to write at: an existing entry to replace (a higher score from the
    /// <i>same</i> game, matched on <see cref="HighScore.GameId"/>, or the worst entry once the board is full), or the
    /// next free slot; or <c>-1</c> if the score does not qualify (negative, or no better than a full board's worst).
    /// Assumes <paramref name="scores"/> is already in canonical (descending) order, as <see cref="Load"/> guarantees.
    /// </summary>
    /// <param name="candidate">The score being offered.</param>
    /// <param name="scores">The current leaderboard, descending by score.</param>
    /// <returns>The index to write <paramref name="candidate"/> at, or <c>-1</c> to reject it.</returns>
    public static int CheckHighScore(HighScore candidate, IReadOnlyList<HighScore> scores)
    {
        int nScores = scores.Count;

        // Same-game de-duplication (FreeCol's gameUUID check): a later, higher score from the same game replaces the
        // earlier one rather than adding a second row. Skipped when the game id is unknown/empty.
        if (!string.IsNullOrEmpty(candidate.GameId))
        {
            for (int i = nScores - 1; i >= 0; i--)
            {
                if (scores[i].GameId == candidate.GameId && candidate.Score > scores[i].Score)
                {
                    return i;
                }
            }
        }

        if (candidate.Score < 0)
        {
            return -1;
        }
        if (nScores == 0)
        {
            return 0;
        }
        if (nScores >= MaxEntries)
        {
            // The board is full — only beat the worst (last) entry.
            return candidate.Score > scores[nScores - 1].Score ? nScores - 1 : -1;
        }
        return nScores;
    }

    /// <summary>
    /// Offers <paramref name="candidate"/> to <paramref name="scores"/>, returning the updated, canonical leaderboard
    /// and whether the score made it in — a port of FreeCol's <c>newHighScore</c>. If <see cref="CheckHighScore"/>
    /// accepts, the entry is written at its slot (replacing an existing row or appended), then the list is tidied
    /// (sorted descending, capped at <see cref="MaxEntries"/>). The input is not mutated.
    /// </summary>
    /// <param name="candidate">The score to add.</param>
    /// <param name="scores">The current leaderboard.</param>
    /// <returns>The new leaderboard and <c>Added</c> = whether the score qualified.</returns>
    public static (IReadOnlyList<HighScore> Scores, bool Added) Add(HighScore candidate, IReadOnlyList<HighScore> scores)
    {
        var list = new List<HighScore>(scores);
        int idx = CheckHighScore(candidate, list);
        if (idx < 0)
        {
            return (Tidy(list), false);
        }
        if (idx < list.Count)
        {
            list[idx] = candidate;
        }
        else
        {
            list.Add(candidate);
        }
        return (Tidy(list), true);
    }

    /// <summary>
    /// Tidies a list into canonical form — sorted by descending score and capped at <see cref="MaxEntries"/> — a port
    /// of FreeCol's <c>tidyScores</c>. Ties are broken by the more recent date (newest first), keeping the order stable
    /// and deterministic. Returns a new list; the input is not mutated.
    /// </summary>
    /// <param name="scores">The scores to tidy.</param>
    /// <returns>The canonical, capped, descending leaderboard.</returns>
    public static IReadOnlyList<HighScore> Tidy(IEnumerable<HighScore> scores) =>
        scores
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.DateUtc)
            .Take(MaxEntries)
            .ToList();

    /// <summary>Serializes a leaderboard to its JSON document (already canonical-ordered).</summary>
    /// <param name="scores">The scores to write.</param>
    /// <returns>The JSON text for the high-score file.</returns>
    public static string ToJson(IEnumerable<HighScore> scores) =>
        JsonSerializer.Serialize(Tidy(scores), JsonOptions);

    /// <summary>
    /// Parses a leaderboard from its JSON document and returns it tidied (canonical, capped). A missing, empty or
    /// unreadable document yields an empty leaderboard rather than throwing — high scores must never crash the game
    /// (FreeCol's <c>loadHighScores</c> is likewise tolerant of a missing/corrupt file).
    /// </summary>
    /// <param name="json">The high-score file's JSON text, or null/empty for a first run.</param>
    /// <returns>The leaderboard, descending by score.</returns>
    public static IReadOnlyList<HighScore> Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<HighScore>();
        }
        try
        {
            List<HighScore>? scores = JsonSerializer.Deserialize<List<HighScore>>(json, JsonOptions);
            return Tidy(scores ?? new List<HighScore>());
        }
        catch (JsonException)
        {
            return new List<HighScore>();
        }
    }
}
