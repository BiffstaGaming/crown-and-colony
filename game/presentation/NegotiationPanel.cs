using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The diplomacy / negotiation dialog (86d3c9xpt, 86d3c9ubw): the human's side of the treaty table over the
/// already-shipped backend (<see cref="Game.PendingHumanProposals"/>, <see cref="Game.EvaluateTrade(int, DiplomaticTrade)"/>,
/// <see cref="Game.SettleTrade"/>). It has two jobs, both presentation-only (ADR-006) — every rule and every gate is a
/// <see cref="Game"/> oracle; the panel renders state and forwards commands.
/// <list type="number">
/// <item><b>Answer AI offers</b> — surfaces each <see cref="DiplomaticTrade"/> a foreign power has proactively offered
/// the human this turn (alliance / cease-fire / peace, queued by <see cref="Game.ProposeProactiveTreaties"/>). The human
/// <i>accepts</i> (applied via <see cref="Game.SettleTrade"/>) or <i>declines</i> (the offer is simply dropped).</item>
/// <item><b>Open a negotiation</b> — pick a contacted rival and a stance to propose (peace / cease-fire / alliance), build
/// a single-clause <see cref="DiplomaticTrade"/>, and route it through the rival's own
/// <see cref="Game.EvaluateTrade(int, DiplomaticTrade)"/>: if the AI accepts it is settled; otherwise the offer is
/// declined. This mirrors FreeCol's <c>SCOUT_COLONY_NEGOTIATE</c> path (<c>InGameController.moveScoutColony</c>).</item>
/// </list>
/// </summary>
/// <remarks>
/// Faithful subset (documented): a human-opened proposal is a single <b>bare stance clause</b> answered single-shot via
/// the pure evaluator (no multi-round haggling, no gold sweetener) — FreeCol's full negotiation dialog lets the player
/// assemble arbitrary gold/goods/unit/colony clauses and haggle. The accept/decline of an AI offer and the AI's verdict
/// on a human offer both run through the same tested backend, so the panel adds no rules. Draws no RNG (the human's
/// accept and the AI's <see cref="Game.EvaluateTrade(int, DiplomaticTrade)"/> are deterministic, ADR-009); no save change
/// (the pending queue and the trade are transient).
/// </remarks>
public partial class NegotiationPanel : PanelContainer
{
    private Game _game = null!;
    private Action _onChanged = () => { };

    /// <summary>The AI offers awaiting the human's answer, drained from <see cref="Game.TakePendingHumanProposals"/> when the panel opens (so each is answered once).</summary>
    private readonly List<DiplomaticTrade> _pending = [];

    /// <summary>When the panel was opened by a scout standing at a rival colony, the colony being negotiated with (so the proposer list is pinned to its owner); null for the general diplomacy view.</summary>
    private int? _scoutColonyOwnerId;

    /// <summary>When non-null, the panel is in scout-mission-menu mode (Spy / Negotiate) for the rival colony at this owner, rather than the diplomacy offer/propose view.</summary>
    private (Colony Colony, Action OnSpy, Action OnNegotiate)? _scoutMissions;

    private string _outcome = "";

    /// <summary>
    /// Opens the general diplomacy view: drains any pending AI offers for the human and lets the human open a fresh
    /// negotiation with any contacted rival. <paramref name="onChanged"/> runs after every accept/decline/propose so the
    /// controller can surface a status notice and refresh the map (a settled treaty may flip a stance).
    /// </summary>
    public void Open(Game game, Action onChanged)
    {
        Init(game, onChanged, scoutColonyOwnerId: null);
    }

    /// <summary>
    /// Opens the negotiation view pinned to a single rival — the FreeCol <c>SCOUT_COLONY_NEGOTIATE</c> entry: a scout at
    /// the gate of <paramref name="rivalColony"/> talks to its owner. Pending AI offers are still surfaced, but the
    /// "open a negotiation" chooser is pre-targeted at this colony's owner.
    /// </summary>
    public void OpenForColony(Game game, Colony rivalColony, Action onChanged)
    {
        Init(game, onChanged, scoutColonyOwnerId: rivalColony.OwnerId);
    }

    private void Init(Game game, Action onChanged, int? scoutColonyOwnerId)
    {
        _game = game;
        _onChanged = onChanged;
        _scoutColonyOwnerId = scoutColonyOwnerId;
        _scoutMissions = null;
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
        _scoutMissions = (rivalColony, onSpy, onNegotiate);
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

    /// <summary>A one-line human-readable summary of a single-clause stance offer (the only clause kind the proactive backend and this panel build).</summary>
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

        GetNode<Label>("VBox/Title").Text = _scoutColonyOwnerId is { } ownerId
            ? $"Negotiate with the {PowerLabel(ownerId)}"
            : "Diplomacy";
        GetNode<Label>("VBox/Info").Text = _outcome.Length > 0 ? _outcome : DefaultInfo();

        BuildPendingOffers(dynamic);
        BuildProposeOptions(dynamic);
    }

    private string DefaultInfo()
    {
        if (_pending.Count > 0)
        {
            return $"{_pending.Count} foreign power(s) await your answer.";
        }
        return _scoutColonyOwnerId is { } ownerId
            ? $"Offer the {PowerLabel(ownerId)} a change of stance."
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

    /// <summary>The "open a negotiation" chooser: a target rival (or the pinned scout colony's owner) and a stance to propose, each routed through the AI's own evaluator.</summary>
    private void BuildProposeOptions(VBoxContainer dynamic)
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

        dynamic.AddChild(Heading("Propose a treaty"));
        foreach (int targetId in targets)
        {
            Stance current = _game.StanceBetween(HumanId, targetId);
            dynamic.AddChild(Hint($"{PowerLabel(targetId)} — currently {Short(current.ToString()).ToLowerInvariant()}."));
            // The stances worth offering depend on the current relationship (FreeCol only offers realistic transitions):
            // at war you can sue for peace or a cease-fire; at peace you can seek an alliance; allied, nothing to add.
            List<Stance> offers = OfferableStances(current);
            if (offers.Count == 0)
            {
                dynamic.AddChild(Hint("Nothing further to propose to this power."));
                continue;
            }
            var row = new HBoxContainer { Name = "ProposeRow", Alignment = BoxContainer.AlignmentMode.Center };
            foreach (Stance stance in offers)
            {
                row.AddChild(ActionButton($"Offer{stance}", OfferLabel(stance), () => ProposeStance(targetId, stance)));
            }
            dynamic.AddChild(row);
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
        Stance.Peace => "Offer peace",
        Stance.CeaseFire => "Offer cease-fire",
        Stance.Alliance => "Offer alliance",
        _ => $"Offer {stance}",
    };

    /// <summary>Builds a single-clause stance treaty and routes it through the rival's own evaluator: accepted → settled; rejected → declined. Single-shot (no haggling), the documented faithful subset.</summary>
    private void ProposeStance(int targetId, Stance stance)
    {
        var trade = new DiplomaticTrade(HumanId, targetId, TradeContext.Diplomatic)
            .Add(new StanceTradeItem(HumanId, targetId, stance));

        string what = OfferLabel(stance).Replace("Offer ", "");
        if (_game.EvaluateTrade(targetId, trade).Accept)
        {
            _game.SettleTrade(trade);
            _outcome = $"The {PowerLabel(targetId)} accepted your offer of {what}.";
        }
        else
        {
            _outcome = $"The {PowerLabel(targetId)} rejected your offer of {what}.";
        }
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
