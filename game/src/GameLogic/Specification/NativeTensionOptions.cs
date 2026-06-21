using CrownAndColony.GameLogic.Natives;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The native-tension (alarm) tuning numbers — the hostile-act tension deltas, the land-taken and surrender alarms,
/// the per-turn alarm decay, and the first-contact gift range / tales-reveal radius. A small immutable value record
/// (the <see cref="AiTuning"/> / <see cref="MonarchOptions.ArrearsFactor"/> pattern) carried on
/// <see cref="DifficultyOptions.NativeTension"/> and read at the native-tension sites in <c>Game.cs</c>, so a
/// difficulty level — or an Australia-style variant — can retune how readily the natives anger without a code change.
/// </summary>
/// <remarks>
/// <para>
/// <b>Faithful-subset (no FreeCol per-difficulty option).</b> FreeCol's classic spec exposes none of these as a
/// <c>model.difficulty.*</c> / <c>model.option.*</c> option — they are <c>public static final int</c> constants in the
/// engine: the tension deltas and the surrender level in <c>Tension.java</c> (<c>TENSION_ADD_*</c>,
/// <c>TENSION_ADD_LAND_TAKEN</c>, <c>SURRENDERED</c>), the per-turn decay <c>-value/100 - 4</c> inlined in
/// <c>ServerPlayer.csNewTurn</c>, and the gift range / tales radius in <c>IndianSettlement.java</c>
/// (<c>GIFT_MINIMUM/MAXIMUM</c>, <c>TALES_RADIUS</c>). So these stay <b>constant across the shipped levels</b> — every
/// level reads <see cref="ClassicMedium"/> — but are now <b>data-overridable</b> instead of being buried as
/// <c>const</c>s in <c>Game.cs</c> / <c>NativeSettlement.cs</c>. The default game is byte-identical: same values, no
/// spec read, no save change (ADR-009).
/// </para>
/// <para>
/// Because there is no spec option to parse, <see cref="Ruleset.ParseDifficulty"/> simply assigns
/// <see cref="ClassicMedium"/> (the <see cref="MonarchOptions.ArrearsFactor"/> / <see cref="AiTuning"/> pattern). The
/// <see cref="ClassicMedium"/> values reference the existing FreeCol-source constants on <see cref="NativeSettlement"/>
/// and <see cref="GameSession.Game"/> so those stay the single drift-guarded source of truth (<c>NativeConstantsTests</c>).
/// Pure/immutable (ADR-009): no state, no RNG.
/// </para>
/// <para>
/// Not routed here (deliberately): the <b>alarm band limits</b> (<c>AlarmHappyMax</c>…) and <see cref="NativeSettlement.MaxAlarm"/>
/// remain on <see cref="NativeSettlement"/> because the band is computed by <see cref="NativeSettlement.AlarmLevel"/>,
/// which has no <see cref="Ruleset"/> reference; and the <b>unit-promotion probability</b> is <i>already</i> data-driven
/// — it is the per-row <c>probability</c> on the spec's <c>&lt;unit-change-type id="model.unitChange.promotion"&gt;</c>
/// rows (parsed into <see cref="UnitChange.Probability"/>, classic 100), consumed directly by <c>Game.ApplyWinnerPromotion</c>,
/// with no hardcoded constant to route. See <c>docs/systems/natives.md</c> / <c>combat.md</c>.
/// </para>
/// </remarks>
/// <param name="AddMinor">Tension added for a minor slight (FreeCol <c>Tension.TENSION_ADD_MINOR</c>, 100).</param>
/// <param name="AddNormal">Tension added for an ordinary hostile act — being attacked, a tribute insult (FreeCol <c>TENSION_ADD_NORMAL</c>, 200).</param>
/// <param name="AddMajor">Tension added for a major hostile act such as razing a settlement (FreeCol <c>TENSION_ADD_MAJOR</c>, 300).</param>
/// <param name="AddUnitDestroyed">Tension added when one of the nation's units is destroyed (FreeCol <c>TENSION_ADD_UNIT_DESTROYED</c>, 400).</param>
/// <param name="AddSettlementAttacked">Tension added when a settlement is attacked/sacked (FreeCol <c>TENSION_ADD_SETTLEMENT_ATTACKED</c>, 500).</param>
/// <param name="AddCapitalAttacked">Tension added when the capital is attacked (FreeCol <c>TENSION_ADD_CAPITAL_ATTACKED</c>, 600).</param>
/// <param name="LandTaken">Alarm added to the robbed nation when land is taken by force rather than bought (FreeCol <c>TENSION_ADD_LAND_TAKEN</c>, 200).</param>
/// <param name="Surrendered">The alarm a nation's settlements are set to when it surrenders — its capital sacked (FreeCol <c>Tension.SURRENDERED</c> = (Content + Happy)/2 = 350).</param>
/// <param name="DecayDivisor">
/// The proportional term in the per-turn alarm decay (FreeCol <c>ServerPlayer.csNewTurn</c>: <c>-value/100 - 4</c>);
/// each turn alarm cools by <c>value / DecayDivisor + DecayBase</c>. Classic 100.
/// </param>
/// <param name="DecayBase">The flat term in the per-turn alarm decay (FreeCol's <c>- 4</c>); classic 4.</param>
/// <param name="GiftMinimum">Minimum gold in a settlement's first-contact gift (FreeCol <c>IndianSettlement.GIFT_MINIMUM</c>, 10).</param>
/// <param name="GiftMaximum">Maximum gold in a settlement's first-contact gift (FreeCol <c>IndianSettlement.GIFT_MAXIMUM</c>, 80).</param>
/// <param name="TalesRevealRadius">
/// Tiles a chief reveals when first spoken to ("tales of nearby lands"). FreeCol's <c>TALES_RADIUS</c> is 6; our
/// classic default is the existing scaled-down 3 (a documented deviation for our smaller default map — routing the
/// value preserves it, it does not change it). See [natives].
/// </param>
public sealed record NativeTensionOptions(
    int AddMinor,
    int AddNormal,
    int AddMajor,
    int AddUnitDestroyed,
    int AddSettlementAttacked,
    int AddCapitalAttacked,
    int LandTaken,
    int Surrendered,
    int DecayDivisor,
    int DecayBase,
    int GiftMinimum,
    int GiftMaximum,
    int TalesRevealRadius)
{
    /// <summary>
    /// The classic <c>medium</c> native-tension tuning — the fallback and default, and (since FreeCol scales none of
    /// these by difficulty) the value every shipped level uses. The values reference the existing FreeCol-pinned
    /// constants so the single source of truth (and its <c>NativeConstantsTests</c> drift guard) is preserved; routing
    /// them off the old <c>const</c>s is value-preserving, so the default game is byte-identical (ADR-009).
    /// </summary>
    public static readonly NativeTensionOptions ClassicMedium = new(
        AddMinor: NativeSettlement.TensionAddMinor,                       // FreeCol TENSION_ADD_MINOR (100)
        AddNormal: NativeSettlement.TensionAddNormal,                     // TENSION_ADD_NORMAL (200)
        AddMajor: NativeSettlement.TensionAddMajor,                       // TENSION_ADD_MAJOR (300)
        AddUnitDestroyed: NativeSettlement.TensionAddUnitDestroyed,       // TENSION_ADD_UNIT_DESTROYED (400)
        AddSettlementAttacked: NativeSettlement.TensionAddSettlementAttacked, // TENSION_ADD_SETTLEMENT_ATTACKED (500)
        AddCapitalAttacked: NativeSettlement.TensionAddCapitalAttacked,   // TENSION_ADD_CAPITAL_ATTACKED (600)
        LandTaken: NativeSettlement.TensionAddLandTaken,                  // TENSION_ADD_LAND_TAKEN (200)
        Surrendered: NativeSettlement.SurrenderedAlarm,                   // SURRENDERED (350)
        DecayDivisor: 100,                                                // ServerPlayer.csNewTurn: -value/100 - 4
        DecayBase: 4,
        GiftMinimum: 10,                                                  // IndianSettlement.GIFT_MINIMUM
        GiftMaximum: 80,                                                  // IndianSettlement.GIFT_MAXIMUM
        TalesRevealRadius: 3);                                            // FreeCol TALES_RADIUS 6, scaled to 3 (our map)
}
