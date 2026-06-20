using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The end-of-turn message panel (FreeCol's <c>ReportTurnPanel</c>, `86d3c9x53`): after the AI phase, the human
/// may have suffered or received a batch of events (native raids, colony pillages, captured colonies, custom-house
/// sales, …). Rather than concatenating them into the one-line status bar, <see cref="GameController"/> formats each
/// into a player-facing string and hands the list here; this panel lists one row per message in a scrollable column
/// and is dismissed by an <b>OK</b> / close button.
/// <para>
/// Pure presentation (ADR-006): it receives already-formatted strings — the formatting (and the underlying rules)
/// stay in <see cref="GameController"/>/<c>GameLogic</c>. It owns no game state and never mutates the game. Hidden by
/// default; only shown when <see cref="Open"/> is given a non-empty list, so the visual goldens are unaffected.
/// </para>
/// </summary>
public partial class TurnMessagePanel : PanelContainer
{
    public override void _Ready()
    {
        GetNode<Button>("VBox/OkButton").Pressed += Hide;
    }

    /// <summary>
    /// Shows the panel with one row per message. A null/empty list is a no-op (the panel stays hidden) so callers can
    /// route every end-of-turn batch through here without guarding the empty case at the call site.
    /// </summary>
    public void Open(IEnumerable<string> messages)
    {
        List<string> list = messages?.ToList() ?? new List<string>();
        var dynamic = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free();
        }

        if (list.Count == 0)
        {
            Hide();
            return;
        }

        // One named row per message so the L3 test can find them; autowrap keeps long notices inside the panel.
        for (int i = 0; i < list.Count; i++)
        {
            dynamic.AddChild(new Label
            {
                Name = $"Message_{i}",
                Text = list[i],
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }

        Show();
    }
}
