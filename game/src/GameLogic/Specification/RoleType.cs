namespace CrownAndColony.GameLogic.Specification;

/// <summary>One required-good multiple a role consumes/holds — the "equipment" (FreeCol <c>Role.requiredGoods</c>).</summary>
/// <param name="GoodsId">The goods id (e.g. <c>model.goods.muskets</c>).</param>
/// <param name="Amount">Units of that good per role count (soldier = 50 muskets; armed brave = 25).</param>
public sealed record RoleRequiredGoods(string GoodsId, int Amount);

/// <summary>
/// A winner-equipment-capture rule (FreeCol <c>&lt;role-change&gt;</c>, defined on the role the winner
/// would <em>become</em>): a winner currently in role <paramref name="From"/> who defeats a unit in role
/// <paramref name="Capture"/> may capture that equipment and assume the containing role.
/// </summary>
/// <param name="From">The winner's current role id.</param>
/// <param name="Capture">The defeated unit's role id whose equipment is captured.</param>
public sealed record RoleChange(string From, string Capture);

/// <summary>
/// A unit's military status / equipment (FreeCol <c>Role</c>): the goods it carries, the offence and
/// defence it adds, the abilities it grants or requires, the role it downgrades to when its equipment is
/// lost, and the equipment-capture rules it enables. A unit with <see cref="DefaultRoleId"/> is unarmed.
/// Combat reads <see cref="Offence"/>/<see cref="Defence"/> (the folded index-30 additives); equipping reads
/// <see cref="RequiredGoods"/>; the loser-outcome chain reads <see cref="Downgrade"/> and the granted abilities.
/// </summary>
/// <param name="Id">Ruleset id, e.g. <c>model.role.soldier</c>.</param>
/// <param name="Downgrade">The role this one steps down to when its equipment is lost (null = the default role).</param>
/// <param name="MaximumCount">How many equipment multiples may be held (FreeCol <c>maximum-count</c>; default 1, pioneer 5).</param>
/// <param name="ExpertUnit">The expert unit type for this role (e.g. soldier → veteran soldier), or null.</param>
/// <param name="Offence">Offence the role adds, summed from its index-30 additive offence modifiers (soldier +2).</param>
/// <param name="Defence">Defence the role adds, summed from its index-30 additive defence modifiers (soldier +1).</param>
/// <param name="MovementBonus">Movement the role adds (dragoon/scout +9); parsed for completeness, not yet applied to movement.</param>
/// <param name="RequiredGoods">The equipment the role needs per count (empty for the unarmed default role).</param>
/// <param name="RequiredAbilities">Abilities a unit must (not) have to take the role (<c>native</c>, <c>refUnit</c>, <c>canBeEquipped</c>).</param>
/// <param name="GrantedAbilities">Abilities the role grants while held (<c>armed</c>, <c>mounted</c>, <c>disposeOnCombatLoss</c>, <c>captureUnits</c>).</param>
/// <param name="RoleChanges">The equipment-capture rules this role enables (see <see cref="RoleChange"/>).</param>
public sealed record RoleType(
    string Id,
    string? Downgrade,
    int MaximumCount,
    string? ExpertUnit,
    double Offence,
    double Defence,
    double MovementBonus,
    IReadOnlyList<RoleRequiredGoods> RequiredGoods,
    IReadOnlyDictionary<string, bool> RequiredAbilities,
    IReadOnlyDictionary<string, bool> GrantedAbilities,
    IReadOnlyList<RoleChange> RoleChanges)
{
    /// <summary>The unarmed role every unit starts in.</summary>
    public const string DefaultRoleId = "model.role.default";

    /// <summary>Short name derived from the id: <c>model.role.soldier</c> → <c>soldier</c>.</summary>
    public string ShortName => Id[(Id.LastIndexOf('.') + 1)..];

    /// <summary>True for an offensive (armed) role — one that adds offence and so disarms its bearer on a loss.</summary>
    public bool IsOffensive => Offence > 0;

    /// <summary>True when defeating the bearer destroys it outright (FreeCol <c>model.ability.disposeOnCombatLoss</c>).</summary>
    public bool DisposeOnCombatLoss => GrantedAbilities.GetValueOrDefault("model.ability.disposeOnCombatLoss");

    /// <summary>Whether the role may only be taken by a native unit (FreeCol required-ability <c>model.ability.native</c>).</summary>
    public bool RequiresNative => RequiredAbilities.GetValueOrDefault("model.ability.native");

    /// <summary>Whether the role may only be taken by a Royal Expeditionary Force unit (required-ability <c>model.ability.refUnit</c>).</summary>
    public bool RequiresRef => RequiredAbilities.GetValueOrDefault("model.ability.refUnit");
}
