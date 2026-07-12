using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// The Australian Federation variant's <b>novel content</b> (Phase-4d.2–4d.6): the new economy/civic buildings and
/// pioneer tile-improvements authored in <c>australia/specification.xml</c>, plus the two bespoke on-election
/// handlers whose designed mechanic is genuinely new — Edward Hargraves' "Payable Gold" gold rush (a Gold-deposit
/// reveal + immigration surge) and Arthur Phillip's "Survival Rations" (emergency Food + Tools to the first
/// settlement). Every id here is Australia-only: the classic ruleset declares none of it and replays byte-identically
/// (the soak gate proves that separately). A clean load of the Australia ruleset proves the new XML parses — a
/// malformed building/improvement id fails the ruleset load, so the parse assertions below double as validity checks.
/// Sources: docs/australian_federation_mode_md/17 (goods/buildings/improvements) and 08/10 (the Pioneers).
/// </summary>
public class AustralianContentTests
{
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Hargraves = "model.foundingFather.edwardHargraves";
    private const string Phillip = "model.foundingFather.arthurPhillip";
    private const string Macquarie = "model.foundingFather.lachlanMacquarie";
    private const string Parkes = "model.foundingFather.henryParkes";
    private const string Barton = "model.foundingFather.edmundBarton";
    private const string Quick = "model.foundingFather.johnQuick";
    private const string Griffith = "model.foundingFather.samuelGriffith";
    private const string Spence = "model.foundingFather.catherineHelenSpence";
    private const string Angas = "model.foundingFather.georgeFifeAngas"; // WS4.4
    private const string MaryLee = "model.foundingFather.maryLee"; // WS4.4
    private const string GoldResource = "model.resource.silver"; // silver = the reskin's Gold stand-in
    private const string FoodId = "model.goods.food";
    private const string ToolsId = "model.goods.tools";
    private const ulong FederationSeed = 0xFED0A05UL;

    // ───────────────────────── new buildings (4d.2 / 4d.3) parse + are available ─────────────────────────

    [Theory]
    // Economy (4d.2)
    [InlineData("model.building.goldfieldsOffice")]
    [InlineData("model.building.freezingWorks")]
    [InlineData("model.building.railDepot")]
    [InlineData("model.building.telegraphOffice")]
    // Civic (4d.3)
    [InlineData("model.building.federationLeagueHall")]
    [InlineData("model.building.conventionHall")]
    [InlineData("model.building.harbourBattery")]
    public void NewAustralianBuildings_ParseIntoTheAustraliaRuleset_AndAreAbsentFromClassic(string buildingId)
    {
        // Parses + is available in the Australia ruleset…
        BuildingType building = Australia.Building(buildingId);
        Assert.Equal(buildingId, building.Id);

        // …and is Australia-only — classic knows nothing of it (byte-identical guard).
        Assert.DoesNotContain(Classic.BuildingTypes, b => b.Id == buildingId);
    }

    [Fact]
    public void GoldfieldsOffice_IsGatedOnHargraves_GoldRushUnlockAbility()
    {
        // The office can only be built once Hargraves' election grants model.ability.buildGoldfieldsOffice.
        BuildingType office = Australia.Building("model.building.goldfieldsOffice");
        Assert.True(office.RequiredAbilitiesOrEmpty.GetValueOrDefault("model.ability.buildGoldfieldsOffice"));
        Assert.Contains(Australia.Father(Hargraves).Abilities,
            a => a.Id == "model.ability.buildGoldfieldsOffice" && a.Value);
    }

    [Fact]
    public void HarbourBattery_FortifiesTheColony_AndBombardsShips()
    {
        BuildingType battery = Australia.Building("model.building.harbourBattery");
        Assert.Equal(75, battery.DefenceBonus);                       // +75% colony defence
        Assert.True(battery.BombardsShips);                            // coastal guns fire on passing ships
        Assert.True(battery.RequiredAbilitiesOrEmpty.GetValueOrDefault("model.ability.hasPort")); // coastal only
    }

