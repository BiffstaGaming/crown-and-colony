using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="MonarchDialog"/> (`86d3c9xdb`): a pending monarch
/// demand (<c>Game.PendingMonarchDemand</c>) opens the modal with the King's message, and Accept/Decline resolve it
/// via <c>Game.RespondToMonarch</c>. Presentation-only (ADR-006). The pending demand is normally set by the turn
/// tick (a tax rise or mercenary offer); the test injects it by reflection — exactly the seam the other demand-panel
/// L3 tests use — and drives the real panel + buttons.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MonarchDialogTests
{
    [TestCase]
    public async Task PendingTaxDemand_OpensTheModal_WithMessage_AndAcceptResolvesIt()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);

        // A RAISE_TAX demand awaiting an answer (what the King's tick would set): propose a higher rate, citing a good.
        InjectPendingMonarchDemand(game, new PendingMonarchDemand(
            MonarchAction.RaiseTaxAct, TaxRaise: 25, GoodsId: "model.goods.sugar",
            ColonyId: 1, GoodsAmount: 100));
        Refresh(controller); // the next view refresh surfaces the prompt
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/MonarchDialog");
        AssertThat(panel.Visible).IsTrue();
        // The King's message is rendered (not the placeholder) — the proposed rate appears.
        AssertThat(controller.GetNode<Label>("UI/MonarchDialog/VBox/MonarchInfo").Text).Contains("25%");

        controller.GetNode<Button>("UI/MonarchDialog/VBox/Buttons/AcceptButton")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse();                       // answered → modal hidden
        AssertThat(game.PendingMonarchDemand == null).IsTrue();    // and the pending demand cleared (forwarded to GameLogic)
        AssertThat(game.TaxRate).IsEqual(25);                      // Accept raised the tax to the proposed rate
    }

    [TestCase]
    public async Task PendingMercenaryOffer_OpensTheModal_AndDeclineResolvesIt()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);

        // A mercenary offer awaiting an answer: a small force at a price the human can afford.
        var offer = new List<ForceEntry> { new("model.unit.veteranSoldier", "model.role.soldier", 2) };
        InjectPendingMonarchDemand(game, new PendingMonarchDemand(
            MonarchAction.MonarchMercenaries, Offer: offer, Price: 100));
        Refresh(controller);
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/MonarchDialog");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/MonarchDialog/VBox/MonarchInfo").Text).Contains("100 gold");

        controller.GetNode<Button>("UI/MonarchDialog/VBox/Buttons/DeclineButton")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse();                       // answered → modal hidden
        AssertThat(game.PendingMonarchDemand == null).IsTrue();    // and the pending demand cleared
    }

    private static void InjectPendingMonarchDemand(Game game, PendingMonarchDemand demand) =>
        typeof(Game).GetField("_pendingMonarchDemand", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, demand);

    private static void Refresh(GameController controller) =>
        typeof(GameController).GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
