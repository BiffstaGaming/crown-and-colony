using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
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

        var header = controller.GetNode<Label>($"UI/ColonyReportPanel/VBox/Scroll/Dynamic/Colony_{colony.Id}");
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

        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
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
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Units")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Unit report");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
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
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Foreign").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).IsEqual("Foreign affairs"); // a fresh game has 3 landed rival powers to list

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Natives").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).IsEqual("Native nations");

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Religion").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(title.Text).IsEqual("Religion");
        AssertThat(dynamic.GetNodeOrNull("ReligionImmigration")).IsNotNull(); // the immigration bar always renders
    }

    [TestCase]
    public async Task ForeignTab_ShowsEachRivalsMilitaryAndNavalStrength()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Foreign").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Foreign affairs");
        // A fresh game has landed rival powers; each Foreign_{id} row carries the unconditional military + naval
        // strength figures (FreeCol NationSummary.getMilitaryStrength/getNavalStrength via Game.ColonialStrength) and
        // the tension/attitude band (Game.TensionLevelBetween → FreeCol Tension.getLevel).
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        Game game = GetGame(controller);
        Player rival = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        var row = dynamic.GetNodeOrNull<Label>($"Foreign_{rival.PlayerId}");
        AssertThat(row).IsNotNull();
        AssertThat(row!.Text).Contains("military");
        AssertThat(row.Text).Contains("naval");
        AssertThat(row.Text).Contains("attitude"); // the tension/attitude band column
    }

    [TestCase]
    public async Task ForeignTab_WithoutDeWitt_HidesTheSoLFathersAndTaxColumns()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // A fresh game's human Congress holds no father → FreeCol's de-Witt gate keeps SoL%/fathers/tax hidden.
        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Foreign").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        Game game = GetGame(controller);
        Player rival = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        var row = dynamic.GetNodeOrNull<Label>($"Foreign_{rival.PlayerId}");
        AssertThat(row).IsNotNull();
        // The gated columns are absent without de Witt (the row still carries the unconditional military/gold columns).
        AssertThat(row!.Text).Contains("military");
        AssertThat(row.Text).NotContains("SoL");
        AssertThat(row.Text).NotContains("fathers");
        AssertThat(row.Text).NotContains("tax");
    }

    [TestCase]
    public async Task ForeignTab_WithDeWitt_ShowsTheSoLFathersAndTaxColumns()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Elect Jan de Witt into the HUMAN's Congress BEFORE opening the panel (an already-open panel keeps the old game
        // reference — wave-4 L3 lesson). Go through the public SaveGame/Restore path, since the scene-test assembly can't
        // reach GameLogic's internal Congress list (mirrors the Education/Natives tests).
        Game game = GetGame(controller);
        SaveGame save = SaveGame.From(game);
        var players = save.Players!
            .Select(p => p.IsHuman ? p with { Congress = new[] { "model.foundingFather.janDeWitt" } } : p)
            .ToList();
        game = (save with { Players = players }).Restore(game.Ruleset);
        SetGame(controller, game);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Foreign").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        Player rival = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        var row = dynamic.GetNodeOrNull<Label>($"Foreign_{rival.PlayerId}");
        AssertThat(row).IsNotNull();
        // With de Witt elected, each rival row carries the SoL% / father-count / tax% columns (Game.ForeignReportRows).
        AssertThat(row!.Text).Contains("SoL");
        AssertThat(row.Text).Contains("fathers");
        AssertThat(row.Text).Contains("tax");
    }

    [TestCase]
    public async Task NavalTab_ListsTheHumansFleet()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Naval").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Naval");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        // The fleet header always renders; the start units include a carrier (caravel), so a Ship_ row renders too.
        var header = dynamic.GetNodeOrNull<Label>("NavalHeader");
        AssertThat(header).IsNotNull();
        AssertThat(header!.Text).Contains("fleet");
        Game game = GetGame(controller);
        Unit ship = game.PlayerUnits.First(u => u.Type.IsNaval);
        AssertThat(dynamic.GetNodeOrNull<Label>($"Ship_{ship.Id}")).IsNotNull();
    }

    [TestCase]
    public async Task CargoTab_ListsTheHumansCarriers()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Cargo").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Cargo");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("CargoHeader")).IsNotNull();
        // The start units include a carrier → a Cargo_ row renders (it begins empty: "[empty]").
        Game game = GetGame(controller);
        Unit carrier = game.PlayerUnits.First(u => u.Type.IsCarrier || u.Type.CarryTreasure);
        var row = dynamic.GetNodeOrNull<Label>($"Cargo_{carrier.Id}");
        AssertThat(row).IsNotNull();
    }

    [TestCase]
    public async Task RefTab_BreaksDownTheExpeditionaryForceByType()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Ref").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Expeditionary Force");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        // The header (with the land/naval totals) and a land breakdown row both render: the base REF has King's Regulars.
        var header = dynamic.GetNodeOrNull<Label>("RefHeader");
        AssertThat(header).IsNotNull();
        AssertThat(header!.Text).Contains("Royal Expeditionary Force");
        AssertThat(dynamic.GetNodeOrNull("RefLandTitle")).IsNotNull();
        // A "N × King's Regular (infantry)" row is named Ref_kingsRegular_infantry in the classic ruleset's base REF.
        AssertThat(dynamic.GetNodeOrNull<Button>("Ref_kingsRegular_infantry")).IsNotNull(); // an EntityLink cell (86d3fymc5): a flat link Button, not a Label
    }

    [TestCase]
    public async Task ScoreTab_ShowsTheScoreBreakdownAndNationRanking()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Score").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Score");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        var total = dynamic.GetNodeOrNull<Label>("ScoreTotal");
        AssertThat(total).IsNotNull();
        AssertThat(total!.Text).Contains("Your score");
        AssertThat(dynamic.GetNodeOrNull("ScoreRankingHeader")).IsNotNull();
        // The human is one of the ranked colonial powers → a Standing_{id} row renders.
        Game game = GetGame(controller);
        AssertThat(dynamic.GetNodeOrNull<Label>($"Standing_{game.HumanPlayer.PlayerId}")).IsNotNull();
    }

    [TestCase]
    public async Task NativesTab_GroupsDiscoveredSettlementsByNation_WithMostHatedColumn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Mark every native settlement's tile explored via the public save layer (the scene-test assembly can't reach
        // GameLogic's internal fog), so the grouped advisor has at least one discovered nation to render (a fresh
        // game's opening reveal may not uncover one).
        Game game = GetGame(controller);
        SaveGame save = SaveGame.From(game);
        var explored = game.NativeSettlements.Select(s => s.Position.Y * save.MapWidth + s.Position.X).ToList();
        var players = save.Players!.Select(p => p.IsHuman ? p with { Explored = explored } : p).ToList();
        game = (save with { Players = players }).Restore(game.Ruleset);
        SetGame(controller, game);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Natives").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Native nations");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        NativeSettlement settlement = game.NativeSettlements.First(s => game.IsExplored(s.Position));
        // The owning nation's grouped header renders, and the settlement's row carries the most-hated column.
        var nationHeader = dynamic.GetNodeOrNull<Label>($"NativeNation_{Strip(settlement.NationTypeId)}");
        AssertThat(nationHeader).IsNotNull();
        AssertThat(nationHeader!.Text).Contains("tension"); // tribe-wide tension band + stance
        var row = dynamic.GetNodeOrNull<Button>($"Native_{settlement.Position.X}_{settlement.Position.Y}"); // EntityLink cell (86d3fymc5): a flat link Button
        AssertThat(row).IsNotNull();
        AssertThat(row!.Text).Contains("most hated");
        // The nation header also surfaces the derived WAR/cease-fire/peace stance (Game.NativeStanceToward, 86d3fpzqf) —
        // a fresh game is "at peace"; the War/CeaseFire transitions are covered at L1 (NativeAiTests).
        AssertThat(nationHeader.Text).Contains("at peace");
    }

    private static string Strip(string id) => id[(id.LastIndexOf('.') + 1)..];

    [TestCase]
    public async Task TradeTab_ListsTradeableGoodsWithPricesAndVolume()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Trade").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Trade");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        // The net-after-tax summary line always renders.
        AssertThat(dynamic.GetNodeOrNull("TradeTotal")).IsNotNull();
        // Tobacco is always market-tradeable in the classic ruleset → its row renders, with price + sold + income.
        var tobacco = dynamic.GetNodeOrNull<Button>("Trade_tobacco"); // EntityLink cell (86d3fymc5): a flat link Button (Button.Text carries the same content)
        AssertThat(tobacco).IsNotNull();
        AssertThat(tobacco!.Text).Contains("sell");
        AssertThat(tobacco.Text).Contains("buy");
        AssertThat(tobacco.Text).Contains("sold");   // B4 cumulative units-sold column
        AssertThat(tobacco.Text).Contains("income"); // B4 income before/after tax column
    }

    [TestCase]
    public async Task TradeLink_DeepLinksIntoTheColopediaGoodsEntry()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Open through GameController so the report → Colopedia deep-link delegate is wired (86d3fymc5).
        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Trade").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // Tobacco is always tradeable → its Trade_tobacco cell is now a flat link Button; clicking it hides the report
        // and opens the Colopedia on the Goods category, scrolled to the tobacco entry.
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        var tobaccoLink = dynamic.GetNodeOrNull<Button>("Trade_tobacco");
        AssertThat(tobaccoLink).IsNotNull();
        tobaccoLink!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The report closed and the Colopedia opened on the right category + row.
        AssertThat(controller.GetNode<PanelContainer>("UI/ColonyReportPanel").Visible).IsFalse();
        var colopedia = controller.GetNode<PanelContainer>("UI/ColopediaPanel");
        AssertThat(colopedia.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/ColopediaPanel/VBox/ColopediaTitle").Text).Contains("Goods");
        AssertThat(controller.GetNode<VBoxContainer>("UI/ColopediaPanel/VBox/Scroll/Dynamic")
            .GetNodeOrNull<HBoxContainer>("Goods_tobacco")).IsNotNull();
    }

    [TestCase]
    public async Task ColonyLink_DeepLinksIntoTheColopediaBuildEntry()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Found a colony and give it a build target (a docks) BEFORE opening the panel, so its Colony_{id} header is a
        // Colopedia deep-link to that building's entry (a colony with an idle build queue stays a plain label).
        Game game = GetGame(controller);
        Colony colony = game.FoundColony(game.Units[0]);
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!
            .Select(c => c.Id == colony.Id ? c with { CurrentBuild = "model.building.warehouse" } : c)
            .ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        // The colony header is a link Button (the colony is building a warehouse); clicking it opens the Colopedia on the
        // Buildings category at the warehouse entry.
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        var colonyLink = dynamic.GetNodeOrNull<Button>($"Colony_{colony.Id}");
        AssertThat(colonyLink).IsNotNull();
        colonyLink!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<PanelContainer>("UI/ColonyReportPanel").Visible).IsFalse();
        var colopedia = controller.GetNode<PanelContainer>("UI/ColopediaPanel");
        AssertThat(colopedia.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/ColopediaPanel/VBox/ColopediaTitle").Text).Contains("Buildings");
        AssertThat(controller.GetNode<VBoxContainer>("UI/ColopediaPanel/VBox/Scroll/Dynamic")
            .GetNodeOrNull<HBoxContainer>("Buildings_warehouse")).IsNotNull();
    }

    [TestCase]
    public async Task ExplorationTab_RendersFromASeededGame()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Exploration").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Exploration");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
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
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Requirements").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Requirements");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull($"Requirements_{colony.Id}")).IsNotNull(); // the per-colony section header
    }

    // NOTE: the requirements-advisor's two new warning kinds (production-shortage + tile-improvement, 86d3fq20a) are
    // verified at L1 by ColonyRequirementsAdvisorTests (both Game.Reports oracles) and render through the SAME
    // RequirementWarning_{id} path that the existing RequirementsTab_RendersAColonyWarningSection (above) already
    // asserts at L3. A dedicated L3 scene test for them was dropped: driving a pop-1 colony into BOTH a manned-building
    // input shortage AND a live worked-tile suggestion needs a mid-scene SetGame whose new game the already-open panel
    // doesn't pick up (a scene-lifecycle fragility, not a rendering gap).

    [TestCase]
    public async Task MilitaryTab_ShowsHumanStrengthAndRefComparison()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Military").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Military");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
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

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Congress").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Continental Congress");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
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

        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_History").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("History");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
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
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Education")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Education");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
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
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Production")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Production");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
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
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Labour")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Labour");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        // A freshly founded colony's lone resident is a free colonist → its group header renders.
        AssertThat(dynamic.GetNodeOrNull("Labour_freeColonist")).IsNotNull();
    }

    [TestCase]
    public async Task LabourTab_DrillDown_ShowsThePerLocationBreakdownForAType()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Found a colony; its lone resident is a free colonist, and the remaining start units include free colonists on
        // the map → the freeColonist type has colonists in both a colony and "the field".
        Game game = GetGame(controller);
        game.FoundColony(game.Units[0]);

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Labour")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // Drive the drill-down selector to the free-colonist type.
        var selector = controller.GetNode<OptionButton>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/LabourDetailSelector");
        int item = -1;
        for (int i = 0; i < selector.ItemCount; i++)
        {
            if (selector.GetItemText(i) == "Free Colonist")
            {
                item = i;
            }
        }
        AssertThat(item).IsGreaterEqual(0);
        selector.Select(item);
        selector.EmitSignal(OptionButton.SignalName.ItemSelected, item);
        await runner.SimulateFrames(1);

        // The drill-down detail header renders for the chosen type, with a per-location breakdown beneath it.
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        var header = dynamic.GetNodeOrNull<Label>("LabourDetail_freeColonist");
        AssertThat(header).IsNotNull();
        AssertThat(header!.Text).Contains("total");
        AssertThat(header.Text).Contains("Free Colonist");
    }

    [TestCase]
    public async Task HistoryTab_PopulatedAfterFoundingAColony_ListsTheEvent()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game game = GetGame(controller);
        game.FoundColony(game.Units[0]); // records a ColonyFounded history event

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_History")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("History");
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColonyReportPanel/VBox/Scroll/Dynamic");
        // At least one History_<i> row exists (the founded-colony event) — i.e. the log is NOT the empty placeholder.
        AssertThat(dynamic.GetNodeOrNull("HistoryEmpty")).IsNull();
        AssertThat(dynamic.GetNodeOrNull("History_0")).IsNotNull();
    }

    [TestCase]
    public async Task DemographicsTab_RendersAGraph_WithDrawnPixels()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Seed a couple of years so the series has points to plot.
        Game game = GetGame(controller);
        game.FoundColony(game.Units.First(u => !u.Type.IsNaval));
        game.EndTurn();
        game.EndTurn();

        controller.OpenColonyReportPanel();
        await runner.SimulateFrames(1);
        controller.GetNode<Button>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/Tabs/Tab_Demographics")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(3);

        AssertThat(controller.GetNode<Label>("UI/ColonyReportPanel/VBox/ReportTitle").Text).IsEqual("Demographics");
        var graph = controller.GetNode<Control>("UI/ColonyReportPanel/VBox/Scroll/Dynamic/DemographicGraph");
        AssertThat(graph).IsNotNull();
        AssertThat(graph.Size.X).IsGreater(0f); // the graph laid out with a real size

        // RENDER-VERIFY: capture the graph to a temp PNG, read it back, and confirm it drew non-background pixels (the
        // frame + the coloured polylines). Then DELETE the temp PNG — we never commit pngs.
        Image img = controller.GetViewport().GetTexture().GetImage();
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"demographics_{System.Guid.NewGuid():N}.png");
        AssertThat(img.SavePng(tmp)).IsEqual(Error.Ok);
        try
        {
            using var loaded = Image.LoadFromFile(tmp);
            AssertThat(loaded).IsNotNull();
            // The graph occupies a region of the viewport; scan it for any of the three legend colours (green/gold/cyan).
            Rect2 rect = graph.GetGlobalRect();
            bool drew = false;
            int x0 = Mathf.Max(0, (int)rect.Position.X), y0 = Mathf.Max(0, (int)rect.Position.Y);
            int x1 = Mathf.Min(loaded.GetWidth(), (int)rect.End.X), y1 = Mathf.Min(loaded.GetHeight(), (int)rect.End.Y);
            for (int y = y0; y < y1 && !drew; y++)
            {
                for (int x = x0; x < x1 && !drew; x++)
                {
                    Color c = loaded.GetPixel(x, y);
                    bool green = c.G > 0.4f && c.R < 0.6f && c.B < 0.4f;
                    bool gold = c.R > 0.7f && c.G > 0.6f && c.B < 0.5f;
                    bool cyan = c.G > 0.6f && c.B > 0.6f && c.R < 0.6f;
                    if (green || gold || cyan)
                    {
                        drew = true;
                    }
                }
            }
            AssertThat(drew).IsTrue();
        }
        finally
        {
            if (System.IO.File.Exists(tmp))
            {
                System.IO.File.Delete(tmp); // do NOT leave (or commit) the temp png
            }
        }
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