    [Fact]
    public void CivicBuildings_BoostCivicVoice_TelegraphAndLeagueHallOnTheBellsSeam()
    {
        // Telegraph Office +50% Civic Voice (bells); Federation League Hall +25% — the printing-press seam.
        Assert.Equal(50, Australia.Building("model.building.telegraphOffice").BellBonus);
        Assert.Equal(25, Australia.Building("model.building.federationLeagueHall").BellBonus);
        // Convention Hall is a workplace building that produces bells directly (the drafting chamber).
        Assert.Contains(Australia.Building("model.building.conventionHall").Productions,
            p => p.Outputs.Any(o => o.GoodsId == "model.goods.bells"));
    }

    // ───────────────────────── new tile improvements (4d.4) parse + are available ─────────────────────────

    [Theory]
    [InlineData("model.improvement.goldfield", "model.goods.silver")]  // +Gold
    [InlineData("model.improvement.stockRoute", "model.goods.cotton")] // +Wool (cotton stand-in)
    [InlineData("model.improvement.telegraphLine", "model.goods.bells")] // +Civic Voice
    [InlineData("model.improvement.rail", "model.goods.ore")]          // +Ore
    public void NewAustralianImprovements_ParseIntoTheAustraliaRuleset_WithTheirYieldModifier(
        string improvementId, string yieldGoodsId)
    {
        var improvement = Australia.Improvement(improvementId);
        Assert.Equal(improvementId, improvement.Id);
        Assert.Equal("model.role.pioneer", improvement.RequiredRoleId); // pioneer-built, expends a tool load
        Assert.Contains(improvement.Modifiers, m => m.GoodsId == yieldGoodsId);

        // Australia-only — classic has no such improvement.
        Assert.DoesNotContain(Classic.ImprovementTypes, i => i.Id == improvementId);
    }

    [Fact]
    public void AgreementGatedImprovements_AreDeferred_NotYetPresent()
    {
        // Waterhole Camp / Eel Trap depend on the First Nations 4b agreement system (not built) — deliberately absent.
        Assert.DoesNotContain(Australia.ImprovementTypes, i => i.Id == "model.improvement.waterholeCamp");
        Assert.DoesNotContain(Australia.ImprovementTypes, i => i.Id == "model.improvement.eelTrap");
    }

    // ───────────────────────── Hargraves' "Payable Gold" (4d.5) ─────────────────────────

    [Fact]
    public void Hargraves_CarriesTheGoldRushMarker_AndTheStandingGoldBoost()
    {
        FoundingFather hargraves = Australia.Father(Hargraves);
        Assert.Contains(hargraves.Abilities, a => a.Id == "model.ability.goldRush" && a.Value);
        // The standing +100% Gold (silver stand-in) production modifier rides on unchanged.
        Assert.Contains(hargraves.Modifiers, m => m.TargetId == "model.goods.silver" && m.Value == 100);
    }

    [Fact]
    public void ElectingHargraves_RevealsGoldDeposits_OnExploredElevationTilesNearColonies()
    {
        Game game = GoldRushGame(currentFather: Hargraves, liberty: 45); // ≥ first father cost (40)
        int goldBefore = CountGold(game);
        Assert.Equal(0, goldBefore); // no gold placed on the seed map

        game.EndTurn(); // liberty 45 ≥ 40 → Hargraves elected → the gold rush fires

        Assert.Contains(Hargraves, game.Congress);
        int goldAfter = CountGold(game);
        Assert.InRange(goldAfter, 2, 4); // doc 08: 2–4 Gold deposits revealed

        // Every deposit landed on an explored, dry ELEVATION tile the player owns fog over (hills/mountains).
        foreach (Position p in game.Map.Resources.Where(kv => kv.Value == GoldResource).Select(kv => kv.Key))
        {
            Assert.True(game.Map.TerrainAt(p).IsElevation, $"gold at {p} is not on elevation terrain");
        }
    }

