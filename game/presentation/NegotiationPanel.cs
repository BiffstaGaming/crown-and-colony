using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Units;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The diplomacy / negotiation dialog (86d3c9xpt, 86d3c9ubw, 86d3e4bbv): the human's side of the treaty table over
/// the already-shipped backend (<see cref="Game.PendingHumanProposals"/>,
/// <see cref="Game.EvaluateTrade(int, DiplomaticTrade)"/>, <see cref="Game.CounterOffer"/>,
/// <see cref="Game.SettleTrade"/>). It has three jobs, all presentation-only (ADR-006) — every rule and every gate is
/// a <see cref="Game"/> oracle; the panel renders state and forwards commands, inventing no rules.
/// <list type="number">
/// <item><b>Answer AI offers</b> — surfaces each <see cref="DiplomaticTrade"/> a foreign power has proactively offered
/// the human this turn (alliance / cease-fire / peace, queued by <see cref="Game.ProposeProactiveTreaties"/>). The human
/// <i>accepts</i> (applied via <see cref="Game.SettleTrade"/>) or <i>declines</i> (the offer is simply dropped).</item>
/// <item><b>Build a treaty</b> — pick a contacted rival European power and assemble a multi-clause
/// <see cref="DiplomaticTrade"/> (gold given/demanded + goods + a colony given/taken + a unit given/taken + incitement
/// + a change of stance), then submit it. The rival weighs it with its own
/// <see cref="Game.EvaluateTrade(int, DiplomaticTrade)"/>: an acceptable offer is settled; an unacceptable one is run
/// through the rival's own <see cref="Game.CounterOffer"/> (prune + halve-gold + give-up) and any returned counter is
/// surfaced for the human to <i>accept</i> (settled) or <i>reject</i> (dropped). This is FreeCol's
/// <c>NegotiationDialog</c> offer/demand table, reached from the HUD or a scout at a rival colony's gate
/// (<c>moveScoutColony</c>).</item>
/// <item><b>Scout mission menu</b> — a scout at a rival colony's gate chooses Spy or Negotiate
/// (<c>getScoutForeignColonyChoice</c>).</item>
/// </list>
/// </summary>
/// <remarks>
/// Faithful subset (documented): the human assembles arbitrary gold/goods/colony/unit/incite/stance clauses exactly as
/// FreeCol's dialog does, and the AI answers with a single accept / counter / reject round — the panel does not re-loop
/// the human's own counter against a returned counter (the human accepts or rejects what came back), so the multi-round
/// human-side haggle of FreeCol's full table is one shot here. The accept/decline of an AI offer, the AI's verdict on a
/// human offer, and the AI's counter all run through the same tested backend, so the panel adds no rules. The only RNG
/// is inside <see cref="Game.CounterOffer"/> (the AI's give-up roll on its <i>own</i> stream, never the human's stream
/// 0, ADR-009); no save change (the pending queue and the trade are transient).
/// </remarks>
public partial class NegotiationPanel : PanelContainer
{
    private Game _game = null!;
    private Action _onChanged = () => { };

    /// <summary>The AI offers awaiting the human's answer, drained from <see cref="Game.TakePendingHumanProposals"/> when the panel opens (so each is answered once).</summary>
    private readonly List<DiplomaticTrade> _pending = [];

    /// <summary>When the panel was opened by a scout standing at a rival colony, the colony being negotiated with (so the proposer list is pinned to its owner); null for the general diplomacy view.</summary>
    private int? _scoutColonyOwnerId;

    /// <summary>When the panel is pinned to a specific rival colony (opened via <see cref="OpenForColony"/>), that colony — the gate for the carrier-trade and demand-tribute affordances surfaced in the pinned view (86d3fq0dj / 86d3fq0ev); null in the general diplomacy view.</summary>
    private Colony? _pinnedColony;

    /// <summary>When non-null, the panel is in scout-mission-menu mode (Spy / Negotiate) for the rival colony at this owner, rather than the diplomacy offer/propose view.</summary>
    private (Colony Colony, Action OnSpy, Action OnNegotiate)? _scoutMissions;

    /// <summary>When non-null, the panel is in offer-builder mode against this rival power (their <see cref="Player.PlayerId"/>); the accumulated clauses live in <see cref="_clauses"/>.</summary>
    private int? _builderTargetId;

