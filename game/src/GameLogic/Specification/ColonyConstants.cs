namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The colony/production scalar constants FreeCol carries in its data or model code, parsed once from the spec and read
/// wherever a hardcoded balance constant used to live. Carried on <see cref="Ruleset.ColonyConstants"/> — the
/// colony-economy analogue of <see cref="GameOptions"/> (which holds the base <c>gameOptions</c> immigration group).
/// </summary>
/// <remarks>
/// <para>
/// Each value is FreeCol-faithful and identical to the classic constant it replaces, so routing it through the ruleset
/// leaves the default game byte-identical (ADR-009). A spec that omits a source element falls back to its classic value
/// in <see cref="ClassicDefaults"/>. Sources differ by constant — see each parameter — because FreeCol itself keeps
/// some as spec data (the per-colonist food appetite) and some as model-code constants (the growth threshold, the
/// liberty-per-rebel divisor, the default export level); we make all four data-overridable for the variant.
/// </para>
/// <para>
/// Pure and immutable: parsed once, no state, no randomness. <see cref="FoodPerColonist"/> and
/// <see cref="FoodForGrowth"/> are read by <see cref="GameSession.Game"/> (which holds the ruleset) during the colony
/// turn; <see cref="LibertyPerRebel"/> and <see cref="DefaultExportLevel"/> are carried onto each
/// <see cref="Colonies.Colony"/> when it is founded or loaded (the <see cref="Colonies.Colony.Government"/> precedent),
/// since the pure colony model has no ruleset reference.
/// </para>
/// </remarks>
/// <param name="FoodPerColonist">
/// Food a single colonist eats per turn (the free colonist's <c>&lt;consumes id="model.goods.food"&gt;</c> in the spec;
/// classic <b>2</b>). FreeCol sums each unit type's <c>getConsumptionOf(food)</c>; every classic colonist shares the
/// <c>colonist</c> base's value of 2, so the per-colonist figure is that single number. See [colonies].
/// </param>
/// <param name="FoodForGrowth">
/// Stored surplus food a colony must bank to grow a new colonist (FreeCol <c>Settlement.FOOD_PER_COLONIST</c>; classic
/// <b>200</b>, mirrored in the spec as the free colonist's <c>&lt;required-goods id="model.goods.food" value="200"&gt;</c>).
/// Consumed when the colonist is born. See [colonies].
/// </param>
/// <param name="LibertyPerRebel">
/// Liberty (accumulated bells) one rebel colonist represents (FreeCol <c>Colony.LIBERTY_PER_REBEL</c>; classic
/// <b>200</b>). The Sons-of-Liberty percentage is <c>liberty·100 / (LibertyPerRebel·population)</c> and a 100%-SoL
/// colony's liberty caps at <c>LibertyPerRebel·population</c>. A model-code constant in FreeCol; made data here. See [colonies].
/// </param>
/// <param name="DefaultExportLevel">
/// The amount of a good a custom house retains before auto-exporting the surplus, for a good not yet configured
/// (FreeCol <c>ExportData.EXPORT_LEVEL_DEFAULT</c>; classic <b>50</b>). A model-code constant in FreeCol; made data here.
/// See [colonies].
/// </param>
public sealed record ColonyConstants(
    int FoodPerColonist,
    int FoodForGrowth,
    int LibertyPerRebel,
    int DefaultExportLevel)
{
    /// <summary>
    /// The classic ruleset's colony constants — the fallback when a spec omits a source, the source of truth for the
    /// default game's colony economy (2 / 200 / 200 / 50), and the value a colony built without a ruleset (tests)
    /// carries so its behaviour is unchanged.
    /// </summary>
    public static readonly ColonyConstants ClassicDefaults = new(
        FoodPerColonist: 2,
        FoodForGrowth: 200,
        LibertyPerRebel: 200,
        DefaultExportLevel: 50);
}