    [Fact]
    public void ElectingHargraves_TriggersAnImmigrationSurge()
    {
        // A surge banks a full recruit's worth of immigration; on a fresh game with a stocked dock that tips at least
        // one emigrant across (immigration bar filled), which the election-turn loop then converts.
        Game game = GoldRushGame(currentFather: Hargraves, liberty: 45);
        int required = game.ImmigrationRequired;
        Assert.True(required > 0);

        game.EndTurn();
        Assert.Contains(Hargraves, game.Congress);
        // The surge (one full bar) guarantees measurable immigration progress this turn — either the bar was consumed
        // by an emigrant (immigration reset toward 0) or it is banked; in both cases the surge moved the needle.
        // Assert an emigrant was produced: the required threshold rose by the per-emigrant increment.
        Assert.True(game.ImmigrationRequired > required,
            "the gold-rush surge should have produced at least one emigrant, raising the next threshold");
    }

    [Fact]
    public void GoldRush_IsDeterministic_SameSeedSameDeposits()
    {
        var a = GoldRushGame(currentFather: Hargraves, liberty: 45);
        var b = GoldRushGame(currentFather: Hargraves, liberty: 45);
        a.EndTurn();
        b.EndTurn();
        Assert.Equal(
            a.Map.Resources.Where(kv => kv.Value == GoldResource).Select(kv => kv.Key).OrderBy(p => p.Y).ThenBy(p => p.X),
            b.Map.Resources.Where(kv => kv.Value == GoldResource).Select(kv => kv.Key).OrderBy(p => p.Y).ThenBy(p => p.X));
    }

    // ───────────────────────── Arthur Phillip's "Survival Rations" (4d.6) ─────────────────────────

    [Fact]
    public void Phillip_CarriesTheSurvivalRationsMarker_AlongsideHisAutoEquipPayload()
    {
        FoundingFather phillip = Australia.Father(Phillip);
        Assert.Contains(phillip.Abilities, a => a.Id == "model.ability.survivalRations" && a.Value);
        Assert.Contains(phillip.Abilities, a => a.Id == "model.ability.automaticEquipment" && a.Value);
    }

    [Fact]
    public void ElectingPhillip_SuppliesTheFirstSettlement_WithEmergencyFoodAndTools()
    {
        Game game = RationsGame(currentFather: Phillip, liberty: 45);
        int foodBefore = game.Colonies[0].StoreOf(FoodId);
        int toolsBefore = game.Colonies[0].StoreOf(ToolsId);

        game.EndTurn(); // Phillip elected → the first settlement is supplied

        Assert.Contains(Phillip, game.Congress);
        Assert.True(game.Colonies[0].StoreOf(FoodId) > foodBefore, "emergency food should have been delivered");
        Assert.True(game.Colonies[0].StoreOf(ToolsId) >= toolsBefore + 20, "emergency tools should have been delivered");
    }

    // ───────────────────────── Lachlan Macquarie's "Public Works" (4d.6 / M-C remap) ─────────────────────────

    [Fact]
    public void Macquarie_CarriesThePublicWorksMarker_NotAFreeCarpenter()
    {
        FoundingFather macquarie = Australia.Father(Macquarie);
        Assert.Contains(macquarie.Abilities, a => a.Id == "model.ability.publicWorks" && a.Value);
        Assert.Empty(macquarie.FreeUnits); // was a free master carpenter — the wrong (non-public-works) payload
    }

    [Fact]
    public void ElectingMacquarie_LaysAFreeRoad_ByAnEstablishedColony()
    {
        // "Public Works Governor" (doc 10): a pop-3+ colony gains one free road on an adjacent land tile on election.
        // RationsGame is a single pop-3 colony at (1,1) on an all-plains 3×3 map with no improvements — so the handler
        // lays exactly one road on a neighbouring plains tile.
        Game game = RationsGame(currentFather: Macquarie, liberty: 45);
        Assert.DoesNotContain(game.Map.AllImprovements(), i => i.Improvement.IsRoad);

        game.EndTurn(); // Macquarie elected → the public-works road is laid

        Assert.Contains(Macquarie, game.Congress);
        (Position pos, _) = Assert.Single(game.Map.AllImprovements(), i => i.Improvement.IsRoad); // one road, one colony
        Assert.NotEqual(game.Colonies[0].Position, pos); // adjacent to the colony, not its own centre tile
        Assert.False(game.Map.TerrainAt(pos).IsWater);   // and on land
    }

