using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
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
        var body = panel.GetNode<VBoxContainer>($"VBox/{FederationPanel.BodyName}");
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

        var body = panel.GetNode<VBoxContainer>($"VBox/{FederationPanel.BodyName}");
        // The five-step phase tracker, with a chip per phase.
        var tracker = body.GetNodeOrNull<HBoxContainer>("PhaseTracker");
        AssertThat(tracker).IsNotNull();
        AssertThat(tracker!.GetChildCount()).IsEqual(5);
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

        var body = panel.GetNode<VBoxContainer>($"VBox/{FederationPanel.BodyName}");
        var nswMark = body.GetNode<ColorRect>("Region_newSouthWales/Bar/ReferendumMark");
        var tasMark = body.GetNode<ColorRect>("Region_tasmania/Bar/ReferendumMark");
        AssertThat(nswMark.AnchorLeft).IsEqualApprox(0.57f, 0.001f);
        AssertThat(tasMark.AnchorLeft).IsEqualApprox(0.94f, 0.001f);
        AssertThat(nswMark.AnchorLeft).IsLess(tasMark.AnchorLeft); // the per-region bars genuinely differ

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

        var body = panel.GetNode<VBoxContainer>($"VBox/{FederationPanel.BodyName}");
        var button = body.GetNode<Button>("CallConventionButton");
        AssertThat(button.Disabled).IsTrue();
        AssertThat(body.GetNodeOrNull("CallConventionButtonReason")).IsNotNull();

        panel.QueueFree();
    }
}
