namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// How many of a disaster's applicable effects fire when it strikes (FreeCol <c>Disaster.Effects</c>).
/// </summary>
public enum DisasterEffects
{
    /// <summary>Exactly one effect, chosen by weighted random over the effect probabilities.</summary>
    One,

    /// <summary>Each effect is rolled independently against its probability (0..100); any number may fire.</summary>
    Several,

    /// <summary>Every effect fires unconditionally.</summary>
    All,
}

/// <summary>
/// The discrete kinds of disaster effect we resolve (FreeCol <c>Effect</c> ids). A modifier-only effect (a
/// production penalty) carries <see cref="DisasterEffect.Modifiers"/> and uses <see cref="ProductionPenalty"/>;
/// the others are direct colony/player actions identified by this kind.
/// </summary>
public enum DisasterEffectKind
{
    /// <summary>The effect carries production-penalty <see cref="DisasterEffect.Modifiers"/> only — no direct action
    /// (FreeCol <c>lossOfTileProduction</c> / <c>lossOfBuildingProduction</c>, and the bankruptcy effect).</summary>
    ProductionPenalty,

    /// <summary>Plunder a fifth of the colony's plunder value from the owner's gold (FreeCol <c>LOSS_OF_MONEY</c>).</summary>
    LossOfMoney,

    /// <summary>Halve a random stored good in the colony, capped at 50 lost (FreeCol <c>LOSS_OF_GOODS</c>).</summary>
    LossOfGoods,

    /// <summary>Raze/downgrade one of the colony's burnable buildings (FreeCol <c>model.disaster.effect.lossOfBuilding</c> →
    /// <c>LOSS_OF_BUILDING</c>). No-op when the colony has no burnable building.</summary>
    LossOfBuilding,

    /// <summary>Remove one colony colonist; the colony's last colonist takes the colony with it (FreeCol
    /// <c>model.disaster.effect.lossOfUnit</c> → <c>LOSS_OF_UNIT</c>, whose <c>&lt;scope ability-id="model.ability.person"/&gt;</c>
    /// restricts the loss to a person — the kind already implies that target class, so the scope isn't stored). No-op when
    /// the colony has no colonist to lose (unreachable — a colony always has ≥ 1).</summary>
    LossOfUnit,

    /// <summary>Damage (or sink, if it has nowhere to repair) one of the colony's docked ships (FreeCol
    /// <c>model.disaster.effect.damagedUnit</c> → <c>DAMAGED_UNIT</c>, whose <c>&lt;scope ability-id="model.ability.navalUnit"/&gt;</c>
    /// restricts the target to a naval unit — the kind already implies that, so the scope isn't stored). No-op when the
    /// colony has no ship in port.</summary>
    DamagedUnit,
}

/// <summary>
/// One effect a disaster can have (FreeCol <c>Effect</c>): an id (its <see cref="Kind"/>), the percentage chance
/// it fires under a <see cref="DisasterEffects.Several"/> disaster, and any production-penalty modifiers it grants.
/// </summary>
/// <param name="Kind">Which discrete effect this is.</param>
/// <param name="Probability">0..100 chance this effect fires when the disaster's policy is <see cref="DisasterEffects.Several"/>; also the weight when the policy is <see cref="DisasterEffects.One"/>.</param>
/// <param name="Modifiers">Goods-production penalty modifiers this effect applies (FreeCol temporary percentage modifiers, e.g. −50% to a good). Empty for the direct-action effects.</param>
public sealed record DisasterEffect(
    DisasterEffectKind Kind,
    int Probability,
    IReadOnlyList<DisasterModifier> Modifiers);

/// <summary>
/// A production penalty a disaster effect applies to a single good (FreeCol disaster <c>&lt;modifier&gt;</c>):
/// e.g. a temporary −50% to <c>model.goods.grain</c> for three turns.
/// </summary>
/// <param name="GoodsId">The good whose production is penalised (a goods id, e.g. <c>model.goods.rum</c>).</param>
/// <param name="Type">How the value combines with the base (classic disasters all use <see cref="ModifierType.Percentage"/>).</param>
/// <param name="Value">The modifier value (a percentage for <see cref="ModifierType.Percentage"/>, e.g. −50).</param>
/// <param name="Duration">Turns the penalty lasts (0 = permanent until lifted; classic natural disasters use 3, bankruptcy 0).</param>
public sealed record DisasterModifier(
    string GoodsId,
    ModifierType Type,
    double Value,
    int Duration)
{
    /// <summary>Applies this penalty to a running production value.</summary>
    public double ApplyTo(double value) => ModifierMath.Apply(Type, value, Value);
}

/// <summary>
/// A disaster that can strike a colony or (for bankruptcy) a player (FreeCol <c>Disaster</c>): a set of possible
/// effects and the policy for how many of them fire. Natural disasters (flood, tornado, disease, …) are rolled
/// per colony each turn when the <c>model.option.naturalDisasters</c> game option is above 0 (classic default 0,
/// so none ever roll); the special <see cref="BankruptcyId"/> disaster is applied to a player who cannot pay
/// building upkeep (only when <c>model.option.enableUpkeep</c> is on, classic default off).
/// </summary>
/// <param name="Id">The disaster id, e.g. <c>model.disaster.flood</c> or <see cref="BankruptcyId"/>.</param>
/// <param name="Natural">Whether this is a natural disaster (eligible for the per-colony natural-disaster roll). Bankruptcy/indianRaid/conquest are not.</param>
/// <param name="NumberOfEffects">How many of <see cref="Effects"/> fire when this disaster strikes.</param>
/// <param name="Effects">The effects this disaster may apply.</param>
public sealed record Disaster(
    string Id,
    bool Natural,
    DisasterEffects NumberOfEffects,
    IReadOnlyList<DisasterEffect> Effects)
{
    /// <summary>The id of the special bankruptcy disaster (FreeCol <c>Disaster.BANKRUPTCY</c>).</summary>
    public const string BankruptcyId = "model.disaster.bankruptcy";
}