    [Fact]
    public void ElectingMacquarie_IsDeterministic_SameSetupSameRoad()
    {
        // The road choice is RNG-free (first eligible neighbour in (row, column) order) — two identical setups place the
        // road on the same tile, never perturbing the seeded stream (ADR-009).
        Game a = RationsGame(currentFather: Macquarie, liberty: 45);
        Game b = RationsGame(currentFather: Macquarie, liberty: 45);
        a.EndTurn();
        b.EndTurn();
        Assert.Equal(
            a.Map.AllImprovements().Where(i => i.Improvement.IsRoad).Select(i => i.Position),
            b.Map.AllImprovements().Where(i => i.Improvement.IsRoad).Select(i => i.Position));
    }

    // ───────────────────────── Democracy & Federation Pioneers (4d.7) ─────────────────────────

    [Fact]
    public void DemocracyPioneers_CarryTheirAustraliaOnlyFederationAbilities()
    {
        Assert.Contains(Australia.Father(Parkes).Abilities, a => a.Id == "model.ability.tenterfieldOration" && a.Value);
        Assert.Contains(Australia.Father(Barton).Abilities, a => a.Id == "model.ability.conventionDrive" && a.Value);
        Assert.Contains(Australia.Father(Quick).Abilities, a => a.Id == "model.ability.corowaPlan" && a.Value);
        Assert.Contains(Australia.Father(Griffith).Abilities, a => a.Id == "model.ability.draftConstitution" && a.Value);
        Assert.Contains(Australia.Father(Spence).Abilities, a => a.Id == "model.ability.fairRepresentation" && a.Value);

        // Classic knows none of these Federation-loop abilities (byte-identical guard).
        foreach (string ability in new[]
                 {
                     "model.ability.tenterfieldOration", "model.ability.conventionDrive",
                     "model.ability.corowaPlan", "model.ability.draftConstitution", "model.ability.fairRepresentation",
                 })
        {
            Assert.DoesNotContain(Classic.FoundingFathers.SelectMany(f => f.Abilities), a => a.Id == ability);
        }
    }

    [Fact]
    public void ElectingParkes_LiftsFederationSupport_InEveryColonyRegion()
    {
        Game game = FederationGame(out var colonies);
        int nswBefore = colonies[AustraliaColony.NewSouthWales].FederationSupport;
        int waBefore = colonies[AustraliaColony.WesternAustralia].FederationSupport;

        ElectFather(ref game, Parkes, ref colonies);

        Assert.Contains(Parkes, game.Congress);
        // The Tenterfield Oration boosts EVERY colony (both a big eastern colony and a small western one rose).
        Assert.True(colonies[AustraliaColony.NewSouthWales].FederationSupport > nswBefore);
        Assert.True(colonies[AustraliaColony.WesternAustralia].FederationSupport > waBefore);
    }

    [Fact]
    public void ElectingBarton_BanksConventionPoints_DrivingTowardTheConvention()
    {
        Game game = FederationGame(out var colonies);
        int pointsBefore = game.ConventionPoints;

        ElectFather(ref game, Barton, ref colonies);

        Assert.Contains(Barton, game.Congress);
        Assert.True(game.ConventionPoints > pointsBefore, "Barton's convention drive must bank Convention Points");
    }

    [Fact]
    public void ElectingGriffith_ContributesDraftProgress_TowardTheConstitution()
    {
        Game game = FederationGame(out var colonies);
        int progressBefore = game.ConstitutionProgressPercent;

        ElectFather(ref game, Griffith, ref colonies);

        Assert.Contains(Griffith, game.Congress);
        // WS3.3: Griffith's "+30% draft progress" is now a live constitution-meter bonus (not banked Convention Points).
        Assert.Equal(progressBefore + Game.GriffithProgressBonusPercent, game.ConstitutionProgressPercent);
    }

