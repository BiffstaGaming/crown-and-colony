using System;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The strange-mounds decision modal (`86d3cqqu5`): when a human explorer steps onto a strange-mounds Lost-City
/// rumour (<see cref="Game.PendingMounds"/>), this panel asks the player to <b>investigate</b> the mounds (a
/// re-rolled outcome) or <b>leave them be</b>. Presentation only (ADR-006): the rules — the outcome roll, the
/// reward/penalty — live in <c>GameLogic</c> (<see cref="Game.ResolvePendingMounds"/>); this panel forwards the
/// choice and reports the one-line outcome through <see cref="Open"/>'s callback.
/// </summary>
public partial class MoundsDecisionPanel : PanelContainer
{
    private Game _game = null!;
    private Action<string> _onResolved = _ => { };

    public override void _Ready()
    {
        ColonyArt.FramePanel(this); // parchment image frame + dark-ink theme (not Godot's transparent default)
        GetNode<Button>("VBox/Buttons/InvestigateButton").Pressed += () => Resolve(investigate: true);
        GetNode<Button>("VBox/Buttons/DeclineButton").Pressed += () => Resolve(investigate: false);
    }

    /// <summary>Opens the modal for the pending strange-mounds prompt. <paramref name="onResolved"/> gets the one-line outcome.</summary>
    public void Open(Game game, Action<string> onResolved)
    {
        _game = game;
        _onResolved = onResolved;
        GetNode<Label>("VBox/MoundsTitle").Text = "Strange mounds";
        GetNode<Label>("VBox/MoundsInfo").Text =
            "Your explorer has come upon strange mounds on native land.\nInvestigate them, or leave them undisturbed?";
        Show();
    }

    private void Resolve(bool investigate)
    {
        string outcome = _game.ResolvePendingMounds(investigate);
        Hide();
        _onResolved(outcome);
    }
}