    /// <summary>The clauses the human has added to the in-progress treaty against <see cref="_builderTargetId"/> (order preserved, as FreeCol's dialog lists them). Reset whenever the builder opens.</summary>
    private readonly List<TradeItem> _clauses = [];

    /// <summary>When non-null, the AI's counter-offer to the human's last submitted treaty (the trimmed deal it would take), awaiting the human's accept/reject. Cleared on accept/reject or when a new builder opens.</summary>
    private DiplomaticTrade? _counter;

    /// <summary>The gold amount the human's next gold clause will carry, adjusted by the +/- buttons (FreeCol's <c>GoldTradeItemPanel</c> spinner). A single shared value used for both the "we pay" and "we demand" gold clause.</summary>
    private int _goldAmount = 100;

    private string _outcome = "";

    /// <summary>
    /// Opens the general diplomacy view: drains any pending AI offers for the human and lets the human open a fresh
    /// negotiation with any contacted rival. <paramref name="onChanged"/> runs after every accept/decline/settle so the
    /// controller can surface a status notice and refresh the map (a settled treaty may flip a stance).
    /// </summary>
    public void Open(Game game, Action onChanged)
    {
        Init(game, onChanged, scoutColonyOwnerId: null);
    }

    /// <summary>
    /// Opens the negotiation view pinned to a single rival — the FreeCol <c>SCOUT_COLONY_NEGOTIATE</c> entry: a scout at
    /// the gate of <paramref name="rivalColony"/> talks to its owner. Pending AI offers are still surfaced, but the
    /// "build a treaty" chooser is pre-targeted at this colony's owner.
    /// </summary>
    public void OpenForColony(Game game, Colony rivalColony, Action onChanged)
    {
        Init(game, onChanged, scoutColonyOwnerId: rivalColony.OwnerId, pinnedColony: rivalColony);
    }

    private void Init(Game game, Action onChanged, int? scoutColonyOwnerId, Colony? pinnedColony = null)
    {
        _game = game;
        _onChanged = onChanged;
        _scoutColonyOwnerId = scoutColonyOwnerId;
        _pinnedColony = pinnedColony;
        _scoutMissions = null;
        _builderTargetId = null;
        _clauses.Clear();
        _counter = null;
        _goldAmount = 100;
        _outcome = "";
        _pending.Clear();
        _pending.AddRange(game.TakePendingHumanProposals()); // drain once — the human answers each offer here
        Rebuild();
        Show();
    }

    /// <summary>
    /// Opens the scout-mission menu for a scout standing at the gate of <paramref name="rivalColony"/> (86d3c9ubw,
    /// FreeCol <c>getScoutForeignColonyChoice</c>): the choices are <b>Spy</b> (<paramref name="onSpy"/>) and
    /// <b>Negotiate</b> (<paramref name="onNegotiate"/>, which the controller routes back into
    /// <see cref="OpenForColony"/>). Attack is left to the existing map-click assault path (the panel offers the two
    /// peaceful missions). The controller has already confirmed the spy mission is legal before opening this; the
    /// <paramref name="scoutId"/> is carried for the controller's own re-resolution after a mission spends the scout.
    /// This menu does <b>not</b> drain pending offers (a scout at the gate is a unit action, not the diplomacy review).
    /// </summary>
    public void OpenScoutMissions(Game game, Colony rivalColony, int scoutId, Action onSpy, Action onNegotiate)
    {
        _ = scoutId; // the controller owns the scout id; kept in the signature so the entry point reads faithfully
        _game = game;
        _onChanged = () => { };
        _scoutColonyOwnerId = rivalColony.OwnerId;
        _pinnedColony = null;
        _scoutMissions = (rivalColony, onSpy, onNegotiate);
        _builderTargetId = null;
        _clauses.Clear();
        _counter = null;
        _outcome = "";
        _pending.Clear();
        Rebuild();
        Show();
    }

    private int HumanId => _game.HumanPlayer.PlayerId;

    private void Changed()
    {
        _onChanged();
        Rebuild();
    }

    private static string Short(string id) => id.Length == 0 ? id : id[(id.LastIndexOf('.') + 1)..];

