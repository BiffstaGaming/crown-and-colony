using System.Linq;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the Australian-<see cref="FederationPanel"/> (Phase-4a, ADR-021): opened on
/// an Australia game it renders the phase line, the Convention-Points line, and the six per-region support rows; and the
/// Call-Convention action is disabled with its reason shown while the thresholds are unmet (a fresh game). The panel is
/// driven directly with a live Australia <see cref="Game"/> through its <b>public</b> API only — no <c>GameController</c>
/// seam and no internal test hooks (the deeper threshold/win transitions are covered at L1/L2 in
/// <c>FederationVictoryTests</c>). Presentation-only (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FederationPanelTests
{
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();
    private const ulong Seed = 0xFED0A05UL;

    private static FederationPanel AddPanel(Control host)
    {
        var panel = new FederationPanel();
        host.AddChild(panel); // runs _Ready, building the shell
        return panel;
    }

    private static Game NewAustralia() => Game.New(Australia, Seed, mapSource: MapSource.Australia);

    private static Colony FoundIn(Game game, AustraliaColony colony)
    {
        Position tile = AustraliaColonyStart.StartTile(colony);
        Unit colonist = game.SpawnUnit(Australia.Unit(Colony.FreeColonistTypeId), tile);
        return game.FoundColony(colonist);
    }

    [TestCase]
    public async Task Open_RendersThePhaseLine_ConventionPoints_AndSixRegionRows()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales);
        FederationPanel panel = AddPanel(host);

        panel.Open(game, () => { });
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsTrue();
        var body = panel.GetNode<VBoxContainer>($"VBox/Scroll/{FederationPanel.BodyName}");
        AssertThat(body.GetNodeOrNull("PhaseLine")).IsNotNull();
        AssertThat(body.GetNodeOrNull("ConventionPoints")).IsNotNull();
        // One row per federation region, in canonical order.
        foreach (string key in Game.FederationRegionKeys)
        {
            string shortKey = key[(key.LastIndexOf('.') + 1)..];
            AssertThat(body.GetNodeOrNull($"Region_{shortKey}")).IsNotNull();
        }

        panel.QueueFree();
    }

    [TestCase]
    public async Task Open_RendersTheDesignedTreatment_PhaseTracker_AndStyledSupportGauges()
    {
        // WS2.7 UI polish: the panel is lifted from plain labels + default progress bars to a designed treatment. Guard the
        // structural markers of that redesign — the five-step phase tracker, and per-region support GAUGES (a ProgressBar
        // carrying background + fill stylebox overrides), so a regression back to the plain look is caught.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales);
        FederationPanel panel = AddPanel(host);

        panel.Open(game, () => { });
        await runner.SimulateFrames(1);

        var body = panel.GetNode<VBoxContainer>($"VBox/Scroll/{FederationPanel.BodyName}");
        // The six-step phase tracker (WS3.8 — doc 05's named stages), with a chip per stage.
        var tracker = body.GetNodeOrNull<HBoxContainer>("PhaseTracker");
        AssertThat(tracker).IsNotNull();
        AssertThat(tracker!.GetChildCount()).IsEqual(6);
        // The era indicator names the current design phase.
        AssertThat(body.GetNode<Label>("StageName").Text).Contains("Colonial Maturity"); // a fresh 1788 game → Phase 1
        // Each region's bar is a styled gauge (recessed trough + coloured fill), not a stock ProgressBar.
        var bar = body.GetNode<ProgressBar>("Region_newSouthWales/Bar");
        AssertThat(bar.HasThemeStyleboxOverride("fill")).IsTrue();
        AssertThat(bar.HasThemeStyleboxOverride("background")).IsTrue();
        // The referendum-bar marker rides on the gauge.
        AssertThat(bar.GetNodeOrNull("ReferendumMark")).IsNotNull();
        // The title carries the theme's display-title variation (designed hierarchy).
        AssertThat(panel.GetNode<Label>("VBox/FederationTitle").ThemeTypeVariation.ToString()).IsEqual("ColonyTitle");

        panel.QueueFree();
    }

    [TestCase]
    public async Task Open_NamesTheCurrentDesignPhase_AsAnEraIndicator_NotHardcoded()
    {
        // WS3.8: the era indicator names the design phase for the current mechanical state. Advance to ConstitutionDrafted
        // via the save layer (public) so the label must read Game.CurrentFederationStage, not a hard-coded Phase 1.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales);
        Game drafted = (SaveGame.From(game, "australia") with { FederationPhase = (int)FederationPhase.ConstitutionDrafted })
            .Restore(Australia);
        FederationPanel panel = AddPanel(host);

        panel.Open(drafted, () => { });
        await runner.SimulateFrames(1);

        var stageName = panel.GetNode<Label>($"VBox/Scroll/{FederationPanel.BodyName}/StageName");
        AssertThat(stageName.Text).Contains("Phase 4");
        AssertThat(stageName.Text).Contains("Draft Constitution");

        panel.QueueFree();
    }

    [TestCase]
    public async Task Open_AnchorsEachRegionsReferendumMark_AtItsOwnTarget()
    {
        // WS3.2 regression guard: the gold referendum marker on each gauge must sit at that REGION's own target — New
        // South Wales at 0.57, Tasmania at 0.94 — read from Game.ReferendumTargetFor, not a uniform 0.50. A regression to
        // a hard-coded uniform anchor (or feeding SupportColor the old flat bar) would visually undo WS3.2 yet leave the
        // other panel tests green, so pin the two extremes here.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales); // target 57%
        FoundIn(game, AustraliaColony.Tasmania);      // target 94% — the steep small-colony bar
        FederationPanel panel = AddPanel(host);

        panel.Open(game, () => { });
        await runner.SimulateFrames(1);

        var body = panel.GetNode<VBoxContainer>($"VBox/Scroll/{FederationPanel.BodyName}");
        var nswMark = body.GetNode<ColorRect>("Region_newSouthWales/Bar/ReferendumMark");
        var tasMark = body.GetNode<ColorRect>("Region_tasmania/Bar/ReferendumMark");
        AssertThat(nswMark.AnchorLeft).IsEqualApprox(0.57f, 0.001f);
        AssertThat(tasMark.AnchorLeft).IsEqualApprox(0.94f, 0.001f);
        AssertThat(nswMark.AnchorLeft).IsLess(tasMark.AnchorLeft); // the per-region bars genuinely differ

        panel.QueueFree();
    }

    [TestCase]
    public async Task Open_RendersTheAntiFederationOverlay_AndCauseChip_WhenOppositionHasAccrued()
    {
        // WS3.5: a region with banked Anti-Federation Sentiment shows a barn-red opposition band on its gauge (net → raw)
        // and a cause chip naming why. A region with none shows neither.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales);
        FoundIn(game, AustraliaColony.Victoria); // a second region with NO opposition — the control
        // Inject NSW support 40% + 30% opposition via the save layer (the internal setters aren't reachable here): raw 40,
        // net 10, so the band spans a visible slice and "Apathy" (raw < 50) is the live cause.
        SaveGame save = SaveGame.From(game, "australia");
        Game opposed = (save with
        {
            Colonies = save.Colonies!
                .Select(c => c.X == AustraliaColonyStart.StartTile(AustraliaColony.NewSouthWales).X
                          && c.Y == AustraliaColonyStart.StartTile(AustraliaColony.NewSouthWales).Y
                    ? c with { FederationSupport = 80, AntiFederation = 30 }
                    : c)
                .ToList(),
        }).Restore(Australia);
        FederationPanel panel = AddPanel(host);
        panel.Open(opposed, () => { });
        await runner.SimulateFrames(1);

        var body = panel.GetNode<VBoxContainer>($"VBox/Scroll/{FederationPanel.BodyName}");
        // NSW: the opposition band rides the gauge and the cause chip names the driver.
        AssertThat(body.GetNodeOrNull("Region_newSouthWales/Bar/OppositionBand")).IsNotNull();
        var chip = body.GetNode<Label>("Opposition_newSouthWales");
        AssertThat(chip.Text).Contains("opposition");
        AssertThat(chip.Text).Contains("Apathy");
        // Victoria: no opposition → neither the band nor the chip.
        AssertThat(body.GetNodeOrNull("Region_victoria/Bar/OppositionBand")).IsNull();
        AssertThat(body.GetNodeOrNull("Opposition_victoria")).IsNull();

        panel.QueueFree();
    }

    [TestCase]
    public async Task Open_ShowsTheNswQuotaWarning_WhenNswIsShortOfTheMobilisationQuota()
    {
        // WS3.6: with a referendum on the table and NSW below its mobilisation quota, the panel telegraphs the historical
        // 1898 hurdle so a quota failure isn't a gotcha.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales); // a lean pop-1 NSW → mobilisation well below the 1200 quota
        Game drafted = (SaveGame.From(game, "australia") with { FederationPhase = (int)FederationPhase.ConstitutionDrafted })
            .Restore(Australia);
        FederationPanel panel = AddPanel(host);
        panel.Open(drafted, () => { });
        await runner.SimulateFrames(1);

        var body = panel.GetNode<VBoxContainer>($"VBox/Scroll/{FederationPanel.BodyName}");
        var warning = body.GetNode<Label>("NswQuotaWarning");
        AssertThat(warning.Text).Contains("mobilisation quota");

        panel.QueueFree();
    }

    [TestCase]
    public async Task Open_CapsThePanelToTheViewport_AndKeepsCloseReachable()
    {
        // Regression guard: the feature-rich body (drafting section + six support gauges) must scroll inside a capped,
        // centred shell — never push the Close button off-screen (the code-built-overlay overflow trap).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales);
        Game convention = (SaveGame.From(game, "australia") with { FederationPhase = (int)FederationPhase.ConventionCalled, ConventionPoints = 1000 })
            .Restore(Australia);
        FederationPanel panel = AddPanel(host);
        panel.Open(convention, () => { });
        await runner.SimulateFrames(2);

        // The body sits inside a ScrollContainer; Close is pinned in the VBox (a sibling of the scroll) so it is always in
        // the panel; and the panel never exceeds the viewport height (the scroll takes any overflow).
        AssertThat(panel.GetNodeOrNull("VBox/Scroll")).IsNotNull();
        AssertThat(panel.GetNodeOrNull("VBox/CloseButton")).IsNotNull();
        AssertThat(panel.Size.Y).IsLessEqual(panel.GetViewportRect().Size.Y);

        panel.QueueFree();
    }

    [TestCase]
    public async Task CallConvention_IsDisabledWithAReason_UntilThresholdsAreMet()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales); // one region, no accrued support → the convention gate is closed
        FederationPanel panel = AddPanel(host);

        panel.Open(game, () => { });
        await runner.SimulateFrames(1);

        var body = panel.GetNode<VBoxContainer>($"VBox/Scroll/{FederationPanel.BodyName}");
        var button = body.GetNode<Button>("CallConventionButton");
        AssertThat(button.Disabled).IsTrue();
        AssertThat(body.GetNodeOrNull("CallConventionButtonReason")).IsNotNull();

        panel.QueueFree();
    }

    [TestCase]
    public async Task Open_AtConventionCalled_RendersTheDraftConstitutionSection()
    {
        // WS3.3 M2: once a convention is called, the panel shows the Draft-Constitution section — a progress gauge + one
        // row per clause with a Draft button. Advance to the drafting phase (+ points banked) via the save layer.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales);
        Game convention = (SaveGame.From(game, "australia") with { FederationPhase = (int)FederationPhase.ConventionCalled, ConventionPoints = 1000 })
            .Restore(Australia);
        FederationPanel panel = AddPanel(host);

        panel.Open(convention, () => { });
        await runner.SimulateFrames(1);

        var body = panel.GetNode<VBoxContainer>($"VBox/Scroll/{FederationPanel.BodyName}");
        AssertThat(body.GetNodeOrNull("DraftHeader")).IsNotNull();
        AssertThat(body.GetNodeOrNull("ConstitutionProgressBar")).IsNotNull();
        AssertThat(body.GetNodeOrNull("Clause_senateEquality")).IsNotNull();
        // Senate Equality has no gold cost, so with 1000 points banked its Draft button is enabled.
        AssertThat(body.GetNode<Button>("Clause_senateEquality/DraftButton").Disabled).IsFalse();

        panel.QueueFree();
    }

    [TestCase]
    public async Task DraftButton_DraftsTheClause_OnPress()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        Game game = NewAustralia();
        FoundIn(game, AustraliaColony.NewSouthWales);
        Game convention = (SaveGame.From(game, "australia") with { FederationPhase = (int)FederationPhase.ConventionCalled, ConventionPoints = 1000 })
            .Restore(Australia);
        FederationPanel panel = AddPanel(host);
        panel.Open(convention, () => { });
        await runner.SimulateFrames(1);

        AssertThat(convention.ConstitutionProgressPercent).IsEqual(0);
        var body = panel.GetNode<VBoxContainer>($"VBox/Scroll/{FederationPanel.BodyName}");
        body.GetNode<Button>("Clause_senateEquality/DraftButton").EmitSignal(Button.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(convention.ConstitutionProgressPercent).IsEqual(34); // Senate Equality (weight 34) is now drafted
        body = panel.GetNode<VBoxContainer>($"VBox/Scroll/{FederationPanel.BodyName}");
        AssertThat(body.GetNodeOrNull("Clause_senateEquality/DraftButton")).IsNull(); // the row rebuilt to a drafted tick
        AssertThat(body.GetNodeOrNull("Clause_senateEquality/Drafted")).IsNotNull();

        panel.QueueFree();
    }
}
