using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The relationship a First Nations people holds with the colonial player (WS5.3, design doc 18 "Relationship states").
/// Derived on read from the three axes — Respect, Tension and Country Pressure — never stored.
/// </summary>
public enum FirstNationsRelationship
{
    /// <summary>No contact has been made. No diplomacy is possible.</summary>
    Unknown = 0,

    /// <summary>Contact made, but trust is not yet established (Respect below the trade bar). Limited trade.</summary>
    CautiousContact = 1,

    /// <summary>Respect 45+, Tension below 50. Trade and minor knowledge exchange.</summary>
    TradeRelationship = 2,

    /// <summary>Respect 60+, Tension below 40. Formal agreements become possible (WS5.4).</summary>
    AgreementRelationship = 3,

    /// <summary>Respect 80+, Tension below 30. Major knowledge exchange and legitimacy bonuses.</summary>
    TrustedRelationship = 4,

    /// <summary>Tension 60+. Trade reduced, warnings given — outranks the Respect-based states.</summary>
    Strained = 5,

    /// <summary>Tension 80+. Resistance likely; agreements suspended.</summary>
    Hostile = 6,
}

/// <summary>
/// WS5.3 — the First Nations relationship model (design docs 15 "First Nations Design Principles" and 18 "Diplomacy,
/// Tension and Respect Mechanics"). The inherited classic model tracks a single axis (alarm) that only ever rises with
/// colonial presence and is relieved by missions — a conquest-era framing. This adds the designed **three axes**:
///
/// <list type="bullet">
/// <item><b>Respect</b> — trust earned or destroyed by how the colonist behaves. Persisted per nation (save v76),
/// because it is a record of past conduct and cannot be re-derived from the board.</item>
/// <item><b>Country Pressure</b> — the cumulative colonial footprint pressing on Country. <b>Derived</b> from the
/// current board (settlements, population, armed units), so it needs no save state.</item>
/// <item><b>Tension</b> — conflict risk. Reuses the existing per-settlement alarm engine rather than duplicating it,
/// and now takes a per-turn contribution from Country Pressure and from low Respect (doc 18: "Tension increases from
/// Country Pressure, Low Respect").</item>
/// </list>
///
/// <para><b>Scope of this slice.</b> This is the mechanics layer only. The designed Agreements (WS5.4), Knowledge
/// Exchange, diplomatic units and the First Nations diplomacy UI are <b>not</b> built here, and no new player-facing
/// cultural content, naming or framing is introduced — that work is gated on ADR-022 and First Nations consultation
/// (docs 15/16, and the project's binding ICIP rule). The axes and states below are deliberately expressed in
/// gameplay terms so they can be surfaced later under whatever presentation that review settles on.</para>
///
/// <para><b>Off for classic (ADR-009 byte-stability):</b> every path is gated on
/// <see cref="Specification.Ruleset.VictoryFederation"/> — the ruleset flag that marks the Australian variant, the same
/// gate the Anti-Federation Sentiment system and the Federation loop use. A classic game accrues no Respect, applies no
/// Country-Pressure tension, writes no save token and draws no RNG, so its soak stays byte-identical.</para>
/// </summary>
public sealed partial class Game
{
    // ── Scale + tuning (first-pass, tunable like the WS3.7 gate counts) ──────────────────────────────────────

    /// <summary>The maximum value of the Respect and Country Pressure axes — both are read as plain 0–100 percentages (doc 18 states its state thresholds on that scale).</summary>
    internal const int FirstNationsAxisMax = 100;

    /// <summary>
    /// The Respect a people extends on <b>first contact</b> (doc 18: "Cautious Contact, Respect 20–50" — the midpoint).
    /// Deliberately non-zero: the starting position is wary, not hostile, and the colonist has to actively destroy it.
    /// </summary>
    internal const int FirstNationsRespectBaseline = 35;

    /// <summary>Respect gained when the colonist <b>pays</b> for land instead of taking it (doc 18: "Compensation for damage", "Honoured agreements").</summary>
    internal const int RespectLandPaid = 5;

    /// <summary>Respect destroyed when the colonist <b>seizes</b> land (doc 18: "Seizure of land" — the sharpest single loss in this slice, and deliberately larger than the gain from paying).</summary>
    internal const int RespectLandSeized = -12;

    /// <summary>
    /// The share of a nation's Country Pressure that converts to per-turn Tension (doc 18: "Tension increases from
    /// Country Pressure"). Percent — 25 means a nation under a full 100 pressure gains 25 alarm points per turn before
    /// the existing decay, a slow but compounding drag that the colonist cannot fix with a mission alone.
    /// </summary>
    internal const int CountryPressureTensionPercent = 25;