    /// <summary>Display label for a colonial power: its nation short-name (e.g. "Dutch"), or "an unaligned power" for the nation-less human-style player.</summary>
    private string PowerLabel(int playerId)
    {
        Player? p = _game.Players.FirstOrDefault(pl => pl.PlayerId == playerId);
        if (p?.NationId is not { } nationId)
        {
            return "an unaligned power";
        }
        string s = Short(nationId);
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    /// <summary>A one-line human-readable summary of any treaty's clauses (used for both a queued AI offer and a returned counter), proposer-relative.</summary>
    private string DescribeOffer(DiplomaticTrade offer)
    {
        if (offer.Items.Count == 1 && offer.Items[0] is StanceTradeItem stance)
        {
            string verb = stance.Stance switch
            {
                Stance.Alliance => "an alliance",
                Stance.Peace => "a peace treaty",
                Stance.CeaseFire => "a cease-fire",
                Stance.War => "a declaration of war",
                _ => "a treaty",
            };
            return $"The {PowerLabel(offer.ProposerId)} propose {verb}.";
        }
        return $"The {PowerLabel(offer.ProposerId)} propose a treaty ({offer.Items.Count} clauses).";
    }

    /// <summary>One human-readable line for a single clause, from the human's point of view (give / receive).</summary>
    private string DescribeClause(TradeItem item) => item switch
    {
        GoldTradeItem g => g.Source == HumanId
            ? $"You pay {g.Amount} gold."
            : $"The {PowerLabel(item.Source)} pay you {g.Amount} gold.",
        GoodsTradeItem gd => gd.Source == HumanId
            ? $"You give {gd.Amount} {Short(gd.GoodsId)}."
            : $"The {PowerLabel(item.Source)} give you {gd.Amount} {Short(gd.GoodsId)}.",
        ColonyTradeItem c => c.Source == HumanId
            ? $"You cede {ColonyName(c.ColonyId)}."
            : $"The {PowerLabel(item.Source)} cede {ColonyName(c.ColonyId)} to you.",
        UnitTradeItem u => u.Source == HumanId
            ? $"You hand over {UnitLabel(u.UnitId)}."
            : $"The {PowerLabel(item.Source)} hand you {UnitLabel(u.UnitId)}.",
        InciteTradeItem inc => $"The {PowerLabel(inc.Source)} go to war with the {PowerLabel(inc.Victim)}.",
        StanceTradeItem s => s.Stance switch
        {
            Stance.Alliance => "You form an alliance.",
            Stance.Peace => "You agree peace.",
            Stance.CeaseFire => "You agree a cease-fire.",
            Stance.War => "You declare war.",
            _ => "Your stance changes.",
        },
        _ => "A clause.",
    };

    private string ColonyName(int colonyId) =>
        _game.Colonies.FirstOrDefault(c => c.Id == colonyId)?.Name ?? $"colony #{colonyId}";

    private string UnitLabel(int unitId) =>
        _game.Units.FirstOrDefault(u => u.Id == unitId) is { } u ? $"{u.Type.ShortName} #{u.Id}" : $"unit #{unitId}";

    private void Rebuild()
    {
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            dynamic.RemoveChild(child); child.QueueFree(); // detach now (signal-safe), free deferred — avoids freed-while-emitting when a child button's handler drives the rebuild
        }

        // Scout-mission menu mode (a scout at a rival colony's gate): show the Spy / Negotiate missions, not the
        // diplomacy offer/propose lists (FreeCol's getScoutForeignColonyChoice).
        if (_scoutMissions is { } missions)
        {
            GetNode<Label>("VBox/Title").Text = $"Scout at {missions.Colony.Name}";
            GetNode<Label>("VBox/Info").Text = $"Your scout stands at the gate of the {PowerLabel(missions.Colony.OwnerId)} colony.";
            dynamic.AddChild(ActionButton("Spy", "Spy on the colony", () => { Hide(); missions.OnSpy(); }));
            dynamic.AddChild(ActionButton("Negotiate", "Open negotiations", () => missions.OnNegotiate()));
            return;
        }

        // Offer-builder mode (a target chosen): the multi-clause treaty table.
        if (_builderTargetId is { } targetId)
        {
            BuildOfferTable(dynamic, targetId);
            return;
        }

        GetNode<Label>("VBox/Title").Text = _scoutColonyOwnerId is { } ownerId
            ? $"Negotiate with the {PowerLabel(ownerId)}"
            : "Diplomacy";
        GetNode<Label>("VBox/Info").Text = _outcome.Length > 0 ? _outcome : DefaultInfo();

        BuildPendingOffers(dynamic);
        BuildTargetChooser(dynamic);
        BuildPinnedColonyActions(dynamic);
    }

