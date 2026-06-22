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
/// <c>HistoryEvent.getScore</c>); 0 for events that carry no score. A non-zero value is carried by
/// <see cref="HistoryEventKind.RegionDiscovered"/> (the discovered region's score, positive) and by the
/// destruction penalties <see cref="HistoryEventKind.SettlementDestroyed"/> (−5) /
/// <see cref="HistoryEventKind.NationDestroyed"/> (−50, both negative). Lost-city finds
/// (<see cref="HistoryEventKind.CityOfGold"/>) are recorded with score 0 — their value flows through the
/// treasure/gold they yield, exactly as FreeCol records a score-less <c>CITY_OF_GOLD</c> event.
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

    /// <summary>The human declared independence from the Crown (FreeCol <c>DECLARE_INDEPENDENCE</c>).</summary>
    DeclaredIndependence,

    /// <summary>
    /// One of the human's colonies was destroyed (FreeCol <c>COLONY_DESTROYED</c>) — today only by <b>starvation</b>:
    /// its last colonist could not be fed, so the colony was disposed (see <see cref="Game.RunColonyTurn"/>). Recorded
    /// for the History report; <b>carries no score</b> (FreeCol's <c>COLONY_DESTROYED</c> never calls
    /// <c>setScore</c> — losing the colony already costs its units/liberty, so there is no extra penalty).
    /// </summary>
    ColonyDestroyed,

    /// <summary>
    /// The human found one of the Seven Cities of Gold — Cibola (FreeCol <c>CITY_OF_GOLD</c>): a Lost City Rumour
    /// yielded a vast treasure train. Recorded for the History report with <b>score 0</b> — its value flows through
    /// the treasure it spawns (which converts to score via the gold summand once cashed in), exactly as FreeCol
    /// records a score-less <c>CITY_OF_GOLD</c> event.
    /// </summary>
    CityOfGold,

    /// <summary>
    /// The human razed a native settlement (FreeCol <c>DESTROY_SETTLEMENT</c>): a <b>−5 penalty</b>
    /// (<see cref="Game.ScoreSettlementDestroyed"/>, FreeCol <c>SCORE_SETTLEMENT_DESTROYED</c>) folded into the
    /// human's score. An atrocity — burning a camp lowers your standing even as it nets you plunder.
    /// </summary>
    SettlementDestroyed,

    /// <summary>
    /// The human wiped out a native nation — razed its last settlement (FreeCol <c>DESTROY_NATION</c>): a heavier
    /// <b>−50 penalty</b> (<see cref="Game.ScoreNationDestroyed"/>, FreeCol <c>SCORE_NATION_DESTROYED</c>) on top of
    /// the per-settlement <see cref="SettlementDestroyed"/> penalty.
    /// </summary>
    NationDestroyed,
}

public sealed partial class Game
{
    // The human player's chronological history log. Persisted from save v58 (SavedHistoryEvent[], omit-when-empty), so
    // discovery/destruction/independence events and their scores survive save/load. A pre-v58 save (or any save with an
    // empty log) loads with this empty — exactly the prior behaviour — and events accrue as play continues.
    private readonly List<HistoryEvent> _history = [];

    /// <summary>
    /// The human player's notable past events in chronological order (FreeCol's player <c>HistoryEvent</c> list):
    /// colonies founded, wars entered, Founding Fathers elected, regions discovered, cities of gold found, settlements
    /// razed. <b>Persisted</b> from save v58 (omit-when-empty), so the log — and the scores it carries — survives a
    /// save/load round-trip. Read-only for presentation.
    /// </summary>
    public IReadOnlyList<HistoryEvent> History => _history;

    /// <summary>Appends a notable event to the human's <see cref="History"/>, stamped with the current <see cref="Turn"/> and an optional contributing <paramref name="score"/>.</summary>
    private void RecordHistory(HistoryEventKind kind, string description, int score = 0) =>
        _history.Add(new HistoryEvent(kind, Turn, description, score));

    /// <summary>
    /// Re-appends the persisted history events to a freshly <see cref="Restore">restored</see> game, in order (save
    /// v58+). Called once by <see cref="Persistence.SaveGame.Restore"/> before play resumes; a pre-v58 / empty-log save
    /// passes nothing, leaving the log empty exactly as before persistence. Does not re-stamp the turn — each event
    /// keeps its saved <see cref="HistoryEvent.Turn"/> — so the reloaded log is byte-identical to the one saved.
    /// </summary>
    /// <param name="events">The saved events in chronological order.</param>
    internal void RestoreHistory(IEnumerable<HistoryEvent> events) => _history.AddRange(events);

    /// <summary>
    /// The total score the human has earned from recorded history events (FreeCol folds each event's
    /// <c>HistoryEvent.getScore</c> into the player total): the positive region-discovery (exploration) scores minus
    /// the settlement/nation-destruction penalties (−5 / −50). Summed into <see cref="PlayerScore"/> for the human and
    /// exposed for the History/score report. <b>Persisted</b> from save v58 (the log is saved), so a reloaded game's
    /// history score survives — a discovered region's points and an atrocity's penalty are no longer re-earned/lost on
    /// load.
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

    /// <summary>A readable native-nation name from its nation-type id (e.g. <c>model.nationType.apache</c> → "Apache"), or "native" when unknown.</summary>
    private static string SettlementNationDisplayName(string nationTypeId)
    {
        if (nationTypeId.Length == 0)
        {
            return "native";
        }
        string shortName = nationTypeId[(nationTypeId.LastIndexOf('.') + 1)..];
        return shortName.Length == 0 ? "native" : char.ToUpperInvariant(shortName[0]) + shortName[1..];
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
