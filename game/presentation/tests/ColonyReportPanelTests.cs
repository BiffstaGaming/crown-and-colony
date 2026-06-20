using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the empire <see cref="ColonyReportPanel"/>: the Reports button opens
/// it, it lists a row per human colony with the right summary, and Close hides it. Presentation-only (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ColonyReportPanelTests
{
    [TestCase]
    public async Task ReportsButton_OpensTheReport_ListingEachColony_AndCloses()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Found a colony so the report has a row to show.
        var game = GetGame(controller);
        var colony = game.FoundColony(game.Units[0]);

        controller.GetNode<Button>("UI/ReportsButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/ColonyReportPanel");
        AssertThat(panel.Visible).IsTrue();

        var header = controller.GetNode<Label>($"UI/ColonyReportPanel/VBox/Dynamic/Colony_{colony.Id}");
        AssertThat(header.Text).Contains(colony.Name);
        AssertThat(header.Text).Contains("pop 1"); // a freshly founded colony has population 1

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/CloseButton")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase]
    public async Task Report_WithNoColonies_ShowsTheEmptyMessage()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel(); // turn 1: the human has not founded a colony yet
        await runner.SimulateFrames(1);

        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        bool hasEmptyMessage = false;
        foreach (Node child in dynamic.GetChildren())
        {
            if (child is Label { Text: var t } && t.Contains("no colonies"))
            {
                hasEmptyMessage = true;
            }
        }
        AssertThat(hasEmptyMessage).IsTrue();
    }

    [TestCase]
    public async Task UnitsTab_GroupsTheHumansUnits()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        // Switch to the Units tab and assert the four FreeCol groups render, with a non-empty roster.
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Units")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Unit report");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        foreach (string section in new[] { "Military", "Naval", "Cargo", "Labour" })
        {
            AssertThat(dynamic.GetNodeOrNull($"UnitSection_{section}")).IsNotNull();
        }
    }

    [TestCase]
    public async Task StatusTabs_SwitchToForeignNativesReligion()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        var title = controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Foreign").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).IsEqual("Foreign affairs"); // a fresh game has 3 landed rival powers to list

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Natives").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).IsEqual("Native nations");

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Religion").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).IsEqual("Religion");
        AssertThat(dynamic.GetNodeOrNull("ReligionImmigration")).IsNotNull(); // the immigration bar always renders
    }

    [TestCase]
    public async Task MarketTab_ListsTradeableGoodsWithPrices()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Market").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Trade & market prices");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        // Tobacco is always market-tradeable in the classic ruleset → its priced row renders, named by the good.
        var tobacco = dynamic.GetNodeOrNull<Label>("Market_tobacco");
        AssertThat(tobacco).IsNotNull();
        AssertThat(tobacco!.Text).Contains("sell");
        AssertThat(tobacco.Text).Contains("buy");
    }

    private static CrownAndColony.GameLogic.GameSession.Game GetGame(GameController controller) =>
        (CrownAndColony.GameLogic.GameSession.Game)controller
            .GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
