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
/// The trade-route management screen (<c>86d3c9rrd</c>): lists the human player's trade routes (each with its
/// FreeCol-style validation warnings, <c>86d3fpz3w</c>), creates a new <b>multi-stop ring</b> route via a dynamic
/// form — add/remove any number of stops, each a colony <b>or Europe</b> with a multi-good load selector
/// (<c>86d3fpz5g</c>) — assigns a carrier to a route, and deletes one. Like the other panels it only renders state
/// and forwards clicks to the Game oracles (ADR-006) — every rule (validation, the per-turn haul, save) lives in
/// GameLogic (<see cref="Game.CreateTradeRoute"/> / <see cref="Game.AssignTradeRoute"/> /
/// <see cref="Game.RemoveTradeRoute"/> / <see cref="Game.ValidateTradeRoute"/>); the per-turn hauling is automatic
/// (see [trade-routes]). Built programmatically into the fixed <c>VBox/Dynamic</c> shell, like <see cref="EuropePanel"/>.
/// </summary>
public partial class TradeRoutePanel : PanelContainer
{
    private Game _game = null!;
    private Action _onChange = () => { };

    /// <summary>
    /// The in-progress "New route" form (<c>86d3fpz5g</c>): an ordered list of draft stops the player is editing.
    /// It survives a <see cref="Rebuild"/> (Add/Remove mutate it, then re-render) and resets after a route is created.
    /// Defaults to two stops — the classic from→to shape — so the form opens identical to the old fixed editor.
    /// </summary>
    private readonly List<DraftStop> _draftStops = [new DraftStop(), new DraftStop()];

    /// <summary>One editable stop in the New-route form: a chosen location and the set of goods to load there.</summary>
    private sealed class DraftStop
    {
        /// <summary>The selected location dropdown index (0..colonies-1 = a colony; the last item = Europe).</summary>
        public int LocationIndex { get; set; }

        /// <summary>The goods ids ticked to load at this stop (FreeCol allows any number — multi-good).</summary>
        public HashSet<string> LoadGoods { get; } = [];
    }

    /// <summary>Opens the panel. <paramref name="onChange"/> runs after every action.</summary>
    public void Open(Game game, Action onChange)
    {
        ColonyArt.FramePanel(this, dense: true); // parchment image frame + dark-ink in-game theme (not Godot's default gray box)
        _game = game;
        _onChange = onChange;
        ResetDraft();
        Rebuild();
        Show();
    }

    /// <summary>
    /// Resets the New-route form to its default two-stop shape (From = the first colony, To = the second if there is
    /// one, else Europe — matching the classic from→to default the old fixed editor opened with). Called on open and
    /// after a route is created so the next route starts clean.
    /// </summary>
    private void ResetDraft()
    {
        _draftStops.Clear();
        int colonyCount = _game.Colonies.Count(c => c.OwnerId == _game.HumanPlayer.PlayerId);
        _draftStops.Add(new DraftStop { LocationIndex = 0 });
        _draftStops.Add(new DraftStop { LocationIndex = colonyCount >= 2 ? 1 : colonyCount }); // 2nd colony, else Europe
    }