    /// <summary>
    /// The Respect at or below which low trust itself starts feeding Tension (doc 18: "Tension increases from … Low
    /// Respect"). Above this the relationship is stable enough that mistrust adds nothing.
    /// </summary>
    internal const int LowRespectTensionThreshold = 25;

    /// <summary>The per-turn Tension added by a relationship sitting at or below <see cref="LowRespectTensionThreshold"/> Respect.</summary>
    internal const int LowRespectTensionPerTurn = 10;

    /// <summary>
    /// The colonial footprint (summed colony population + armed-unit offence within reach of a nation's settlements)
    /// that reads as a <b>full</b> 100 Country Pressure. First-pass: a handful of grown colonies pressing on one
    /// people's Country saturates the axis.
    /// </summary>
    internal const int CountryPressureFootprintForMax = 120;

    /// <summary>Extra tiles beyond a settlement's claimable radius within which colonial presence counts toward Country Pressure — the same reach the ambient-alarm pass uses, so the two axes read the same board.</summary>
    private const int CountryPressureRadiusBonus = NativeAlarmRadius;

    // ── State (persisted, SaveGame v76; omitted when empty so classic stays byte-identical) ──────────────────

    /// <summary>
    /// Respect each First Nations people holds toward the human, by native <b>nation type id</b> (e.g.
    /// <c>model.nationType.eoraNation</c>) — nation-scoped like tile ownership and the land-taken alarm, not
    /// settlement-scoped. Absent = not yet contacted. Empty for every classic game and for an Australia game that has
    /// met nobody, so it is omitted from the save and the default game stays byte-identical (ADR-009). Persisted
    /// (SaveGame v76).
    /// </summary>
    private readonly Dictionary<string, int> _firstNationsRespect = new();

    /// <summary>The banked Respect values (WS5.3), for save serialisation. Empty unless an Australia game has made contact.</summary>
    internal IReadOnlyDictionary<string, int> FirstNationsRespect => _firstNationsRespect;

    /// <summary>
    /// Re-installs a restored Respect value (save load, v76). Ignores values outside 0–<see cref="FirstNationsAxisMax"/>
    /// defensively, so a classic / no-contact save (which stores none) restores an empty map (ADR-009).
    /// </summary>
    /// <param name="nationTypeId">The First Nations nation type id the value belongs to.</param>
    /// <param name="respect">The banked Respect, 0–100.</param>
    internal void SetFirstNationsRespect(string nationTypeId, int respect)
    {
        if (respect is >= 0 and <= FirstNationsAxisMax)
        {
            _firstNationsRespect[nationTypeId] = respect;
        }
    }

    // ── Respect (persisted axis) ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Respect <paramref name="nationTypeId"/> holds toward the human, 0–100 (WS5.3). Returns 0 for a people the
    /// human has not contacted and for every classic game. A pure, RNG-free read.
    /// </summary>
    /// <param name="nationTypeId">A First Nations nation type id (e.g. <c>model.nationType.eoraNation</c>).</param>
    public int FirstNationsRespectFor(string nationTypeId) =>
        !Ruleset.VictoryFederation ? 0 : _firstNationsRespect.GetValueOrDefault(nationTypeId);

    /// <summary>
    /// Adjusts <paramref name="nationTypeId"/>'s Respect toward the human by <paramref name="delta"/>, clamped 0–100
    /// (WS5.3). Seeds the nation at <see cref="FirstNationsRespectBaseline"/> first if this is the first time its
    /// Respect has ever moved, so a people's record starts from wary-but-open rather than from nothing. <b>A no-op
    /// unless the ruleset enables the Australian variant</b> (classic keeps its single-axis alarm model untouched) →
    /// byte-identical (ADR-009). RNG-free.
    /// </summary>
    /// <param name="nationTypeId">The First Nations nation type id whose Respect moves.</param>
    /// <param name="delta">The change in Respect (positive earns trust, negative destroys it).</param>
    internal void ChangeFirstNationsRespect(string nationTypeId, int delta)
    {
        if (!Ruleset.VictoryFederation || delta == 0)
        {
            return; // classic / non-Australian ruleset — byte-identical (ADR-009)
        }
        int current = _firstNationsRespect.TryGetValue(nationTypeId, out int banked) ? banked : FirstNationsRespectBaseline;
        _firstNationsRespect[nationTypeId] = Math.Clamp(current + delta, 0, FirstNationsAxisMax);
    }