    /// <summary>
    /// The carrier-trade (86d3fq0dj) and demand-tribute (86d3fq0ev) affordances surfaced when the panel is pinned to a
    /// specific rival colony (<see cref="OpenForColony"/>). Each is gated by its <see cref="Game"/> oracle against the
    /// first eligible human unit standing at the colony (a carrier with cargo for trade, an armed unit for tribute), so a
    /// kind with no eligible unit/ability simply does not appear — the panel invents no rules, it only forwards the
    /// command. A no-op in the general diplomacy view (no pinned colony).
    /// </summary>
    private void BuildPinnedColonyActions(VBoxContainer dynamic)
    {
        if (_pinnedColony is not { } colony)
        {
            return;
        }

        // --- Trade goods at this rival colony (a human carrier on/beside it, de Witt's ability, not at war) ---
        // Pick the first adjacent human carrier that can ACTUALLY trade here — one carrying a good the colony can pay for,
        // or one able to afford a good the colony sells. A colony can have several adjacent human carriers, so a laden
        // trader must not be masked by an empty one that merely iterates first (this folds the capability check into the
        // selection, exactly like the DemandTribute pick below; fixes the intermittent L3 flake 86d3hzz6d).
        bool CanTradeHere(Unit u) => _game.Ruleset.GoodsTypes.Any(g =>
        {
            int sellAmount = Math.Min(100, u.CargoOf(g.Id));
            int buyAmount = Math.Min(100, colony.StoreOf(g.Id));
            return (sellAmount > 0 && _game.CheckSellToForeignColony(u, colony.Position, g.Id, sellAmount).Allowed)
                || (buyAmount > 0 && _game.CheckBuyFromForeignColony(u, colony.Position, g.Id, buyAmount).Allowed);
        });
        Unit? trader = _game.CanTradeWithForeignColonies(HumanId)
            ? _game.Units.FirstOrDefault(u =>
                u.OwnerId == HumanId && u.Type.IsCarrier && u.IsOnMap
                && (u.Position == colony.Position || u.Position.IsAdjacentTo(colony.Position))
                && CanTradeHere(u))
            : null;
        if (trader is not null)
        {
            // Sell: the first good aboard the trader the colony can pay for.
            (string GoodsId, int Amount)? sell = _game.Ruleset.GoodsTypes
                .Select(g => (g.Id, Amount: Math.Min(100, trader.CargoOf(g.Id))))
                .Where(g => g.Amount > 0 && _game.CheckSellToForeignColony(trader, colony.Position, g.Id, g.Amount).Allowed)
                .Select(g => ((string, int)?)(g.Id, g.Amount))
                .FirstOrDefault();
            if (sell is { } s)
            {
                dynamic.AddChild(Heading("Trade at this colony"));
                int quote = _game.CheckSellToForeignColony(trader, colony.Position, s.GoodsId, s.Amount).Cost;
                dynamic.AddChild(ActionButton("TradeSell", $"Sell {s.Amount} {Short(s.GoodsId)} for {quote}g", () =>
                {
                    int got = _game.SellToForeignColony(trader, colony.Position, s.GoodsId, s.Amount);
                    _outcome = $"You sold {s.Amount} {Short(s.GoodsId)} to the {PowerLabel(colony.OwnerId)} for {got} gold.";
                    Changed();
                }));
            }

            // Buy: the first good the colony holds that the trader can afford.
            (string GoodsId, int Amount)? buy = _game.Ruleset.GoodsTypes
                .Select(g => (g.Id, Amount: Math.Min(100, colony.StoreOf(g.Id))))
                .Where(g => g.Amount > 0 && _game.CheckBuyFromForeignColony(trader, colony.Position, g.Id, g.Amount).Allowed)
                .Select(g => ((string, int)?)(g.Id, g.Amount))
                .FirstOrDefault();
            if (buy is { } b)
            {
                if (sell is null)
                {
                    dynamic.AddChild(Heading("Trade at this colony"));
                }
                int quote = _game.CheckBuyFromForeignColony(trader, colony.Position, b.GoodsId, b.Amount).Cost;
                dynamic.AddChild(ActionButton("TradeBuy", $"Buy {b.Amount} {Short(b.GoodsId)} for {quote}g", () =>
                {
                    int paid = _game.BuyFromForeignColony(trader, colony.Position, b.GoodsId, b.Amount);
                    _outcome = $"You bought {b.Amount} {Short(b.GoodsId)} from the {PowerLabel(colony.OwnerId)} for {paid} gold.";
                    Changed();
                }));
            }
        }

        // --- Demand tribute under threat of force (an armed human unit on/beside the colony) ---
        Unit? bully = _game.Units.FirstOrDefault(u =>
            u.OwnerId == HumanId && u.IsOnMap
            && (u.Position == colony.Position || u.Position.IsAdjacentTo(colony.Position))
            && _game.CheckDemandTributeFromColony(u, colony.Position).Allowed);
        if (bully is not null)
        {
            dynamic.AddChild(ActionButton("DemandTribute", $"Demand tribute from the {PowerLabel(colony.OwnerId)}", () =>
            {
                Game.EuropeanTributeResult result = _game.DemandTributeFromColony(bully, colony.Position);
                _outcome = result.Paid
                    ? $"The {PowerLabel(colony.OwnerId)} paid {result.Gold} gold in tribute."
                    : $"The {PowerLabel(colony.OwnerId)} refused your demand.";
                Changed();
            }));
        }
    }

