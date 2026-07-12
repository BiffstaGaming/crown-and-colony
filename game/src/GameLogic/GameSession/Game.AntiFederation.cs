using CrownAndColony.GameLogic.Colonies;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The Australian-Federation <b>Anti-Federation Sentiment</b> system (WS3.5, design doc <c>05_Federation_Victory_System.md</c>
/// and the doc-06 <c>anti_federation[colony]</c> variable). Each of the human's colonies banks a per-colony
/// <see cref="Colony.AntiFederation"/> opposition value (0–<see cref="AntiFederationCap"/>%) that <b>grows over time</b> from
/// tagged causes — a colony sitting below <see cref="LowSupportThreshold"/> earned support ("Apathy"), a high Crown tax
/// ("Crown pressure"), and a one-off spike when a referendum is rejected or New South Wales fails its mobilisation quota
/// ("Referendum setback") — and <b>decays</b> each turn (faster while the reconciler Pioneers John Quick and Edmund Barton
/// sit in the Convention, the "lower the recovery cost" buy-down).
///
/// <para><b>Where it bites (Chris's decision, referendum-stage only):</b> opposition <em>grows and shows</em> throughout the
/// campaign, but only <b>drags effective support at the referendum</b> — the vote gate (<see cref="CheckPutToReferendum"/>)
/// and the referendum roll (<see cref="AverageSettledSupport"/>) read the opposition-reduced
/// <see cref="RegionNetFederationSupport"/>, while the earlier convention gate, Griffith's hardest-colony pick, and the
/// panel's support bars keep reading the <b>raw</b> earned <see cref="RegionFederationSupport"/>. So the convention stays a
/// pure nation-building climb (it already carries the steep WS3.7 maturity gate) and opposition is a focused late-campaign
/// obstacle. The shared <see cref="RegionFederationSupport"/> oracle is deliberately <b>not</b> re-pointed.</para>
///
/// <para><b>Off for classic (ADR-009 byte-stability):</b> accrual is gated on
/// <see cref="Specification.Ruleset.VictoryFederation"/> and the human player, so a classic game (and every AI colony)
/// never accrues — <see cref="Colony.AntiFederation"/> stays 0 everywhere, <see cref="RegionNetFederationSupport"/> equals the
/// raw support, the cause oracles return empty, and the per-colony save field is omitted (SaveGame v75) → byte-identical.
/// Accrual is integer, id-ordered and RNG-free (ADR-009).</para>
/// </summary>
public sealed partial class Game
{
    // ── Cause thresholds + accrual/decay magnitudes (WS3.5 first-pass, tunable like the WS3.7 gate counts). ────────
    // Read the "apathy" trigger from RAW support (colony.FederationSupportPercent), never from the opposition-reduced net,
    // so opposition can't feed back on itself into a runaway ratchet — it stays an honest tug-of-war (design invariant).

    /// <summary>A colony below this raw Federation Support percentage is politically apathetic and accrues opposition (matches the uniform referendum fallback bar, <see cref="RegionSupportForReferendum"/> = 50 — a colony under half-support is disengaged).</summary>
    internal const int LowSupportThreshold = 50;

    /// <summary>Opposition accrued per turn to a colony whose raw support is below <see cref="LowSupportThreshold"/> ("Apathy") — the strongest cause (a colony you neglect turns against Federation), net +2/turn after base decay; not a runaway lock.</summary>
    internal const int LowSupportAccrual = 3;

    /// <summary>The King's tax rate (<see cref="Player.TaxRate"/>) at or above which imperial pressure accrues opposition nation-wide ("Crown pressure") — 40%+ reads as oppressive, tying the monarch's tax-hike loop into the Federation drag.</summary>
    internal const int CrownPressureTaxThreshold = 40;

    /// <summary>Opposition accrued per turn to <b>every</b> human colony while the tax is at/above <see cref="CrownPressureTaxThreshold"/> — a smaller universal drag than the low-support cause, but a genuine driver (net +1/turn after base decay), so high tax alone can build opposition even on a well-supported colony.</summary>
    internal const int CrownPressureAccrual = 2;

    /// <summary>Base opposition <b>decay</b> per turn (cooling on its own): with an active cause opposition still nets upward, with none it fades — an active tug-of-war, not a ratchet.</summary>
    internal const int AntiFederationBaseDecay = 1;

    /// <summary>Extra opposition decay per turn while John Quick (the Corowa reconciler) sits in the human's Congress — the "lower the recovery cost" buy-down, read live from the persisted Congress (no new save state; the Quick-relief precedent).</summary>
    internal const int QuickDecayBonus = 1;

    /// <summary>Extra opposition decay per turn while Edmund Barton (the convention-drive reconciler) sits in the human's Congress — stacks with <see cref="QuickDecayBonus"/> (both seated → decay 3/turn, out-pacing a single active cause). Read live from Congress.</summary>
    internal const int BartonDecayBonus = 1;

    /// <summary>One-off opposition spike added to every human colony when a referendum is <b>rejected at the roll</b> (WS3.5, formalising the retired permanent ~10% support shed as a <em>decaying</em> drag — Chris's "decaying spike only" decision, faithful to the design's "temporary anti-Federation momentum").</summary>
    internal const int FailedReferendumSpike = 20;

    /// <summary>Smaller opposition spike added to the human's <b>New South Wales</b> colonies only when the referendum fails NSW's mobilisation <b>quota</b> (WS3.6 — Chris's "the quota-failure costs something" decision). Smaller than the full roll-rejection spike because a quota shortfall is a turnout technicality, not an outright rejection; still decaying (recoverable), so repeated quota misses don't permanently scar NSW.</summary>
    internal const int NswQuotaFailureSpike = 10;

    /// <summary>The opposition ceiling (%): opposition is bounded so a colony's net referendum support can drop by at most this many points (a 100%-raw colony still nets ≥ 40%) — heavy drag, but a colony's earned support is never fully erased. A whole-percent int, so it round-trips a save exactly.</summary>
    internal const int AntiFederationCap = 60;

    // ── Accrual (called from AccumulateLibertyAndElectFathers, after the per-colony liberty pass) ──────────────────

    /// <summary>
    /// Grows and decays each colony's <see cref="Colony.AntiFederation"/> for the turn (WS3.5). <b>Gated on the ruleset's
    /// Federation victory and the human player</b>, so a classic game and every AI accrue nothing (ADR-009). Runs
    /// <b>unconditionally per colony</b> (not inside the bell-output guard that gates <see cref="AccrueFederationSupport"/>),
    /// so opposition still grows on a zero- or negative-bell turn. Per colony the accrual is the sum of its active causes
    /// (raw support below <see cref="LowSupportThreshold"/>; the human's tax at/above <see cref="CrownPressureTaxThreshold"/>)
    /// less the per-turn decay (base + the seated reconcilers' bonuses), clamped 0–<see cref="AntiFederationCap"/>. The
    /// low-support trigger reads <b>raw</b> <see cref="Colony.FederationSupportPercent"/> (never the opposition-reduced net),
    /// so opposition cannot ratchet on itself. Colonies are visited in id order (deterministic); RNG-free.
    /// </summary>
    /// <param name="player">The player whose colonies accrue opposition (only the human does; a no-op for the AIs).</param>
    private void AccrueAntiFederationSentiment(Player player)
    {
        if (!Ruleset.VictoryFederation || !player.IsHuman)
        {
            return; // classic / non-Federation ruleset, or an AI player — byte-identical (ADR-009)
        }
        int decay = AntiFederationBaseDecay
            + (HasAbilityFor(_human, QuickCorowaAbility) ? QuickDecayBonus : 0)
            + (HasAbilityFor(_human, ConventionDriveAbility) ? BartonDecayBonus : 0);
        bool crownPressure = player.TaxRate >= CrownPressureTaxThreshold;
        foreach (Colony colony in ColoniesOf(player).OrderBy(c => c.Id))
        {
            int accrual = (colony.FederationSupportPercent < LowSupportThreshold ? LowSupportAccrual : 0)
                + (crownPressure ? CrownPressureAccrual : 0);
            colony.AddAntiFederation(accrual - decay, AntiFederationCap);
        }
    }

    // ── Read oracles (the panel + the referendum gate/roll consume these; all pure, RNG-free) ─────────────────────

    /// <summary>
    /// The human's average <b>Anti-Federation Sentiment</b> in <paramref name="regionKey"/> (WS3.5): the unweighted mean of
    /// the <see cref="Colony.AntiFederation"/> of every human colony whose tile lies in that region, 0 when the human holds
    /// none. 0 everywhere for a classic game (never accrues). The panel draws this as the red opposition overlay on each
    /// region's support gauge. Mirrors the unweighted per-colony average <see cref="RegionFederationSupport"/> uses.
    /// </summary>
    /// <param name="regionKey">One of <see cref="FederationRegionKeys"/> (e.g. <c>model.region.newSouthWales</c>).</param>
    public int RegionAntiFederationSentiment(string regionKey)
    {
        List<Colony> inRegion = HumanColoniesInRegion(regionKey);
        return inRegion.Count == 0 ? 0 : inRegion.Sum(c => c.AntiFederation) / inRegion.Count;
    }

    /// <summary>
    /// The human's <b>net</b> (opposition-reduced) Federation Support in <paramref name="regionKey"/> (WS3.5): the unweighted
    /// mean over the human colonies there of <c>clamp(FederationSupportPercent − AntiFederation, 0, 100)</c> — the effective
    /// support that counts <b>at the referendum</b>. Equals the raw <see cref="RegionFederationSupport"/> whenever no
    /// opposition has accrued (so classic and every existing scenario are unchanged, ADR-009). Read only by the referendum
    /// gate (<see cref="CheckPutToReferendum"/>) and the referendum roll (<see cref="AverageSettledSupport"/>); the earlier
    /// convention gate and the panel bars keep reading the raw earned support (Chris's referendum-stage-only-bite decision).
    /// </summary>
    /// <param name="regionKey">One of <see cref="FederationRegionKeys"/> (e.g. <c>model.region.newSouthWales</c>).</param>
    public int RegionNetFederationSupport(string regionKey)
    {
        List<Colony> inRegion = HumanColoniesInRegion(regionKey);
        return inRegion.Count == 0
            ? 0
            : inRegion.Sum(c => Math.Clamp(c.FederationSupportPercent - c.AntiFederation, 0, 100)) / inRegion.Count;
    }

    /// <summary>
    /// The <b>live cause tags</b> driving a region's Anti-Federation Sentiment (WS3.5), for the panel's opposition chip:
    /// "Apathy" (the region's raw support is below <see cref="LowSupportThreshold"/>), "Crown pressure" (the human's tax is
    /// at/above <see cref="CrownPressureTaxThreshold"/>), and "Referendum setback" (a referendum has been held and not yet
    /// carried). Derived live from current state — <b>no persisted cause history</b>. Empty for a classic game, for a region
    /// the human holds no colony in, or a region with no opposition banked. Order is fixed (apathy, crown, referendum).
    /// </summary>
    /// <param name="regionKey">One of <see cref="FederationRegionKeys"/> (e.g. <c>model.region.newSouthWales</c>).</param>
    public IReadOnlyList<string> RegionAntiFederationCauses(string regionKey)
    {
        if (!Ruleset.VictoryFederation
            || HumanColoniesInRegion(regionKey).Count == 0
            || RegionAntiFederationSentiment(regionKey) <= 0)
        {
            return [];
        }
        var causes = new List<string>();
        // "Apathy" is a PER-COLONY accrual driver, so surface it whenever ANY colony in the region is below the threshold
        // (the region average could sit above 50 while one neglected colony still accrues) — matches AccrueAntiFederationSentiment.
        if (HumanColoniesInRegion(regionKey).Any(c => c.FederationSupportPercent < LowSupportThreshold))
        {
            causes.Add("Apathy");
        }
        if (_human.TaxRate >= CrownPressureTaxThreshold)
        {
            causes.Add("Crown pressure");
        }
        if (_federationPhase == FederationPhase.Referendum && _referendumAttempts > 0 && !_referendumCarried)
        {
            causes.Add("Referendum setback");
        }
        return causes;
    }
}
