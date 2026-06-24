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
/// (a <em>from</em> colony, a <em>to</em> stop that may be another colony <b>or Europe</b>, and a good to load at each
/// stop), assigns a carrier to a route, and deletes one. Like
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

        // — Create a route — (needs at least one own colony: the From stop; the To stop can be Europe, always available)
        dynamic.AddChild(SectionLabel("New route"));
        if (colonies.Count < 1)
        {
            dynamic.AddChild(new Label
            {
                Text = "Found at least one colony to make a route (you can trade it with Europe).",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return;
        }

        // The "To" stop may be a colony OR Europe. Europe rides at the end of the dropdown (index == colonies.Count),
        // so a docked ship sells the colony's surplus there (and buys the "load at second stop" good back). FROM is always
        // a colony (you load a colony's goods to haul out); the engine accepts a Europe stop anywhere (TradeRouteStop.Europe).
        var fromOpt = new OptionButton { Name = "FromColony" };
        var toOpt = new OptionButton { Name = "ToColony" };
        foreach (Colony c in colonies)
        {
            fromOpt.AddItem(c.Name);
            toOpt.AddItem(c.Name);
        }
        int europeItemIndex = colonies.Count; // Europe is the item after the last colony in the To dropdown
        toOpt.AddItem("Europe");
        fromOpt.Selected = 0;
        toOpt.Selected = 1; // default the two ends to different colonies

        // Goods to load at each stop (Load nothing = deliver/sell everything the carrier holds). The first selector
        // loads at the FROM colony (haul a colony's surplus out); the second loads at the TO stop — at a colony that's
        // a pick-up, at Europe that's a BUY (e.g. tools/muskets back home).
        var goods = _game.Ruleset.GoodsTypes.Where(g => _game.Market.IsTradeable(g.Id)).ToList();
        var loadOpt = BuildGoodsOption("LoadGoods", goods);
        var loadToOpt = BuildGoodsOption("LoadGoodsTo", goods);

        dynamic.AddChild(LabeledRow("From", fromOpt));
        dynamic.AddChild(LabeledRow("To", toOpt));
        dynamic.AddChild(LabeledRow("Load at first stop", loadOpt));
        dynamic.AddChild(LabeledRow("Load at second stop", loadToOpt));
        dynamic.AddChild(ActionButton("CreateRoute", "Create route", () =>
        {
            int fromIdx = fromOpt.Selected;
            int toIdx = toOpt.Selected;
            bool toEurope = toIdx == europeItemIndex;
            if (fromIdx < 0 || toIdx < 0 || (!toEurope && fromIdx == toIdx))
            {
                return; // need two distinct ends (a colony vs Europe is always distinct)
            }
            List<string> loadIds = loadOpt.Selected > 0 ? [goods[loadOpt.Selected - 1].Id] : [];
            List<string> loadToIds = loadToOpt.Selected > 0 ? [goods[loadToOpt.Selected - 1].Id] : [];
            TradeRouteStop secondStop = toEurope
                ? TradeRouteStop.Europe(loadToIds)
                : new TradeRouteStop(colonies[toIdx].Id, loadToIds);
            List<TradeRouteStop> stops =
            [
                new(colonies[fromIdx].Id, loadIds),
                secondStop,
            ];
            _game.CreateTradeRoute(_game.HumanPlayer, $"Route {_game.HumanPlayer.TradeRoutes.Count + 1}", stops);
            Changed();
        }));
    }

    /// <summary>Builds a "Load nothing" + one-item-per-tradeable-good dropdown (index 0 = load nothing, index i = <paramref name="goods"/>[i-1]).</summary>
    private static OptionButton BuildGoodsOption(string name, List<GoodsType> goods)
    {
        var opt = new OptionButton { Name = name };
        opt.AddItem("Load nothing");
        foreach (GoodsType g in goods)
        {
            opt.AddItem(g.ShortName);
        }
        opt.Selected = 0;
        return opt;
    }

    /// <summary>
    /// Renders a route's stops as "A → B → …". A <b>Europe</b> stop (<see cref="TradeRouteStop.IsEurope"/>) shows as
    /// "Europe"; a colony stop shows the colony's name (or "?" if its colony has since vanished).
    /// </summary>
    private string DescribeStops(TradeRoute route) =>
        string.Join(" → ", route.Stops.Select(s =>
            s.IsEurope
                ? "Europe"
                : _game.Colonies.FirstOrDefault(c => c.Id == s.ColonyId)?.Name ?? "?"));

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
