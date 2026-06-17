namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// A yield bonus a bonus-resource grants to a goods type (FreeCol resource-type
/// <c>&lt;modifier&gt;</c>): e.g. a minerals deposit's +3 ore, or a prime-sugar
/// tile's ×2 sugar. A modifier scoped to a unit type (e.g. an expert-farmer bonus)
/// applies only when that expert works the tile — folded in at the colony turn by
/// <c>Game.TileYield(player, workerType, …)</c> (86d3b6nrz slice 4); the unscoped
/// ones apply to any colonist (in <c>TileYieldPotential</c>).
/// </summary>
/// <param name="GoodsId">The goods this boosts (e.g. <c>model.goods.ore</c>).</param>
/// <param name="Type">How it combines with the tile's base yield.</param>
/// <param name="Value">The modifier value.</param>
/// <param name="Index">Application order (FreeCol <c>modifierIndex</c>; resource bonuses are index 10).</param>
/// <param name="ScopeUnitTypes">Unit types it is restricted to (empty = applies to any colonist).</param>
public sealed record ResourceModifier(
    string GoodsId, ModifierType Type, double Value, int Index, IReadOnlyList<string> ScopeUnitTypes)
{
    /// <summary>True when this bonus applies to any colonist (no unit-type scope).</summary>
    public bool IsUnscoped => ScopeUnitTypes.Count == 0;

    /// <summary>Applies this modifier to a running yield value.</summary>
    public double ApplyTo(double value) => ModifierMath.Apply(Type, value, Value);
}

/// <summary>
/// A bonus-resource type from the ruleset (game, minerals, prime timber, …) — the
/// special deposits that sit on a tile and boost what a colonist produces there.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.resource.minerals</c>.</param>
/// <param name="Modifiers">The yield bonuses this resource grants.</param>
public sealed record ResourceType(string Id, IReadOnlyList<ResourceModifier> Modifiers)
{
    /// <summary>Short name derived from the id: <c>model.resource.minerals</c> → <c>minerals</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];
}
