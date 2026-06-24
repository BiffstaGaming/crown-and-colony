using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.App;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The re-openable <b>message log</b>: the per-turn notices that flash up in the dismissible
/// <see cref="TurnMessagePanel"/> after each End Turn vanish once dismissed, so this panel keeps a history of them —
/// grouped under a "Turn N" header in the order they happened — that the player can re-open at any time from the HUD.
/// FreeCol keeps an equivalent persistent message history; ours is now <b>persisted across save/load</b> (save v59) via
/// <see cref="GameController"/>, so a reloaded game keeps its log.
/// <para>
/// Each logged notice carries a <see cref="GameLogic.App.MessageCategory"/> (combat / diplomacy / economy / natives /
/// monarch / colony), and the panel shows a row of per-category checkboxes; un-ticking a category hides every notice of
/// that kind and re-renders the log. The hidden set is persisted as a client option in <c>settings.cfg</c>
/// (FreeCol's per-type <c>guiShow…</c> message toggles + <c>MESSAGES_GROUP_BY</c>), so the choice survives across
/// sessions.
/// </para>
/// <para>
/// Pure presentation (ADR-006): it receives already-formatted English strings (the same ones the turn-message panel
/// shows) plus their turn number and category — the formatting and the underlying rules stay in
/// <see cref="GameController"/> / <c>GameLogic</c>. It owns no game state and never mutates the game; the hidden-set is a
/// client preference, not game state. Hidden by default; opened from the HUD.
/// </para>
/// </summary>
public partial class MessageLogPanel : PanelContainer
{
    /// <summary>One logged notice: its category and the already-formatted player-facing text.</summary>
    /// <param name="Category">The kind of event, for grouping/filtering.</param>
    /// <param name="Text">The already-formatted notice string.</param>
    public readonly record struct LogMessage(MessageCategory Category, string Text);

    /// <summary>One logged batch: the turn it happened on and the categorized, player-facing messages, in order.</summary>
    /// <param name="Turn">The game turn the messages were surfaced on.</param>
    /// <param name="Messages">The categorized notice records for that turn.</param>
    public readonly record struct Entry(int Turn, IReadOnlyList<LogMessage> Messages);

    // Display order + label for each category's filter checkbox (matches the MessageCategory enum order).
    private static readonly (MessageCategory Category, string Label)[] CategoryFilters =
    {
        (MessageCategory.Combat, "Combat"),
        (MessageCategory.Diplomacy, "Diplomacy"),
        (MessageCategory.Economy, "Economy"),
        (MessageCategory.Natives, "Natives"),
        (MessageCategory.Monarch, "Monarch"),
        (MessageCategory.Colony, "Colony"),
    };

    // Held between Open and a checkbox toggle so a filter change can re-render the same history without re-fetching it.
    private IReadOnlyList<Entry> _history = Array.Empty<Entry>();
    private ISet<MessageCategory> _hidden = new HashSet<MessageCategory>();
    private Action<MessageCategory, bool>? _onToggle;

    public override void _Ready()
    {
        GetNode<Button>("VBox/CloseButton").Pressed += Hide;
    }

    /// <summary>
    /// Rebuilds the log from the full history (newest turn first), applying the player's category filter, and shows the
    /// panel. Notices in a hidden category are omitted; a turn whose every notice is hidden drops its header too. An
    /// empty (or fully-filtered) history renders a single "no messages" line, so the player always gets feedback.
    /// </summary>
    /// <param name="history">Every logged turn's notices, oldest-first as accumulated; rendered newest-first here.</param>
    /// <param name="hidden">The categories the player has chosen to hide (a client option; mutated live as boxes toggle).</param>
    /// <param name="onToggle">Called when a category box is toggled (category, nowHidden) so the host can persist the choice.</param>
    public void Open(IReadOnlyList<Entry> history, ISet<MessageCategory> hidden, Action<MessageCategory, bool> onToggle)
    {
        _history = history;
        _hidden = hidden;
        _onToggle = onToggle;
        BuildFilterBar();
        Render();
        Show();
    }

    // Builds (once per Open) the category-filter checkbox row, ticking each box to match the current hidden set. A
    // toggle updates the hidden set, notifies the host (to persist), and re-renders the log live.
    private void BuildFilterBar()
    {
        var bar = GetNode<HBoxContainer>("VBox/FilterBar");
        foreach (Node child in bar.GetChildren())
        {
            bar.RemoveChild(child); child.QueueFree();
        }
        foreach ((MessageCategory category, string label) in CategoryFilters)
        {
            var box = new CheckBox
            {
                Name = $"Filter_{category}",
                Text = label,
                ButtonPressed = !_hidden.Contains(category), // ticked = shown
            };
            MessageCategory captured = category;
            box.Toggled += pressed =>
            {
                if (pressed)
                {
                    _hidden.Remove(captured);
                }
                else
                {
                    _hidden.Add(captured);
                }
                _onToggle?.Invoke(captured, !pressed); // !pressed == nowHidden
                Render();
            };
            bar.AddChild(box);
        }
    }

    // Rebuilds the scrollable log body from the held history, filtered by the hidden set. Signal-safe rebuild
    // (detach now, free deferred) — mirrors ColonyReportPanel / TurnMessagePanel.
    private void Render()
    {
        var dynamic = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            dynamic.RemoveChild(child); child.QueueFree();
        }

        bool anyShown = false;
        // Newest turn first (a player opening the log wants the latest events at the top).
        for (int i = _history.Count - 1; i >= 0; i--)
        {
            Entry entry = _history[i];
            List<LogMessage> visible = entry.Messages.Where(m => !_hidden.Contains(m.Category)).ToList();
            if (visible.Count == 0)
            {
                continue; // every notice this turn is filtered out — drop the empty header too
            }
            anyShown = true;
            dynamic.AddChild(new Label { Name = $"LogTurn_{entry.Turn}", Text = $"— Turn {entry.Turn} —" });
            foreach (LogMessage message in visible)
            {
                dynamic.AddChild(new Label
                {
                    Text = $"    {message.Text}",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                });
            }
        }

        if (!anyShown)
        {
            dynamic.AddChild(new Label
            {
                Name = "LogEmpty",
                Text = _history.Count == 0
                    ? "No messages yet — events from each turn will be collected here."
                    : "No messages match the current filter.",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
        }
    }
}
