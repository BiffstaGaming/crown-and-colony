using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
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
    public async Task TradeTab_ListsTradeableGoodsWithPricesAndVolume()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Trade").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Trade");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        // The net-after-tax summary line always renders.
        AssertThat(dynamic.GetNodeOrNull("TradeTotal")).IsNotNull();
        // Tobacco is always market-tradeable in the classic ruleset → its row renders, with price + sold + income.
        var tobacco = dynamic.GetNodeOrNull<Label>("Trade_tobacco");
        AssertThat(tobacco).IsNotNull();
        AssertThat(tobacco!.Text).Contains("sell");
        AssertThat(tobacco.Text).Contains("buy");
        AssertThat(tobacco.Text).Contains("sold");   // B4 cumulative units-sold column
        AssertThat(tobacco.Text).Contains("income"); // B4 income before/after tax column
    }

    [TestCase]
    public async Task ExplorationTab_RendersFromASeededGame()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Exploration").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Exploration");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        // The human's opening tile reveal discovers at least one land region, so the header + a Region_ row render;
        // if (rarely) none is discovered, the empty-state line renders instead — either way the tab built a row.
        bool rendered = dynamic.GetNodeOrNull("ExplorationHeader") is not null
            || dynamic.GetNodeOrNull("ExplorationEmpty") is not null;
        AssertThat(rendered).IsTrue();
    }

    [TestCase]
    public async Task RequirementsTab_RendersAColonyWarningSection()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Found a colony so the requirements report has a per-colony section.
        Game game = GetGame(controller);
        Colony colony = game.FoundColony(game.Units[0]);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Requirements").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Requirements");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        AssertThat(dynamic.GetNodeOrNull($"Requirements_{colony.Id}")).IsNotNull(); // the per-colony section header
    }

    [TestCase]
    public async Task MilitaryTab_ShowsHumanStrengthAndRefComparison()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Military").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Military");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        var human = dynamic.GetNodeOrNull<Label>("MilitaryHuman");
        AssertThat(human).IsNotNull();
        AssertThat(human!.Text).Contains("Land units");
        // The REF comparison row renders with the King's expeditionary-force counts.
        var ref_ = dynamic.GetNodeOrNull<Label>("MilitaryRef");
        AssertThat(ref_).IsNotNull();
        AssertThat(ref_!.Text).Contains("Naval units");
    }

    [TestCase]
    public async Task CongressTab_ShowsTheFoundingFatherElectionState()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Congress").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Continental Congress");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        // The recruit + liberty-progress header always renders, named for the test.
        var progress = dynamic.GetNodeOrNull<Label>("CongressProgress");
        AssertThat(progress).IsNotNull();
        AssertThat(progress!.Text).Contains("liberty");
    }

    [TestCase]
    public async Task HistoryTab_ListsNotablePastEvents()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Founding a colony records a ColonyFounded history event — the History tab should then list it.
        var game = GetGame(controller);
        var colony = game.FoundColony(game.Units[0]);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_History").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("History");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        // The colony-founded event ("Turn N: Founded <colony>.") is listed — it may not be first, since a
        // region discovery during the human's opening tile reveal can precede it in the history log.
        bool listsTheFounding = false;
        for (int i = 0; dynamic.GetNodeOrNull<Label>($"History_{i}") is { } label; i++)
        {
            if (label.Text.Contains(colony.Name)) { listsTheFounding = true; break; }
        }
        AssertThat(listsTheFounding).IsTrue();
    }

    [TestCase]
    public async Task EducationTab_ListsTheSchoolsTeacherAndStudent()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Found a colony, then via the save layer give it a schoolhouse staffed by an expert farmer (the teacher) and a
        // second colonist (the idle free-colonist student) — building/staffing are internal to GameLogic, so we go
        // through the public SaveGame/Restore path, mirroring ColonyPanelTests.
        Game game = GetGame(controller);
        Colony founded = game.FoundColony(game.Units[0]);
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!.Select(c => c.Id == founded.Id
            ? c with
            {
                Population = 2, // 1 teacher in the schoolhouse + 1 idle free-colonist student
                Buildings = c.Buildings!.Append("model.building.schoolhouse").ToList(),
                BuildingWorkers = new Dictionary<string, int> { ["model.building.schoolhouse"] = 1 },
                BuildingWorkerTypes = new Dictionary<string, IReadOnlyList<string>>
                {
                    ["model.building.schoolhouse"] = new[] { "model.unit.expertFarmer" },
                },
            }
            : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Education")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Education");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        AssertThat(dynamic.GetNodeOrNull($"School_{founded.Id}")).IsNotNull();
        var student = dynamic.GetNodeOrNull<Label>($"Student_{founded.Id}_schoolhouse");
        AssertThat(student).IsNotNull();
        AssertThat(student!.Text).Contains("Free Colonist"); // the student being raised
        AssertThat(student.Text).Contains("Expert Farmer");  // toward the teacher's expertise
    }

    [TestCase]
    public async Task ProductionTab_SelectsAGood_AndBreaksDownEachColony()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // A founded colony comes with a town hall (produces bells), so picking "bells" shows a producer row.
        Game game = GetGame(controller);
        Colony founded = game.FoundColony(game.Units[0]);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Production")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Production");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        var selector = dynamic.GetNodeOrNull<OptionButton>("ProductionGood");
        AssertThat(selector).IsNotNull();

        // Select "bells" (a non-farmed good the town hall makes) and assert the colony's breakdown row renders.
        int bellsItem = -1;
        for (int i = 0; i < selector!.ItemCount; i++)
        {
            if (selector.GetItemText(i) == "Bells")
            {
                bellsItem = i;
            }
        }
        AssertThat(bellsItem).IsGreaterEqual(0);
        selector.Select(bellsItem);
        selector.EmitSignal(OptionButton.SignalName.ItemSelected, bellsItem);
        await runner.SimulateFrames(1);

        var row = dynamic.GetNodeOrNull<Label>($"Production_{founded.Id}");
        AssertThat(row).IsNotNull();
        AssertThat(row!.Text).Contains(founded.Name);
        AssertThat(row.Text).Contains("Bells");
    }

    [TestCase]
    public async Task LabourTab_ListsEveryColonistGroupedByType()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // The start units include free colonists; founding one keeps the rest on the map → the Labour roster is non-empty
        // and has a free-colonist group.
        Game game = GetGame(controller);
        game.FoundColony(game.Units[0]);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Dynamic/Tabs/Tab_Labour")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Labour");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Dynamic");
        // A freshly founded colony's lone resident is a free colonist → its group header renders.
        AssertThat(dynamic.GetNodeOrNull("Labour_freeColonist")).IsNotNull();
    }

    private static Game GetGame(GameController controller) =>
        (Game)controller
            .GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, game);
}