    private void Changed()
    {
        _onChange();
        Rebuild();
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/TradeRouteTitle").Text = "Trade routes";
        var dynamic = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
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

            // Advisory warnings (86d3fpz3w): the engine's FreeCol-style verify() (Game.ValidateTradeRoute) returns
            // plain-English problems; render each in amber beneath this route's row. Warns, never blocks (ADR-006: we
            // only surface the read). Named per-route + per-index for testability.
            IReadOnlyList<TradeRouteWarning> warnings = _game.ValidateTradeRoute(route);
            for (int w = 0; w < warnings.Count; w++)
            {
                var warningLabel = new Label
                {
                    Name = w == 0 ? $"RouteWarning_{routeId}" : $"RouteWarning_{routeId}_{w}",
                    Text = $"⚠ {warnings[w].Message}",
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                };
                warningLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.65f, 0.15f)); // amber = advisory
                dynamic.AddChild(warningLabel);
            }
        }

        // — Create a route — a DYNAMIC, multi-stop form (86d3fpz5g). The player adds/removes any number of stops; each
        // stop picks a location (a colony OR Europe — Europe rides at the end of the dropdown) and ticks any number of
        // goods to load there (multi-good — FreeCol's stop cargo is a list). On submit we build the N-element
        // List<TradeRouteStop> and hand it to the existing CreateTradeRoute oracle (ADR-006: no rule logic here).
        // A route needs at least one own colony to be useful (the engine requires every non-Europe stop to be owned).
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

        var goods = _game.Ruleset.GoodsTypes.Where(g => _game.Market.IsTradeable(g.Id)).ToList();
        int europeItemIndex = colonies.Count; // Europe is the item after the last colony in every stop's location dropdown

        // Render one row per draft stop. Each row owns its location dropdown + per-good check buttons, bound back to the
        // _draftStops model so a Rebuild (after Add/Remove) re-renders the player's in-progress choices.
        for (int s = 0; s < _draftStops.Count; s++)
        {
            int stopIndex = s; // capture for the closures below
            DraftStop draft = _draftStops[s];
            draft.LocationIndex = Math.Clamp(draft.LocationIndex, 0, europeItemIndex); // a removed colony can't leave a stale index

            var stopBox = new VBoxContainer { Name = $"Stop_{stopIndex}" };

            var locOpt = new OptionButton { Name = $"StopLocation_{stopIndex}" };
            foreach (Colony c in colonies)
            {
                locOpt.AddItem(c.Name);
            }
            locOpt.AddItem("Europe");
            locOpt.Selected = draft.LocationIndex;
            locOpt.ItemSelected += index => _draftStops[stopIndex].LocationIndex = (int)index;

            var header = new HBoxContainer();
            header.AddChild(Grow(new Label { Text = $"Stop {stopIndex + 1}" }));
            header.AddChild(locOpt);
            // Remove this stop (kept enabled only while more than two stops remain — a route needs ≥ 2).
            if (_draftStops.Count > 2)
            {
                header.AddChild(ActionButton($"RemoveStop_{stopIndex}", "Remove stop", () =>
                {
                    _draftStops.RemoveAt(stopIndex);
                    Rebuild();
                }));
            }
            stopBox.AddChild(header);

            // Multi-good load selector: one CheckBox per tradeable good, ticked goods = this stop's LoadGoodsIds.
            var goodsBox = new HFlowContainer { Name = $"StopGoods_{stopIndex}" };
            goodsBox.AddChild(new Label { Text = "Load:" });
            foreach (GoodsType g in goods)
            {
                string goodsId = g.Id;
                var check = new CheckBox
                {
                    // Use the short name in the node id (Godot node names can't hold the dotted goods id; ShortName is
                    // the unique last segment, e.g. "sugar"). The full id still flows into LoadGoods/the engine below.
                    Name = $"Stop_{stopIndex}_Good_{g.ShortName}",
                    Text = Naming.Humanize(g.ShortName), // "tradeGoods" → "Trade Goods" (node name above stays raw)
                    ButtonPressed = draft.LoadGoods.Contains(goodsId),
                };
                check.Toggled += pressed =>
                {
                    if (pressed)
                    {
                        _draftStops[stopIndex].LoadGoods.Add(goodsId);
                    }
                    else
                    {
                        _draftStops[stopIndex].LoadGoods.Remove(goodsId);
                    }
                };
                goodsBox.AddChild(check);
            }
            stopBox.AddChild(goodsBox);
            dynamic.AddChild(stopBox);
        }

        dynamic.AddChild(ActionButton("AddStopButton", "Add stop", () =>
        {
            _draftStops.Add(new DraftStop { LocationIndex = 0 });
            Rebuild();
        }));

        dynamic.AddChild(ActionButton("CreateRoute", "Create route", () =>
        {
            if (_draftStops.Count < 2)
            {
                return; // a route needs at least two stops to move goods (the engine warns; we don't even submit)
            }
            List<TradeRouteStop> stops = _draftStops.Select(d =>
            {
                List<string> loadIds = d.LoadGoods.ToList();
                return d.LocationIndex == europeItemIndex
                    ? TradeRouteStop.Europe(loadIds)
                    : new TradeRouteStop(colonies[d.LocationIndex].Id, loadIds);
            }).ToList();
            _game.CreateTradeRoute(_game.HumanPlayer, $"Route {_game.HumanPlayer.TradeRoutes.Count + 1}", stops);
            ResetDraft();
            Changed();
        }));
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
