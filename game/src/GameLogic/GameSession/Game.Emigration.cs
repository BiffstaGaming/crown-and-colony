using System.Linq;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Units;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A pending emigration choice (FreeCol's <c>selectRecruit</c>): when a player who has earned William Brewster
/// (<c>model.ability.selectRecruit</c>) is due an emigrant, the engine pauses and offers the three dock recruits so
/// the player can pick which one steps ashore in Europe, rather than a random one auto-emigrating. The same seam
/// also serves the <b>Fountain of Youth</b> immigration burst (FreeCol <c>MigrationType.FOUNTAIN</c>): a Brewster
/// human picks each of the <c>dx</c> FoY immigrants, distinguished by <see cref="IsFountainOfYouth"/>.
/// </summary>
/// <param name="PlayerId">The player the choice belongs to (the human; an AI never pauses).</param>
/// <param name="RecruitTypeIds">The unit-type ids of the recruits currently on the dock, by slot (the choices).</param>
/// <param name="IsFountainOfYouth">True when this is a Fountain-of-Youth pick (no immigration is consumed; <see cref="Remaining"/> picks are owed). False for an ordinary Brewster emigrant.</param>
/// <param name="Remaining">For a Fountain-of-Youth burst, how many picks (including this one) are still owed; ignored for an ordinary emigrant (whose re-arm is driven by banked immigration).</param>
public sealed record PendingEmigrationChoice(
    int PlayerId,
    IReadOnlyList<string> RecruitTypeIds,
    bool IsFountainOfYouth = false,
    int Remaining = 0);

public sealed partial class Game
{
    // Set by the emigration step when a selectRecruit human is due an emigrant; cleared by ChooseEmigrant. In-memory
    // only (not persisted): a pending choice that survived a save would be a rare edge case, deferred to the save
    // stream — a reloaded game simply re-pauses on the next turn the player is still due an emigrant.
    private PendingEmigrationChoice? _pendingEmigration;

    /// <summary>
    /// The emigration choice awaiting the human player, or null when none is pending (FreeCol's <c>selectRecruit</c>
    /// prompt). Set only for a human who has earned William Brewster; the UI shows the three recruits and resolves it
    /// via <see cref="ChooseEmigrant"/>. Read-only for presentation (ADR-006).
    /// </summary>
    public PendingEmigrationChoice? PendingEmigration => _pendingEmigration;

    /// <summary>
    /// Resolves the <see cref="PendingEmigration"/> choice: the recruit in <paramref name="slot"/> emigrates to Europe
    /// (its dock slot refills with a fresh weighted draw). For an ordinary Brewster emigrant, immigration is consumed
    /// and the next target rises — exactly the per-emigrant bookkeeping the auto-path does (FreeCol
    /// <c>ServerPlayer.csEmigrate</c>, the SELECT case) — and the choice re-arms while crosses still owe an emigrant.
    /// For a <b>Fountain of Youth</b> pick (FreeCol <c>MigrationType.FOUNTAIN</c>) <b>no immigration is consumed</b>;
    /// the burst's remaining count drops by one and the choice re-arms until all <c>dx</c> picks are made. Otherwise
    /// it clears. No-op (returns null) when no choice is pending or the slot is out of range.
    /// </summary>
    /// <param name="slot">The dock slot (0..<see cref="RecruitSlots"/>−1) of the recruit to take.</param>
    /// <returns>The emigrated unit, docked in Europe, or null when there was nothing to resolve.</returns>
    public Unit? ChooseEmigrant(int slot)
    {
        if (_pendingEmigration is not { } pending || PlayerById(pending.PlayerId) is not { } player)
        {
            return null;
        }
        if (slot < 0 || slot >= player.RecruitDock.Count)
        {
            return null;
        }

        Unit recruit = Emigrate(player, slot);   // extract + dock refill (same as the auto-path); FoY skips the cut

        if (pending.IsFountainOfYouth)
        {
            // FreeCol MigrationType.FOUNTAIN: only the burst counter falls — no reduceImmigration / no target rise.
            int remaining = pending.Remaining - 1;
            _pendingEmigration = remaining > 0 && player.RecruitDock.Count > 0
                ? new PendingEmigrationChoice(player.PlayerId, player.RecruitDock.ToList(), IsFountainOfYouth: true, Remaining: remaining)
                : null;
            return recruit;
        }

        ReduceImmigration(player);
        player.ImmigrationRequired += Ruleset.Difficulty.CrossesIncrement;

        // A bumper crop of crosses can owe more than one emigrant in a turn — re-arm with the refilled dock so the
        // player chooses each in turn; otherwise the backlog is cleared and play resumes.
        _pendingEmigration = player.RecruitDock.Count > 0 && player.Immigration >= EffectiveImmigrationRequired(player)
            ? new PendingEmigrationChoice(player.PlayerId, player.RecruitDock.ToList())
            : null;
        return recruit;
    }

    /// <summary>
    /// FreeCol <c>equipEuropeanRecruits</c> (<c>model.option.equipEuropeanRecruits</c>, classic default <b>true</b>):
    /// a recruit emigrating/recruited from the Europe dock steps ashore <b>already equipped in its unit type's default
    /// role</b> (FreeCol <c>ServerPlayer.csEmigrate</c> spawns <c>new ServerUnit(…, recruit.getRole())</c>, whose role
    /// is <c>UnitType.getDefaultRole()</c> — see <c>Europe.addRecruitable</c>), instead of the bare unarmed default
    /// role. In the classic ruleset four expert types carry a non-default <c>&lt;default-role&gt;</c>, so they alone
    /// arrive equipped (every other recruit keeps <see cref="Specification.RoleType.DefaultRoleId"/>):
    /// <list type="bullet">
    ///   <item><description>veteran soldier → <c>model.role.soldier</c> (steps off the boat already a soldier),</description></item>
    ///   <item><description>hardy pioneer → <c>model.role.pioneer</c> (already a pioneer),</description></item>
    ///   <item><description>seasoned scout → <c>model.role.scout</c> (already a scout),</description></item>
    ///   <item><description>jesuit missionary → <c>model.role.missionary</c> (already a missionary).</description></item>
    /// </list>
    /// Faithful-subset simplification: FreeCol reads each unit type's parsed <c>&lt;default-role&gt;</c> child; our
    /// <see cref="Specification.UnitType"/> does not yet parse that field (and the ruleset parser is out of scope for
    /// this change), so the mapping is the classic ruleset's four declarations, looked up via <see cref="DefaultRecruitRole"/>.
    /// Like FreeCol's <c>new ServerUnit(…, role)</c> (which calls <c>changeRole(role, role.getMaximumCount())</c>), the
    /// recruit's <see cref="Unit.RoleCount"/> is the role's <see cref="Specification.RoleType.MaximumCount"/> — so a hardy pioneer
    /// arrives with the pioneer role's full 5 tool-loads, the others with 1. The equip is purely deterministic — it
    /// sets the role on an already-spawned unit and draws <b>no RNG</b> — so the human's stream 0 and the seeded
    /// default game stay byte-identical (ADR-009); only the role/role-count of the (rare, recruit-probability 1) expert
    /// recruit changes, and that already round-trips through <see cref="Unit.RoleId"/> (no save bump).
    /// </summary>
    /// <param name="unit">The freshly docked recruit to equip in place (see <see cref="CreateEuropeRecruit"/>).</param>
    private void EquipEuropeanRecruit(Unit unit)
    {
        if (DefaultRecruitRole(unit.Type.Id) is not { } roleId)
        {
            return; // ordinary colonist: keeps the unarmed default role (the common case)
        }
        unit.RoleId = roleId;
        unit.RoleCount = Ruleset.Role(roleId).MaximumCount; // FreeCol ServerUnit ctor: changeRole(role, role.getMaximumCount())
    }

    /// <summary>
    /// The non-default role a recruit of <paramref name="unitTypeId"/> arrives equipped in (FreeCol
    /// <c>UnitType.getDefaultRole()</c>), or <c>null</c> when the type emigrates in the unarmed
    /// <see cref="Specification.RoleType.DefaultRoleId"/> (every type but the four classic experts). The classic ruleset's
    /// <c>&lt;default-role&gt;</c> declarations, kept here because <see cref="Specification.UnitType"/> does not parse
    /// that spec field yet — see <see cref="EquipEuropeanRecruit"/>. Returns null for any type the ruleset does not
    /// define the mapped role for (a minimal/variant ruleset), so the recruit safely falls back to the default role.
    /// </summary>
    private string? DefaultRecruitRole(string unitTypeId)
    {
        string? roleId = unitTypeId switch
        {
            "model.unit.veteranSoldier" => "model.role.soldier",
            "model.unit.hardyPioneer" => "model.role.pioneer",
            "model.unit.seasonedScout" => "model.role.scout",
            "model.unit.jesuitMissionary" => "model.role.missionary",
            _ => null,
        };
        return roleId is not null && Ruleset.Roles.Any(r => r.Id == roleId) ? roleId : null;
    }

    /// <summary>
    /// The classic spec's <c>model.option.mandatoryColonyYear</c> (1600): the year by which a colonial power must
    /// have a foothold in the New World. <b>Before</b> this year a player with no colonies and no surviving units —
    /// who also cannot scrape together a normal immigrant — is rescued by a survival auto-recruit (see
    /// <see cref="MaybeSurvivalRecruit"/>); <b>from</b> this year on there is no such rescue (FreeCol
    /// <c>ServerPlayer.checkForDeath</c> returns <c>IS_DEAD</c> instead of <c>IS_AUTORECRUIT</c>). This is the
    /// <em>classic-default alias</em> only; the live value is the parsed <see cref="Specification.Ruleset.GameOptions"/>
    /// bundle (<see cref="Specification.GameOptions.MandatoryColonyYear"/>), which <see cref="IsSurvivalStalled"/>
    /// reads. Aliased to <see cref="Specification.GameOptions.ClassicDefaults"/> so the bundle is the single source of
    /// truth (86d3drpgg).
    /// </summary>
    internal static readonly int MandatoryColonyYear = Specification.GameOptions.ClassicDefaults.MandatoryColonyYear;

    /// <summary>
    /// FreeCol's <b>survival</b> migration (<c>Europe.MigrationType.SURVIVAL</c>, fired from
    /// <c>ServerPlayer.checkForDeath</c> returning <c>IS_AUTORECRUIT</c> and resolved by
    /// <c>ServerPlayer.csEmigrate(0, SURVIVAL, …)</c>): a colonial player who has been starved of immigrants — no
    /// colonies producing crosses, an empty/stalled pool, and nothing left in the New World — gets one <b>free</b>
    /// colonist on its Europe dock so it is never permanently extinguished by a dry immigration pipeline. The forced
    /// emigrant <b>does not</b> consume immigration or raise the target (it is an emergency top-up, not a normal
    /// emigration), and is drawn on the player's own immigration RNG stream (<see cref="RandomFor"/>, ADR-009) so the
    /// default game — whose chapels always make crosses, so its pool is never perpetually empty — stays byte-identical
    /// and the rule only fires for a genuinely cross-starved player.
    /// <para>
    /// In-boundary derivation (no persisted stall counter, so no save bump): the "stall" is <b>structural</b>, read
    /// from current state rather than counted over turns — a player with no colonies, an empty pool, and no viable
    /// unit on the map simply cannot reach the recruit threshold, which is exactly the sustained-empty-pool condition
    /// FreeCol rescues. The turn gate is the <see cref="MandatoryColonyYear"/> upper bound.
    /// </para>
    /// </summary>
    /// <param name="player">The colonial player to check at the end of its immigration accrual.</param>
    /// <returns>The survival colonist docked in Europe, or null when the gate did not fire.</returns>
    private Unit? MaybeSurvivalRecruit(Player player)
    {
        if (!IsSurvivalStalled(player))
        {
            return null;
        }

        // FreeCol csEmigrate(slot 0, SURVIVAL): take the front dock recruit, refill the dock, land it in Europe.
        // No ReduceImmigration / no target raise — the SURVIVAL case does not fall through to NORMAL. The dock refill
        // draws on a dedicated SURVIVAL stream (see SurvivalRandom), NOT the player's main stream, so the human's
        // stream 0 stays byte-identical whether or not the AI ever strands the player (ADR-009 — the same isolation
        // IsHumanDefeated relies on: the human's turn sequence must not depend on AI actions).
        IGameRandom random = SurvivalRandom(player);
        string typeId = player.RecruitDock[0];
        player.RecruitDockList.RemoveAt(0);
        player.RecruitDockList.Add(DrawRecruitType(player, random));
        return CreateEuropeRecruit(player, typeId);
    }

    /// <summary>RNG stream reserved for survival auto-recruit draws (86d3drn0e) — a high id well clear of every
    /// per-player stream (<c>Player.RngStreamId</c> = playerId + 1) and the other reserved streams, so a forced
    /// survival emigrant never correlates with or shifts the human's economy stream 0 (ADR-009).</summary>
    private const ulong SurvivalStreamId = 104;

    /// <summary>
    /// The generator a survival auto-recruit draws its colonist type from: a <b>transient, isolated</b> stream so the
    /// rescue never perturbs the player's main stream (for the human, stream 0). It is forked deterministically from
    /// the player's current RNG state (<see cref="IGameRandom.SaveState"/>) — which for the human is byte-identical
    /// across AI-divergent games (stream 0 is the ADR-009 invariant), so the draw is reproducible per seed and on
    /// reload without any persisted survival-RNG field (no save bump) — salted by the <see cref="SurvivalStreamId"/>
    /// stream and the current <see cref="Game.Turn"/> so a player stranded again on a later turn need not draw the
    /// same type. Reading the source state via <c>SaveState</c> does <b>not</b> advance it, so the player's main
    /// stream is untouched.
    /// </summary>
    private IGameRandom SurvivalRandom(Player player) =>
        new Pcg32Random(RandomFor(player).SaveState().State + (ulong)Turn, SurvivalStreamId);

    /// <summary>
    /// Whether <paramref name="player"/> qualifies for a survival auto-recruit this turn (the in-boundary, derivable
    /// distillation of FreeCol's <c>checkForDeath ⇒ IS_AUTORECRUIT</c> gate): a <b>non-human colonial</b> power
    /// (a foreign power — see the human carve-out below; a rebel or independent nation gets no resupply from Europe),
    /// <b>before</b> the <see cref="MandatoryColonyYear"/>, with a live Europe dock (no dock ⇒ no Europe ⇒ no rescue),
    /// and <b>no colonist anywhere</b> — <b>no colonies</b> (so nothing producing crosses: its only immigration inflow
    /// is the flat player bonus, which alone can never sustain the nation) and <b>no person unit on the map, in
    /// Europe, or at sea</b>. This <em>structural</em> starvation is FreeCol's sustained-empty-pool condition: a
    /// player in this state cannot recover on its own. The "no colonist anywhere" clause mirrors FreeCol's
    /// <c>hasColonist ⇒ IS_ALIVE</c> short-circuit — once a survival colonist is waiting (in Europe, ready to ship
    /// home) the player is no longer stranded, so the rescue does <b>not</b> refire next turn (it tops the player up
    /// to exactly one free colonist, not one per turn). The check reads banked state, not a turn counter, so it needs
    /// no persisted field (no save bump).
    /// <para>
    /// <b>Human carve-out (ADR-009).</b> The survival recruit only ever fires <em>because</em> a player was stripped
    /// of all units — for the human that can only happen through AI actions, and the human's entire scoped state
    /// (stream 0, gold, dock) must stay independent of what the AI does within a turn (the invariant
    /// <see cref="IsHumanDefeated"/> documents and the stream-0 byte-stability tests guard). So the rescue is wired
    /// for <b>foreign powers only</b>, each drawing on its own isolated stream; the human keeps the existing
    /// conservative "alive on any surviving unit, no false defeat" stance (the pre-1600 free-recruit nuance for the
    /// human remains an explicitly-unmodelled corner, as <see cref="IsHumanDefeated"/> already notes). The default
    /// human therefore never triggers this, and the seeded default game is byte-identical.
    /// </para>
    /// </summary>
    private bool IsSurvivalStalled(Player player) =>
        !player.IsHuman
        && player.PlayerType == PlayerType.Colonial
        && CurrentYear < Ruleset.GameOptions.MandatoryColonyYear
        && player.RecruitDock.Count > 0
        && !ColoniesOf(player).Any()
        && !_units.Any(u => IsOwnedBy(u, player) && u.Type.IsPerson);
}