    /// <summary>
    /// Records that the human has made contact with <paramref name="nationTypeId"/> (WS5.3), seeding its Respect at
    /// <see cref="FirstNationsRespectBaseline"/> if nothing is banked yet. Idempotent — an already-recorded
    /// relationship is left exactly as it stands, so re-contact never launders away destroyed trust. A no-op for
    /// classic (ADR-009).
    /// </summary>
    /// <param name="nationTypeId">The First Nations nation type id contact was made with.</param>
    internal void RecordFirstNationsContact(string nationTypeId)
    {
        if (Ruleset.VictoryFederation && !_firstNationsRespect.ContainsKey(nationTypeId))
        {
            _firstNationsRespect[nationTypeId] = FirstNationsRespectBaseline;
        }
    }

    /// <summary>
    /// Whether the human has made contact with <paramref name="nationTypeId"/> (WS5.3): either its Respect is on record,
    /// or the human's explored fog covers one of its settlements. The fog branch means simply finding a people's
    /// community counts as contact, without needing a separate persisted flag. False for classic.
    /// </summary>
    /// <param name="nationTypeId">A First Nations nation type id.</param>
    public bool HasContactedFirstNations(string nationTypeId)
    {
        if (!Ruleset.VictoryFederation)
        {
            return false;
        }
        return _firstNationsRespect.ContainsKey(nationTypeId)
            || _nativeSettlements.Any(s => s.NationTypeId == nationTypeId && _human.Explored.Contains(s.Position));
    }

    // ── Country Pressure (derived axis — no save state) ──────────────────────────────────────────────────────

    /// <summary>
    /// The <b>Country Pressure</b> the human's presence exerts on <paramref name="nationTypeId"/>, 0–100 (WS5.3, doc 18
    /// "Represents cumulative colonial pressure on Country"). Derived from the current board — for every settlement of
    /// that people, the human colonies within reach contribute their population and every human armed land unit within
    /// reach contributes its offence — scaled against <see cref="CountryPressureFootprintForMax"/>. Reuses the same
    /// reach as the ambient-alarm pass so the two axes read the same footprint.
    ///
    /// <para>Deriving rather than accumulating is a deliberate first-pass simplification: it means pressure <em>falls</em>
    /// when the colonist withdraws (doc 18 lists "Withdrawal from sensitive sites" as a Tension reliever), and it costs
    /// no save state. The design's roads / rail / telegraph / pastoral-run / mine sources are not yet counted — those
    /// need a tile-improvement ownership read that does not exist (the same model gap the ambient-alarm tile-control
    /// branch documents). Recorded as a follow-up.</para>
    /// </summary>
    /// <param name="nationTypeId">A First Nations nation type id.</param>
    /// <returns>0–100; 0 for classic and for a people with no settlements.</returns>
    public int CountryPressureFor(string nationTypeId)
    {
        if (!Ruleset.VictoryFederation)
        {
            return 0;
        }
        int footprint = 0;
        foreach (NativeSettlement settlement in _nativeSettlements.Where(s => s.NationTypeId == nationTypeId))
        {
            int radius = Ruleset.Settlement(settlement.SettlementTypeId).ClaimableRadius + CountryPressureRadiusBonus;
            foreach (Colony colony in _colonies.Where(IsHumanOwned))
            {
                if (ChebyshevDistance(colony.Position, settlement.Position) <= radius)
                {
                    footprint += colony.Population;
                }
            }
            foreach (Unit unit in _units.Where(u => u.IsOnMap && IsHumanOwned(u) && !u.Type.IsNaval))
            {
                if (ChebyshevDistance(unit.Position, settlement.Position) <= radius)
                {
                    footprint += (int)unit.Type.Offence; // unarmed colonists (offence 0) press on nothing
                }
            }
        }
        return Math.Clamp(footprint * FirstNationsAxisMax / CountryPressureFootprintForMax, 0, FirstNationsAxisMax);
    }

    // ── Tension (reuses the existing alarm engine, read on the 0–100 scale) ──────────────────────────────────

    /// <summary>
    /// The Tension <paramref name="nationTypeId"/> holds toward the human on the doc-18 <b>0–100</b> scale (WS5.3) — the
    /// existing nation-level alarm (<see cref="TribeTensionFor"/>, 0–<see cref="MaxTension"/>) rescaled, so the designed
    /// relationship thresholds can be stated in the design's own numbers without forking the alarm engine. A pure read.
    /// </summary>
    /// <param name="nationTypeId">A First Nations nation type id.</param>
    public int FirstNationsTensionFor(string nationTypeId) =>
        !Ruleset.VictoryFederation
            ? 0
            : Math.Clamp(TribeTensionFor(nationTypeId, _human.PlayerId) * FirstNationsAxisMax / MaxTension, 0, FirstNationsAxisMax);

