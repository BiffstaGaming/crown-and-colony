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

/// <summary>
/// A Founding Father from the ruleset. Fathers are elected to the Continental
/// Congress by accumulating liberty; each has age weights that make it more or
/// less likely to be offered as the game progresses. (Their gameplay effects are
/// recorded but not yet applied — a later slice.)
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.foundingFather.adamSmith</c>.</param>
/// <param name="Type">The category this father is offered under.</param>
/// <param name="Weight1">Offer weight in the early game (age 1); 0 = never offered then.</param>
/// <param name="Weight2">Offer weight in the mid game (age 2).</param>
/// <param name="Weight3">Offer weight in the late game (age 3).</param>
public sealed record FoundingFather(
    string Id,
    FatherType Type,
    int Weight1,
    int Weight2,
    int Weight3)
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
