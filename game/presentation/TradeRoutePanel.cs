using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The trade-route management screen (<c>86d3c9rrd</c>): lists the human player's trade routes, creates a new route
/// (pick two own colonies + a good to load at the first stop), assigns a carrier to a route, and deletes one. Like
/// the other panels it only renders state and forwards clicks to the Game oracles (ADR-006) — every rule
/// (validation, the per-turn haul, save) lives in GameLogic (<see cref="Game.CreateTradeRoute"/> /
/// <see cref="Game.AssignTradeRoute"/> / <see cref="Game.RemoveTradeRoute"/>); the per-turn hauling is automatic
/// (see [trade-routes]). Built programmatically into the fixed <c>VBox/Dynamic</c> shell, like <see cref="EuropePanel"/>.
/// </summary>
public partial class TradeRoutePanel : PanelContainer
{
    private Game _game = null!;
    private Action _onChange = () => { };

    /// <summary>Opens the panel. <paramref name="onChange"/> runs after every action.</summary>
    public void Open(Game game, Action onChange)
    {
        _game = game;
        _onChange = onChange;
        Rebuild();
        Show();
    }

    private void Changed()
    {
        _onChange();
        Rebuild();
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/TradeRouteTitle").Text = "Trade routes";
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            dynamic.RemoveChild(child); child.QueueFree(); // detach now (Rebuild reuses names), free deferred — signal-safe
        }

        var colonies = _game.Colonies
            .Where(c => c.OwnerId == _game.HumanPlayer.PlayerId)
            .OrderBy(c => c.Id).ToList();
        var carriers = _game.PlayerUnits.Where(u => u.Type.IsCarrier).OrderBy(u => u.Id).ToList();

        // — Existing routes —
        dynamic.AddChild(SectionLabel("Your routes"));
        if (_game.HumanPlayer.TradeRoutes.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "(none yet)", HorizontalAlignment = HorizontalAlignment.Center });
        }
        foreach (TradeRoute route in _game.HumanPlayer.TradeRoutes)
        {
            int routeId = route.Id;
            var row = new HBoxContainer();
            row.AddChild(Grow(new Label { Text = $"{route.Name}: {DescribeStops(route)}" }));

            // Assign one of the player's carriers to this route.
            var assign = new OptionButton { Name = $"Assign_{routeId}" };
            assign.AddItem("Assign carrier…");
            var assignable = carriers.Where(u => _game.CheckAssignTradeRoute(u, routeId).Allowed).ToList();
            foreach (Unit carrier in assignable)
            {
                assign.AddItem($"{carrier.Type.ShortName} #{carrier.Id}");
            }
            assign.ItemSelected += index =>
            {
                if (index > 0)
                {
                    _game.AssignTradeRoute(assignable[(int)index - 1], routeId);
                    Changed();
                }
            };
            row.AddChild(assign);

            row.AddChild(ActionButton($"Delete_{routeId}", "Delete", () =>
            {
                _game.RemoveTradeRoute(_game.HumanPlayer, routeId);
                Changed();
            }));
            dynamic.AddChild(row);
        }

        // — Create a route — (needs at least two own colonies to move anything)
        dynamic.AddChild(SectionLabel("New route"));
        if (colonies.Count < 2)
        {
            dynamic.AddChild(new Label
            {
                Text = "Found at least two colonies to make a route.",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        var fromOpt = new OptionButton { Name = "FromColony" };
        var toOpt = new OptionButton { Name = "ToColony" };
        foreach (Colony c in colonies)
        {
            fromOpt.AddItem(c.Name);
            toOpt.AddItem(c.Name);
        }
        fromOpt.Selected = 0;
        toOpt.Selected = 1; // default the two ends to different colonies

        var loadOpt = new OptionButton { Name = "LoadGoods" };
        loadOpt.AddItem("Load nothing");
        var goods = _game.Ruleset.GoodsTypes.Where(g => _game.Market.IsTradeable(g.Id)).ToList();
        foreach (GoodsType g in goods)
        {
            loadOpt.AddItem(g.ShortName);
        }
        loadOpt.Selected = 0;

        dynamic.AddChild(LabeledRow("From", fromOpt));
        dynamic.AddChild(LabeledRow("To", toOpt));
        dynamic.AddChild(LabeledRow("Load at first stop", loadOpt));
        dynamic.AddChild(ActionButton("CreateRoute", "Create route", () =>
        {
            int fromIdx = fromOpt.Selected;
            int toIdx = toOpt.Selected;
            if (fromIdx < 0 || toIdx < 0 || fromIdx == toIdx)
            {
                return; // need two distinct colonies
            }
            List<string> loadIds = loadOpt.Selected > 0
                ? [goods[loadOpt.Selected - 1].Id]
                : [];
            List<TradeRouteStop> stops =
            [
                new(colonies[fromIdx].Id, loadIds),
                new(colonies[toIdx].Id, []),
            ];
            _game.CreateTradeRoute(_game.HumanPlayer, $"Route {_game.HumanPlayer.TradeRoutes.Count + 1}", stops);
            Changed();
        }));
    }

    private string DescribeStops(TradeRoute route) =>
        string.Join(" → ", route.Stops.Select(s =>
            _game.Colonies.FirstOrDefault(c => c.Id == s.ColonyId)?.Name ?? "?"));

    private static HBoxContainer LabeledRow(string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddChild(Grow(new Label { Text = label }));
        row.AddChild(control);
        return row;
    }

    private static Label Grow(Label label)
    {
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = $"— {text} —",
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static Button ActionButton(string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        return button;
    }
}
