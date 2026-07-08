using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests for the Australian Federation <b>presentation reskin</b> (P8: 86d3kwty1 goods /
/// 86d3kwtvc units / 86d3mm25r buildings / 86d3kwu0c chrome). These open the affected panels with the controller's
/// active variant set to <see cref="GameVariants.Australia"/> and assert the Australian display text renders
/// (goods/units read their Australian names; the chrome titles say "First Nations" / "Imperial Pressure" /
/// "Federation Convention"). The reskin is a display-layer rename over the SAME classic-shaped game — the ids are
/// unchanged (ADR-018) — so a classic game with the Australia overrides applied is a valid, sufficient probe.
/// <para>
/// The existing classic panel suites (<see cref="ColonyReportPanelTests"/> etc.) run the default variant and assert
/// the classic strings ("Continental Congress", "Native nations", "Royal Expeditionary Force", "Bells"); those stay
/// green because every override/chrome field defaults to the classic value. This file ADDS the Australia assertions.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AustraliaReskinPanelTests
{
    [TestCase]
    public async Task ColonyReport_UnderAustralia_TitlesNativesAsFirstNations()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        SetVariant(controller, GameVariants.Australia);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        // The Natives tab title reads the variant's chrome word ("First Nations", never "Native nations").
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Natives")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text)
            .IsEqual("First Nations");
    }

    [TestCase]
    public async Task ColonyReport_UnderAustralia_TitlesCongressAsFederationConvention()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        SetVariant(controller, GameVariants.Australia);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Congress")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text)
            .IsEqual("Federation Convention");
    }

    [TestCase]
    public async Task ColonyReport_UnderAustralia_RefHeaderReadsImperialPressure()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        SetVariant(controller, GameVariants.Australia);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Ref")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var header = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic")
            .GetNodeOrNull<Label>("RefHeader");
        AssertThat(header).IsNotNull();
        AssertThat(header!.Text).Contains("Imperial Pressure");     // the variant's REF name
        AssertThat(header.Text).NotContains("Royal Expeditionary"); // never the classic name under Australia
    }

    [TestCase]
    public async Task ColonyReport_UnderAustralia_ProductionSelectorRenamesBellsToCivicVoice()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        SetVariant(controller, GameVariants.Australia);

        // A founded colony's town hall produces bells → the Production selector lists that good, renamed "Civic Voice".
        Game game = GameOf(controller);
        game.FoundColony(game.Units[0]);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Production")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var selector = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic")
            .GetNodeOrNull<OptionButton>("ProductionGood");
        AssertThat(selector).IsNotNull();
        bool hasCivicVoice = false, hasBells = false;
        for (int i = 0; i < selector!.ItemCount; i++)
        {
            if (selector.GetItemText(i) == "Civic Voice") { hasCivicVoice = true; }
            if (selector.GetItemText(i) == "Bells") { hasBells = true; }
        }
        AssertThat(hasCivicVoice).IsTrue();  // the Australian name renders
        AssertThat(hasBells).IsFalse();      // the classic name does not
    }

    [TestCase]
    public async Task ColonyPanel_UnderAustralia_RendersGoodsWithAustralianNames()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        SetVariant(controller, GameVariants.Australia);

        Game game = GameOf(controller);
        Colony colony = game.FoundColony(game.Units[0]);

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);

        // The colony screen's goods band lists every good by its display name. Under Australia "bells" reads "Civic
        // Voice" and "cotton" reads "Wool" — scan the whole panel's label text for the renamed goods, and confirm the
        // classic names are gone. (The node NAMES still use the short id, so we assert on visible Text only.)
        var panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");
        string allText = CollectLabelText(panel);
        AssertThat(allText).Contains("Civic Voice"); // bells → Civic Voice
        AssertThat(allText).NotContains("Bells");    // the classic good name is gone
        AssertThat(allText).NotContains("Cotton");   // cotton reads "Wool" everywhere under Australia
    }

    [TestCase]
    public async Task Colopedia_UnderAustralia_RenamesGoodsAndBuildings()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        SetVariant(controller, GameVariants.Australia);

        controller.OpenColopediaPanel(); // opens on the Goods category
        await runner.SimulateFrames(1);

        // The Goods_bells row keeps its short-id NODE name, but its visible fact text reads the Australian name.
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColopediaPanel/VBox/Scroll/Dynamic");
        var bellsRow = dynamic.GetNodeOrNull<HBoxContainer>("Goods_bells");
        AssertThat(bellsRow).IsNotNull();
        AssertThat(CollectLabelText(bellsRow!)).Contains("Civic Voice"); // bells → Civic Voice
    }

    // ── helpers ──

    /// <summary>Recursively concatenates every <see cref="Label"/> / <see cref="Button"/> / <see cref="OptionButton"/> text under a node.</summary>
    private static string CollectLabelText(Node root)
    {
        var sb = new System.Text.StringBuilder();
        Collect(root, sb);
        return sb.ToString();

        static void Collect(Node n, System.Text.StringBuilder sb)
        {
            switch (n)
            {
                case Label l: sb.Append(l.Text).Append('\n'); break;
                case Button b: sb.Append(b.Text).Append('\n'); break;
            }
            foreach (Node child in n.GetChildren())
            {
                Collect(child, sb);
            }
        }
    }

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    /// <summary>Sets the controller's active variant (the field the Open* methods read for chrome + display overrides).</summary>
    private static void SetVariant(GameController controller, GameVariant variant) =>
        controller.GetType()
            .GetField("_variant", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, variant);
}
