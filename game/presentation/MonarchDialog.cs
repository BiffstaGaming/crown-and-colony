using System;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The Monarch action modal (`86d3c9xdb`): when the player's home-nation King makes a demand awaiting an answer
/// (<see cref="Game.PendingMonarchDemand"/>) — a <b>tax rise</b> (RAISE_TAX) or a <b>mercenary offer</b>
/// (MONARCH/HESSIAN_MERCENARIES) — this panel surfaces the King's message and asks the player to <b>accept</b> or
/// <b>decline</b>, forwarding the choice to <see cref="Game.RespondToMonarch"/>. Presentation only (ADR-006): the
/// rules — the proposed rate, the Boston-Tea-Party fallout of a refusal, the mercenary force/price, the King's
/// displeasure on a declined affordable offer — all live in <c>GameLogic</c>; this panel renders the demand and
/// forwards the boolean, then reports a one-line outcome through <see cref="Open"/>'s callback.
///
/// <para>The King's other actions (lower/waive tax, declare war/peace, free support, REF build-up) apply
/// immediately in GameLogic with no pending seam, so they never open this dialog — only the two answerable demands
/// above set <see cref="Game.PendingMonarchDemand"/>.</para>
/// </summary>
public partial class MonarchDialog : PanelContainer
{
    private Game _game = null!;
    private Action<string> _onResolved = _ => { };
    private MonarchAction _action;

    public override void _Ready()
    {
        ColonyArt.FramePanel(this); // parchment image frame + dark-ink theme (not Godot's default gray box)
        GetNode<Button>("VBox/Buttons/AcceptButton").Pressed += () => Resolve(accept: true);
        GetNode<Button>("VBox/Buttons/DeclineButton").Pressed += () => Resolve(accept: false);
    }

    /// <summary>
    /// Opens the modal for the pending <see cref="Game.PendingMonarchDemand"/> on <paramref name="game"/>. A no-op
    /// (stays hidden) when no demand is pending. <paramref name="onResolved"/> runs once the player answers, with a
    /// one-line outcome for the status bar.
    /// </summary>
    public void Open(Game game, Action<string> onResolved)
    {
        if (game.PendingMonarchDemand is not { } demand)
        {
            return; // nothing pending — leave the dialog hidden (no churn)
        }
        _game = game;
        _onResolved = onResolved;
        _action = demand.Action;
        GetNode<Label>("VBox/MonarchTitle").Text = TitleFor(demand);
        GetNode<Label>("VBox/MonarchInfo").Text = Describe(demand);
        GetNode<Button>("VBox/Buttons/AcceptButton").Text = AcceptLabelFor(demand);
        GetNode<Button>("VBox/Buttons/DeclineButton").Text = DeclineLabelFor(demand);
        Show();
    }

    private void Resolve(bool accept)
    {
        _game.RespondToMonarch(accept);
        Hide();
        _onResolved(OutcomeFor(_action, accept));
    }

    /// <summary>The King's banner for a demand (tax rise vs mercenary offer).</summary>
    private static string TitleFor(PendingMonarchDemand demand) =>
        IsTaxDemand(demand.Action) ? "His Majesty raises your taxes" : "His Majesty offers mercenaries";

    /// <summary>The King's parchment message describing the demand the player must answer.</summary>
    private string Describe(PendingMonarchDemand demand)
    {
        if (IsTaxDemand(demand.Action))
        {
            string goods = Naming.Humanize(_game.Ruleset.Goods(demand.GoodsId!).ShortName);
            string colony = ColonyName(demand.ColonyId);
            return $"The Crown demands a tax rise from {_game.TaxRate}% to {demand.TaxRaise}%, " +
                   $"citing your {goods} at {colony}.\n" +
                   "Yield to the new rate, or refuse and hold a tea party — dumping the goods and provoking a boycott?";
        }

        int units = demand.Offer!.Sum(e => e.Count);
        string force = units == 1 ? "1 veteran soldier" : $"{units} veteran soldiers";
        return $"The Crown offers you {force} for {demand.Price} gold, to be mustered on the docks of Europe.\n" +
               "Hire them, or decline — but a King whose generosity is spurned grows cold.";
    }

    /// <summary>Accept-button label per demand kind.</summary>
    private static string AcceptLabelFor(PendingMonarchDemand demand) =>
        IsTaxDemand(demand.Action) ? "Pay the tax" : "Hire them";

    /// <summary>Decline-button label per demand kind.</summary>
    private static string DeclineLabelFor(PendingMonarchDemand demand) =>
        IsTaxDemand(demand.Action) ? "Refuse (tea party)" : "Decline";

    /// <summary>A one-line status-bar outcome for the answered demand.</summary>
    private static string OutcomeFor(MonarchAction action, bool accept)
    {
        if (IsTaxDemand(action))
        {
            return accept
                ? "You yielded to the King's new tax rate."
                : "You refused — the colony holds a tea party.";
        }
        return accept
            ? "You hired the King's mercenaries."
            : "You declined the King's offer.";
    }

    private static bool IsTaxDemand(MonarchAction action) =>
        action is MonarchAction.RaiseTaxAct or MonarchAction.RaiseTaxWar;

    /// <summary>The display name of the colony holding the taxed goods (falls back to its id if it has since vanished).</summary>
    private string ColonyName(int colonyId) =>
        _game.Colonies.FirstOrDefault(c => c.Id == colonyId)?.Name ?? $"colony #{colonyId}";
}
