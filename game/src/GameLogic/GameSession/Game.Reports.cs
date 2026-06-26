using CrownAndColony.GameLogic.Natives;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// Read-only report oracles (ADR-006): the pure, RNG-free reads the empire-report panels
/// (the presentation layer's <c>ColonyReportPanel</c>) surface but do not compute — the per-type Royal
/// Expeditionary Force composition (the REF intelligence report), the colonial nation ranking by final score (the
/// score / nation report), and the named tension band a colonial pair sits in (the Foreign Affairs report's
/// tension/attitude column). Each mutates nothing and draws no randomness, so a report read leaves the game
/// byte-identical (ADR-009); none is persisted. FreeCol surfaces the same figures in its <c>Report*Panel</c> family
/// (<c>ReportRequirementsPanel</c> siblings) and <c>NationSummary</c>.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// One block of like units in the Royal Expeditionary Force's current composition (a display projection of a
    /// <see cref="ForceEntry"/>): the unit type, the role it carries (e.g. infantry / cavalry, null for artillery and
    /// ships), how many there are, and whether the block is naval. Used by the REF intelligence report to list the
    /// force as "N × King's Regular (cavalry)" rows rather than a bare land/naval count.
    /// </summary>
    /// <param name="UnitTypeId">The ruleset unit-type id of the block (e.g. <c>model.unit.kingsRegular</c>).</param>
    /// <param name="RoleId">The ruleset role id the units carry, or <c>null</c> when the type has no distinguishing role (artillery, man-o-war).</param>
    /// <param name="Count">How many units the block holds.</param>
    /// <param name="IsNaval">Whether this is a naval block (a transport/escort) rather than a land block.</param>
    public readonly record struct RefForceBlock(string UnitTypeId, string? RoleId, int Count, bool IsNaval);

    /// <summary>
    /// One colonial power's standing in the nation ranking (the score / nation report): the player, its final
    /// <see cref="PlayerScore"/>, and its colony / unit counts. A pure value projection for display; ordering and
    /// rank are the panel's concern (it sorts by <see cref="Score"/> descending).
    /// </summary>
    /// <param name="Player">The ranked player (the human or a foreign colonial power, including an independent nation).</param>
    /// <param name="Score">The player's final score (<see cref="PlayerScore"/>).</param>
    /// <param name="ColonyCount">How many colonies the player owns.</param>
    /// <param name="UnitCount">How many (non-native) units the player owns.</param>
    public readonly record struct NationStanding(Player Player, int Score, int ColonyCount, int UnitCount);

    /// <summary>
    /// The Royal Expeditionary Force's <b>current composition</b>, block by block, for the REF intelligence report (a
    /// read-only oracle, ADR-006; FreeCol <c>Monarch.getExpeditionaryForce</c> projected to per-type counts). Returns
    /// the land blocks first (King's Regulars in their infantry/cavalry roles, then artillery), then the naval blocks
    /// (men-o-war), each a <see cref="RefForceBlock"/>. The force materialises its base composition for the read
    /// without storing it (no save change — the persisted REF is whatever the saved force holds), exactly as
    /// <see cref="ExpeditionaryForceStrength"/> does. The presentation surfaces these rows so the player can watch the
    /// King's army grow (via ADD_TO_REF) before deciding whether to break away. RNG-free; never mutates.
    /// </summary>
    /// <returns>The REF's land blocks (in force order) followed by its naval blocks.</returns>
    public IReadOnlyList<RefForceBlock> ExpeditionaryForceComposition()
    {
        Force ref_ = _refForce ?? BuildBaseRef(); // read-only: don't store a lazily-built base (the saved force stays the truth)
        var blocks = new List<RefForceBlock>(ref_.LandUnits.Count + ref_.NavalUnits.Count);
        foreach (ForceEntry e in ref_.LandUnits)
        {
            blocks.Add(new RefForceBlock(e.UnitTypeId, e.RoleId, e.Count, IsNaval: false));
        }
        foreach (ForceEntry e in ref_.NavalUnits)
        {
            blocks.Add(new RefForceBlock(e.UnitTypeId, e.RoleId, e.Count, IsNaval: true));
        }
        return blocks;
    }

    /// <summary>
    /// The colonial nation ranking by final score, highest first (the score / nation report; FreeCol surfaces the
    /// score in its report/high-score screens). A read-only oracle (ADR-006): every colonial-or-independent power —
    /// the human and the foreign powers, but not the natives or the Royal Expeditionary Force — with its
    /// <see cref="PlayerScore"/> and its colony / unit counts, ordered by score descending (ties broken by
    /// <see cref="Player.PlayerId"/> for stability). Pure and RNG-free; never mutates, never persisted.
    /// </summary>
    /// <returns>The ranked standings, best score first.</returns>
    public IReadOnlyList<NationStanding> NationRanking() =>
        _players
            .Where(p => p.PlayerType is PlayerType.Colonial or PlayerType.Rebel or PlayerType.Independent)
            .Select(p => new NationStanding(
                p,
                PlayerScore(p),
                _colonies.Count(c => c.OwnerId == p.PlayerId),
                _units.Count(u => u.OwnerId == p.PlayerId && !u.IsNative)))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Player.PlayerId)
            .ToList();

    /// <summary>
    /// The named tension band <paramref name="a"/> holds toward <paramref name="b"/> (the Foreign Affairs report's
    /// tension/attitude column; FreeCol <c>Tension.getLevel</c>). A read-only oracle (ADR-006) banding the raw
    /// <see cref="TensionBetween"/> scalar through FreeCol's <c>Tension.Level</c> thresholds — Happy ≤ 100, Content ≤
    /// 600, Displeased ≤ 700, Angry ≤ 800, else Hateful — reusing the <see cref="AlarmLevel"/> enum (whose bands are
    /// the same FreeCol <c>Tension.Level</c> figures the natives use), so the banding lives in the engine rather than
    /// the panel. Pure and RNG-free.
    /// </summary>
    /// <param name="a">The player whose attitude is read (their <see cref="Player.PlayerId"/>).</param>
    /// <param name="b">The player the attitude is toward (their <see cref="Player.PlayerId"/>).</param>
    /// <returns>The tension band, from <see cref="AlarmLevel.Happy"/> (calm) to <see cref="AlarmLevel.Hateful"/>.</returns>
    public AlarmLevel TensionLevelBetween(int a, int b)
    {
        int tension = TensionBetween(a, b);
        // FreeCol Tension.Level thresholds (HAPPY 100, CONTENT 600, DISPLEASED 700, ANGRY 800, HATEFUL 1000) — the
        // same bands the native AlarmLevel uses, so we reuse that enum rather than declare a parallel one.
        return tension <= 100 ? AlarmLevel.Happy
            : tension <= 600 ? AlarmLevel.Content
            : tension <= 700 ? AlarmLevel.Displeased
            : tension <= 800 ? AlarmLevel.Angry
            : AlarmLevel.Hateful;
    }
}