    /// <summary>
    /// The per-turn Tension contribution from Country Pressure and low Respect (WS5.3, doc 18: "Tension increases from
    /// Country Pressure, Low Respect"). For each contacted people, a share
    /// (<see cref="CountryPressureTensionPercent"/>) of its Country Pressure — plus a flat
    /// <see cref="LowRespectTensionPerTurn"/> when Respect has fallen to <see cref="LowRespectTensionThreshold"/> or
    /// below — is added to every one of its settlements' alarm toward the human, on the engine's own 0–1100 scale.
    ///
    /// <para>This is what makes the model more than a rename: under the inherited engine a colonist could sit on a
    /// people's Country indefinitely as long as no unit was adjacent, and a mission would hold alarm at zero. Now the
    /// footprint itself keeps pressing, and a relationship the colonist has actually degraded keeps degrading.</para>
    ///
    /// <para>Runs from <see cref="EndTurn"/> immediately after the ambient-alarm pass. A strict no-op for classic
    /// (ADR-009) — no alarm moves, so the classic soak is byte-identical. Deterministic: no RNG, stable iteration.</para>
    /// </summary>
    internal void ApplyFirstNationsPressureTension()
    {
        if (!Ruleset.VictoryFederation)
        {
            return; // classic keeps its inherited single-axis alarm model exactly as it was (ADR-009)
        }

        foreach (string nationTypeId in _nativeSettlements.Select(s => s.NationTypeId).Distinct().Order())
        {
            if (!HasContactedFirstNations(nationTypeId))
            {
                continue; // a people the colonist has never met exerts and feels nothing
            }

            int pressure = CountryPressureFor(nationTypeId);
            int delta = pressure * CountryPressureTensionPercent / 100;
            if (_firstNationsRespect.TryGetValue(nationTypeId, out int respect) && respect <= LowRespectTensionThreshold)
            {
                delta += LowRespectTensionPerTurn;
            }
            if (delta <= 0)
            {
                continue;
            }

            // The axes are 0–100; the alarm engine is 0–MaxTension. Scale up so a full-pressure relationship moves the
            // underlying alarm meaningfully rather than by a rounding error.
            int alarmDelta = delta * MaxTension / FirstNationsAxisMax;
            foreach (NativeSettlement settlement in _nativeSettlements.Where(s => s.NationTypeId == nationTypeId))
            {
                ChangeNativeAlarm(settlement, _human.PlayerId, alarmDelta);
            }
        }
    }

    // ── Relationship state (derived from all three axes) ─────────────────────────────────────────────────────

    /// <summary>
    /// The <see cref="FirstNationsRelationship"/> <paramref name="nationTypeId"/> currently holds with the human
    /// (WS5.3) — doc 18's relationship table, read live from Respect and Tension. <b>Tension outranks Respect</b>: a
    /// people pushed to Strained or Hostile is in that state however much trust was previously banked, which is the
    /// point of keeping the two axes separate. A pure, RNG-free read; always
    /// <see cref="FirstNationsRelationship.Unknown"/> for classic.
    /// </summary>
    /// <param name="nationTypeId">A First Nations nation type id.</param>
    public FirstNationsRelationship RelationshipWithFirstNations(string nationTypeId)
    {
        if (!Ruleset.VictoryFederation || !HasContactedFirstNations(nationTypeId))
        {
            return FirstNationsRelationship.Unknown;
        }

        int tension = FirstNationsTensionFor(nationTypeId);
        if (tension >= 80) return FirstNationsRelationship.Hostile;
        if (tension >= 60) return FirstNationsRelationship.Strained;

        int respect = FirstNationsRespectFor(nationTypeId);
        if (respect >= 80 && tension < 30) return FirstNationsRelationship.TrustedRelationship;
        if (respect >= 60 && tension < 40) return FirstNationsRelationship.AgreementRelationship;
        if (respect >= 45 && tension < 50) return FirstNationsRelationship.TradeRelationship;
        return FirstNationsRelationship.CautiousContact;
    }

    /// <summary>
    /// Every First Nations people on the map with its three axes and current relationship state (WS5.3) — the data a
    /// future First Nations panel (WS5.6, consultation-gated) draws, and the shape the
    /// <see cref="CommonwealthScorecardForHuman"/> First Nations category will move onto once Agreements land. Ordered
    /// by nation type id (deterministic). Empty for classic.
    /// </summary>
    public IReadOnlyList<(string NationTypeId, int Respect, int Tension, int CountryPressure, FirstNationsRelationship Relationship)> FirstNationsSummary()
    {
        if (!Ruleset.VictoryFederation)
        {
            return [];
        }
        return _nativeSettlements
            .Select(s => s.NationTypeId)
            .Distinct()
            .Order()
            .Select(id => (id, FirstNationsRespectFor(id), FirstNationsTensionFor(id), CountryPressureFor(id), RelationshipWithFirstNations(id)))
            .ToList();
    }
}
