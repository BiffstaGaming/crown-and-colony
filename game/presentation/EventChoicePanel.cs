using System;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The historical-event choice modal (WS1.1b, the Australian 1788–1901 event system): when a human is offered a
/// <b>multi-option</b> historical event (<see cref="Game.PendingEventOffer"/> — a genuine dilemma such as the Gold Rush,
/// Eureka Stockade, or the Shearers' Strike), this panel shows the event's title + prompt and a button per choice.
/// Pressing one calls <see cref="Game.ChooseEventOption"/>; the chosen option's effects apply and the offer clears.
/// An ignored offer auto-resolves to the heaviest-weight option at the next end of turn (engine-side), so the panel
/// never blocks progress.
/// <para>
/// Presentation only (ADR-006): the event rules — eligibility, the weighted draw, the effects — live in
/// <c>GameLogic</c>; this panel forwards the chosen option id and reports the outcome through <see cref="Open"/>'s
/// callback. Single-option events auto-resolve without a prompt, so they never reach this panel. Classic ships no
/// historical events (never sets a pending offer), so this panel never shows in a classic game. Built on the shared
/// parchment frame; hidden by default. Modelled on <see cref="EmigrationChoicePanel"/>.
/// </para>
/// </summary>
public partial class EventChoicePanel : PanelContainer
{
    private Game _game = null!;
    private Action<string> _onResolved = _ => { };

    /// <summary>
    /// Opens the modal for the pending historical-event choice over <paramref name="game"/>. <paramref name="onResolved"/>
    /// receives the chosen option's one-line outcome (its <c>recordHistory</c> text) for the message log. A no-op (stays
    /// hidden) when no event choice is pending or the offered event is not in the ruleset.
    /// </summary>
    public void Open(Game game, Action<string> onResolved)
    {
        ColonyArt.FramePanel(this); // parchment image frame + dark-ink theme (not Godot's transparent default)
        _game = game;
        _onResolved = onResolved;
        if (game.PendingEventOffer is not { } offer || game.Ruleset.HistoricalEvent(offer.EventId) is not { } ev)
        {
            Hide();
            return;
        }

        GetNode<Label>("VBox/EventTitle").Text = ev.DisplayName;
        var prompt = GetNode<Label>("VBox/EventPrompt");
        prompt.Text = ev.Prompt ?? string.Empty;
        prompt.Visible = !string.IsNullOrWhiteSpace(ev.Prompt);

        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            dynamic.RemoveChild(child); child.QueueFree(); // detach now (signal-safe), free deferred — a button's handler drives the resolve
        }

        foreach (string optionId in offer.OptionIds)
        {
            string chosen = optionId; // capture for the closure
            var button = new Button
            {
                Name = $"Choose_{optionId}",
                Text = ev.Option(optionId)?.DisplayLabel ?? Naming.Humanize(optionId),
            };
            button.Pressed += () => Resolve(chosen);
            dynamic.AddChild(button);
        }

        Show();
    }

    private void Resolve(string optionId)
    {
        // The offer is transient — it can auto-resolve on End Turn while this popup is still up. If it is already gone,
        // there is nothing to choose (ChooseEventOption would throw), so just drop the stale popup.
        if (_game.PendingEventOffer is not { } offer || _game.Ruleset.HistoricalEvent(offer.EventId) is not { } ev)
        {
            Hide();
            return;
        }

        // Capture the outcome line (the chosen option's recordHistory text) BEFORE resolving — ChooseEventOption clears
        // the pending offer.
        string outcome = ev.Option(optionId)?.Effects.FirstOrDefault(e => e.Kind == EventEffectKind.RecordHistory)?.Text ?? string.Empty;

        _game.ChooseEventOption(optionId);
        Hide();
        _onResolved(outcome);
    }
}