    [Fact]
    public void ElectingSpence_LiftsFederationSupport_InTheSmallColoniesOnly()
    {
        // Two identical games: one elects Spence, one elects nobody. The election turn also banks the usual Civic-Voice
        // accrual into EVERY colony, so we compare each colony's gain to the no-election control to isolate Spence's boost.
        Game control = FederationGame(out var controlColonies);
        var controlBefore = SupportByColony(controlColonies);
        control.EndTurn(); // a plain turn — the natural per-colony accrual, no father elected
        var controlAfter = SupportByColony(ReresolveAll(control));

        Game game = FederationGame(out var colonies);
        var before = SupportByColony(colonies);
        ElectFather(ref game, Spence, ref colonies);
        var after = SupportByColony(colonies);

        Assert.Contains(Spence, game.Congress);

        // The three SMALL colonies (SA/Tas/WA) gain MORE than the plain-accrual control (Spence's boost on top)…
        foreach (AustraliaColony c in new[] { AustraliaColony.SouthAustralia, AustraliaColony.Tasmania, AustraliaColony.WesternAustralia })
        {
            Assert.True(after[c] - before[c] > controlAfter[c] - controlBefore[c],
                $"small colony {c} should gain more than the no-Spence control");
        }
        // …while the two LARGE eastern colonies (NSW/Vic) gain no more than the plain-accrual control (untouched by Spence).
        foreach (AustraliaColony c in new[] { AustraliaColony.NewSouthWales, AustraliaColony.Victoria })
        {
            Assert.True(after[c] - before[c] <= controlAfter[c] - controlBefore[c],
                $"large colony {c} should gain no more than the no-Spence control");
        }
    }

    [Fact]
    public void ElectingQuick_LowersTheReferendumPassThreshold_LettingAMarginalVoteCarry()
    {
        // With every region at exactly its own target (WS3.2) the gate opens and the average support (~75%) sets the pass
        // bar; a referendum carries when that average (plus Quick's +10 Corowa relief) beats a seeded 0–99 roll. Some seeds'
        // rolls land in the marginal band where the vote FAILS without Quick but CARRIES with him — find one, proving his
        // relief lowers the effective pass threshold (read live from the persisted Congress; only Australia reaches
        // HoldReferendum, so classic stays byte-identical). supportPercent 0 leaves each region at its own target.
        for (ulong seed = 1; seed <= 400; seed++)
        {
            bool withoutQuick, withQuick;
            try
            {
                withoutQuick = RunReferendum(seed, withQuick: false, supportPercent: 0);
                withQuick = RunReferendum(seed, withQuick: true, supportPercent: 0);
            }
            catch (LandClaimRequiredException)
            {
                continue; // this seed places a First Nations land claim on a colony start tile — skip it
            }
            if (!withoutQuick && withQuick)
            {
                return; // Quick's relief flipped a marginal vote from fail → carry
            }
        }
        Assert.Fail("expected some seed in 1..400 to yield a marginal referendum Quick's +10 relief could flip");
    }

    [Fact]
    public void Quick_NeverTurnsACarryingReferendumIntoAFailure()
    {
        // Quick can only ever help: at any support level, a vote that carries without him must still carry with him.
        for (int support = Game.RegionSupportForReferendum; support <= 100; support += 10)
        {
            bool withoutQuick = RunReferendum(FederationSeed, withQuick: false, supportPercent: support);
            bool withQuick = RunReferendum(FederationSeed, withQuick: true, supportPercent: support);
            Assert.True(!withoutQuick || withQuick, $"Quick must never turn a carrying vote into a failure (support {support})");
        }
    }

    // ───────────────────────── WS4.4: second Federation-support clauses ─────────────────────────

    [Fact]
    public void WS44Pioneers_CarryTheirAustraliaOnlyFederationSupportAbilities()
    {
        // WS4.4 adds a second, region-scoped Federation-support clause to four Pioneers. Angas + Mary Lee get a new
        // Australia-only ability; Barton + Griffith extend their EXISTING conventionDrive/draftConstitution handlers
        // (no new ability — asserted by the on-election tests below).
        Assert.Contains(Australia.Father(Angas).Abilities, a => a.Id == "model.ability.colonialCredit" && a.Value);
        Assert.Contains(Australia.Father(MaryLee).Abilities, a => a.Id == "model.ability.womensSuffrage" && a.Value);

        // Classic knows neither (byte-identical guard).
        foreach (string ability in new[] { "model.ability.colonialCredit", "model.ability.womensSuffrage" })
        {
            Assert.DoesNotContain(Classic.FoundingFathers.SelectMany(f => f.Abilities), a => a.Id == ability);
        }
    }

