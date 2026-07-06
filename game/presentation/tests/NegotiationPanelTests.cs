using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
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
    /// <summary>
    /// Resets the process-global Godot <see cref="Input"/> singleton before and after every case so this suite is
    /// order-independent under the CI run order (mirrors <c>InputTests.ResetGlobalInputState</c> — the proven isolation
    /// pattern in this repo). Godot's <see cref="Input"/>, the viewport's GUI-drag state, and static fields are
    /// process-global and persist across GdUnit cases within one headless run. A prior L3 case that left a mouse button
    /// flagged held or the viewport in a "gui is dragging" state (e.g. the Europe/Colony drag-drop cases, now fixed to
    /// call <c>BuildDragPayload</c>) would then contend with this panel's input. Releasing the left mouse button and
    /// flushing the buffer at case boundaries closes that bleed regardless of which suite ran before us. This is a
    /// belt-and-suspenders guard alongside the source fix in the drag-drop tests; it changes no product behaviour.
    /// </summary>
    [BeforeTest]
    public void ResetGlobalInputStateBeforeEachTest() => ResetGlobalInputState();

    [AfterTest]
    public void ResetGlobalInputStateAfterEachTest() => ResetGlobalInputState();

    private static void ResetGlobalInputState()
    {
        // Release the left mouse button in case a prior case (e.g. an InputTests click that didn't complete cleanly, or an
        // Europe/Colony drag) left a press flagged held on the global Input.
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        // Release every key the sibling L3 suites drive (InputTests presses W/G/N/B/D/Space/Enter/F1/F5/F9 + arrows + Ctrl),
        // so no key stays flagged pressed across suites and bleeds a spurious action into this panel's fresh scene. This is
        // the exact key set InputTests.ResetGlobalInputState releases — kept in lockstep so whichever suite runs first, the
        // process-global Input is clean before ours.
        foreach (Key key in new[]
        {
            Key.W, Key.G, Key.N, Key.B, Key.D, Key.Space, Key.Enter, Key.F1, Key.F5, Key.F9,
            Key.Left, Key.Right, Key.Up, Key.Down, Key.Ctrl,
        })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        }
        // Park the cursor at a neutral origin so any relative motion starts from a known position.
        Input.WarpMouse(Vector2.Zero);
        // Drain anything queued above (and any leftover from the previous case) before this case runs.
        Input.FlushBufferedEvents();
    }

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

    // ---- Player-initiated rival actions (86d3fq0bf / 86d3fq0dj / 86d3fq0ev) ----

    [TestCase]
    public async Task DeclareWar_AppearsForAnAtPeaceRival_AndForwardsToWar()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        SetStance(game, humanId, foreignId, Stance.Peace);

        controller.OpenNegotiationPanel();
        await runner.SimulateFrames(1);
        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        AssertThat(panel.Visible).IsTrue();

        PressFirstButton(panel, $"Negotiate_{foreignId}");
        await runner.SimulateFrames(1);

        // The unilateral declare-war action is offered (peace→war is not in the de-escalation ladder, so it's a button).
        Button? declare = FindButton(panel, "DeclareWar");
        AssertThat(declare != null).IsTrue();

        declare!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.StanceBetween(humanId, foreignId)).IsEqual(Stance.War);
        AssertThat(game.StanceBetween(foreignId, humanId)).IsEqual(Stance.War);
    }

    [TestCase]
    public async Task TradeAtRivalColony_AppearsForADeWittCarrier_AndForwardsTheSale()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        // A rival coastal colony, the human at peace and holding de Witt, a funded rival, and a laden human carrier beside it.
        Colony rival = FoundCoastalColony(game, foreignId);
        SetStance(game, humanId, foreignId, Stance.Peace);
        GrantDeWitt(game, humanId);
        SetGold(game.Players.First(p => p.PlayerId == foreignId), 5000);
        Position water = rival.Position.Neighbours().First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater);
        Unit ship = game.SpawnUnit(game.Ruleset.Unit("model.unit.caravel"), water);
        AddCargo(ship, Tobacco, 100);

        // Sanity: the engine oracle must allow the sale before the panel can surface it (pinpoints any staging gap).
        AssertThat(game.CanTradeWithForeignColonies(humanId)).IsTrue();
        AssertThat(ship.CargoOf(Tobacco)).IsEqual(100);
        AssertThat(game.CheckSellToForeignColony(ship, rival.Position, Tobacco, 100).Allowed).IsTrue();

        controller.OpenNegotiationForColony(rival);
        // OpenForColony -> Init runs Rebuild() + Show() SYNCHRONOUSLY (NegotiationPanel.Init), so the panel is visible
        // and its buttons exist immediately. Assert them BEFORE any SimulateFrames so no bled held-key input from a
        // prior L3 suite (the recurring 86d3hzz6d flake) can reprocess the running scene in a frame between the
        // synchronous build and the button lookup — the frame is only needed AFTER the button press below.
        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        AssertThat(panel.Visible).IsTrue();

        Button? sell = FindButton(panel, "TradeSell");
        AssertThat(sell != null).IsTrue();

        int storeBefore = rival.StoreOf(Tobacco);
        sell!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(rival.StoreOf(Tobacco) > storeBefore).IsTrue(); // the goods moved into the rival warehouse
        AssertThat(ship.CargoOf(Tobacco) < 100).IsTrue();          // and left the hold
    }

    [TestCase]
    public async Task TradeAtRivalColony_PrefersACarrierThatCanTrade_WhenAnEmptyOneIsAlsoAdjacent()
    {
        // Regression for the 86d3hzz6d flake: a colony can have several adjacent human carriers. The panel must surface
        // the trade using a carrier that can ACTUALLY trade, not mask a laden ship behind an empty carrier that merely
        // iterates first. (The flake was CI-only, caused by a sibling suite's held-key input bleeding the human's
        // starting ship adjacent to the rival colony — but the fix is a real product robustness improvement.)
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        Colony rival = FoundCoastalColony(game, foreignId);
        SetStance(game, humanId, foreignId, Stance.Peace);
        GrantDeWitt(game, humanId);
        SetGold(game.Players.First(p => p.PlayerId == foreignId), 5000);
        Position water = rival.Position.Neighbours().First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater);
        Unit empty = game.SpawnUnit(game.Ruleset.Unit("model.unit.caravel"), water); // spawned FIRST -> iterates first, carries nothing
        Unit laden = game.SpawnUnit(game.Ruleset.Unit("model.unit.caravel"), water);
        AddCargo(laden, Tobacco, 100);

        controller.OpenNegotiationForColony(rival);
        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        Button? sell = FindButton(panel, "TradeSell");
        AssertThat(sell != null).IsTrue(); // the empty carrier did NOT mask the laden one

        int storeBefore = rival.StoreOf(Tobacco);
        sell!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(rival.StoreOf(Tobacco) > storeBefore).IsTrue(); // the laden ship's cargo was the one traded
        AssertThat(laden.CargoOf(Tobacco) < 100).IsTrue();
        AssertThat(empty.CargoOf(Tobacco)).IsEqual(0);             // the empty ship was untouched
    }

    [TestCase]
    public async Task DemandTributeFromColony_AppearsForAnArmedUnit_AndForwards()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        Colony rival = FoundCoastalColony(game, foreignId);
        SetStance(game, humanId, foreignId, Stance.Peace);
        Position adj = rival.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.ColonyAt(n) == null && game.NativeSettlementAt(n) == null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit gun = game.SpawnUnit(game.Ruleset.Unit("model.unit.artillery"), adj); // human-owned offensive unit

        controller.OpenNegotiationForColony(rival);
        // OpenForColony -> Init runs Rebuild() + Show() SYNCHRONOUSLY (NegotiationPanel.Init), so the panel is visible
        // and its buttons exist immediately. Assert them BEFORE any SimulateFrames so no bled held-key input from a
        // prior L3 suite (the recurring 86d3hzz6d flake) can reprocess the running scene in a frame between the
        // synchronous build and the button lookup — the frame is only needed AFTER the button press below.
        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        AssertThat(panel.Visible).IsTrue();

        Button? demand = FindButton(panel, "DemandTribute");
        AssertThat(demand != null).IsTrue();

        demand!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(gun.MovementLeft).IsEqual(0); // the demand resolved and ended the unit's turn
    }

    // ---- Scout-mission menu (86d3f62r5 / 86d3c9ubw) ----

    [TestCase]
    public async Task ScoutMissionMenu_OffersSpyAndNegotiate_AndSpyGlimpsesTheColony()
    {
        // 86d3f62r5: the scout-at-the-gate mission menu (OpenScoutMissions → Spy / Negotiate) was reachable but undriven
        // by L3. Stage a scout adjacent to a rival colony, open the mission menu, and fire Spy → the scout's spy mission
        // runs (it always succeeds — the scout's turn ends).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        Colony rival = FoundCoastalColony(game, foreignId);
        SetStance(game, humanId, foreignId, Stance.Peace);
        Unit scout = StageScoutBeside(game, rival, humanId);
        AssertThat(game.CheckSpyOnColony(scout, rival.Position).Allowed).IsTrue(); // sanity: the spy mission is legal

        // Open the mission menu exactly as the controller does (spy + negotiate callbacks routed back through it).
        int scoutId = scout.Id;
        bool negotiateOpened = false;
        ((NegotiationPanel)controller.GetNode<PanelContainer>("UI/NegotiationPanel")).OpenScoutMissions(
            game, rival, scoutId,
            onSpy: () => controller.GetType().GetMethod("SpyOnRivalColony", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(controller, [scoutId, rival.Position]),
            onNegotiate: () => negotiateOpened = true);
        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        AssertThat(panel.Visible).IsTrue();

        // Both missions are offered.
        AssertThat(FindButton(panel, "Spy")).IsNotNull();
        AssertThat(FindButton(panel, "Negotiate")).IsNotNull();

        // Fire Spy → the scout spends its turn (FreeCol spySettlement always succeeds and ends the scout's turn).
        FindButton(panel, "Spy")!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(game.Units.First(u => u.Id == scoutId).MovementLeft).IsEqual(0); // the spy ended the scout's turn
        AssertThat(negotiateOpened).IsFalse(); // Spy, not Negotiate
    }

    [TestCase]
    public async Task ScoutMissionMenu_Negotiate_RoutesToTheColonyNegotiation()
    {
        // The mission menu's Negotiate choice forwards to the colony-pinned negotiation (OpenForColony); assert the
        // callback fired (the controller routes it into OpenNegotiationForColony).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        Colony rival = FoundCoastalColony(game, foreignId);
        SetStance(game, humanId, foreignId, Stance.Peace);
        Unit scout = StageScoutBeside(game, rival, humanId);

        bool negotiateFired = false;
        ((NegotiationPanel)controller.GetNode<PanelContainer>("UI/NegotiationPanel")).OpenScoutMissions(
            game, rival, scout.Id, onSpy: () => { }, onNegotiate: () => negotiateFired = true);
        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");

        FindButton(panel, "Negotiate")!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(negotiateFired).IsTrue(); // the Negotiate mission routed to the colony negotiation
    }

    // ---- Offer-builder clause coverage (86d3f62r5) — reject-counter + goods/colony/unit/incite ----

    [TestCase]
    public async Task OfferBuilder_RejectCounter_DropsTheDeal_LeavingTheStanceUnchanged()
    {
        // 86d3f62r5: the RejectCounter branch was undriven (only AcceptCounter was). Same setup as the accept-counter
        // test — a large gold demand the rival can't accept, so it counters — but the human REJECTS: nothing settles
        // (the stance stays Peace) and the builder closes.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        SetStance(game, humanId, foreignId, Stance.Peace);

        controller.OpenNegotiationPanel();
        await runner.SimulateFrames(1);
        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");

        PressFirstButton(panel, $"Negotiate_{foreignId}");
        await runner.SimulateFrames(1);
        PressFirstButton(panel, "AddGoldGive"); // a small give the counter keeps
        await runner.SimulateFrames(1);
        for (int i = 0; i < 50; i++) // pump to a 5100g demand the rival can't accept net-positive
        {
            PressFirstButton(panel, "GoldPlus");
        }
        await runner.SimulateFrames(1);
        PressFirstButton(panel, "AddGoldDemand");
        await runner.SimulateFrames(1);
        PressFirstButton(panel, "Submit");
        await runner.SimulateFrames(1);

        Button? rejectCounter = FindButton(panel, "RejectCounter");
        AssertThat(rejectCounter != null).IsTrue(); // a counter was surfaced

        rejectCounter!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // Rejecting applies nothing — the stance stays Peace and the counter is consumed (no RejectCounter button left).
        AssertThat(game.StanceBetween(humanId, foreignId)).IsEqual(Stance.Peace);
        AssertThat(FindButton(panel, "RejectCounter") == null).IsTrue();
    }

    [TestCase]
    public async Task OfferBuilder_AddGoodsClause_AppearsInTheRunningOffer()
    {
        // 86d3f62r5: the goods clause was undriven. With a stocked human colony and a rival colony, the "Give N goods"
        // add-clause button is offered; pressing it appends a clause to the running offer (a ClauseRow renders).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        Colony human = FoundCoastalColony(game, humanId);
        AddColonyStore(human, Tobacco, 100); // a non-empty stockpile → the goods clause is offered
        FoundCoastalColony(game, foreignId); // a rival colony the goods are given TO
        SetStance(game, humanId, foreignId, Stance.Peace);

        var panel = OpenBuilderAgainst(controller, foreignId);
        await runner.SimulateFrames(1);

        Button? addGoods = FindButton(panel, "AddGoods");
        AssertThat(addGoods != null).IsTrue();
        addGoods!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(FindNodeNamed(panel, "ClauseRow_0")).IsNotNull(); // the goods clause joined the running offer
    }

    [TestCase]
    public async Task OfferBuilder_AddColonyClauses_AppearInTheRunningOffer()
    {
        // 86d3f62r5: the colony give/demand clauses were undriven. With a human colony and a rival colony, both the
        // "Cede <ours>" and "Demand <theirs>" buttons are offered; each appends a clause.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        FoundCoastalColony(game, humanId);
        FoundCoastalColony(game, foreignId);
        SetStance(game, humanId, foreignId, Stance.Peace);

        var panel = OpenBuilderAgainst(controller, foreignId);
        await runner.SimulateFrames(1);

        AssertThat(FindButton(panel, "AddColonyGive")).IsNotNull();
        AssertThat(FindButton(panel, "AddColonyDemand")).IsNotNull();
        FindButton(panel, "AddColonyGive")!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(FindNodeNamed(panel, "ClauseRow_0")).IsNotNull(); // ceding our colony joined the offer

        FindButton(panel, "AddColonyDemand")!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(FindNodeNamed(panel, "ClauseRow_1")).IsNotNull(); // demanding theirs joined it too
    }

    [TestCase]
    public async Task OfferBuilder_AddUnitClauses_AppearInTheRunningOffer()
    {
        // 86d3f62r5: the unit give/demand clauses were undriven. The human always has starting units (give); staging a
        // rival unit makes the demand offered too. Each add appends a clause.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = ForeignPowerId(game);

        SetStance(game, humanId, foreignId, Stance.Peace);
        // A rival non-native unit somewhere on land → the "Demand <their unit>" clause is offered.
        Position spot = game.Map.AllPositions().First(p =>
            game.Map.InBounds(p) && !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) == null && game.NativeSettlementAt(p) == null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        Unit rivalUnit = game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), spot);
        SetUnitOwner(rivalUnit, foreignId);

        var panel = OpenBuilderAgainst(controller, foreignId);
        await runner.SimulateFrames(1);

        AssertThat(FindButton(panel, "AddUnitGive")).IsNotNull();
        AssertThat(FindButton(panel, "AddUnitDemand")).IsNotNull();
        FindButton(panel, "AddUnitGive")!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(FindNodeNamed(panel, "ClauseRow_0")).IsNotNull(); // handing over our unit joined the offer

        FindButton(panel, "AddUnitDemand")!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(FindNodeNamed(panel, "ClauseRow_1")).IsNotNull(); // demanding theirs joined it too
    }

    [TestCase]
    public async Task OfferBuilder_AddInciteClause_AppearsInTheRunningOffer()
    {
        // 86d3f62r5: the incite clause (have the rival go to war with a THIRD contacted power) was undriven. Contact two
        // rivals so an incite victim exists; the Incite button is offered against the second, and pressing it appends a clause.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        // Two distinct contacted colonial rivals: the target we negotiate with, and the victim we incite war on.
        List<int> rivals = game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial)
            .Select(p => p.PlayerId).Take(2).ToList();
        AssertThat(rivals.Count).IsEqual(2); // the classic start has several colonial powers
        int targetId = rivals[0];
        int victimId = rivals[1];
        SetStance(game, humanId, targetId, Stance.Peace);
        SetStance(game, humanId, victimId, Stance.Peace); // contacted → a valid incite victim

        var panel = OpenBuilderAgainst(controller, targetId);
        await runner.SimulateFrames(1);

        Button? incite = FindButton(panel, "AddIncite");
        AssertThat(incite != null).IsTrue();
        incite!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(FindNodeNamed(panel, "ClauseRow_0")).IsNotNull(); // the incite clause joined the running offer
    }

    // ---- helpers ----

    private const string Tobacco = "model.goods.tobacco";

    /// <summary>Opens the negotiation panel and steps into the offer-builder against <paramref name="targetId"/> (the "Negotiate_<id>" row press).</summary>
    private static PanelContainer OpenBuilderAgainst(GameController controller, int targetId)
    {
        controller.OpenNegotiationPanel();
        var panel = controller.GetNode<PanelContainer>("UI/NegotiationPanel");
        PressFirstButton(panel, $"Negotiate_{targetId}");
        return panel;
    }

    /// <summary>Stages a human scout (model.role.scout, full movement) on a free land tile adjacent to <paramref name="colony"/>.</summary>
    private static Unit StageScoutBeside(Game game, Colony colony, int ownerId)
    {
        Position adj = colony.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.ColonyAt(n) == null && game.NativeSettlementAt(n) == null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit scout = game.SpawnUnit(game.Ruleset.Unit("model.unit.seasonedScout"), adj);
        SetUnitOwner(scout, ownerId);
        typeof(Unit).GetProperty("RoleId")!.GetSetMethod(nonPublic: true)!.Invoke(scout, ["model.role.scout"]);
        return scout;
    }

    /// <summary>Sets a unit's internal-set <c>OwnerId</c> by reflection (no public setter).</summary>
    private static void SetUnitOwner(Unit unit, int ownerId) =>
        typeof(Unit).GetProperty("OwnerId")!.SetValue(unit, ownerId);

    /// <summary>Adds goods to a colony's warehouse via the internal <c>Colony.AddGoods</c> (not visible to this assembly) by reflection.</summary>
    private static void AddColonyStore(Colony colony, string goodsId, int amount) =>
        typeof(Colony).GetMethod("AddGoods", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(colony, [goodsId, amount]);

    /// <summary>The first descendant node (recursive) named <paramref name="name"/>, or null (for asserting a ClauseRow rendered).</summary>
    private static Node? FindNodeNamed(Node root, string name)
    {
        if (root.Name == name)
        {
            return root;
        }
        foreach (Node child in root.GetChildren())
        {
            if (FindNodeNamed(child, name) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static int ForeignPowerId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    /// <summary>Founds a coastal colony (adjacent to a water tile) for <paramref name="ownerId"/> via a spawned colonist, then reassigns its owner.</summary>
    private static Colony FoundCoastalColony(Game game, int ownerId)
    {
        Position site = game.Map.AllPositions().First(p =>
            game.Map.InBounds(p) && !game.Map.TerrainAt(p).IsWater && game.Map.TerrainAt(p).CanSettle
            && game.ColonyAt(p) == null && game.NativeSettlementAt(p) == null && !game.Map.IsNativeOwned(p)
            && !game.Units.Any(u => u.IsOnMap && u.Position == p)
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater) // coastal: a water neighbour (the sell tests dock a ship there)
            // AND a FREE LAND neighbour, guaranteed here so callers that place an adjacent land unit (the demand-tribute
            // test's artillery) always find one — the old predicate required only a water neighbour, so when this first
            // coastal site's land neighbours were all occupied (starting units / a native settlement, which varies with
            // the scene-suite's process state) the caller's `.First(free land)` threw "sequence contains no matching
            // element" — a recurring L3 flake surfacing on DemandTributeFromColony (86d3kgbu2 flaky-scene sweep).
            && p.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
                && game.ColonyAt(n) == null && game.NativeSettlementAt(n) == null
                && !game.Units.Any(u => u.IsOnMap && u.Position == n))
            && p.Neighbours().All(n => !game.Map.InBounds(n) || game.ColonyAt(n) == null));
        Unit founder = game.SpawnUnit(game.Ruleset.Unit(Colony.FreeColonistTypeId), site);
        Colony colony = game.FoundColony(founder);
        SetOwner(colony, ownerId);
        return colony;
    }

    /// <summary>Grants Jan de Witt to a player by reflecting into the internal live <c>CongressList</c> (the foreign-trade ability gate).</summary>
    private static void GrantDeWitt(Game game, int playerId)
    {
        Player p = game.Players.First(pl => pl.PlayerId == playerId);
        var congress = (System.Collections.Generic.List<string>)typeof(Player)
            .GetProperty("CongressList", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(p)!;
        congress.Add("model.foundingFather.janDeWitt");
    }

    /// <summary>Sets a colony's internal-set <c>OwnerId</c> by reflection (no public setter).</summary>
    private static void SetOwner(Colony colony, int ownerId) =>
        typeof(Colony).GetProperty("OwnerId")!.SetValue(colony, ownerId);

    /// <summary>Sets a player's internal-set <c>Gold</c> by reflection (no public setter).</summary>
    private static void SetGold(Player player, int gold) =>
        typeof(Player).GetProperty("Gold")!.SetValue(player, gold);

    /// <summary>Loads cargo onto a ship via the internal <c>Unit.AddCargo</c> by reflection (not visible to this assembly).</summary>
    private static void AddCargo(Unit ship, string goodsId, int amount) =>
        typeof(Unit).GetMethod("AddCargo", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ship, [goodsId, amount]);

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
