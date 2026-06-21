using System;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The Find Settlement / locate-colony dialog (FreeCol's "Find Settlement", `86d3c9xkx`): lists the human player's
/// colonies; choosing one recenters the main camera on it. Pure presentation (ADR-006) — reads `Game.Colonies`
/// (filtered to the human) and forwards the chosen position to the host's recenter callback. Built programmatically
/// into the fixed <c>VBox/Dynamic</c> shell, like the other panels.
/// </summary>
public partial class FindSettlementPanel : PanelContainer
{
    /// <summary>Opens the dialog. <paramref name="onPick"/> receives the chosen colony's position; then the dialog hides.</summary>
    public void Open(Game game, Action<CrownAndColony.GameLogic.World.Position> onPick)
    {
        GetNode<Label>("VBox/FindTitle").Text = "Find settlement";
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            dynamic.RemoveChild(child); child.QueueFree(); // detach now (signal-safe), free deferred — avoids freed-while-emitting when a child button's handler drives the rebuild
        }

        var colonies = game.Colonies
            .Where(c => c.OwnerId == game.HumanPlayer.PlayerId)
            .OrderBy(c => c.Id)
            .ToList();

        if (colonies.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "You have no colonies yet.", HorizontalAlignment = HorizontalAlignment.Center });
        }
        foreach (Colony c in colonies)
        {
            CrownAndColony.GameLogic.World.Position pos = c.Position;
            var button = new Button { Name = $"Find_{c.Id}", Text = $"{c.Name}  (pop {c.Population})  ({pos.X},{pos.Y})" };
            button.Pressed += () =>
            {
                onPick(pos);
                Hide();
            };
            dynamic.AddChild(button);
        }

        Show();
    }
}
