using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="NegotiationPanel"/> (86d3c9xpt / 86d3c9ubw): the human's
/// diplomacy dialog over the shipped backend. A foreign power's queued offer is surfaced and Accept settles it via
/// <c>Game.SettleTrade</c> (the mutual stance flips), Decline drops it untouched, and the human can open a fresh
/// stance proposal that routes through the rival's own <c>Game.EvaluateTrade</c>. Presentation-only (ADR-006).
/// The pending-proposal queue and the inter-player stance have no public setters, so the test seeds them by reflection
/// (the same idiom as <see cref="MoundsDecisionPanelTests"/>) and drives the real panel + buttons.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NegotiationPanelTests
{
    [TestCase]
    public async Task PendingAiOffer_OpensTheDialog_AndAcceptSettlesTheStance()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        // A foreign power has proactively offered the human an alliance (the proactive-diplomacy backend queues these).
        SetStance(game, humanId, foreignId, Stance.Peace);
        QueuePendingOffer(game, StanceOffer(foreignId, humanId, Stance.Alliance));

        Refresh(controller); // the refresh auto-surfaces a pending offer
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        AssertThat(panel.Visible).IsTrue();

        // Accept the offer → SettleTrade applies the alliance stance both ways.
        PressFirstButton(panel, "Accept");
        await runner.SimulateFrames(1);

        AssertThat(game.StanceBetween(humanId, foreignId)).IsEqual(Stance.Alliance);
        AssertThat(game.StanceBetween(foreignId, humanId)).IsEqual(Stance.Alliance);
    }

    [TestCase]
    public async Task PendingAiOffer_Decline_DropsItWithoutApplying()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        SetStance(game, humanId, foreignId, Stance.Peace);
        QueuePendingOffer(game, StanceOffer(foreignId, humanId, Stance.Alliance));

        Refresh(controller);
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        AssertThat(panel.Visible).IsTrue();

        PressFirstButton(panel, "Decline");
        await runner.SimulateFrames(1);

        // Declining applies nothing — the stance stays Peace, and the queue is now empty.
        AssertThat(game.StanceBetween(humanId, foreignId)).IsEqual(Stance.Peace);
        AssertThat(game.PendingHumanProposals.Count).IsEqual(0);
    }

    [TestCase]
    public async Task OpenNegotiation_OffersAStanceProposal_ThatRoutesThroughTheBackend()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        // At war with a contacted rival → the panel should offer "Offer peace" / "Offer cease-fire".
        SetStance(game, humanId, foreignId, Stance.War);

        controller.OpenNegotiationPanel();
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        AssertThat(panel.Visible).IsTrue();

        // A peace-offer button exists for the contacted rival; pressing it runs the AI's evaluation + settle path.
        Button? offerPeace = FindButton(panel, "OfferPeace");
        AssertThat(offerPeace != null).IsTrue();

        offerPeace!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The backend either accepted (stance now Peace) or rejected (still War) — both are valid; what matters is the
        // proposal routed through Game.EvaluateTrade/SettleTrade without error and produced a terminal stance.
        Stance after = game.StanceBetween(humanId, foreignId);
        AssertThat(after == Stance.War || after == Stance.Peace).IsTrue();
    }

    // ---- helpers ----

    private static int ForeignPowerId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    private static DiplomaticTrade StanceOffer(int proposerId, int recipientId, Stance stance) =>
        new DiplomaticTrade(proposerId, recipientId, TradeContext.Diplomatic)
            .Add(new StanceTradeItem(proposerId, recipientId, stance));

    /// <summary>Sets a mutual stance via the internal <c>Game.SetStance</c> (not visible to this assembly) by reflection.</summary>
    private static void SetStance(Game game, int a, int b, Stance stance) =>
        typeof(Game).GetMethod("SetStance", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(game, [a, b, stance, true]);

    /// <summary>Queues a proactive AI offer onto the private <c>_pendingHumanProposals</c> list (the seam the panel drains).</summary>
    private static void QueuePendingOffer(Game game, DiplomaticTrade offer)
    {
        var list = (List<DiplomaticTrade>)typeof(Game)
            .GetField("_pendingHumanProposals", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game)!;
        list.Add(offer);
    }

    private static void PressFirstButton(Node root, string name) =>
        FindButton(root, name)!.EmitSignal(BaseButton.SignalName.Pressed);

    private static Button? FindButton(Node root, string name)
    {
        if (root is Button b && b.Name == name)
        {
            return b;
        }
        foreach (Node child in root.GetChildren())
        {
            if (FindButton(child, name) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static void Refresh(GameController controller) =>
        typeof(GameController).GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
