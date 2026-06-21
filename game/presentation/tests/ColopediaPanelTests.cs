using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="ColopediaPanel"/>: the Colopedia button opens it on the
/// Goods category (a row per ruleset goods type, a tradeable good showing its market price), the category tabs switch
/// to the other ruleset views (Terrain/Units/Buildings/Fathers/Nations/Resources/Concepts), the Concepts tab lists the
/// free-text help topics and switches the detail pane, and Close hides it.
/// Presentation-only (ADR-006) over <c>Game.Ruleset</c>/<c>Game.Market</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ColopediaPanelTests
{
    [TestCase]
    public async Task ColopediaButton_OpensTheGoodsReference_ListingEachGood_AndCloses()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.GetNode<Button>("UI/ColopediaButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/ColopediaPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/ColopediaPanel/VBox/ColopediaTitle").Text).Contains("Goods");

        // Tobacco is always in the classic ruleset and is market-tradeable → its row renders, named by the good,
        // showing the readable name plus its sell/buy market price.
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColopediaPanel/VBox/Scroll/Dynamic");
        var tobacco = dynamic.GetNodeOrNull<Label>("Goods_tobacco");
        AssertThat(tobacco).IsNotNull();
        AssertThat(tobacco!.Text).Contains("Tobacco");
        AssertThat(tobacco.Text).Contains("sell");
        AssertThat(tobacco.Text).Contains("buy");

        controller.GetNode<Button>("UI/ColopediaPanel/VBox/CloseButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase]
    public async Task CategoryTabs_SwitchToTheOtherRulesetViews()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColopediaPanel();
        await runner.SimulateFrames(1);
        var title = controller.GetNode<Label>("UI/ColopediaPanel/VBox/ColopediaTitle");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColopediaPanel/VBox/Scroll/Dynamic");

        // Terrain: plains is always in the classic ruleset → a named, fact-bearing row.
        controller.GetNode<Button>("UI/ColopediaPanel/VBox/Scroll/Dynamic/Tabs/Cat_Terrain").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).Contains("Terrain");
        AssertThat(dynamic.GetNodeOrNull<Label>("Terrain_plains")).IsNotNull();

        // Units: the free colonist is always present.
        controller.GetNode<Button>("UI/ColopediaPanel/VBox/Scroll/Dynamic/Tabs/Cat_Units").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).Contains("Units");
        AssertThat(dynamic.GetNodeOrNull<Label>("Units_freeColonist")).IsNotNull();

        // Buildings: the town hall is always present.
        controller.GetNode<Button>("UI/ColopediaPanel/VBox/Scroll/Dynamic/Tabs/Cat_Buildings").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).Contains("Buildings");
        AssertThat(dynamic.GetNodeOrNull<Label>("Buildings_townHall")).IsNotNull();

        // Founding Fathers: Adam Smith is always offered.
        controller.GetNode<Button>("UI/ColopediaPanel/VBox/Scroll/Dynamic/Tabs/Cat_Fathers").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).Contains("Founding Fathers");
        AssertThat(dynamic.GetNodeOrNull<Label>("Fathers_adamSmith")).IsNotNull();

        // Nations: the Dutch are always a colonial power.
        controller.GetNode<Button>("UI/ColopediaPanel/VBox/Scroll/Dynamic/Tabs/Cat_Nations").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).Contains("Nations");
        AssertThat(dynamic.GetNodeOrNull<Label>("Nations_dutch")).IsNotNull();

        // Resources: minerals are always a bonus resource.
        controller.GetNode<Button>("UI/ColopediaPanel/VBox/Scroll/Dynamic/Tabs/Cat_Resources").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).Contains("Resources");
        AssertThat(dynamic.GetNodeOrNull<Label>("Resources_minerals")).IsNotNull();

        // Concepts: free-text help topics (FreeCol's ConceptDetailPanel) — a curated stub set, not a ruleset collection.
        controller.GetNode<Button>("UI/ColopediaPanel/VBox/Scroll/Dynamic/Tabs/Cat_Concepts").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).Contains("Concepts");
        // The "Founding a colony" topic is always present and shown in the detail pane by default.
        AssertThat(dynamic.GetNodeOrNull<Button>("ConceptsRow/ConceptList/Concept_Foundingacolony")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull<Label>("ConceptsRow/ConceptDetail/ConceptTitle")!.Text).Contains("Founding a colony");
    }

    [TestCase]
    public async Task ConceptsTab_ListsHelpTopics_AndSelectingOneShowsItsText()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColopediaPanel();
        await runner.SimulateFrames(1);
        var title = controller.GetNode<Label>("UI/ColopediaPanel/VBox/ColopediaTitle");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColopediaPanel/VBox/Scroll/Dynamic");

        controller.GetNode<Button>("UI/ColopediaPanel/VBox/Scroll/Dynamic/Tabs/Cat_Concepts").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).Contains("Concepts");

        // The curated stub topics each render as a named left-list button.
        var list = controller.GetNode<VBoxContainer>("UI/ColopediaPanel/VBox/Scroll/Dynamic/ConceptsRow/ConceptList");
        AssertThat(list.GetNodeOrNull<Button>("Concept_Foundingacolony")).IsNotNull();
        AssertThat(list.GetNodeOrNull<Button>("Concept_FoundingFathers")).IsNotNull();
        AssertThat(list.GetNodeOrNull<Button>("Concept_DeclaringIndependence")).IsNotNull();

        // The detail pane opens on the first topic; its text is the explanatory copy, not just the title.
        var detailText = controller.GetNode<Label>("UI/ColopediaPanel/VBox/Scroll/Dynamic/ConceptsRow/ConceptDetail/ConceptText");
        AssertThat(detailText.Text).Contains("colonist");

        // Selecting a different topic switches the detail pane to that topic's title + text.
        list.GetNode<Button>("Concept_DeclaringIndependence").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(controller.GetNode<Label>("UI/ColopediaPanel/VBox/Scroll/Dynamic/ConceptsRow/ConceptDetail/ConceptTitle").Text)
            .Contains("Declaring Independence");
        AssertThat(controller.GetNode<Label>("UI/ColopediaPanel/VBox/Scroll/Dynamic/ConceptsRow/ConceptDetail/ConceptText").Text)
            .Contains("independence");
    }
}
