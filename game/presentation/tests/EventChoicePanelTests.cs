using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="EventChoicePanel"/> (WS1.1b): a pending multi-option
/// historical event opens the modal with its authored title, prompt, and one labelled button per choice; pressing one
/// resolves it via <c>Game.ChooseEventOption</c>. Presentation-only (ADR-006). The engine only sets a pending offer for
/// a human's multi-option event through a seeded turn, so the test injects it by reflection over a live Australia game
/// and drives the real panel.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class EventChoicePanelTests
{
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();

    [TestCase]
    public async Task PendingEvent_OpensTheModal_WithAuthoredText_AndChoosingResolvesIt()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // A live Australia game with a genuine 1854 dilemma pending (Eureka: reform vs suppress) — the state the engine
        // arms for a human offered a multi-option historical event.
        Game game = Game.New(Australia, 0xE0EAUL, mapSource: MapSource.Australia);
        InjectPendingOffer(game, new Game.EventOffer("event.eurekaStockade", null, new List<string> { "reform", "suppress" }));

        var panel = controller.GetNode<EventChoicePanel>("UI/EventChoicePanel");
        panel.Open(game, _ => { });
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/EventChoicePanel/VBox/EventTitle").Text).Contains("Eureka");   // authored name
        AssertThat(controller.GetNode<Label>("UI/EventChoicePanel/VBox/EventPrompt").Text).IsNotEmpty();        // authored prompt

        // One labelled button per option; each carries its authored choice text (not blank / a raw id).
        var reform = controller.GetNodeOrNull<Button>("UI/EventChoicePanel/VBox/Dynamic/Choose_reform");
        var suppress = controller.GetNodeOrNull<Button>("UI/EventChoicePanel/VBox/Dynamic/Choose_suppress");
        AssertThat(reform).IsNotNull();
        AssertThat(suppress).IsNotNull();
        AssertThat(reform!.Text).IsNotEmpty();

        reform.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse();                  // chosen → modal hidden
        AssertThat(game.PendingEventOffer == null).IsTrue();  // and the offer cleared (ChooseEventOption ran)
    }

    [TestCase]
    public async Task PendingEvent_AutoResolvedWhileOpen_StaleClickHidesWithoutCrashing()
    {
        // The offer is transient — it auto-resolves at End Turn. If the player presses End Turn instead of answering, the
        // popup is left showing an already-resolved dilemma; a click must NOT crash (ChooseEventOption would throw) and the
        // stale popup must hide. Regression for the WS1.1b review finding.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game game = Game.New(Australia, 0xE0EAUL, mapSource: MapSource.Australia);
        InjectPendingOffer(game, new Game.EventOffer("event.eurekaStockade", null, new List<string> { "reform", "suppress" }));
        var panel = controller.GetNode<EventChoicePanel>("UI/EventChoicePanel");
        panel.Open(game, _ => { });
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsTrue();

        InjectPendingOffer(game, null); // the offer auto-resolved on End Turn while the popup was still up
        controller.GetNode<Button>("UI/EventChoicePanel/VBox/Dynamic/Choose_reform").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse(); // hidden, no InvalidMoveException thrown
    }

    private static void InjectPendingOffer(Game game, Game.EventOffer? offer) =>
        typeof(Game).GetField("_pendingEventOffer", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, offer);
}
