namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// One year's snapshot of the human nation's headline figures (Sid Meier's Colonization's end-of-game <b>demographics</b>
/// screen; FreeCol's score/history trend reporting): the calendar year it was taken, the colonial population, the gold in
/// the treasury, and the player score at that moment. The engine appends one of these at each year-rollover; presentation
/// reads the series for the demographic line graph. A pure value record — RNG-free, carries no ids.
/// </summary>
/// <param name="Year">The calendar year the snapshot was taken (e.g. 1492), per the ruleset <see cref="Specification.Calendar"/>.</param>
/// <param name="Population">The human's total colonial population — the sum of every human colony's resident count.</param>
/// <param name="Gold">The human's gold in the treasury that year.</param>
/// <param name="Score">The human's player score that year (FreeCol <c>Player.getScore</c> / our <see cref="Game.Score"/>).</param>
public readonly record struct DemographicSnapshot(int Year, int Population, int Gold, int Score);

public sealed partial class Game
{
    // The human nation's per-year demographic series (population / gold / score over time). One entry per calendar year,
    // appended at the year-rollover by RecordYearlyDemographics. Persisted from save v65 (SavedDemographicSnapshot[],
    // omit-when-empty), so the trend graph survives save/load. A pre-v65 save (or any with no snapshots) loads with this
    // empty — exactly the prior behaviour — and the series accrues again as play continues.
    private readonly List<DemographicSnapshot> _demographics = [];

    /// <summary>
    /// The human nation's year-by-year demographic series in chronological order (the end-game demographics screen's
    /// source): for each completed year, the colonial population, treasury gold, and player score at that year-rollover.
    /// <b>Persisted</b> from save v65 (omit-when-empty), so the series — and the trend graph it drives — survives a
    /// save/load round-trip. Read-only for presentation (ADR-006).
    /// </summary>
    public IReadOnlyList<DemographicSnapshot> Demographics => _demographics;

    /// <summary>
    /// The human's <b>current</b> total colonial population — the sum of every human colony's resident count
    /// (<see cref="Colonies.Colony.Population"/>). The headline demographic figure; read by
    /// <see cref="RecordYearlyDemographics"/> at each year-rollover and exposed live for any caller that wants the
    /// running total. Pure and RNG-free.
    /// </summary>
    public int HumanPopulation =>
        _colonies.Where(c => c.OwnerId == _human.PlayerId).Sum(c => c.Population);

    /// <summary>
    /// Appends one <see cref="DemographicSnapshot"/> for the year just completed — the human's population, gold and score
    /// at this moment — to the persisted <see cref="Demographics"/> series, <b>at most once per calendar year</b>. Called
    /// once per <see cref="EndTurn"/> at the turn-rollover with the year that has just ended; it no-ops when that year is
    /// already the last recorded one, so the post-1600 two-turns-per-year era still records a single snapshot per year
    /// (FreeCol/Col1 demographics are a per-year series, not per-turn). Pure and <b>RNG-free</b> — it only reads existing
    /// totals and appends a value record, drawing no randomness, so a game's stream-0 sequence is untouched (ADR-009) and
    /// the soak stays byte-identical and twin-deterministic.
    /// </summary>
    /// <param name="year">The calendar year that has just completed (the year of the turn before the increment).</param>
    private void RecordYearlyDemographics(int year)
    {
        if (_demographics.Count > 0 && _demographics[^1].Year == year)
        {
            return; // already snapshotted this year (the post-1600 second turn of the year) — one entry per year
        }
        _demographics.Add(new DemographicSnapshot(year, HumanPopulation, Gold, Score));
    }

    /// <summary>
    /// Re-appends the persisted demographic series to a freshly <see cref="Restore">restored</see> game, in order (save
    /// v65+). Called once by <see cref="Persistence.SaveGame.Restore"/> before play resumes; a pre-v65 / empty-series save
    /// passes nothing, leaving the series empty exactly as before persistence. Each snapshot keeps its saved year/figures,
    /// so the reloaded series is byte-identical to the one saved.
    /// </summary>
    /// <param name="snapshots">The saved snapshots in chronological order.</param>
    internal void RestoreDemographics(System.Collections.Generic.IEnumerable<DemographicSnapshot> snapshots) =>
        _demographics.AddRange(snapshots);
}