    [Fact]
    public void ElectingAngas_LiftsFederationSupport_InSouthAustraliaOnly() =>
        AssertElectionBoostsOneRegionOnly(Angas, AustraliaColony.SouthAustralia);

    [Fact]
    public void ElectingMaryLee_LowersSouthAustraliasReferendumTarget() =>
        // WS3.2: Mary Lee's suffrage advocacy lowers SA's referendum target by 5 (80 → 75) — the honest form of her clause
        // (was a +support proxy). A separate ability test confirms she carries womensSuffrage.
        AssertElectionReducesRegionTarget(MaryLee, AustraliaColony.SouthAustralia, 5);

    [Fact]
    public void ElectingBarton_LowersNewSouthWalesReferendumTarget() =>
        // WS3.2: Barton's convention drive lowers NSW's referendum target by 3 (57 → 54). His Convention-Points banking is
        // unchanged (a separate test).
        AssertElectionReducesRegionTarget(Barton, AustraliaColony.NewSouthWales, 3);

    [Fact]
    public void ElectingGriffith_LowersTheHardestRegionsReferendumTarget()
    {
        // WS3.2: Griffith, as chief drafter, lowers the HARDEST settled region's referendum target by 5. Which region is
        // hardest is state-dependent (resolved by the handler at election time), so assert the robust invariant: exactly
        // one region's target dropped, and by exactly 5. His Convention-Points banking is unchanged (a separate test).
        Game game = FederationGame(out var colonies);
        var before = TargetsByRegion(game); // no reduction yet → every region at its base target
        ElectFather(ref game, Griffith, ref colonies);
        var after = TargetsByRegion(game);

        Assert.Contains(Griffith, game.Congress);
        var moved = AustraliaColonyStart.All.Where(c => after[c] != before[c]).ToList();
        Assert.Single(moved);
        Assert.Equal(before[moved[0]] - 5, after[moved[0]]);
    }

    /// <summary>
    /// Asserts that electing <paramref name="fatherId"/> lowers <paramref name="region"/>'s referendum target by exactly
    /// <paramref name="reduction"/> percentage points, leaving every other region's target untouched (WS3.2 — the honest
    /// "target −N" Pioneer clauses of Barton/Mary Lee).
    /// </summary>
    private static void AssertElectionReducesRegionTarget(string fatherId, AustraliaColony region, int reduction)
    {
        Game game = FederationGame(out var colonies);
        var before = TargetsByRegion(game);
        ElectFather(ref game, fatherId, ref colonies);
        var after = TargetsByRegion(game);

        Assert.Contains(fatherId, game.Congress);
        Assert.Equal(before[region] - reduction, after[region]);
        foreach (AustraliaColony c in AustraliaColonyStart.All.Where(c => c != region))
        {
            Assert.Equal(before[c], after[c]); // only the one region's target moves
        }
    }

    /// <summary>Each region's effective referendum target (WS3.2), keyed by region — the read the Pioneer target-reduction clauses move.</summary>
    private static Dictionary<AustraliaColony, int> TargetsByRegion(Game game) =>
        AustraliaColonyStart.All.ToDictionary(c => c, c => game.ReferendumTargetFor(AustraliaColonyStart.RegionKey(c)));

