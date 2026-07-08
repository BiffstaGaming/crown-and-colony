using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The Australian Federation variant's <b>novel on-election effects</b> (Phase-4d.5/4d.6) — the small handful of
/// Pioneer perks whose designed mechanic is genuinely new and so cannot ride the reused modifier/ability/event
/// vocabulary (ADR-017) that carries every other Pioneer. Each is a keyed handler gated on an
/// <b>Australian-only ability</b> the Pioneer declares in <c>australia/specification.xml</c>; the classic ruleset
/// declares none of these abilities, so <see cref="ApplyAustralianElectionEffects"/> is a no-op for classic and
/// the default game stays byte-identical (ADR-009 — every roll is drawn from the owner's injected
/// <see cref="IGameRandom"/>, and only when a gating ability is present).
///
/// <para>Wired from <see cref="ElectAndRefreshFounders"/>, once, at the moment a Pioneer joins the Congress —
/// alongside the reused free-unit / boycott-lift / reveal-colonies handlers.</para>
/// </summary>
public sealed partial class Game
{
    /// <summary>Edward Hargraves' "Payable Gold" marker — his election triggers the gold rush (doc 08). Australia-only.</summary>
    private const string GoldRushAbility = "model.ability.goldRush";

    /// <summary>Arthur Phillip's "Survival Rations" marker — his election supplies the first settlement (doc 10). Australia-only.</summary>
    private const string SurvivalRationsAbility = "model.ability.survivalRations";

    /// <summary>The Gold deposit placed by the gold rush (the reskin's <c>silver</c> = Gold stand-in resource).</summary>
    private const string GoldResourceId = "model.resource.silver";

    /// <summary>The fewest / most Gold deposits Hargraves reveals on election (doc 08: "2–4 Gold deposits").</summary>
    private const int MinGoldDeposits = 2;
    private const int MaxGoldDeposits = 4;

    /// <summary>Immigration progress banked by Hargraves' one-off migration surge — one full recruit's worth toward the next emigrant.</summary>
    private const int GoldRushImmigrationSurge = 1;

    /// <summary>Emergency Food granted to Arthur Phillip's first settlement (doc 10: "emergency Food and Tools").</summary>
    private const int SurvivalRationsFood = 100;

    /// <summary>Emergency Tools granted to Arthur Phillip's first settlement.</summary>
    private const int SurvivalRationsTools = 20;

    /// <summary>
    /// Applies any Australian-variant bespoke election effect the just-elected <paramref name="elected"/> Pioneer
    /// carries, for <paramref name="player"/>. A no-op unless the Pioneer declares one of the Australian-only gating
    /// abilities (so classic — which declares none — is byte-identical). Called once, on election, from
    /// <see cref="ElectAndRefreshFounders"/>.
    /// </summary>
    /// <param name="player">The player who just elected the Pioneer.</param>
    /// <param name="elected">The elected founding-father id.</param>
    private void ApplyAustralianElectionEffects(Player player, string elected)
    {
        FoundingFather father = Ruleset.Father(elected);
        if (father.Abilities.Any(a => a.Id == GoldRushAbility && a.Value))
        {
            ApplyGoldRush(player); // Edward Hargraves — "Payable Gold"
        }
        if (father.Abilities.Any(a => a.Id == SurvivalRationsAbility && a.Value))
        {
            ApplySurvivalRations(player); // Arthur Phillip — "Survival Rations"
        }
    }

    /// <summary>
    /// Edward Hargraves' "Payable Gold" (doc 08): reveals 2–4 Gold deposits on the player's own explored, dry
    /// <em>elevation</em> tiles (hills/mountains — where gold is mined) that carry no resource yet, and triggers one
    /// immigration surge (the gold-rush migration). Candidate tiles are gathered near the player's colonies (within
    /// the colony sight ring), sorted deterministically, and drawn from the owner's <see cref="IGameRandom"/> so the
    /// result is reproducible (ADR-009). Each chosen tile gets a Gold resource with a rolled finite quantity and is
    /// revealed into the player's fog. A no-op with no eligible tile (leaves the immigration surge intact regardless).
    /// </summary>
    /// <param name="player">The player who elected Hargraves.</param>
    private void ApplyGoldRush(Player player)
    {
        IGameRandom random = RandomFor(player);

        // Gather the player's explored, dry, resource-free elevation tiles near their colonies (deterministic order).
        var candidates = ColoniesOf(player)
            .OrderBy(c => c.Id)
            .SelectMany(c => TilesInRange(c.Position, ColonySightRadius))
            .Distinct()
            .Where(p => player.ExploredSet.Contains(p)
                        && !Map.TerrainAt(p).IsWater
                        && Map.TerrainAt(p).IsElevation
                        && Map.ResourceAt(p) is null)
            .OrderBy(p => p.Y).ThenBy(p => p.X)
            .ToList();

        // 2–4 deposits (doc 08), capped at the eligible-tile count. random.Next(min..=max) via an inclusive span.
        int wanted = MinGoldDeposits + random.Next(MaxGoldDeposits - MinGoldDeposits + 1);
        int toPlace = Math.Min(wanted, candidates.Count);
        for (int i = 0; i < toPlace; i++)
        {
            // Draw a random remaining candidate (swap-remove keeps it O(1) and the draw sequence deterministic).
            int pick = random.Next(candidates.Count);
            Position pos = candidates[pick];
            candidates[pick] = candidates[^1];
            candidates.RemoveAt(candidates.Count - 1);

            Map.SetResource(pos, GoldResourceId);
            Map.SetResourceQuantity(pos, NullIfLimitless(Ruleset.Resource(GoldResourceId).RollQuantity(random)));
            RevealAround(player, pos, 0); // the strike is seen (the tile itself; the ring is already colony-explored)
        }

        // One major immigration surge (doc 08: "Triggers one major immigration surge") — bank progress toward the
        // next emigrant. We run inside the election step (AccumulateLibertyAndElectFathers); the later
        // AccumulateImmigrationAndEmigrate step this same turn converts the now-full bar into an emigrant.
        player.Immigration += GoldRushImmigrationSurge * EffectiveImmigrationRequired(player);
    }

    /// <summary>
    /// Arthur Phillip's "Survival Rations" (doc 10): the founding governor supplies the colony — the player's first
    /// settlement (lowest colony id) receives emergency Food and Tools. A no-op for a colony-less player. The design's
    /// starvation-floor and slowed-First-Contact-tension clauses are deferred (they need a starvation-override and the
    /// First Nations tension system, neither of which exists yet).
    /// </summary>
    /// <param name="player">The player who elected Phillip.</param>
    private void ApplySurvivalRations(Player player)
    {
        Colony? first = ColoniesOf(player).OrderBy(c => c.Id).FirstOrDefault();
        if (first is null)
        {
            return; // no settlement to supply
        }
        first.AddGoods(Colony.FoodId, SurvivalRationsFood);
        first.AddGoods(ToolsGoodsId, SurvivalRationsTools);
    }
}
