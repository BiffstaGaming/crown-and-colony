using System;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The native tribute-demand modal: when a brave demands tribute of one of the human's colonies during the AI
/// phase (<see cref="Game.PendingDemand"/>), this panel asks the player to <b>pay</b>
/// (<see cref="Game.AcceptPendingDemand"/>) or <b>refuse</b> (<see cref="Game.RefusePendingDemand"/>). Presentation
/// only (ADR-006): the rules — what was demanded, the transfer, the alarm change — all live in <c>GameLogic</c>;
/// this panel renders the demand and forwards the choice, then reports a one-line outcome through
/// <see cref="Open"/>'s callback. An unanswered demand is auto-refused by the next End Turn (the engine backstops it).
/// </summary>
public partial class NativeDemandPanel : PanelContainer
{
    private Game _game = null!;
    private Action<string> _onResolved = _ => { };

    public override void _Ready()
    {
        ColonyArt.FramePanel(this); // parchment image frame + dark-ink theme (not Godot's transparent default)
        GetNode<Button>("VBox/Buttons/PayButton").Pressed += Pay;
        GetNode<Button>("VBox/Buttons/RefuseButton").Pressed += Refuse;
    }

    /// <summary>
    /// Opens the modal for a pending <paramref name="demand"/>. <paramref name="onResolved"/> runs once the player
    /// answers, with a one-line outcome for the status bar.
    /// </summary>
    public void Open(Game game, NativeDemand demand, Action<string> onResolved)
    {
        _game = game;
        _onResolved = onResolved;
        GetNode<Label>("VBox/DemandTitle").Text = $"The {NationLabel(demand.DemandingNationId)} demand tribute";
        GetNode<Label>("VBox/DemandInfo").Text = Describe(demand);
        Show();
    }

    private void Pay()
    {
        bool paid = _game.AcceptPendingDemand();
        Resolve(paid ? "You paid the tribute — the tribe is calmer for now." : "There was nothing left to give.");
    }

    private void Refuse()
    {
        _game.RefusePendingDemand();
        Resolve("You refused their demand — the warriors may strike.");
    }

    private void Resolve(string outcome)
    {
        Hide();
        _onResolved(outcome);
    }

    private string Describe(NativeDemand demand)
    {
        string what = demand.GoodsId is null
            ? $"{demand.Amount} gold"
            : $"{demand.Amount} {_game.Ruleset.Goods(demand.GoodsId).ShortName}";
        return $"They demand {what} from {demand.ColonyName}.\nPay tribute, or refuse and risk a raid?";
    }

    /// <summary>The display label for a nation id (e.g. <c>model.nation.apache</c> → "Apache").</summary>
    private static string NationLabel(string nationId)
    {
        string shortName = nationId[(nationId.LastIndexOf('.') + 1)..];
        return char.ToUpperInvariant(shortName[0]) + shortName[1..];
    }
}
