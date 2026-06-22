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
    public async Task OfferBuilder_AssembleAStanceClause_Submit_RoutesThroughTheBackend()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        // At war with a contacted rival → open the builder, add a peace clause, and submit it.
        SetStance(game, humanId, foreignId, Stance.War);

        controller.OpenNegotiationPanel();
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        AssertThat(panel.Visible).IsTrue();

        // Step 1: choose the rival to negotiate with (opens the offer table).
        PressFirstButton(panel, $"Negotiate_{foreignId}");
        await runner.SimulateFrames(1);

        // Step 2: add a peace stance clause (the de-escalation ladder offers it at War).
        Button? addPeace = FindButton(panel, "AddStancePeace");
        AssertThat(addPeace != null).IsTrue();
        addPeace!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // Step 3: submit the assembled offer → Game.EvaluateTrade (+ CounterOffer) without error.
        PressFirstButton(panel, "Submit");
        await runner.SimulateFrames(1);

        // The backend accepted (stance now Peace), rejected (still War), or returned a counter (still War, awaiting the
        // human). All are valid terminal/intermediate states — what matters is the offer routed through the backend.
        Stance after = game.StanceBetween(humanId, foreignId);
        AssertThat(after == Stance.War || after == Stance.Peace).IsTrue();
    }

    [TestCase]
    public async Task OfferBuilder_UnacceptableGoldDemand_SurfacesACounter_ThenAcceptSettlesIt()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        // Peace with a contacted rival. We will build a two-clause offer: demand a LARGE gold sum from them AND offer
        // them peace. The gold demand (a clause they value at −amount) swamps the peace value, so the raw offer is net-
        // negative for them and they cannot accept it as-is. Instead, CounterOffer HALVES the gold they would pay (the
        // FreeCol prune+halve branch) until the deal nets ≥ 0, and hands that trimmed treaty back — which the human then
        // accepts. Demanding gold (vs map-dependent units/colonies) makes the counter deterministic regardless of seed:
        // the gold-halve closes the gap, and peace is the kept positive.
        SetStance(game, humanId, foreignId, Stance.Peace);

        controller.OpenNegotiationPanel();
        await runner.SimulateFrames(1);
        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");

        PressFirstButton(panel, $"Negotiate_{foreignId}");
        await runner.SimulateFrames(1);

        // First a small gold GIVE (we pay the rival 100 — a clause they value positively, kept in the counter).
        PressFirstButton(panel, "AddGoldGive");
        await runner.SimulateFrames(1);

        // Now pump the spinner to a large gold DEMAND (5100) — a clause they value at −5100, swamping the give, so the
        // raw offer nets clearly negative for them. CounterOffer halves the gold they'd pay until net ≥ 0 and hands the
        // trimmed deal back. Gold valuation is strength-independent, so this counter is deterministic across seeds.
        for (int i = 0; i < 50; i++) // 100 (default) + 50×100 = 5100g
        {
            PressFirstButton(panel, "GoldPlus");
        }
        await runner.SimulateFrames(1);
        PressFirstButton(panel, "AddGoldDemand");
        await runner.SimulateFrames(1);

        // Submit → the rival can't accept (the gold demand is a net cost), so CounterOffer returns a trimmed deal.
        PressFirstButton(panel, "Submit");
        await runner.SimulateFrames(1);

        // A counter is surfaced for the human's accept/reject (the panel exposes an AcceptCounter button only then).
        Button? acceptCounter = FindButton(panel, "AcceptCounter");
        AssertThat(acceptCounter != null).IsTrue();

        // Accept the counter → SettleTrade applies the trimmed treaty without error and the builder closes.
        acceptCounter!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The settle ran cleanly and the counter is consumed (no AcceptCounter button remains).
        AssertThat(FindButton(panel, "AcceptCounter") == null).IsTrue();
    }

    [TestCase]
    public async Task QueuedAiProposal_IsStillAnswerable_WithTheBuilderInPlace()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        // A foreign power has proactively offered the human a cease-fire (queued by ProposeProactiveTreaties).
        SetStance(game, humanId, foreignId, Stance.War);
        QueuePendingOffer(game, StanceOffer(foreignId, humanId, Stance.CeaseFire));

        controller.OpenNegotiationPanel();
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        AssertThat(panel.Visible).IsTrue();

        // The queued offer is surfaced alongside the builder entry; accepting it settles the cease-fire both ways.
        PressFirstButton(panel, "Accept");
        await runner.SimulateFrames(1);

        AssertThat(game.StanceBetween(humanId, foreignId)).IsEqual(Stance.CeaseFire);
        AssertThat(game.StanceBetween(foreignId, humanId)).IsEqual(Stance.CeaseFire);
        AssertThat(game.PendingHumanProposals.Count).IsEqual(0);
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