    private string DefaultInfo()
    {
        if (_pending.Count > 0)
        {
            return $"{_pending.Count} foreign power(s) await your answer.";
        }
        return _scoutColonyOwnerId is { } ownerId
            ? $"Build a treaty to offer the {PowerLabel(ownerId)}."
            : "Open talks with a rival power, or wait for them to come to you.";
    }

    /// <summary>One Accept/Decline row per queued AI offer (FreeCol's incoming-treaty dialog).</summary>
    private void BuildPendingOffers(VBoxContainer dynamic)
    {
        if (_pending.Count == 0)
        {
            return;
        }
        dynamic.AddChild(Heading("Offers awaiting your answer"));
        foreach (DiplomaticTrade offer in _pending.ToList()) // snapshot: accept/decline mutates _pending
        {
            dynamic.AddChild(Hint(DescribeOffer(offer)));
            var row = new HBoxContainer { Name = "OfferRow", Alignment = BoxContainer.AlignmentMode.Center };
            row.AddChild(ActionButton("Accept", "Accept", () =>
            {
                _game.SettleTrade(offer);   // apply the treaty via the existing backend (stance change + tension)
                _pending.Remove(offer);
                _outcome = $"You accepted the {PowerLabel(offer.ProposerId)} offer.";
                Changed();
            }));
            row.AddChild(ActionButton("Decline", "Decline", () =>
            {
                _pending.Remove(offer);     // drop it — nothing is applied
                _outcome = $"You declined the {PowerLabel(offer.ProposerId)} offer.";
                Changed();
            }));
            dynamic.AddChild(row);
        }
    }

    /// <summary>The "open a negotiation" chooser: a target rival (or the pinned scout colony's owner). Choosing one opens the offer builder against that power.</summary>
    private void BuildTargetChooser(VBoxContainer dynamic)
    {
        // Contacted colonial rivals (a stance has been recorded — i.e. not Uncontacted). When opened by a scout at a
        // colony, the target is pinned to that colony's owner; otherwise every contacted rival is offered.
        List<int> targets = _scoutColonyOwnerId is { } pinned
            ? (_game.StanceBetween(HumanId, pinned) != Stance.Uncontacted ? [pinned] : [])
            : _game.ForeignNationStances(HumanId)
                .Where(s => s.Stance != Stance.Uncontacted)
                .Select(s => s.PlayerId)
                .ToList();

        if (targets.Count == 0)
        {
            dynamic.AddChild(Hint("You have not yet met any rival power to negotiate with."));
            return;
        }

        dynamic.AddChild(Heading("Open a negotiation"));
        foreach (int targetId in targets)
        {
            Stance current = _game.StanceBetween(HumanId, targetId);
            dynamic.AddChild(ActionButton(
                $"Negotiate_{targetId}",
                $"Negotiate with the {PowerLabel(targetId)} (currently {Short(current.ToString()).ToLowerInvariant()})",
                () => OpenBuilder(targetId)));
        }
    }