    /// <summary>
    /// Asserts that electing <paramref name="fatherId"/> lifts Federation Support in <paramref name="boostedRegion"/>'s
    /// colony <b>more</b> than a no-election control, while every other region gains no more than the control — isolating
    /// the region-scoped on-election +support boost from the turn's ordinary Civic-Voice accrual (Angas' clause, WS4.4).
    /// </summary>
    private static void AssertElectionBoostsOneRegionOnly(string fatherId, AustraliaColony boostedRegion)
    {
        Game control = FederationGame(out var controlColonies);
        var controlBefore = SupportByColony(controlColonies);
        control.EndTurn();
        var controlAfter = SupportByColony(ReresolveAll(control));

        Game game = FederationGame(out var colonies);
        var before = SupportByColony(colonies);
        ElectFather(ref game, fatherId, ref colonies);
        var after = SupportByColony(colonies);

        Assert.Contains(fatherId, game.Congress);
        Assert.True(after[boostedRegion] - before[boostedRegion] > controlAfter[boostedRegion] - controlBefore[boostedRegion],
            $"{boostedRegion} should gain more than the no-election control");
        foreach (AustraliaColony c in AustraliaColonyStart.All.Where(c => c != boostedRegion))
        {
            Assert.True(after[c] - before[c] <= controlAfter[c] - controlBefore[c],
                $"{c} should gain no more than the no-election control (only {boostedRegion} is boosted)");
        }
    }

    // ───────────────────────── fixtures ─────────────────────────

    private static int CountGold(Game game) => game.Map.Resources.Count(kv => kv.Value == GoldResource);

    /// <summary>A snapshot of each colony's banked Federation Support, keyed by region.</summary>
    private static Dictionary<AustraliaColony, int> SupportByColony(Dictionary<AustraliaColony, Colony> colonies) =>
        colonies.ToDictionary(kv => kv.Key, kv => kv.Value.FederationSupport);

    /// <summary>Re-resolves the six founded colonies of <paramref name="game"/> by their (stable) start tile.</summary>
    private static Dictionary<AustraliaColony, Colony> ReresolveAll(Game game) =>
        AustraliaColonyStart.All.ToDictionary(
            c => c,
            c => game.Colonies.Single(col => col.Position == AustraliaColonyStart.StartTile(c)));

    /// <summary>
    /// A live Australia-map game (regions resolve) with one colony founded in each of the six colony regions, and a
    /// little Federation Support banked in each so the boosts have a visible base to lift. Returned with the colonies
    /// keyed by region.
    /// </summary>
    private static Game FederationGame(out Dictionary<AustraliaColony, Colony> colonies)
    {
        Game game = Game.New(Australia, FederationSeed, mapSource: MapSource.Australia);
        colonies = new Dictionary<AustraliaColony, Colony>();
        foreach (AustraliaColony colony in AustraliaColonyStart.All)
        {
            Position tile = AustraliaColonyStart.StartTile(colony);
            Unit colonist = game.SpawnUnit(Australia.Unit(Colony.FreeColonistTypeId), tile);
            Colony founded = game.FoundColony(colonist);
            founded.AddFederationSupport(founded.RebelLibertyDivisor * founded.Population * 10 / 100); // ~10% base
            colonies[colony] = founded;
        }
        return game;
    }

    /// <summary>
    /// Stages <paramref name="fatherId"/> for election on the human's player row (via the save layer, the
    /// <see cref="FoundingFatherTests"/> pattern) and ends the turn so he is elected and his on-election effect fires.
    /// Re-resolves <paramref name="colonies"/> against the restored game (the boosts mutate the restored colonies).
    /// </summary>
    private static void ElectFather(ref Game game, string fatherId, ref Dictionary<AustraliaColony, Colony> colonies)
    {
        SaveGame primed = SaveGame.From(game, "australia") with
        {
            Players = SaveGame.From(game, "australia").Players!
                .Select(p => p.IsHuman ? p with { CurrentFather = fatherId, Liberty = 100_000 } : p).ToList(),
        };
        Game elected = SaveGame.FromJson(primed.ToJson()).Restore(Australia);
        elected.EndTurn();
        game = elected;

        // Re-key the colonies to the restored instances by their (stable) start tile, so the caller reads the mutated ones.
        var restored = new Dictionary<AustraliaColony, Colony>();
        foreach (AustraliaColony colony in AustraliaColonyStart.All)
        {
            Position tile = AustraliaColonyStart.StartTile(colony);
            restored[colony] = elected.Colonies.Single(c => c.Position == tile);
        }
        colonies = restored;
    }

