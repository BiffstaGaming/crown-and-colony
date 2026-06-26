using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.App;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the re-openable <see cref="MessageLogPanel"/> (`86d3e4buh` /
/// `86d3f0wdv`): the Messages button opens the log, an empty history shows the empty-state line, an accumulated history
/// renders one "Turn N" section per logged turn with its notices, and the per-category filter checkboxes hide every
/// notice of an un-ticked category (dropping a turn header that empties out). Presentation-only (ADR-006); the history
/// is the <see cref="GameController"/>'s <c>_messageLog</c>, seeded here by reflection — exactly the seam the other panel
/// L3 tests use — so the test does not depend on a particular turn's RNG events.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MessageLogPanelTests
{
    [TestCase]
    public async Task MessagesButton_OpensTheLog_EmptyByDefault()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.GetNode<Button>("UI/MessageLogButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/MessageLogPanel");
        AssertThat(panel.Visible).IsTrue();
        // A fresh game has logged nothing yet → the empty-state line renders.
        var dynamic = controller.GetNode<VBoxContainer>("UI/MessageLogPanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("LogEmpty")).IsNotNull();

        controller.GetNode<Button>("UI/MessageLogPanel/VBox/CloseButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase]
    public async Task Log_WithHistory_RendersATurnSectionPerLoggedTurn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        ShowAllCategories(controller); // a prior test (or a stale settings.cfg) may have hidden a category — start with all shown

        // Seed the session log with two turns of notices (the seam the controller fills after each End Turn).
        var log = MessageLogOf(controller);
        log.Add(new MessageLogPanel.Entry(3, new List<MessageLogPanel.LogMessage>
        {
            new(MessageCategory.Combat, "A privateer sank your Caravel at (4,5)!"),
        }));
        log.Add(new MessageLogPanel.Entry(7, new List<MessageLogPanel.LogMessage>
        {
            new(MessageCategory.Monarch, "The Crown lowered your tax rate to 18%."),
        }));

        controller.OpenMessageLogPanel();
        await runner.SimulateFrames(1);

        var dynamic = controller.GetNode<VBoxContainer>("UI/MessageLogPanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("LogTurn_3")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("LogTurn_7")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("LogEmpty")).IsNull(); // not the empty state once there is history
    }

    [TestCase]
    public async Task UntickingACategory_HidesItsNotices_AndDropsTheEmptiedTurnHeader()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        ShowAllCategories(controller); // start from a clean filter so the un-tick below is the only hidden category

        // Turn 3 holds only a Combat notice; turn 7 holds only a Monarch notice.
        var log = MessageLogOf(controller);
        log.Add(new MessageLogPanel.Entry(3, new List<MessageLogPanel.LogMessage>
        {
            new(MessageCategory.Combat, "A privateer sank your Caravel at (4,5)!"),
        }));
        log.Add(new MessageLogPanel.Entry(7, new List<MessageLogPanel.LogMessage>
        {
            new(MessageCategory.Monarch, "The Crown lowered your tax rate to 18%."),
        }));

        controller.OpenMessageLogPanel();
        await runner.SimulateFrames(1);
        var dynamic = controller.GetNode<VBoxContainer>("UI/MessageLogPanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("LogTurn_3")).IsNotNull(); // combat turn shown initially

        // Un-tick Combat → the turn-3 section (its only notice) disappears; turn 7 (Monarch) stays.
        var combatBox = controller.GetNode<CheckBox>("UI/MessageLogPanel/VBox/FilterBar/Filter_Combat");
        combatBox.ButtonPressed = false;
        combatBox.EmitSignal(BaseButton.SignalName.Toggled, false);
        await runner.SimulateFrames(1);

        AssertThat(dynamic.GetNodeOrNull("LogTurn_3")).IsNull();    // combat hidden → empty turn header dropped
        AssertThat(dynamic.GetNodeOrNull("LogTurn_7")).IsNotNull(); // monarch still shown
    }

    [TestCase]
    public async Task Log_RendersADiplomacyNotice_FromTheFirstContactAndStanceFeeds()
    {
        // 86d3f62qw: the first-contact / stance-change notices land in the log under the Diplomacy category and render.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        ShowAllCategories(controller);

        var log = MessageLogOf(controller);
        log.Add(new MessageLogPanel.Entry(5, new List<MessageLogPanel.LogMessage>
        {
            new(MessageCategory.Diplomacy, "🤝 You have made contact with the French. You are at peace."),
        }));

        controller.OpenMessageLogPanel();
        await runner.SimulateFrames(1);

        var dynamic = controller.GetNode<VBoxContainer>("UI/MessageLogPanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("LogTurn_5")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("LogEmpty")).IsNull();

        // The Diplomacy filter checkbox exists and hides the notice when un-ticked (category integration).
        var diploBox = controller.GetNode<CheckBox>("UI/MessageLogPanel/VBox/FilterBar/Filter_Diplomacy");
        diploBox.ButtonPressed = false;
        diploBox.EmitSignal(BaseButton.SignalName.Toggled, false);
        await runner.SimulateFrames(1);
        AssertThat(dynamic.GetNodeOrNull("LogTurn_5")).IsNull(); // the only notice that turn was Diplomacy → header dropped

        ShowAllCategories(controller); // leave the filter clean
    }

    private static List<MessageLogPanel.Entry> MessageLogOf(GameController controller) =>
        (List<MessageLogPanel.Entry>)controller.GetType()
            .GetField("_messageLog", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    // Resets the live category filter so every test starts with all categories shown — the /root/Settings autoload (and
    // its settings.cfg) is shared across the scene-test session, so a prior test's hidden category could otherwise leak in.
    private static void ShowAllCategories(GameController controller) =>
        controller.GetNodeOrNull<SettingsService>("/root/Settings")?.Settings.HiddenMessageCategories.Clear();
}