    /// <summary>Switches the panel into offer-builder mode against <paramref name="targetId"/> with a fresh, empty treaty.</summary>
    private void OpenBuilder(int targetId)
    {
        _builderTargetId = targetId;
        _clauses.Clear();
        _counter = null;
        _goldAmount = 100;
        _outcome = "";
        Rebuild();
    }

    /// <summary>The multi-clause offer table against <paramref name="targetId"/>: the running clause list, the add-clause controls, Submit, and (after a submit) any AI counter for accept/reject.</summary>
    private void BuildOfferTable(VBoxContainer dynamic, int targetId)
    {
        GetNode<Label>("VBox/Title").Text = $"Treaty with the {PowerLabel(targetId)}";
        GetNode<Label>("VBox/Info").Text = _outcome.Length > 0
            ? _outcome
            : "Assemble your offer, then Submit. The rival will accept, counter, or reject.";

        // A returned counter takes over the table: show its clauses and an Accept/Reject choice.
        if (_counter is { } counter)
        {
            dynamic.AddChild(Heading("Their counter-offer"));
            foreach (TradeItem item in counter.Items)
            {
                dynamic.AddChild(Hint(DescribeClause(item)));
            }
            var crow = new HBoxContainer { Name = "CounterRow", Alignment = BoxContainer.AlignmentMode.Center };
            crow.AddChild(ActionButton("AcceptCounter", "Accept counter", () =>
            {
                _game.SettleTrade(counter);
                _outcome = $"You accepted the {PowerLabel(targetId)} counter-offer.";
                _counter = null;
                _builderTargetId = null;
                Changed();
            }));
            crow.AddChild(ActionButton("RejectCounter", "Reject counter", () =>
            {
                _outcome = $"You rejected the {PowerLabel(targetId)} counter-offer.";
                _counter = null;
                _builderTargetId = null;
                Changed();
            }));
            dynamic.AddChild(crow);
            return;
        }

        // The running clause list, each with a Remove button.
        dynamic.AddChild(Heading("Your offer"));
        if (_clauses.Count == 0)
        {
            dynamic.AddChild(Hint("(empty — add a clause below)"));
        }
        else
        {
            for (int i = 0; i < _clauses.Count; i++)
            {
                int index = i;
                var crow = new HBoxContainer { Name = $"ClauseRow_{index}" };
                crow.AddChild(Hint(DescribeClause(_clauses[index])));
                crow.AddChild(ActionButton($"RemoveClause_{index}", "Remove", () =>
                {
                    _clauses.RemoveAt(index);
                    Rebuild();
                }));
                dynamic.AddChild(crow);
            }
        }

        dynamic.AddChild(new HSeparator());
        BuildClauseControls(dynamic, targetId);

        dynamic.AddChild(new HSeparator());
        var actions = new HBoxContainer { Name = "OfferActions", Alignment = BoxContainer.AlignmentMode.Center };
        var submit = new Button { Name = "Submit", Text = "Submit offer", Disabled = _clauses.Count == 0 };
        submit.Pressed += () => Submit(targetId);
        actions.AddChild(submit);
        actions.AddChild(ActionButton("BackToTargets", "Cancel", () =>
        {
            _builderTargetId = null;
            _clauses.Clear();
            _outcome = "";
            Rebuild();
        }));
        dynamic.AddChild(actions);
    }

