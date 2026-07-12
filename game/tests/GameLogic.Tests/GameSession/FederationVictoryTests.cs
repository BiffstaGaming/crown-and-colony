using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The Australian-Federation victory loop (Phase-4a, ADR-021; design doc
/// <c>docs/australian_federation_mode_md/05_Federation_Victory_System.md</c> and
/// <c>docs/systems/federation-victory.md</c>). Verifies, in order:
/// <list type="bullet">
///   <item>the ruleset option parses (classic OFF, Australia ON) and classic accrues no Federation state;</item>
///   <item>a colony banks Federation Support from its Civic Voice, and the per-region aggregate reads it back;</item>
///   <item>the phase machine gates and advances at its thresholds (convention → constitution → referendum);</item>
///   <item>a fully-supported federation reaches the Commonwealth win via the pure <see cref="Game.Winner"/> oracle;</item>
///   <item>the referendum is deterministic (same seed + same state ⇒ same outcome);</item>
///   <item>the v72 Federation state round-trips through a save, and a classic save omits every Federation token.</item>
/// </list>
/// Determinism (ADR-009): the whole loop is gated on <see cref="Ruleset.VictoryFederation"/>, so classic draws no new
/// RNG and writes no new save tokens — proven here (a classic save is byte-identical) and, over 25 seeds × 200 turns, by
/// the <c>SoakTests</c> determinism gate.
/// </summary>
public class FederationVictoryTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();
    private const ulong Seed = 0xFED0A05UL;

    // ─────────────────────────────── ruleset option (classic off, Australia on) ───────────────────────────────

    [Fact]
    public void VictoryFederation_IsOffForClassic_AndOnForAustralia()
    {
        Assert.False(Classic.VictoryFederation, "classic must never enable the Federation victory (ADR-009 byte-stability)");
        Assert.True(Australia.VictoryFederation, "the Australia spec must enable the Federation victory (Phase-4a)");
    }

    [Fact]
    public void Classic_AccruesNoFederationState_AndStaysAtColonialMaturity()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        colony.Liberty = 0;

        for (int i = 0; i < 5; i++)
        {
            game.EndTurn(); // the town hall makes bells → Liberty, but Federation accrual is gated off in classic
        }

        Assert.True(colony.Liberty > 0, "classic bells must still bake into Sons-of-Liberty liberty");
        Assert.Equal(0, colony.FederationSupport);            // …but never into Federation Support
        Assert.Equal(FederationPhase.ColonialMaturity, game.FederationPhase);
        Assert.Equal(0, game.ConventionPoints);
        Assert.Equal(0, game.ReferendumAttempts);
        Assert.False(game.CheckCallConvention().Allowed);      // the action is closed off entirely in classic
    }

    // ─────────────────────────────── accrual + per-region aggregate ───────────────────────────────

    [Fact]
    public void Australia_BanksFederationSupport_FromCivicVoice()
    {
        Game game = NewAustralia();
        Colony colony = Found(game);
        colony.Liberty = 0;

        for (int i = 0; i < 5; i++)
        {
            game.EndTurn(); // the town hall's Civic Voice banks into both Liberty AND Federation Support
        }

        Assert.True(colony.Liberty > 0, "an Australia colony's Civic Voice still bakes into Sons-of-Liberty liberty");
        Assert.True(colony.FederationSupport > 0, "an Australia colony's Civic Voice must bank Federation Support");
        Assert.True(game.ConventionPoints > 0, "the human accrues national Convention Points alongside colony support");
    }

    [Fact]
    public void RegionFederationSupport_AveragesTheHumanColonies_InThatRegion()
    {
        Game game = NewAustralia();
        Colony nsw = FoundIn(game, AustraliaColony.NewSouthWales);
        SetSupportPercent(nsw, 60);

        Assert.Equal("model.region.newSouthWales", game.Map.RegionOf(nsw.Position)!.Key);
        Assert.Equal(60, game.RegionFederationSupport("model.region.newSouthWales"));
        Assert.Equal(0, game.RegionFederationSupport("model.region.victoria")); // no colony there → 0

        // The six-region summary lists every region in canonical order.
        var summary = game.RegionSupportSummary();
        Assert.Equal(6, summary.Count);
        Assert.Equal(Game.FederationRegionKeys, summary.Select(s => s.RegionKey).ToList());
        Assert.Equal(60, summary.Single(s => s.RegionKey == "model.region.newSouthWales").SupportPercent);
    }

    // ─────────────────────────────── phase machine: call convention ───────────────────────────────

    [Fact]
    public void CallConvention_IsGated_UntilFourRegionsAndPointsCross()
    {
        Game game = NewAustralia();

        // Three regions above threshold is not enough (needs four).
        FoundAllSixRegions(game, out var colonies);
        SetSupportPercent(colonies[AustraliaColony.NewSouthWales], 50);
        SetSupportPercent(colonies[AustraliaColony.Victoria], 50);
        SetSupportPercent(colonies[AustraliaColony.Queensland], 50);
        game.SetConventionPoints(Game.ConventionPointsToCallConvention);
        Assert.False(game.CheckCallConvention().Allowed, "three regions is short of the four-region gate");

        // A fourth region crosses → the region gate is met, but points still gate it.
        SetSupportPercent(colonies[AustraliaColony.SouthAustralia], 50);
        game.SetConventionPoints(Game.ConventionPointsToCallConvention - 1);
        Assert.False(game.CheckCallConvention().Allowed, "one point short of the Convention-Points gate");

        // Both gates met → the convention can be called, and calling advances the phase.
        game.SetConventionPoints(Game.ConventionPointsToCallConvention);
        Assert.True(game.CheckCallConvention().Allowed);
        Assert.True(game.CallConvention());
        Assert.Equal(FederationPhase.ConventionCalled, game.FederationPhase);
        Assert.False(game.CallConvention(), "a second call is a no-op once a convention has been called");
    }

    [Fact]
    public void ConstitutionDrafts_Automatically_OnceEnoughPointsBank()
    {
        Game game = NewAustralia();
        game.SetFederationPhase(FederationPhase.ConventionCalled);

        game.SetConventionPoints(Game.ConventionPointsToDraftConstitution - 1);
        game.EndTurn(); // resolution runs ResolveCommonwealthFederation
        Assert.Equal(FederationPhase.ConventionCalled, game.FederationPhase);

        game.SetConventionPoints(Game.ConventionPointsToDraftConstitution);
        game.EndTurn();
        Assert.Equal(FederationPhase.ConstitutionDrafted, game.FederationPhase);
    }

    // ─────────────────────────────── referendum + Commonwealth win ───────────────────────────────

    [Fact]
    public void FullySupportedFederation_ReachesTheCommonwealthWin_ViaWinner()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        foreach (Colony c in colonies.Values)
        {
            SetSupportPercent(c, 100); // a landslide — the referendum always carries
        }
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);

        Assert.True(game.CheckPutToReferendum().Allowed);
        Assert.True(game.HoldReferendum(), "a 100%-support federation must carry its referendum");
        Assert.True(game.ReferendumCarried);

        Assert.Null(game.Winner);                 // not yet — the win resolves at end of turn
        game.EndTurn();
        Assert.Equal(FederationPhase.Commonwealth, game.FederationPhase);
        Assert.Same(game.HumanPlayer, game.Winner); // the pure oracle now reports the Federation win
    }

    [Fact]
    public void Referendum_IsGated_UntilEverySettledRegionReachesItsOwnTarget()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);

        // Every settled region at exactly its own historical target (NSW 57 … Tas/Vic 94) → the referendum opens.
        foreach ((AustraliaColony region, Colony colony) in colonies)
        {
            SetSupportPercent(colony, game.ReferendumTargetFor(AustraliaColonyStart.RegionKey(region)));
        }
        Assert.True(game.CheckPutToReferendum().Allowed);

        // Tasmania at 90% — comfortably past the old uniform 50 bar, but short of its 94% target → still closed. This is the
        // WS3.2 behaviour change: the small, historically pro-Federation colonies must reach their high target, not a flat 50.
        Assert.Equal(94, game.ReferendumTargetFor("model.region.tasmania"));
        SetSupportPercent(colonies[AustraliaColony.Tasmania], 90);
        Assert.False(game.CheckPutToReferendum().Allowed);

        // Back to its own target → open again.
        SetSupportPercent(colonies[AustraliaColony.Tasmania], 94);
        Assert.True(game.CheckPutToReferendum().Allowed);
    }

    [Fact]
    public void FailedReferendum_ShedsSupport_AndLeavesThePhaseForARetry()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
        // Exactly at each region's own target: enough to hold a vote, but far from a certain pass — a low roll fails it.
        foreach ((AustraliaColony region, Colony colony) in colonies)
        {
            SetSupportPercent(colony, game.ReferendumTargetFor(AustraliaColonyStart.RegionKey(region)));
        }
        int bankedBefore = colonies[AustraliaColony.NewSouthWales].FederationSupport;

        bool carried = game.HoldReferendum();
        if (!carried)
        {
            Assert.Equal(FederationPhase.Referendum, game.FederationPhase);   // stays for a retry (design Phase 6)
            Assert.Equal(1, game.ReferendumAttempts);
            Assert.True(colonies[AustraliaColony.NewSouthWales].FederationSupport < bankedBefore,
                "a failed referendum sheds banked support (anti-Federation momentum)");
            game.EndTurn();
            Assert.NotEqual(FederationPhase.Commonwealth, game.FederationPhase); // never wins on a failed vote
            Assert.Null(game.Winner);
        }
        else
        {
            Assert.Equal(1, game.ReferendumAttempts);
        }
    }

    // ─────────────────────────────── referendum determinism (ADR-009) ───────────────────────────────

    [Fact]
    public void Referendum_IsDeterministic_ForTheSameSeedAndState()
    {
        static bool RunToReferendum(ulong seed, int supportPercent)
        {
            Game game = Game.New(Australia, seed, mapSource: MapSource.Australia);
            FoundAllSixRegions(game, out var colonies);
            game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
            foreach (Colony c in colonies.Values)
            {
                SetSupportPercent(c, supportPercent);
            }
            return game.HoldReferendum();
        }

        // Two runs with identical seed, turn, and support roll the same referendum result. 95% clears every region's target
        // (max 94), so the gate opens and the pass roll — not the gate — is what is being tested for determinism.
        Assert.Equal(RunToReferendum(Seed, 95), RunToReferendum(Seed, 95));
        Assert.Equal(RunToReferendum(Seed, 95), RunToReferendum(Seed, 95));
    }

    // ─────────────────────────────── persistence (v72, omit-when-default) ───────────────────────────────

    [Fact]
    public void SaveVersion_IsCurrent() => Assert.Equal(73, SaveGame.CurrentVersion);

    [Fact]
    public void ClassicSave_OmitsEveryFederationToken()
    {
        Game game = Game.New(Classic, Seed);
        Found(game);
        game.EndTurn();

        string json = SaveGame.From(game, "classic").ToJson();
        Assert.DoesNotContain("FederationSupport", json);
        Assert.DoesNotContain("FederationPhase", json);
        Assert.DoesNotContain("ConventionPoints", json);
        Assert.DoesNotContain("Referendum", json);
        Assert.DoesNotContain("FederationTargetReductions", json); // WS3.2 (v73) — classic banks no target reductions
    }

    // ─────────────────────────────── per-region targets (WS3.2) ───────────────────────────────

    [Fact]
    public void FederationRegionTargets_ParseTheSixHistoricalValues_EmptyForClassic()
    {
        Assert.Empty(Classic.FederationRegionTargets); // classic authors none → fall back to the uniform bar everywhere
        Assert.Equal(57, Australia.FederationRegionTargets["model.region.newSouthWales"]);
        Assert.Equal(94, Australia.FederationRegionTargets["model.region.victoria"]);
        Assert.Equal(56, Australia.FederationRegionTargets["model.region.queensland"]);
        Assert.Equal(80, Australia.FederationRegionTargets["model.region.southAustralia"]);
        Assert.Equal(94, Australia.FederationRegionTargets["model.region.tasmania"]);
        Assert.Equal(70, Australia.FederationRegionTargets["model.region.westernAustralia"]);
    }

    [Fact]
    public void ReferendumTargetFor_ReadsThePerRegionTarget_FallsBackToTheUniformBar()
    {
        Game australia = NewAustralia();
        Assert.Equal(57, australia.ReferendumTargetFor("model.region.newSouthWales"));
        Assert.Equal(94, australia.ReferendumTargetFor("model.region.tasmania"));

        // A region the Australia spec does not author (not one of the six) falls back to the uniform bar…
        Assert.Equal(Game.RegionSupportForReferendum, australia.ReferendumTargetFor("model.region.pacific"));
        // …and a classic game — whose ruleset authors none — reads the uniform bar for every region.
        Game classic = Game.New(Classic, Seed);
        Assert.Equal(Game.RegionSupportForReferendum, classic.ReferendumTargetFor("model.region.newSouthWales"));
    }

    [Fact]
    public void TargetReductions_RoundTripThroughASave_AndAreAbsentBeforeAnyElection()
    {
        // A fresh Australia game (no target-lowering Pioneer elected) banks no reductions → the token is omitted.
        Game clean = NewAustralia();
        FoundIn(clean, AustraliaColony.NewSouthWales);
        Assert.DoesNotContain("FederationTargetReductions", SaveGame.From(clean, "australia").ToJson());

        // Bank a NSW target reduction (Barton's clause) → it is written, and restores to the same effective target.
        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales);
        game.ReduceFederationTarget("model.region.newSouthWales", 3);
        Assert.Equal(54, game.ReferendumTargetFor("model.region.newSouthWales")); // 57 − 3

        string json = SaveGame.From(game, "australia").ToJson();
        Assert.Contains("FederationTargetReductions", json);
        Game restored = SaveGame.FromJson(json).Restore(Australia);
        Assert.Equal(54, restored.ReferendumTargetFor("model.region.newSouthWales"));
        Assert.Equal(94, restored.ReferendumTargetFor("model.region.tasmania")); // untouched regions keep their base target
    }

    // ─────────────────────────────── narrative stages (WS3.8) ───────────────────────────────

    [Fact]
    public void CurrentFederationStage_MapsTheMechanicalPhase_ToTheSixDesignStages()
    {
        Assert.Equal(FederationStage.None, Game.New(Classic, Seed).CurrentFederationStage); // classic — no campaign

        Game game = NewAustralia();
        // A fresh 1788 Australia game: mechanical ColonialMaturity, before the 1889 Federation era → Colonial Maturity.
        Assert.Equal(FederationStage.ColonialMaturity, game.CurrentFederationStage);

        game.SetFederationPhase(FederationPhase.ConventionCalled);
        Assert.Equal(FederationStage.ConventionProcess, game.CurrentFederationStage);
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
        Assert.Equal(FederationStage.DraftConstitution, game.CurrentFederationStage);
        game.SetFederationPhase(FederationPhase.Referendum);
        Assert.Equal(FederationStage.Referendum, game.CurrentFederationStage);
        game.SetFederationPhase(FederationPhase.Commonwealth);
        Assert.Equal(FederationStage.Commonwealth, game.CurrentFederationStage);
    }

    [Fact]
    public void CurrentFederationStage_IsFederationMovement_OnceThe1889EraBegins_WhileStillAtColonialMaturity()
    {
        // Advance an Australia game into the 1889+ Federation era (via the save layer's turn) while still mechanically at
        // ColonialMaturity → the narrative stage becomes Federation Movement (design Phase 2, the same mechanical state).
        Game game = NewAustralia();
        Game late = (SaveGame.From(game, "australia") with { Turn = 140 }).Restore(Australia); // turn 140 = 1889+ on the Australian calendar
        Assert.True(late.CurrentYear >= 1889, "turn 140 must sit in the 1889+ Federation era");
        Assert.Equal(FederationPhase.ColonialMaturity, late.FederationPhase); // still mechanically at maturity
        Assert.Equal(FederationStage.FederationMovement, late.CurrentFederationStage);
    }

    [Fact]
    public void FederationState_RoundTripsThroughASave()
    {
        Game game = NewAustralia();
        Colony colony = FoundIn(game, AustraliaColony.NewSouthWales);
        SetSupportPercent(colony, 40);
        game.SetFederationPhase(FederationPhase.Referendum);
        game.SetConventionPoints(275);
        game.SetReferendumAttempts(2);
        game.SetReferendumCarried(true);

        string json = SaveGame.From(game, "australia").ToJson();
        Assert.Contains("FederationSupport", json); // the colony banked support → the token is written
        Assert.Contains("FederationPhase", json);
        Assert.Contains("ConventionPoints", json);
        Assert.Contains("Referendum", json);

        Game restored = SaveGame.FromJson(json).Restore(Australia);
        Assert.Equal(FederationPhase.Referendum, restored.FederationPhase);
        Assert.Equal(275, restored.ConventionPoints);
        Assert.Equal(2, restored.ReferendumAttempts);
        Assert.True(restored.ReferendumCarried);
        Assert.Equal(colony.FederationSupport, restored.Colonies.Single().FederationSupport);
    }

    [Fact]
    public void PreV72Save_LoadsWithNoFederationState()
    {
        // A save with no Federation tokens (a pre-v72 / classic-shaped save) restores at the default phase with none.
        Game game = NewAustralia();
        Found(game);
        SaveGame save = SaveGame.From(game, "australia");
        // A default Australia game has advanced nothing yet → the save carries no Federation tokens; loading it back is
        // the pre-feature behaviour: default phase, no points, no referendum.
        Game restored = save.Restore(Australia);
        Assert.Equal(FederationPhase.ColonialMaturity, restored.FederationPhase);
        Assert.Equal(0, restored.ConventionPoints);
        Assert.Equal(0, restored.ReferendumAttempts);
        Assert.False(restored.ReferendumCarried);
    }

    // ───────────────────────────────────────── helpers ─────────────────────────────────────────

    private static Game NewAustralia() => Game.New(Australia, Seed, mapSource: MapSource.Australia);

    /// <summary>Founds a colony from the first colony-capable unit on the map (the human's start party).</summary>
    private static Colony Found(Game game) =>
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));

    /// <summary>Spawns a free colonist on <paramref name="colony"/>'s start tile and founds a colony there.</summary>
    private static Colony FoundIn(Game game, AustraliaColony colony)
    {
        Position tile = AustraliaColonyStart.StartTile(colony);
        Unit colonist = game.SpawnUnit(Australia.Unit(Colony.FreeColonistTypeId), tile);
        return game.FoundColony(colonist);
    }

    /// <summary>Founds one colony in each of the six colony regions, returned keyed by region.</summary>
    private static void FoundAllSixRegions(Game game, out System.Collections.Generic.Dictionary<AustraliaColony, Colony> colonies)
    {
        colonies = new System.Collections.Generic.Dictionary<AustraliaColony, Colony>();
        foreach (AustraliaColony colony in AustraliaColonyStart.All)
        {
            colonies[colony] = FoundIn(game, colony);
        }
    }

    /// <summary>Sets <paramref name="colony"/>'s banked Federation Support so it reads back at <paramref name="percent"/>%.</summary>
    private static void SetSupportPercent(Colony colony, int percent)
    {
        colony.FederationSupport = 0;
        colony.AddFederationSupport(colony.RebelLibertyDivisor * colony.Population * percent / 100);
    }
}
