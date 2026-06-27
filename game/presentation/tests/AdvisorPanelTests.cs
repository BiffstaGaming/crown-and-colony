using System.Collections.Generic;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the active-unit <see cref="AdvisorPanel"/> (parity <c>86d3fq1re</c>):
/// handed a recommendation list it renders one row per recommendation and reveals itself; an empty list keeps it
/// hidden; and the Close button hides it and raises <c>Dismissed</c>. The panel is driven directly (instantiated and
/// fed the oracle's output) — it needs no <c>GameController</c> seam, so the feature is verified without it. The advisor
/// is presentation-only (ADR-006); the test hands it the same <see cref="AdvisorRecommendation"/> records the oracle
/// produces.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AdvisorPanelTests
{
    private static AdvisorPanel AddPanel(Control host)
    {
        var panel = new AdvisorPanel();
        host.AddChild(panel); // runs _Ready, building the node tree
        return panel;
    }

    private static IReadOnlyList<AdvisorRecommendation> SampleAdvice() => new[]
    {
        new AdvisorRecommendation(AdvisorRecommendationKind.FoundColony, "You could found a colony here."),
        new AdvisorRecommendation(AdvisorRecommendationKind.Idle, "No orders — fortify, sentry, or move this unit."),
    };

    [TestCase]
    public async Task Show_WithRecommendations_RendersARowPerAdviceAndReveals()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn"); // any Control host will do
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        AdvisorPanel panel = AddPanel(host);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse(); // hidden until fed advice

        panel.Show(SampleAdvice());
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsTrue();
        var rows = panel.GetNode<VBoxContainer>($"VBox/{AdvisorPanel.RowsContainerName}");
        AssertThat(rows.GetNodeOrNull("Advice_FoundColony")).IsNotNull();
        AssertThat(rows.GetNodeOrNull("Advice_Idle")).IsNotNull();
        // The text the oracle handed in is shown verbatim (a bulleted line).
        AssertThat(rows.GetNode<Label>("Advice_FoundColony").Text).Contains("found a colony");

        panel.QueueFree();
    }

    [TestCase]
    public async Task Show_WithEmptyAdvice_StaysHidden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        AdvisorPanel panel = AddPanel(host);
        panel.Show(new List<AdvisorRecommendation>()); // nothing to advise
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse();
        var rows = panel.GetNode<VBoxContainer>($"VBox/{AdvisorPanel.RowsContainerName}");
        AssertThat(rows.GetChildCount()).IsEqual(0);

        panel.QueueFree();
    }

    [TestCase]
    public async Task CloseButton_HidesAndRaisesDismissed()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        AdvisorPanel panel = AddPanel(host);
        panel.Show(SampleAdvice());
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsTrue();

        bool dismissed = false;
        panel.Dismissed += () => dismissed = true;

        panel.GetNode<Button>("VBox/CloseButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse(); // closing hides the card
        AssertThat(dismissed).IsTrue();      // and tells the host (so it can stop re-showing it)

        panel.QueueFree();
    }

    [TestCase]
    public async Task Reshow_ReplacesTheRows_NotAppends()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        AdvisorPanel panel = AddPanel(host);
        panel.Show(SampleAdvice()); // 2 rows
        await runner.SimulateFrames(1);

        // Re-show with a single different recommendation: the old rows are gone, only the new one remains.
        panel.Show(new[] { new AdvisorRecommendation(AdvisorRecommendationKind.Improve, "Build a road here.") });
        await runner.SimulateFrames(2);

        var rows = panel.GetNode<VBoxContainer>($"VBox/{AdvisorPanel.RowsContainerName}");
        AssertThat(rows.GetChildCount()).IsEqual(1);
        AssertThat(rows.GetNodeOrNull("Advice_Improve")).IsNotNull();
        AssertThat(rows.GetNodeOrNull("Advice_FoundColony")).IsNull();

        panel.QueueFree();
    }
}
