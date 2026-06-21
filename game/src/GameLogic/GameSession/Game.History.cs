namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// One notable event in the human player's game history (FreeCol's <c>HistoryEvent</c>): the kind of thing that
/// happened, the turn it happened on, a ready-to-read description, and the score it contributed. Presentation reads
/// these for the History report; the engine appends them as the events occur.
/// </summary>
/// <param name="Kind">The category of event (colony founded / war declared / father elected / region discovered).</param>
/// <param name="Turn">The game turn on which it happened.</param>
/// <param name="Description">A player-facing one-line description (no ids), e.g. "Founded New Amsterdam."</param>
/// <param name="Score">
/// The exploration/achievement score this event contributed to the player's total (FreeCol
/// <c>HistoryEvent.getScore</c>); 0 for events that carry no score. Today only
/// <see cref="HistoryEventKind.RegionDiscovered"/> carries a non-zero value (the discovered region's score).
/// </param>
public sealed record HistoryEvent(HistoryEventKind Kind, int Turn, string Description, int Score = 0);

/// <summary>The kinds of notable event recorded in the player's <see cref="Game.History"/>.</summary>
public enum HistoryEventKind
{
    /// <summary>The human founded a new colony.</summary>
    ColonyFounded,

    /// <summary>The human entered a state of war with a rival power.</summary>
    WarDeclared,

    /// <summary>The human elected a Founding Father to the Continental Congress.</summary>
    FatherElected,

    /// <summary>The human discovered a geographic region for the first time (FreeCol <c>DISCOVER_REGION</c>).</summary>
    RegionDiscovered,
}

public sealed partial class Game
{
    // In-memory only this wave (NOT persisted — a save field is a follow-up, owned by the save hotspot stream). A
    // reloaded game therefore starts with an empty history; events accrue again as play continues.
    private readonly List<HistoryEvent> _history = [];

    /// <summary>
    /// The human player's notable past events in chronological order (FreeCol's player <c>HistoryEvent</c> list):
    /// colonies founded, wars entered, Founding Fathers elected. <b>In-memory only</b> this wave — not saved, so a
    /// reloaded game's history begins empty (a persisted event log is a follow-up). Read-only for presentation.
    /// </summary>
    public IReadOnlyList<HistoryEvent> History => _history;

    /// <summary>Appends a notable event to the human's <see cref="History"/>, stamped with the current <see cref="Turn"/> and an optional contributing <paramref name="score"/>.</summary>
    private void RecordHistory(HistoryEventKind kind, string description, int score = 0) =>
        _history.Add(new HistoryEvent(kind, Turn, description, score));

    /// <summary>
    /// The total score the human has earned from recorded history events (FreeCol folds each event's
    /// <c>HistoryEvent.getScore</c> into the player total). Today only region-discovery events carry a score, so this
    /// is the human's accumulated <b>exploration-discovery</b> score; it is summed into <see cref="PlayerScore"/> for
    /// the human and exposed for the History/score report. <b>In-memory only</b> this wave — the history log is not
    /// persisted, so a reloaded game's discovery score must be re-earned (the per-region discovered state IS saved, so
    /// a re-revealed region is not re-discovered/re-scored).
    /// </summary>
    public int HistoryEventScore => _history.Sum(h => h.Score);

    /// <summary>A readable nation name for a player id (e.g. <c>model.nation.dutch</c> → "Dutch"), or "rival" when unknown.</summary>
    private string NationDisplayName(int playerId)
    {
        string? id = PlayerById(playerId)?.NationId;
        if (id is null || id.Length == 0)
        {
            return "rival";
        }
        string shortName = id[(id.LastIndexOf('.') + 1)..];
        return shortName.Length == 0 ? "rival" : char.ToUpperInvariant(shortName[0]) + shortName[1..];
    }

    /// <summary>A readable Founding Father name from its ruleset id (e.g. <c>model.foundingFather.adamSmith</c> → "Adam Smith").</summary>
    private string FatherDisplayName(string fatherId)
    {
        string shortName = Ruleset.Father(fatherId).ShortName;
        var sb = new System.Text.StringBuilder(shortName.Length + 4);
        for (int i = 0; i < shortName.Length; i++)
        {
            char c = shortName[i];
            if (i == 0)
            {
                sb.Append(char.ToUpperInvariant(c));
            }
            else
            {
                if (char.IsUpper(c) && !char.IsUpper(shortName[i - 1]))
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