    /// <summary>
    /// The add-clause controls — one row per FreeCol clause kind (gold give/demand, goods, colony give/demand, unit
    /// give/demand, incite, stance). Each "Add" picks a concrete, currently-valid instance (the first eligible colony /
    /// unit / goods, the spun gold amount, a stance from the de-escalation ladder) and appends a <see cref="TradeItem"/>
    /// to <see cref="_clauses"/>; a kind with nothing eligible is shown as a disabled hint. The valuation and validity
    /// stay in the backend — these controls only build the clause objects.
    /// </summary>
    private void BuildClauseControls(VBoxContainer dynamic, int targetId)
    {
        dynamic.AddChild(Heading("Add a clause"));

        // --- Gold (give / demand) with a +/- spinner shared by both directions ---
        var goldRow = new HBoxContainer { Name = "GoldRow", Alignment = BoxContainer.AlignmentMode.Center };
        goldRow.AddChild(ActionButton("GoldMinus", "−100", () => { _goldAmount = Math.Max(0, _goldAmount - 100); Rebuild(); }));
        goldRow.AddChild(new Label { Name = "GoldAmount", Text = $"{_goldAmount}g" });
        goldRow.AddChild(ActionButton("GoldPlus", "+100", () => { _goldAmount += 100; Rebuild(); }));
        goldRow.AddChild(ActionButton("AddGoldGive", "Pay", () =>
        {
            if (_goldAmount > 0) { _clauses.Add(new GoldTradeItem(HumanId, targetId, _goldAmount)); }
            Rebuild();
        }));
        goldRow.AddChild(ActionButton("AddGoldDemand", "Demand", () =>
        {
            if (_goldAmount > 0) { _clauses.Add(new GoldTradeItem(targetId, HumanId, _goldAmount)); }
            Rebuild();
        }));
        dynamic.AddChild(goldRow);

        // --- Goods: the first non-empty stockpile in one of the human's colonies → the rival's first colony ---
        Colony? humanColony = _game.Colonies.FirstOrDefault(c => c.OwnerId == HumanId);
        Colony? rivalColony = _game.Colonies.FirstOrDefault(c => c.OwnerId == targetId);
        (string GoodsId, int Amount)? goods = humanColony is null
            ? null
            : _game.Ruleset.GoodsTypes
                .Select(g => (g.Id, Amount: humanColony.StoreOf(g.Id)))
                .Where(g => g.Amount > 0)
                .Select(g => ((string, int)?)(g.Id, Math.Min(100, g.Amount)))
                .FirstOrDefault();
        if (humanColony is not null && rivalColony is not null && goods is { } gd)
        {
            dynamic.AddChild(ActionButton("AddGoods", $"Give {gd.Amount} {Short(gd.GoodsId)} (from {humanColony.Name})", () =>
            {
                _clauses.Add(new GoodsTradeItem(HumanId, targetId, humanColony.Id, rivalColony.Id, gd.GoodsId, gd.Amount));
                Rebuild();
            }));
        }

        // --- Colony (give one of ours / demand one of theirs) ---
        Colony? giveColony = _game.Colonies.FirstOrDefault(c => c.OwnerId == HumanId);
        if (giveColony is not null)
        {
            dynamic.AddChild(ActionButton("AddColonyGive", $"Cede {giveColony.Name}", () =>
            {
                _clauses.Add(new ColonyTradeItem(HumanId, targetId, giveColony.Id));
                Rebuild();
            }));
        }
        Colony? takeColony = _game.Colonies.FirstOrDefault(c => c.OwnerId == targetId);
        if (takeColony is not null)
        {
            dynamic.AddChild(ActionButton("AddColonyDemand", $"Demand {takeColony.Name}", () =>
            {
                _clauses.Add(new ColonyTradeItem(targetId, HumanId, takeColony.Id));
                Rebuild();
            }));
        }

        // --- Unit (give one of ours / demand one of theirs) — non-native units only ---
        Unit? giveUnit = _game.Units.FirstOrDefault(u => u.OwnerId == HumanId && u.OwnerNationId is null);
        if (giveUnit is not null)
        {
            dynamic.AddChild(ActionButton("AddUnitGive", $"Hand over {giveUnit.Type.ShortName} #{giveUnit.Id}", () =>
            {
                _clauses.Add(new UnitTradeItem(HumanId, targetId, giveUnit.Id));
                Rebuild();
            }));
        }
        Unit? takeUnit = _game.Units.FirstOrDefault(u => u.OwnerId == targetId && u.OwnerNationId is null);
        if (takeUnit is not null)
        {
            dynamic.AddChild(ActionButton("AddUnitDemand", $"Demand {takeUnit.Type.ShortName} #{takeUnit.Id}", () =>
            {
                _clauses.Add(new UnitTradeItem(targetId, HumanId, takeUnit.Id));
                Rebuild();
            }));
        }

        // --- Incite: have the rival go to war with a third contacted colonial power ---
        int? victim = _game.ForeignNationStances(HumanId)
            .Where(s => s.Stance != Stance.Uncontacted && s.PlayerId != targetId)
            .Select(s => (int?)s.PlayerId)
            .FirstOrDefault();
        if (victim is { } victimId)
        {
            dynamic.AddChild(ActionButton("AddIncite", $"Incite war on the {PowerLabel(victimId)}", () =>
            {
                _clauses.Add(new InciteTradeItem(targetId, HumanId, victimId));
                Rebuild();
            }));
        }

        // --- Stance: the realistic transitions from the current relationship (FreeCol's de-escalation ladder) ---
        List<Stance> stances = OfferableStances(_game.StanceBetween(HumanId, targetId));
        if (stances.Count > 0)
        {
            var stanceRow = new HBoxContainer { Name = "StanceRow", Alignment = BoxContainer.AlignmentMode.Center };
            foreach (Stance stance in stances)
            {
                stanceRow.AddChild(ActionButton($"AddStance{stance}", OfferLabel(stance), () =>
                {
                    // One stance clause at a time — replace any existing one (two stance clauses contradict).
                    _clauses.RemoveAll(c => c is StanceTradeItem);
                    _clauses.Add(new StanceTradeItem(HumanId, targetId, stance));
                    Rebuild();
                }));
            }
            dynamic.AddChild(stanceRow);
        }

        // --- Declare war (86d3fq0bf): a UNILATERAL act, not a treaty clause the rival accepts. Shown only when the rival
        // is a contacted at-peace power (CanDeclareWar); it forwards straight to Game.DeclareWar and ends the builder. ---
        if (_game.CanDeclareWar(HumanId, targetId))
        {
            dynamic.AddChild(ActionButton("DeclareWar", $"Declare war on the {PowerLabel(targetId)}", () =>
            {
                _game.DeclareWar(HumanId, targetId); // csChangeStance(WAR) — mutual war + the stance-change tension spike
                _outcome = $"You declared war on the {PowerLabel(targetId)}.";
                _builderTargetId = null;
                _clauses.Clear();
                Changed();
            }));
        }
    }

