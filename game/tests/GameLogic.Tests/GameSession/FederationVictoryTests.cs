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
        FoundAllSixRegions(game, out var colonies);
        // Satisfy the WS3.7 Phase-1/2 prerequisites first (they gate before the support/points checks), so this test
        // isolates the Phase-3 region-support + Convention-Points gates. The four capitals are NSW/Vic/Qld/SA.
        SatisfyMaturityAndMovement(game, colonies);

        // Three of the four capital regions above threshold is not enough (needs four).
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
    public void ConstitutionDrafts_Automatically_OnceClauseProgressReaches80Percent()
    {
        Game game = NewAustralia();
        game.SetFederationPhase(FederationPhase.ConventionCalled);
        game.SetConventionPoints(500);
        game.HumanPlayer.Gold = 5000;

        // Two of the three clauses = 67%, below the 80% completion gate → not yet drafted at end of turn (WS3.3).
        Assert.True(game.DraftClause("senateEquality"));
        Assert.True(game.DraftClause("capitalCompromise"));
        Assert.Equal(67, game.ConstitutionProgressPercent);
        game.EndTurn(); // resolution runs ResolveCommonwealthFederation
        Assert.Equal(FederationPhase.ConventionCalled, game.FederationPhase);

        // The third clause → 100% → the constitution drafts automatically at end of turn.
        Assert.True(game.DraftClause("intercolonialFreeTrade"));
        Assert.Equal(100, game.ConstitutionProgressPercent);
        game.EndTurn();
        Assert.Equal(FederationPhase.ConstitutionDrafted, game.FederationPhase);
    }

    [Fact]
    [Trait("Category", "E2E")]
    public void FullFederationJourney_MaturityToConventionToDraftToReferendumToCommonwealth()
    {
        // The COMPLETE Federation loop through the real player actions (no SetFederationPhase shortcuts) — proving the
        // depth streams integrate: WS3.7 maturity/movement gates → the convention → WS3.3 clause drafting → the WS3.2
        // per-region referendum → the Commonwealth win.
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);

        // Phase 1/2 — mature four capitals + trade routes + Parkes (movement); then bank a landslide of support + points.
        SatisfyMaturityAndMovement(game, colonies);
        MakeCapital(colonies[AustraliaColony.WesternAustralia]); // WS3.6: mature WA's goldfields so it joins (the NSW quota is waived below by drafting Capital Compromise)
        Assert.True(game.CheckColonialMaturity().Allowed);
        Assert.True(game.CheckFederationMovement().Allowed);
        foreach (Colony c in colonies.Values)
        {
            SetSupportPercent(c, 100);
        }
        game.SetConventionPoints(1000);
        game.HumanPlayer.Gold = 5000;

        // Phase 3 — call the convention.
        Assert.True(game.CheckCallConvention().Allowed);
        Assert.True(game.CallConvention());
        Assert.Equal(FederationPhase.ConventionCalled, game.FederationPhase);

        // Phase 4 — draft the three clauses past the 80% gate; the constitution completes at end of turn.
        Assert.True(game.DraftClause("senateEquality"));
        Assert.True(game.DraftClause("capitalCompromise"));
        Assert.True(game.DraftClause("intercolonialFreeTrade"));
        Assert.Equal(100, game.ConstitutionProgressPercent);
        game.EndTurn();
        Assert.Equal(FederationPhase.ConstitutionDrafted, game.FederationPhase);

        // Phase 5/6 — every region past its own referendum target (a landslide carries); the Commonwealth is proclaimed next turn.
        game.HumanPlayer.TaxRate = 0; // no Crown pressure at vote time
        MakeCapital(colonies[AustraliaColony.WesternAustralia]); // restore WA to a capital (the mid-journey turn starved the food-less test colony below capital size)
        foreach (Colony c in colonies.Values)
        {
            SetSupportPercent(c, 100); // re-bank (the clause effects + a turn's accrual shifted support)
            c.AntiFederation = 0;      // clear any mid-journey opposition so the landslide carries cleanly (net == raw == 100)
        }
        Assert.True(game.CheckPutToReferendum().Allowed);
        Assert.True(game.HoldReferendum(), "a 100%-support federation must carry its referendum");
        Assert.Null(game.Winner);
        game.EndTurn();
        Assert.Equal(FederationPhase.Commonwealth, game.FederationPhase);
        Assert.Same(game.HumanPlayer, game.Winner);
    }

    // ─────────────────────────────── referendum + Commonwealth win ───────────────────────────────

    [Fact]
    public void FullySupportedFederation_ReachesTheCommonwealthWin_ViaWinner()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        SatisfyReferendumSpecialRules(colonies); // WS3.6: WA joins + NSW clears its quota
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
        SatisfyReferendumSpecialRules(colonies); // WS3.6: WA joins (else its hold-out blocks the whole vote); NSW quota is moot (this test never holds a referendum)
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);

        // Every settled region at exactly its own historical target (NSW 57 … Tas/Vic 94; WA 55 once matured) → the referendum opens.
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
    public void FailedReferendum_SpikesAntiFederation_AndLeavesThePhaseForARetry()
    {
        // The gate requires every region at its own target (WA matured → 55), so the average NET support is pinned at ~72 →
        // a roll ≥ 72 rejects the vote (~28% of seeds). Search the fixed seed range for one whose FIRST referendum fails, so the WS3.5
        // spike is ALWAYS exercised — a single fixed seed could land on a carry and vacuously skip the assertions.
        for (ulong seed = 1; seed < 500; seed++)
        {
            Game game = Game.New(Australia, seed, mapSource: MapSource.Australia);
            System.Collections.Generic.Dictionary<AustraliaColony, Colony> colonies;
            try
            {
                FoundAllSixRegions(game, out colonies);
            }
            catch (LandClaimRequiredException)
            {
                continue; // this seed seats a colony on native-owned land — skip it and try the next
            }
            SatisfyReferendumSpecialRules(colonies); // WS3.6: reach the ROLL (not the WA hold-out gate or the NSW quota pre-empt)
            game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
            foreach ((AustraliaColony region, Colony colony) in colonies)
            {
                SetSupportPercent(colony, game.ReferendumTargetFor(AustraliaColonyStart.RegionKey(region)));
            }
            Colony nsw = colonies[AustraliaColony.NewSouthWales];
            int bankedBefore = nsw.FederationSupport;
            Assert.Equal(0, nsw.AntiFederation); // no opposition banked yet

            if (game.HoldReferendum())
            {
                continue; // this seed carried — try the next until we hit a genuine rejection
            }

            // A rejected referendum (WS3.5, Chris's "decaying spike only" decision): spikes DECAYING anti-Federation
            // sentiment on every colony and no longer permanently sheds banked support — the drag erodes over time rather
            // than scarring the movement. The phase stays at Referendum for a retry (design Phase 6).
            Assert.Equal(FederationPhase.Referendum, game.FederationPhase);
            Assert.Equal(1, game.ReferendumAttempts);
            Assert.Equal(bankedBefore, nsw.FederationSupport); // no permanent support shed
            Assert.True(nsw.AntiFederation > 0, "a failed referendum spikes anti-Federation sentiment (temporary momentum)");
            // The "Referendum setback" cause tag now surfaces (phase == Referendum, an attempt logged, not carried).
            Assert.Contains("Referendum setback",
                game.RegionAntiFederationCauses(AustraliaColonyStart.RegionKey(AustraliaColony.NewSouthWales)));

            game.EndTurn();
            Assert.NotEqual(FederationPhase.Commonwealth, game.FederationPhase); // never wins on a failed vote
            Assert.Null(game.Winner);
            return; // asserted a genuine failure — done
        }

        Assert.Fail("no seed in 1..499 produced a failed first referendum — the referendum gate/roll math must have changed");
    }

    // ─────────────────────────────── referendum determinism (ADR-009) ───────────────────────────────

    [Fact]
    public void Referendum_IsDeterministic_ForTheSameSeedAndState()
    {
        static bool RunToReferendum(ulong seed, int supportPercent)
        {
            Game game = Game.New(Australia, seed, mapSource: MapSource.Australia);
            FoundAllSixRegions(game, out var colonies);
            SatisfyReferendumSpecialRules(colonies); // WS3.6: reach the ROLL (not the WA hold-out gate or the NSW quota pre-empt)
            game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
            foreach (Colony c in colonies.Values)
            {
                SetSupportPercent(c, supportPercent);
            }
            return game.HoldReferendum();
        }

        // Two runs with identical seed, turn, and support roll the same referendum result. 95% clears every region's target
        // (max 94), so the gate opens and the pass ROLL — not the gate, the WA hold-out or the NSW quota — is tested here.
        Assert.Equal(RunToReferendum(Seed, 95), RunToReferendum(Seed, 95));
        Assert.Equal(RunToReferendum(Seed, 95), RunToReferendum(Seed, 95));
    }

    // ─────────────────────────────── anti-Federation sentiment (WS3.5) ───────────────────────────────

    private const string QuickId = "model.foundingFather.johnQuick";
    private const string BartonId = "model.foundingFather.edmundBarton";

    [Fact]
    public void AntiFederation_GrowsWhenSupportLow_MoreThanWhenSupportHigh()
    {
        Game game = NewAustralia();
        Colony low = FoundIn(game, AustraliaColony.NewSouthWales);
        Colony high = FoundIn(game, AustraliaColony.Victoria);
        SetSupportPercent(low, 15);   // apathetic → opposition grows (low-support cause active)
        SetSupportPercent(high, 90);  // engaged → no low-support cause
        for (int i = 0; i < 4; i++)
        {
            game.HumanPlayer.TaxRate = 0; // isolate the low-support cause (no Crown pressure on either)
            game.EndTurn();
        }
        Assert.True(low.AntiFederation > 0, "a chronically low-support colony accrues Anti-Federation Sentiment");
        Assert.True(low.AntiFederation > high.AntiFederation, "the neglected colony accrues more opposition than the well-supported one");
    }

    [Fact]
    public void AntiFederation_GrowsUnderHighTax_EvenOnAWellSupportedColony()
    {
        Game game = NewAustralia();
        Colony colony = FoundIn(game, AustraliaColony.Victoria);
        SetSupportPercent(colony, 90); // engaged → only Crown pressure can drive opposition here
        for (int i = 0; i < 5; i++)
        {
            game.HumanPlayer.TaxRate = 60; // oppressive imperial tax (≥ CrownPressureTaxThreshold)
            game.EndTurn();
        }
        Assert.True(colony.AntiFederation > 0, "high Crown tax accrues opposition nation-wide, even on a well-supported colony");
    }

    [Fact]
    public void AntiFederation_DecaysWithNoCause_FasterWithTheReconcilerPioneers()
    {
        static int RemainingAfterDecay(bool electReconcilers)
        {
            Game game = NewAustralia();
            Colony colony = FoundIn(game, AustraliaColony.Victoria);
            SetSupportPercent(colony, 90);   // no low-support cause
            colony.AntiFederation = 30;      // pre-existing opposition to erode
            if (electReconcilers)
            {
                game.HumanPlayer.CongressList.Add(QuickId);   // Corowa reconciler
                game.HumanPlayer.CongressList.Add(BartonId);  // convention-drive reconciler
            }
            for (int i = 0; i < 4; i++)
            {
                game.HumanPlayer.TaxRate = 0; // no Crown pressure
                game.EndTurn();
            }
            return colony.AntiFederation;
        }

        int withoutHelp = RemainingAfterDecay(false);
        int withHelp = RemainingAfterDecay(true);
        Assert.True(withoutHelp < 30, "opposition decays on its own when no cause is active");
        Assert.True(withHelp < withoutHelp, "Quick + Barton accelerate the decay (the 'lower the recovery cost' buy-down)");
    }

    [Fact]
    public void AntiFederation_NeverExceedsTheCap()
    {
        Game game = NewAustralia();
        Colony colony = FoundIn(game, AustraliaColony.NewSouthWales);
        SetSupportPercent(colony, 5);
        colony.AntiFederation = Game.AntiFederationCap - 1;
        for (int i = 0; i < 5; i++)
        {
            game.HumanPlayer.TaxRate = 60; // both causes active — maximum accrual pressure
            game.EndTurn();
        }
        Assert.Equal(Game.AntiFederationCap, colony.AntiFederation); // clamped, never exceeds the cap
    }

    [Fact]
    public void AntiFederation_IsDeterministic_ForTheSameSeedAndState()
    {
        static int Run()
        {
            Game game = NewAustralia();
            Colony colony = FoundIn(game, AustraliaColony.NewSouthWales);
            SetSupportPercent(colony, 20);
            for (int i = 0; i < 5; i++)
            {
                game.HumanPlayer.TaxRate = 45;
                game.EndTurn();
            }
            return colony.AntiFederation;
        }
        Assert.Equal(Run(), Run()); // integer, id-ordered, RNG-free (ADR-009)
    }

    [Fact]
    public void AntiFederation_BitesOnNetSupport_ButLeavesTheRawReadsAlone()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        string nsw = AustraliaColonyStart.RegionKey(AustraliaColony.NewSouthWales);
        SetSupportPercent(colonies[AustraliaColony.NewSouthWales], 70);
        colonies[AustraliaColony.NewSouthWales].AntiFederation = 40;

        Assert.Equal(70, game.RegionFederationSupport(nsw));        // raw — the convention gate + panel bars read this
        Assert.Equal(40, game.RegionAntiFederationSentiment(nsw));  // opposition overlay
        Assert.Equal(30, game.RegionNetFederationSupport(nsw));     // net = 70 − 40 — only the referendum reads this
    }

    [Fact]
    public void AntiFederation_CanBlockAReferendum_ThatRawSupportWouldPass()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        MakeCapital(colonies[AustraliaColony.WesternAustralia]); // future-proof for the WS3.6 WA goldfields gate (harmless in M1)
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
        foreach ((AustraliaColony region, Colony colony) in colonies)
        {
            SetSupportPercent(colony, game.ReferendumTargetFor(AustraliaColonyStart.RegionKey(region)));
        }
        Assert.True(game.CheckPutToReferendum().Allowed); // every region at its raw target, no opposition → gate open

        // Pile opposition on NSW: its NET support falls below target, so the vote is blocked — though the RAW read still meets it.
        string nsw = AustraliaColonyStart.RegionKey(AustraliaColony.NewSouthWales);
        colonies[AustraliaColony.NewSouthWales].AntiFederation = 30;
        Assert.True(game.RegionFederationSupport(nsw) >= game.ReferendumTargetFor(nsw)); // raw still meets the bar
        Assert.False(game.CheckPutToReferendum().Allowed);                               // …but net (referendum) does not
    }

    [Fact]
    public void AntiFederationCauses_ReflectTheActiveDrivers()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        string nsw = AustraliaColonyStart.RegionKey(AustraliaColony.NewSouthWales);
        SetSupportPercent(colonies[AustraliaColony.NewSouthWales], 20); // below the apathy threshold
        colonies[AustraliaColony.NewSouthWales].AntiFederation = 10;    // opposition present → causes surface
        game.HumanPlayer.TaxRate = 60;                                  // Crown pressure active

        var causes = game.RegionAntiFederationCauses(nsw);
        Assert.Contains("Apathy", causes);
        Assert.Contains("Crown pressure", causes);
        Assert.DoesNotContain("Referendum setback", causes); // no referendum held yet
    }

    [Fact]
    public void AntiFederation_DoesNotBiteTheConventionGate_OnlyTheReferendum()
    {
        // Set up a fully-openable convention (maturity + movement + 100% support + points).
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        SatisfyMaturityAndMovement(game, colonies);
        foreach (Colony c in colonies.Values)
        {
            SetSupportPercent(c, 100); // re-bank to 100% against the capital populations
        }
        game.SetConventionPoints(Game.ConventionPointsToCallConvention);
        Assert.True(game.CheckCallConvention().Allowed); // all prerequisites met, no opposition

        // Pile MAXIMUM opposition on every colony. It drags NET (referendum) support hard, but the convention gate reads
        // RAW support — so the convention must STILL open (design intent #1: opposition bites only at the referendum).
        foreach (Colony c in colonies.Values)
        {
            c.AntiFederation = Game.AntiFederationCap;
        }
        Assert.True(game.CheckCallConvention().Allowed,
            "opposition must not bite the convention gate — it bites only at the referendum stage");
    }

    [Fact]
    public void Classic_NeverAccruesAntiFederation()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = Found(game);
        for (int i = 0; i < 5; i++)
        {
            game.HumanPlayer.TaxRate = 60;
            game.EndTurn();
        }
        Assert.Equal(0, colony.AntiFederation); // gated off in classic (ADR-009)
    }

    [Fact]
    public void AntiFederation_RoundTripsThroughASave_AndOmitsAtZero()
    {
        Game game = NewAustralia();
        Colony colony = FoundIn(game, AustraliaColony.NewSouthWales);
        colony.AntiFederation = 25;
        string json = SaveGame.From(game, "australia").ToJson();
        Assert.Contains("AntiFederation", json);
        Game restored = SaveGame.FromJson(json).Restore(Australia);
        Assert.Equal(25, restored.Colonies.Single(c => c.Id == colony.Id).AntiFederation);

        // Omitted when 0: a fresh Australia game with no opposition writes no AntiFederation token (byte-identical to v74).
        Game clean = NewAustralia();
        FoundIn(clean, AustraliaColony.Victoria);
        Assert.DoesNotContain("AntiFederation", SaveGame.From(clean, "australia").ToJson());
    }

    // ─────────────────────────────── NSW quota + WA late-entry (WS3.6) ───────────────────────────────

    private const string WesternAustraliaKey = "model.region.westernAustralia";

    [Fact]
    public void WaLateEntry_HoldsOutUntilItsGoldfieldsMature_ThenJoinsAt55()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
        foreach (Colony c in colonies.Values)
        {
            SetSupportPercent(c, 95); // comfortably above every base target, including WA's 70
        }
        Assert.Equal(70, game.ReferendumTargetFor(WesternAustraliaKey));   // un-matured → base target
        Assert.False(game.CheckPutToReferendum().Allowed);                 // WA holds out even at 95% — it blocks the whole vote

        // WA's goldfields boom into an established colony → it joins, and its target drops 70 → 55.
        MakeCapital(colonies[AustraliaColony.WesternAustralia]);
        SetSupportPercent(colonies[AustraliaColony.WesternAustralia], 95); // re-seat against the new pop-10 ceiling
        Assert.Equal(55, game.ReferendumTargetFor(WesternAustraliaKey));   // 70 − 15 late-entry cut
        Assert.True(game.CheckPutToReferendum().Allowed);                  // WA joined + every region past its target → gate open
    }

    [Fact]
    public void NswMobilisationQuota_FailsTheReferendum_DeterministicallyAndCostsSomething()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        MakeCapital(colonies[AustraliaColony.WesternAustralia]); // WA joins so the gate opens and we reach the quota pre-empt
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
        foreach ((AustraliaColony region, Colony colony) in colonies)
        {
            SetSupportPercent(colony, game.ReferendumTargetFor(AustraliaColonyStart.RegionKey(region)));
        }
        Colony nsw = colonies[AustraliaColony.NewSouthWales]; // a lean pop-1 colony at 57% → mobilisation ~114 << 1200
        Assert.False(game.CheckNswReferendumQuota().Allowed); // advisory: NSW has the % but not the mobilisation quota
        Assert.True(game.CheckPutToReferendum().Allowed);     // the GATE is open (the quota bites in HoldReferendum, not the gate)
        int supportBefore = nsw.FederationSupport;

        // Holding the referendum fails DETERMINISTICALLY on the NSW quota — before any roll — so it fails on every seed.
        Assert.False(game.HoldReferendum());
        Assert.Equal(FederationPhase.Referendum, game.FederationPhase); // stays for a retry
        Assert.Equal(1, game.ReferendumAttempts);
        Assert.Equal(supportBefore, nsw.FederationSupport);            // a turnout technicality — no support shed
        Assert.True(nsw.AntiFederation > 0, "a quota failure spikes a small, NSW-only opposition (Chris's 'costs something')");
        // Other colonies are untouched (unlike a genuine roll rejection, which spikes every colony).
        Assert.Equal(0, colonies[AustraliaColony.Victoria].AntiFederation);
    }

    [Fact]
    public void NswQuota_IsWaivedByCapitalCompromise_AndClearedByMobilisation()
    {
        // Waiver path: drafting the Capital Compromise clause (the 1899 concessions) lets the referendum through despite a
        // short NSW mobilisation.
        Game waived = NewAustralia();
        FoundAllSixRegions(waived, out var colonies);
        MakeCapital(colonies[AustraliaColony.WesternAustralia]);
        waived.SetFederationPhase(FederationPhase.ConstitutionDrafted);
        foreach (Colony c in colonies.Values)
        {
            SetSupportPercent(c, 100);
        }
        Assert.False(waived.CheckNswReferendumQuota().Allowed);     // still short on numbers…
        waived.SetDraftedClause("capitalCompromise");              // …but the 1899 concessions waive it
        Assert.True(waived.CheckNswReferendumQuota().Allowed);
        Assert.True(waived.HoldReferendum(), "the waiver lets a 100%-support vote reach — and carry — the roll");

        // Mobilisation path: growing NSW's civic base past the quota also clears it (no waiver needed).
        Game grown = NewAustralia();
        FoundAllSixRegions(grown, out var big);
        MakeCapital(big[AustraliaColony.WesternAustralia]);
        big[AustraliaColony.NewSouthWales].Population = 20; // pop 20 at 100% banks 4000 > the 1200 quota
        grown.SetFederationPhase(FederationPhase.ConstitutionDrafted);
        foreach (Colony c in big.Values)
        {
            SetSupportPercent(c, 100);
        }
        Assert.True(grown.CheckNswReferendumQuota().Allowed);
        Assert.True(grown.HoldReferendum());
    }

    [Fact]
    public void AllSixColonies_AreMandatory_TheVoteIsBlockedUntilEveryRegionIsFounded()
    {
        Game game = NewAustralia();
        // Found only the five EASTERN regions (skip Western Australia) at a landslide — you must not be able to dodge WA's
        // steep target by never settling it (Chris's "all six mandatory" decision, WS3.6).
        var colonies = new System.Collections.Generic.Dictionary<AustraliaColony, Colony>();
        foreach (AustraliaColony region in new[] { AustraliaColony.NewSouthWales, AustraliaColony.Victoria, AustraliaColony.Queensland, AustraliaColony.SouthAustralia, AustraliaColony.Tasmania })
        {
            colonies[region] = FoundIn(game, region);
        }
        game.SetFederationPhase(FederationPhase.ConstitutionDrafted);
        colonies[AustraliaColony.NewSouthWales].Population = 20; // clear the NSW quota so it isn't what blocks the vote
        foreach (Colony c in colonies.Values)
        {
            SetSupportPercent(c, 100);
        }
        Assert.False(game.CheckPutToReferendum().Allowed); // only 5 of 6 regions founded → blocked

        // Found and mature the sixth region (WA); now all six are settled and it can proceed (WA joins at 55).
        Colony wa = FoundIn(game, AustraliaColony.WesternAustralia);
        MakeCapital(wa);
        SetSupportPercent(wa, 100);
        Assert.True(game.CheckPutToReferendum().Allowed);
    }

    [Fact]
    public void WaLateEntry_AndNswQuota_AreNoOpsForClassic()
    {
        Game classic = Game.New(Classic, Seed);
        Found(classic);
        Assert.Equal(Game.RegionSupportForReferendum, classic.ReferendumTargetFor(WesternAustraliaKey)); // uniform 50 — no WA late-entry cut
        Assert.True(classic.CheckNswReferendumQuota().Allowed);                                          // never blocks in classic
    }

    // ─────────────────────────────── persistence (v72, omit-when-default) ───────────────────────────────

    [Fact]
    public void SaveVersion_IsCurrent() => Assert.Equal(76, SaveGame.CurrentVersion);

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
        Assert.DoesNotContain("AntiFederation", json); // WS3.5 (v75) — classic accrues no opposition
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

    // ─────────────────────────────── phase-1/2 prerequisite gates (WS3.7) ───────────────────────────────

    private const string ParkesId = "model.foundingFather.henryParkes";

    [Fact]
    public void PhaseGates_ReturnNo_ForClassic()
    {
        Game classic = Game.New(Classic, Seed);
        Assert.False(classic.CheckColonialMaturity().Allowed);   // classic has no Federation campaign — a strict No, no state
        Assert.False(classic.CheckFederationMovement().Allowed);
    }

    [Fact]
    public void ColonialMaturity_NeedsFourCapitals_SettledRegions_AndTradeRoutes()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        Assert.False(game.CheckColonialMaturity().Allowed); // fresh outposts → the capital gate is closed

        foreach (AustraliaColony c in new[] { AustraliaColony.NewSouthWales, AustraliaColony.Victoria, AustraliaColony.Queensland, AustraliaColony.SouthAustralia })
        {
            MakeCapital(colonies[c]);
        }
        Assert.False(game.CheckColonialMaturity().Allowed); // four capitals + six settled regions, but no trade routes yet

        AddTradeRoutes(game, Game.Phase1TradeRoutes);
        Assert.True(game.CheckColonialMaturity().Allowed);

        colonies[AustraliaColony.SouthAustralia].Population = 2; // drop one capital below tier → only three capital regions
        Assert.False(game.CheckColonialMaturity().Allowed);
    }

    [Fact]
    public void FederationMovement_NeedsTheSignal_FourRegions_AndTwoNewspapers()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        Assert.False(game.CheckFederationMovement().Allowed); // 1788, no Parkes → the movement has not stirred

        game.HumanPlayer.CongressList.Add(ParkesId); // the movement signal
        Assert.False(game.CheckFederationMovement().Allowed); // six settled regions, but no newspapers yet

        colonies[AustraliaColony.NewSouthWales].AddBuilding("model.building.newspaper");
        colonies[AustraliaColony.Victoria].AddBuilding("model.building.newspaper");
        Assert.True(game.CheckFederationMovement().Allowed);
    }

    [Fact]
    public void CallConvention_IsGatedByMaturityAndMovement_ButOpensWhenAllAreMet()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        foreach (Colony c in colonies.Values)
        {
            SetSupportPercent(c, 100);
        }
        game.SetConventionPoints(Game.ConventionPointsToCallConvention);
        // Support + Convention Points alone no longer open the convention — the Phase-1/2 prerequisites gate it (WS3.7).
        Assert.False(game.CheckCallConvention().Allowed);

        // Meet Phase 1 (four Colonial Capitals + trade routes) and Phase 2 (Parkes + the capitals' newspapers).
        SatisfyMaturityAndMovement(game, colonies);
        foreach (Colony c in colonies.Values)
        {
            SetSupportPercent(c, 100); // re-bank to 100% against the new (larger) capital populations
        }

        Assert.True(game.CheckColonialMaturity().Allowed);
        Assert.True(game.CheckFederationMovement().Allowed);
        Assert.True(game.CheckCallConvention().Allowed, "maturity + movement + support + points all met → the convention opens");
        Assert.True(game.CallConvention());
    }

    [Fact]
    public void CurrentFederationStage_IsFederationMovement_WhenParkesElected_BeforeThe1889Era()
    {
        Game game = NewAustralia(); // 1788 — well before the Federation era
        Assert.Equal(FederationStage.ColonialMaturity, game.CurrentFederationStage);
        game.HumanPlayer.CongressList.Add(ParkesId);
        Assert.Equal(FederationStage.FederationMovement, game.CurrentFederationStage); // Parkes stirs the movement early
    }

    // ─────────────────────────────── constitution drafting (WS3.3) ───────────────────────────────

    private const string GriffithId = "model.foundingFather.samuelGriffith";

    [Fact]
    public void Classic_HasNoConstitutionClauses_AndDraftsNothing()
    {
        Game classic = Game.New(Classic, Seed);
        Assert.Empty(classic.ConstitutionClauses());
        Assert.Equal(0, classic.ConstitutionProgressPercent);
        Assert.False(classic.CheckDraftClause("senateEquality").Allowed);
        Assert.DoesNotContain("DraftedConstitutionClauses", SaveGame.From(classic, "classic").ToJson());
    }

    [Fact]
    public void CheckDraftClause_GatesOnPhase_Points_Gold_Duplicate_AndUnknown()
    {
        Game game = NewAustralia();
        Assert.False(game.CheckDraftClause("senateEquality").Allowed); // before the convention → cannot draft

        game.SetFederationPhase(FederationPhase.ConventionCalled);
        game.HumanPlayer.Gold = 0;
        game.SetConventionPoints(0);
        Assert.False(game.CheckDraftClause("senateEquality").Allowed);    // no Convention Points
        game.SetConventionPoints(1000);
        Assert.True(game.CheckDraftClause("senateEquality").Allowed);     // Senate Equality has no gold cost
        Assert.False(game.CheckDraftClause("capitalCompromise").Allowed); // …but Capital Compromise needs gold
        Assert.False(game.CheckDraftClause("bogusClause").Allowed);       // unknown id

        Assert.True(game.DraftClause("senateEquality"));
        Assert.False(game.CheckDraftClause("senateEquality").Allowed);    // already drafted
    }

    [Fact]
    public void DraftClause_DeductsCosts_AndAppliesTheOneOffEffect()
    {
        Game game = NewAustralia();
        FoundAllSixRegions(game, out var colonies);
        game.SetFederationPhase(FederationPhase.ConventionCalled);
        game.SetConventionPoints(1000);
        game.HumanPlayer.Gold = 5000;

        int saBefore = colonies[AustraliaColony.SouthAustralia].FederationSupport;
        Assert.True(game.DraftClause("senateEquality"));  // SA/Tas/WA gain support
        Assert.Equal(1000 - 90, game.ConventionPoints);   // Convention Points deducted
        Assert.True(colonies[AustraliaColony.SouthAustralia].FederationSupport > saBefore);

        Assert.Equal(57, game.ReferendumTargetFor("model.region.newSouthWales"));
        int goldBefore = game.HumanPlayer.Gold;
        Assert.True(game.DraftClause("capitalCompromise")); // NSW/Vic referendum target −3, costs gold
        Assert.Equal(goldBefore - 600, game.HumanPlayer.Gold);
        Assert.Equal(54, game.ReferendumTargetFor("model.region.newSouthWales")); // 57 − 3
    }

    [Fact]
    public void ConstitutionProgress_SumsClauseWeights_PlusGriffithsDerivedBonus()
    {
        Game game = NewAustralia();
        game.SetFederationPhase(FederationPhase.ConventionCalled);
        game.SetConventionPoints(1000);
        game.HumanPlayer.Gold = 5000;
        Assert.Equal(0, game.ConstitutionProgressPercent);

        game.DraftClause("senateEquality");    // 34
        game.DraftClause("capitalCompromise"); // +33 = 67 (< the 80% gate)
        Assert.Equal(67, game.ConstitutionProgressPercent);

        // Griffith in Congress contributes a live +30% (no on-election banking, WS3.3) → 97% ≥ 80.
        game.HumanPlayer.CongressList.Add(GriffithId);
        Assert.Equal(97, game.ConstitutionProgressPercent);
    }

    [Fact]
    public void DraftedClauses_RoundTripThroughASave_AndAreAbsentBeforeAnyDraft()
    {
        Game clean = NewAustralia();
        Assert.DoesNotContain("DraftedConstitutionClauses", SaveGame.From(clean, "australia").ToJson());

        Game game = NewAustralia();
        game.SetFederationPhase(FederationPhase.ConventionCalled);
        game.SetConventionPoints(1000);
        game.HumanPlayer.Gold = 5000;
        game.DraftClause("senateEquality");

        string json = SaveGame.From(game, "australia").ToJson();
        Assert.Contains("DraftedConstitutionClauses", json);
        Game restored = SaveGame.FromJson(json).Restore(Australia);
        Assert.Contains("senateEquality", restored.DraftedClauses);
        Assert.Equal(34, restored.ConstitutionProgressPercent); // the drafted clause's weight is restored
    }

    // ───────────────────────────────────────── helpers ─────────────────────────────────────────

    private static Game NewAustralia() => Game.New(Australia, Seed, mapSource: MapSource.Australia);

    /// <summary>Promotes a colony to a Colonial Capital (population 10 + port + newspaper + school) for the WS3.7 gate tests.</summary>
    private static void MakeCapital(Colony colony)
    {
        colony.Population = 10;
        colony.AddBuilding("model.building.docks");
        colony.AddBuilding("model.building.newspaper");
        colony.AddBuilding("model.building.schoolhouse");
    }

    /// <summary>Adds <paramref name="count"/> two-stop trade routes to the human's route list (the WS3.7 Phase-1 trade-route gate reads the count).</summary>
    private static void AddTradeRoutes(Game game, int count)
    {
        for (int i = 0; i < count; i++)
        {
            game.HumanPlayer.TradeRoutesList.Add(new TradeRoute(i + 1, $"Route {i + 1}",
                [new TradeRouteStop(i * 2 + 1, []), new TradeRouteStop(i * 2 + 2, [])]));
        }
    }

    /// <summary>
    /// Satisfies the WS3.6 special referendum rules so a test can reach the vote: matures Western Australia's goldfields (a
    /// Colonial Capital, so WA stops holding out and its target drops 70 → 55) and grows New South Wales' civic base
    /// (population 20, so at ≥57% it banks ≥ 2280 > <see cref="Game.NswMobilisationQuota"/> 1200). Call BEFORE seating
    /// support so <c>ReferendumTargetFor(WA)</c> already reads 55.
    /// </summary>
    private static void SatisfyReferendumSpecialRules(System.Collections.Generic.Dictionary<AustraliaColony, Colony> colonies)
    {
        MakeCapital(colonies[AustraliaColony.WesternAustralia]); // WA goldfields boom → it joins; target 70 → 55
        colonies[AustraliaColony.NewSouthWales].Population = 20;  // clears the NSW mobilisation quota at its 57% target
    }

    /// <summary>Satisfies the WS3.7 Phase-1 (four Colonial Capitals + trade routes) and Phase-2 (Parkes + the capitals' newspapers) gates for the four eastern regions.</summary>
    private static void SatisfyMaturityAndMovement(Game game, System.Collections.Generic.Dictionary<AustraliaColony, Colony> colonies)
    {
        foreach (AustraliaColony c in new[] { AustraliaColony.NewSouthWales, AustraliaColony.Victoria, AustraliaColony.Queensland, AustraliaColony.SouthAustralia })
        {
            MakeCapital(colonies[c]); // pop 10 + docks + newspaper + school → Colonial Capital (its newspaper also feeds the Phase-2 count)
        }
        AddTradeRoutes(game, Game.Phase1TradeRoutes);
        game.HumanPlayer.CongressList.Add(ParkesId);
    }

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
