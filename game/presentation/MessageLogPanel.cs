using System.Collections.Generic;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The re-openable <b>message log</b>: the per-turn notices that flash up in the dismissible
/// <see cref="TurnMessagePanel"/> after each End Turn vanish once dismissed, so this panel keeps an in-session
/// history of them — grouped under a "Turn N" header in the order they happened — that the player can re-open at any
/// time from the HUD. FreeCol keeps an equivalent persistent message history; ours is <b>session-only</b> (held in
/// <see cref="GameController"/>, never serialized — keeping the save format unchanged), so the log starts empty on a
/// fresh load. A follow-up may persist it (which would bump the save format).
/// <para>
/// Pure presentation (ADR-006): it receives already-formatted English strings (the same ones the turn-message panel
/// shows) plus their turn number — the formatting and the underlying rules stay in <see cref="GameController"/> /
/// <c>GameLogic</c>. It owns no game state and never mutates the game. Hidden by default; opened from the HUD.
/// </para>
/// </summary>
public partial class MessageLogPanel : PanelContainer
{
    /// <summary>One logged batch: the turn it happened on and the player-facing messages, in order.</summary>
    /// <param name="Turn">The game turn the messages were surfaced on.</param>
    /// <param name="Messages">The already-formatted notice strings for that turn.</param>
    public readonly record struct Entry(int Turn, IReadOnlyList<string> Messages);

    public override void _Ready()
    {
        GetNode<Button>("VBox/CloseButton").Pressed += Hide;
    }

    /// <summary>
    /// Rebuilds the log from the full in-session history (newest turn first) and shows the panel. An empty history
    /// renders a single "no messages yet" line, so the player always gets feedback when they open it.
    /// </summary>
    /// <param name="history">Every logged turn's notices, oldest-first as accumulated; rendered newest-first here.</param>
    public void Open(IReadOnlyList<Entry> history)
    {
        var dynamic = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            dynamic.RemoveChild(child); child.QueueFree(); // signal-safe rebuild (detach now, free deferred) — mirrors ColonyReportPanel
        }

        if (history.Count == 0)
        {
            dynamic.AddChild(new Label
            {
                Name = "LogEmpty",
                Text = "No messages yet — events from each turn will be collected here.",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            Show();
            return;
        }

        // Newest turn first (a player opening the log wants the latest events at the top).
        for (int i = history.Count - 1; i >= 0; i--)
        {
            Entry entry = history[i];
            dynamic.AddChild(new Label { Name = $"LogTurn_{entry.Turn}", Text = $"— Turn {entry.Turn} —" });
            foreach (string message in entry.Messages)
            {
                dynamic.AddChild(new Label
                {
                    Text = $"    {message}",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                });
            }
        }
        Show();
    }
}