    /// <summary>The realistic stance offers from a current stance (mirrors FreeCol's de-escalation ladder): war → peace/cease-fire; cease-fire → peace/alliance; peace → alliance.</summary>
    private static List<Stance> OfferableStances(Stance current) => current switch
    {
        Stance.War => [Stance.Peace, Stance.CeaseFire],
        Stance.CeaseFire => [Stance.Peace, Stance.Alliance],
        Stance.Peace => [Stance.Alliance],
        _ => [],
    };

    private static string OfferLabel(Stance stance) => stance switch
    {
        Stance.Peace => "Add peace",
        Stance.CeaseFire => "Add cease-fire",
        Stance.Alliance => "Add alliance",
        _ => $"Add {stance}",
    };

    /// <summary>
    /// Submits the assembled treaty (FreeCol's <c>send</c> button): routes it through the rival's own
    /// <see cref="Game.EvaluateTrade(int, DiplomaticTrade)"/>. Accepted → <see cref="Game.SettleTrade"/> applies it and
    /// the builder closes. Rejected → the rival's <see cref="Game.CounterOffer"/> is asked for a trimmed deal; a returned
    /// counter is surfaced for accept/reject (next <see cref="Rebuild"/>), and a flat rejection (no counter) closes the
    /// builder with a notice. All rules live in the backend; the panel only forwards and renders.
    /// </summary>
    private void Submit(int targetId)
    {
        var trade = new DiplomaticTrade(HumanId, targetId, TradeContext.Diplomatic);
        foreach (TradeItem item in _clauses)
        {
            trade.Add(item);
        }

        if (_game.EvaluateTrade(targetId, trade).Accept)
        {
            _game.SettleTrade(trade);
            _outcome = $"The {PowerLabel(targetId)} accepted your treaty.";
            _builderTargetId = null;
            _clauses.Clear();
            Changed();
            return;
        }

        // Not acceptable as-is — let the rival prune/halve and counter (its own give-up roll, its own RNG stream).
        DiplomaticTrade? counter = _game.CounterOffer(targetId, trade);
        if (counter is null || counter.Items.Count == 0)
        {
            _outcome = $"The {PowerLabel(targetId)} rejected your treaty.";
            _builderTargetId = null;
            _clauses.Clear();
            Changed();
            return;
        }

        _counter = counter; // surfaced on the next rebuild for accept/reject
        _outcome = $"The {PowerLabel(targetId)} counter your offer.";
        Changed();
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        ThemeTypeVariation = "ColonyTitle",
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        HorizontalAlignment = HorizontalAlignment.Center,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
    };

    private static Button ActionButton(string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        return button;
    }
}