    /// <summary>
    /// Runs a single referendum at the given seed and per-colony support level, with or without John Quick in the human's
    /// Congress, and returns whether it carried — the pair proves Quick's Corowa relief lowers the pass threshold.
    /// </summary>
    private static bool RunReferendum(ulong seed, bool withQuick, int supportPercent)
    {
        Game game = Game.New(Australia, seed, mapSource: MapSource.Australia);
        var colonies = new Dictionary<AustraliaColony, Colony>();
        foreach (AustraliaColony colony in AustraliaColonyStart.All)
        {
            Position tile = AustraliaColonyStart.StartTile(colony);
            Unit colonist = game.SpawnUnit(Australia.Unit(Colony.FreeColonistTypeId), tile);
            colonies[colony] = game.FoundColony(colonist);
        }

        if (withQuick)
        {
            SaveGame primed = SaveGame.From(game, "australia") with
            {
                Players = SaveGame.From(game, "australia").Players!
                    .Select(p => p.IsHuman ? p with { Congress = new List<string> { Quick } } : p).ToList(),
            };
            game = SaveGame.FromJson(primed.ToJson()).Restore(Australia);
        }

        // Seat every region at least at its own referendum target (WS3.2) so the per-region gate opens — the pass ROLL,
        // which Quick's Corowa relief shifts, is what these tests exercise (not the gate). A supportPercent above a region's
        // target lifts it further (used by the monotonicity test to sweep the average from ~75% up to 100%).
        Game g = game;
        Colony Reresolve(AustraliaColony c) =>
            g.Colonies.Single(col => col.Position == AustraliaColonyStart.StartTile(c));
        foreach (AustraliaColony colony in AustraliaColonyStart.All)
        {
            Colony col = Reresolve(colony);
            int level = Math.Max(supportPercent, g.ReferendumTargetFor(AustraliaColonyStart.RegionKey(colony)));
            col.FederationSupport = 0;
            col.AddFederationSupport(col.RebelLibertyDivisor * col.Population * level / 100);
        }
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
        return game.HoldReferendum();
    }

    /// <summary>
    /// A 5×5 Australia-ruleset game: a pop-3 colony at the centre on plains, ringed by explored <b>hills</b> tiles
    /// (the gold-rush candidates) plus the colony tile, with the chosen current father staged one liberty short so
    /// he is elected on the next EndTurn. Enough food is stocked so the colony doesn't starve.
    /// </summary>
    private static Game GoldRushGame(string currentFather, int liberty)
    {
        const int w = 5, h = 5;
        // All hills except the colony centre (plains, settleable) — so every non-centre tile is a gold candidate.
        var terrain = Enumerable.Repeat("model.tile.hills", w * h).ToList();
        int centre = 2 * w + 2; // (2,2)
        terrain[centre] = "model.tile.plains";

        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 42,
            RandomIncrement = 1,
            MapWidth = w,
            MapHeight = h,
            Terrain = terrain,
            Units = [],
            Explored = Enumerable.Range(0, w * h).ToList(), // whole map explored (fog lifted)
            Colonies = [new SavedColony(1, "Ballarat", 2, 2, 3,
                Stores: new Dictionary<string, int> { [FoodId] = 100 })],
            Congress = null,
            CurrentFather = currentFather,
            Liberty = liberty,
        };
        return save.Restore(Australia);
    }

    /// <summary>A 3×3 Australia-ruleset game with a single pop-3 colony, the given father staged for election.</summary>
    private static Game RationsGame(string currentFather, int liberty)
    {
        var terrain = Enumerable.Repeat("model.tile.plains", 9).ToList();
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 7,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = terrain,
            Units = [],
            Explored = Enumerable.Range(0, 9).ToList(),
            Colonies = [new SavedColony(1, "Sydney Cove", 1, 1, 3,
                Stores: new Dictionary<string, int> { [FoodId] = 50 })],
            Congress = null,
            CurrentFather = currentFather,
            Liberty = liberty,
        };
        return save.Restore(Australia);
    }
}
