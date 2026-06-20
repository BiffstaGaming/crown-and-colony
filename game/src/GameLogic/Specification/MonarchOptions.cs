namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The home-nation Monarch's difficulty-scoped tuning numbers — FreeCol's <c>model.difficulty.monarch</c> option
/// group plus the REF base composition (<c>model.option.refSize</c>) and the boycott back-tax factor. Carried on
/// <see cref="DifficultyOptions.Monarch"/> so the monarch logic reads data instead of hardcoded constants, making
/// the King transposable per game-mode / variant (the Australia variant can restate these). A small value record
/// (no <see cref="Ruleset"/> dependency); pure and immutable (ADR-009).
/// </summary>
/// <param name="Meddling">
/// How readily the King meddles (spec <c>model.option.monarchMeddling</c>; medium 2). Drives <c>dx = 1 + Meddling</c>
/// (3 at medium), which sets the grace period (<c>(6 − dx)·10</c> = 30 turns) and every monarch-action weight.
/// </param>
/// <param name="MaximumTaxRate">The tax cap the King will never raise past (spec <c>model.option.maximumTax</c>; medium 65).</param>
/// <param name="TaxAdjustment">
/// The tax-roll spread control (spec <c>model.option.taxAdjustment</c>; medium 2). A raise adds <c>1 + rnd[0, 3 +
/// turn/((6 − TaxAdjustment)·10))</c> (divisor 40 at medium); a reduction subtracts <c>1 + rnd[0, 10 − TaxAdjustment)</c>
/// (1 + rnd[0,8) at medium). The transforms stay in code; this stores the raw value.
/// </param>
/// <param name="MercenaryPricePercent">
/// The percentage of the European purchase price the King charges for a mercenary (spec <c>model.option.mercenaryPrice</c>;
/// medium 65 → offer price = europeanPurchasePrice × 65%).
/// </param>
/// <param name="SupportLandMountedUnits">
/// The number of mounted veterans a SUPPORT_LAND grant delivers at this level (spec <c>model.option.monarchSupport</c>;
/// medium 2). SUPPORT_LAND is never offered at medium (dx == 3), but the handler honours the level for fidelity.
/// </param>
/// <param name="ArrearsFactor">
/// The boycott back-tax multiplier — a tea-party boycott sets the good's arrears to <c>salePrice × ArrearsFactor</c>
/// (default 300). Spec <c>model.option.arrearsFactor</c> exists (classic medium 500) but the classic game has always
/// used 300 here; to stay value-preserving this defaults to 300 and is <b>not</b> read from the spec option (a variant
/// can still restate it on this record). See [monarchy].
/// </param>
/// <param name="RefBaseInfantry">King's-regular infantry in the base REF (spec <c>model.option.refSize.soldiers</c> number; medium 31). See [royal-expeditionary-force].</param>
/// <param name="RefBaseCavalry">King's-regular cavalry in the base REF (spec <c>model.option.refSize.dragoons</c> number; medium 15).</param>
/// <param name="RefBaseArtillery">Artillery in the base REF (spec <c>model.option.refSize.artillery</c> number; medium 14).</param>
/// <param name="RefBaseManOWar">Men-o-war in the base REF (spec <c>model.option.refSize.menOfWar</c> number; medium 8).</param>
public sealed record MonarchOptions(
    int Meddling,
    int MaximumTaxRate,
    int TaxAdjustment,
    int MercenaryPricePercent,
    int SupportLandMountedUnits,
    int ArrearsFactor,
    int RefBaseInfantry,
    int RefBaseCavalry,
    int RefBaseArtillery,
    int RefBaseManOWar)
{
    /// <summary>The classic <c>model.difficulty.medium</c> monarch values — the fallback and default source of truth.</summary>
    public static readonly MonarchOptions ClassicMedium = new(
        Meddling: 2,
        MaximumTaxRate: 65,
        TaxAdjustment: 2,
        MercenaryPricePercent: 65,
        SupportLandMountedUnits: 2,
        ArrearsFactor: 300,
        RefBaseInfantry: 31,
        RefBaseCavalry: 15,
        RefBaseArtillery: 14,
        RefBaseManOWar: 8);
}
