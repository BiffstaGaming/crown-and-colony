using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The Australian Federation variant's <b>novel on-election effects</b> (Phase-4d.5/4d.6/4d.7) — the handful of
/// Pioneer perks whose designed mechanic is genuinely new and so cannot ride the reused modifier/ability/event
/// vocabulary (ADR-017) that carries every other Pioneer. Two feed the gold-rush/settlement content (Edward Hargraves,
/// Arthur Phillip); five drive the <b>Federation victory loop</b> (Phase-4d.7 — the Democracy &amp; Federation Pioneers:
/// Henry Parkes, Edmund Barton, John Quick, Samuel Griffith, Catherine Helen Spence). Each is a keyed handler gated on
/// an <b>Australian-only ability</b> the Pioneer declares in <c>australia/specification.xml</c>; the classic ruleset
/// declares none of these abilities, so <see cref="ApplyAustralianElectionEffects"/> is a no-op for classic and
/// the default game stays byte-identical (ADR-009 — every roll is drawn from the owner's injected
/// <see cref="IGameRandom"/>, and only when a gating ability is present). The Federation-state boosts additionally
/// short-circuit unless <see cref="Specification.Ruleset.VictoryFederation"/> is on (the Federation loop is Australia-only).
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

    /// <summary>Lachlan Macquarie's "Public Works" marker — his election lays a free road by each established colony (doc 10). Australia-only.</summary>
    private const string PublicWorksAbility = "model.ability.publicWorks";

    // ── Democracy & Federation Pioneers (Phase-4d.7) — each gated on an Australia-only ability so classic never runs it ──

    /// <summary>Henry Parkes' "Tenterfield Oration" marker — his election lifts Federation Support in every colony (doc 11). Australia-only.</summary>
    private const string TenterfieldOrationAbility = "model.ability.tenterfieldOration";

    /// <summary>Edmund Barton's "Nation for a Continent" marker — his election drives the convention (Convention Points) (doc 11). Australia-only.</summary>
    private const string ConventionDriveAbility = "model.ability.conventionDrive";

    /// <summary>John Quick's "Corowa Plan" marker — while in Congress it lowers the referendum pass threshold (doc 11). Australia-only.</summary>
    private const string QuickCorowaAbility = "model.ability.corowaPlan";

    /// <summary>Samuel Griffith's "Draft Constitution" marker — his election advances the constitutional drafting (Convention Points) (doc 11). Australia-only.</summary>
    private const string DraftConstitutionAbility = "model.ability.draftConstitution";

    /// <summary>Catherine Helen Spence's "Fair Representation" marker — her election lifts Federation Support in the small colonies (doc 11). Australia-only.</summary>
    private const string FairRepresentationAbility = "model.ability.fairRepresentation";

    /// <summary>George Fife Angas' "Colonial Credit" marker (WS4.4, doc 08 clause 4) — his election lifts Federation Support in South Australia. Australia-only.</summary>
    private const string ColonialCreditAbility = "model.ability.colonialCredit";

    /// <summary>Mary Lee's "Women's Suffrage" marker (WS4.4, doc 12 clause 2) — the SA suffrage campaigner's election lifts Federation Support in South Australia. Australia-only.</summary>
    private const string WomensSuffrageAbility = "model.ability.womensSuffrage";

    /// <summary>Federation Support Henry Parkes' Tenterfield Oration adds to every colony on election (doc 11: "+10 Federation Support" — scaled up to a meaningful jump against the <c>200 × population</c> support ceiling; a balance placeholder, see docs/systems/federation-victory.md §5).</summary>
    private const int TenterfieldOrationSupport = 60;

    /// <summary>Extra Federation Support Catherine Helen Spence's Fair Representation adds to each small-colony (SA/Tas/WA) on election (doc 11: "+25% Federation Support for small colonies"; a balance placeholder, see docs/systems/federation-victory.md §5).</summary>
    private const int FairRepresentationSupport = 45;

    /// <summary>Convention Points Edmund Barton's convention drive banks on election (doc 11: enter the final sequence earlier) — half the call-convention gate, a decisive push toward it without auto-winning.</summary>
    private const int ConventionDrivePoints = ConventionPointsToCallConvention / 2;

    /// <summary>Federation Support John Quick's "Corowa Plan" adds to the referendum pass threshold while he sits in Congress (doc 11: failed-referendum recoverability / a marginal vote likelier to carry).</summary>
    internal const int QuickReferendumRelief = 10;

    // ── The region-scoped / hardest-colony Federation Pioneer clauses. Angas is a +support boost; Barton, Griffith and
    //    Mary Lee lower a region's referendum TARGET directly (WS3.2 — the honest form of their doc "target −N" clauses,
    //    replacing WS4.4's +support proxy now that per-region targets exist). ──

    /// <summary>Federation Support George Fife Angas' "Colonial Credit" adds to South Australia on election (doc 08 clause 4: "South Australia gains +5 Federation Support").</summary>
    private const int ColonialCreditSupport = 5;

    /// <summary>Percentage points Mary Lee's SA suffrage advocacy shaves off South Australia's referendum target on election (doc 12 clause 2: "South Australia Federation target −5").</summary>
    private const int WomensSuffrageTargetReduction = 5;

    /// <summary>Percentage points Edmund Barton's convention drive shaves off New South Wales' referendum target on election (doc 11 clause 3: "NSW support target −3").</summary>
    private const int BartonNswTargetReduction = 3;

    /// <summary>Percentage points Samuel Griffith's drafting shaves off the hardest settled region's referendum target on election (doc 11 clause 2: "hardest colony target −5").</summary>
    private const int GriffithHardestTargetReduction = 5;

    /// <summary>The Gold deposit placed by the gold rush. Since WS6.1 this is a <b>real</b> Australian resource
    /// (<c>model.resource.gold</c>) — it was previously the classic <c>silver</c> resource standing in for Gold.</summary>
    private const string GoldResourceId = "model.resource.gold";

    /// <summary>The fewest / most Gold deposits Hargraves reveals on election (doc 08: "2–4 Gold deposits").</summary>
    private const int MinGoldDeposits = 2;
    private const int MaxGoldDeposits = 4;

    /// <summary>Immigration progress banked by Hargraves' one-off migration surge — one full recruit's worth toward the next emigrant.</summary>
    private const int GoldRushImmigrationSurge = 1;

    /// <summary>Emergency Food granted to Arthur Phillip's first settlement (doc 10: "emergency Food and Tools").</summary>
    private const int SurvivalRationsFood = 100;

    /// <summary>Emergency Tools granted to Arthur Phillip's first settlement.</summary>
    private const int SurvivalRationsTools = 20;

    /// <summary>The minimum colony population for Lachlan Macquarie's public-works road (doc 10: "colonies of population 3+").</summary>
    private const int PublicWorksMinPopulation = 3;

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
        if (father.Abilities.Any(a => a.Id == PublicWorksAbility && a.Value))
        {
            ApplyPublicWorks(player); // Lachlan Macquarie — "Public Works Governor"
        }
        if (father.Abilities.Any(a => a.Id == TenterfieldOrationAbility && a.Value))
        {
            ApplyTenterfieldOration(player); // Henry Parkes — "Tenterfield Oration"
        }
        if (father.Abilities.Any(a => a.Id == ConventionDriveAbility && a.Value))
        {
            ApplyConventionDrive(player); // Edmund Barton — "Nation for a Continent"
        }
        if (father.Abilities.Any(a => a.Id == DraftConstitutionAbility && a.Value))
        {
            ApplyDraftConstitution(player); // Samuel Griffith — "Draft Constitution"
        }
        if (father.Abilities.Any(a => a.Id == FairRepresentationAbility && a.Value))
        {
            ApplyFairRepresentation(player); // Catherine Helen Spence — "Fair Representation"
        }
        if (father.Abilities.Any(a => a.Id == ColonialCreditAbility && a.Value))
        {
            ApplyColonialCredit(player); // George Fife Angas — "Colonial Credit" (WS4.4, doc 08 C4)
        }
        if (father.Abilities.Any(a => a.Id == WomensSuffrageAbility && a.Value))
        {
            ApplyWomensSuffrage(player); // Mary Lee — SA suffrage advocacy (WS4.4, doc 12 C2)
        }
        // John Quick — "Corowa Plan": no on-election handler. His referendum relief is read live from the persisted
        // Congress in Game.Federation.HoldReferendum (ReferendumThresholdRelief), so electing him needs no state change.
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

    /// <summary>
    /// Lachlan Macquarie's "Public Works Governor" (doc 10): the public-works programme lays new roads. On election
    /// every one of <paramref name="player"/>'s colonies of population <see cref="PublicWorksMinPopulation"/>+ gains one
    /// free road on an adjacent buildable land tile that carries none yet (the design's clause 1, exact). Deterministic
    /// and RNG-free (ADR-009): colonies are visited in stable id order and the target is the first eligible neighbour in
    /// (row, column) order, so the choice never draws from the RNG stream. A no-op for a colony with no eligible tile,
    /// and — since only Macquarie declares the gating ability — for every classic game (byte-identical).
    /// </summary>
    /// <param name="player">The player who elected Macquarie.</param>
    private void ApplyPublicWorks(Player player)
    {
        TileImprovementType road = Ruleset.Improvement(TileImprovementType.RoadId);
        foreach (Colony colony in ColoniesOf(player).OrderBy(c => c.Id))
        {
            if (colony.Population < PublicWorksMinPopulation)
            {
                continue; // only established colonies (doc 10: population 3+)
            }

            // The first eligible neighbour in (row, column) order — a land tile the road applies to that has no road yet.
            Position? target = TilesInRange(colony.Position, 1)
                .Where(p => p != colony.Position
                            && !Map.TerrainAt(p).IsWater
                            && road.AppliesTo(Map.TerrainAt(p))
                            && !Map.ImprovementsAt(p).Any(i => i.IsRoad))
                .OrderBy(p => p.Y).ThenBy(p => p.X)
                .Select(p => (Position?)p)
                .FirstOrDefault();

            if (target is { } pos)
            {
                Map.AddImprovement(pos, road);
            }
        }
    }

    /// <summary>
    /// Henry Parkes' "Tenterfield Oration" (doc 11): the "Father of Federation"'s famous 1889 speech that launched the
    /// movement — on election, <b>every</b> one of the player's colonies gains a one-off <see cref="TenterfieldOrationSupport"/>
    /// Federation Support boost across all colony regions. Delegates to <see cref="AddFederationSupportToAllColonies"/>,
    /// which is a no-op (byte-identical) unless the ruleset enables the Federation victory. RNG-free.
    /// </summary>
    /// <param name="player">The player who elected Parkes.</param>
    private void ApplyTenterfieldOration(Player player) =>
        AddFederationSupportToAllColonies(player, TenterfieldOrationSupport);

    /// <summary>
    /// Edmund Barton's "Nation for a Continent" (doc 11): the first Prime Minister and lead campaigner drives the
    /// constitutional process forward — on election the human's Federation movement banks a decisive
    /// <see cref="ConventionDrivePoints"/> lump of national Convention Points, letting the player enter the convention
    /// sequence markedly earlier (without auto-winning — the region-support gate still applies). Delegates to
    /// <see cref="AddConventionPoints"/>, a no-op (byte-identical) unless the ruleset enables the Federation victory.
    /// RNG-free.
    /// </summary>
    /// <param name="player">The player who elected Barton (Convention Points are national — the human's movement).</param>
    private void ApplyConventionDrive(Player player)
    {
        if (player.IsHuman)
        {
            AddConventionPoints(ConventionDrivePoints);
            // WS3.2 (doc 11 clause 3): Barton, a New South Welshman and the first PM, lowers New South Wales' referendum
            // TARGET (was WS4.4's +support proxy). Federation-gated inside ReduceFederationTarget → classic byte-identical.
            ReduceFederationTarget(AustraliaColonyStart.RegionKey(AustraliaColony.NewSouthWales), BartonNswTargetReduction);
        }
    }

    /// <summary>
    /// Samuel Griffith's "Draft Constitution" (doc 11): the chief drafter of the Australian constitution. His "+30% Draft
    /// Constitution progress" clause is now a <b>live draft-progress bonus</b> (WS3.3) — read from his Congress seat by
    /// <see cref="ConstitutionProgressPercent"/> (<see cref="GriffithProgressBonus"/>), no on-election banking needed, so
    /// there is nothing to do here for the drafting side. On election he still lowers the hardest settled region's
    /// referendum target (WS3.2, doc 11 clause 2). Federation-gated → classic byte-identical. RNG-free.
    /// </summary>
    /// <param name="player">The player who elected Griffith.</param>
    private void ApplyDraftConstitution(Player player)
    {
        if (player.IsHuman)
        {
            // WS3.2 (doc 11 clause 2): as chief drafter Griffith wins over the most reluctant colony — lowers the hardest
            // settled region's referendum TARGET. His "+30% draft progress" is the live ConstitutionProgressPercent bonus.
            ReduceHardestRegionTarget(GriffithHardestTargetReduction);
        }
    }

    /// <summary>
    /// George Fife Angas' "Colonial Credit" (WS4.4, doc 08 clause 4): the financier and "Father of South Australia" — on
    /// election, once the Federation movement is visible, South Australia gains a one-off <see cref="ColonialCreditSupport"/>
    /// Federation Support boost. Delegates to <see cref="AddFederationSupportToRegion"/> (South Australia), a no-op
    /// (byte-identical) unless the ruleset enables the Federation victory. RNG-free. (His other designed clauses — clearing a
    /// debt, rush-buy discount, immigration growth — are deferred: no rush-buy or immigration-growth-rate system exists yet.)
    /// </summary>
    /// <param name="player">The player who elected Angas.</param>
    private void ApplyColonialCredit(Player player) =>
        AddFederationSupportToRegion(player, AustraliaColonyStart.RegionKey(AustraliaColony.SouthAustralia), ColonialCreditSupport);

    /// <summary>
    /// Mary Lee's South-Australian suffrage advocacy (WS3.2, doc 12 clause 2): the suffrage campaigner whose South Australia
    /// led the colonies (and the world) on votes for women — on election, South Australia's referendum <b>target</b> is
    /// lowered by <see cref="WomensSuffrageTargetReduction"/> percentage points (was WS4.4's +support proxy). Delegates to
    /// <see cref="ReduceFederationTarget"/>, a no-op (byte-identical) unless the ruleset enables the Federation victory, and
    /// human-only (the target is the human's referendum bar). RNG-free. (Her other clauses — Town-Hall/Newspaper Civic Voice
    /// (shipped), the suffrage reform event, and the reform victory grade — are the standing bells modifier + deferred
    /// reform/victory-grade systems.)
    /// </summary>
    /// <param name="player">The player who elected Mary Lee.</param>
    private void ApplyWomensSuffrage(Player player)
    {
        if (player.IsHuman)
        {
            ReduceFederationTarget(AustraliaColonyStart.RegionKey(AustraliaColony.SouthAustralia), WomensSuffrageTargetReduction);
        }
    }

    /// <summary>
    /// Catherine Helen Spence's "Fair Representation" (doc 11): the effective-voting campaigner who reassured the smaller
    /// colonies (whose fear of eastern domination was the real Federation obstacle) — on election, every one of the
    /// player's colonies in the <b>small</b> regions (South Australia, Tasmania, Western Australia) gains a one-off
    /// <see cref="FairRepresentationSupport"/> Federation Support boost. Delegates to
    /// <see cref="AddFederationSupportToSmallColonies"/>, a no-op (byte-identical) unless the ruleset enables the
    /// Federation victory. RNG-free.
    /// </summary>
    /// <param name="player">The player who elected Spence.</param>
    private void ApplyFairRepresentation(Player player) =>
        AddFederationSupportToSmallColonies(player, FairRepresentationSupport);
}
