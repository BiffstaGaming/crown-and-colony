namespace CrownAndColony.GameLogic.Specification;

/// <summary>The five categories a Founding Father belongs to (one is offered per category).</summary>
public enum FatherType
{
    /// <summary>Trade fathers (e.g. Adam Smith, Jakob Fugger).</summary>
    Trade,

    /// <summary>Exploration fathers.</summary>
    Exploration,

    /// <summary>Military fathers.</summary>
    Military,

    /// <summary>Political fathers.</summary>
    Political,

    /// <summary>Religious fathers.</summary>
    Religious,
}

/// <summary>How a <see cref="FatherModifier"/> combines with a base value (FreeCol <c>Modifier.ModifierType</c>).</summary>
public enum ModifierType
{
    /// <summary><c>base + value</c>.</summary>
    Additive,

    /// <summary><c>base × value</c>.</summary>
    Multiplicative,

    /// <summary><c>base + base·value/100</c>.</summary>
    Percentage,
}

/// <summary>
/// A bonus a Founding Father applies to something (FreeCol <c>&lt;modifier&gt;</c>):
/// e.g. Thomas Jefferson's +50% to <c>model.goods.bells</c>.
/// </summary>
/// <param name="TargetId">What it modifies (e.g. a goods id <c>model.goods.bells</c>).</param>
/// <param name="Type">How it combines with the base value.</param>
/// <param name="Value">The modifier value (a percentage for <see cref="ModifierType.Percentage"/>).</param>
/// <param name="Index">Application order — modifiers apply in ascending index (FreeCol <c>modifierIndex</c>).</param>
public sealed record FatherModifier(string TargetId, ModifierType Type, double Value, int Index)
{
    /// <summary>Applies this modifier to a running value.</summary>
    public double ApplyTo(double value) => Type switch
    {
        ModifierType.Additive => value + Value,
        ModifierType.Multiplicative => value * Value,
        _ => value + (value * Value / 100.0), // Percentage
    };
}

/// <summary>
/// A capability a Founding Father grants or denies (FreeCol <c>&lt;ability&gt;</c>):
/// e.g. William Brewster's <c>selectRecruit</c>, or his <c>canRecruitUnit=false</c>
/// scoped to indentured servants and petty criminals.
/// </summary>
/// <param name="Id">Ability id, e.g. <c>model.ability.selectRecruit</c>.</param>
/// <param name="Value">Whether the ability is granted (true) or denied (false).</param>
/// <param name="ScopeTypes">Unit/type ids the ability is scoped to (empty = unscoped).</param>
public sealed record FatherAbility(string Id, bool Value, IReadOnlyList<string> ScopeTypes);

/// <summary>
/// A Founding Father from the ruleset. Fathers are elected to the Continental
/// Congress by accumulating liberty; each has age weights that make it more or
/// less likely to be offered as the game progresses, and carries the modifiers
/// and abilities its election grants.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.foundingFather.adamSmith</c>.</param>
/// <param name="Type">The category this father is offered under.</param>
/// <param name="Weight1">Offer weight in the early game (age 1); 0 = never offered then.</param>
/// <param name="Weight2">Offer weight in the mid game (age 2).</param>
/// <param name="Weight3">Offer weight in the late game (age 3).</param>
/// <param name="Modifiers">Bonuses this father applies when elected.</param>
/// <param name="Abilities">Capabilities this father grants/denies when elected.</param>
public sealed record FoundingFather(
    string Id,
    FatherType Type,
    int Weight1,
    int Weight2,
    int Weight3,
    IReadOnlyList<FatherModifier> Modifiers,
    IReadOnlyList<FatherAbility> Abilities)
{
    /// <summary>Short name derived from the id: <c>model.foundingFather.adamSmith</c> → <c>adamSmith</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>The offer weight for a 1-based age (1, 2 or 3).</summary>
    public int WeightForAge(int age) => age switch
    {
        1 => Weight1,
        2 => Weight2,
        _ => Weight3,
    };
}
