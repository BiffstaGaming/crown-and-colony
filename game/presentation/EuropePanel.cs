using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The Europe screen (Phase 4 slice 6): the recruitment dock, the treasury, the
/// immigration clock, and the ships and colonists currently in Europe — with the
/// actions that move them (recruit, board, put on the dock, sail home). Like the
/// colony screen it only renders state and forwards clicks to the Game oracles
/// (ADR-006); all rules live in GameLogic.
/// </summary>
public partial class EuropePanel : PanelContainer
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

    private static string Short(string id) => id[(id.LastIndexOf('.') + 1)..];

    private void Rebuild()
    {
        GetNode<Label>("VBox/EuropeTitle").Text = "Europe";
        GetNode<Label>("VBox/EuropeInfo").Text =
            $"Treasury: {_game.Gold} gold\n" +
            $"Immigration: {_game.Immigration}/{_game.ImmigrationRequired}" +
            $"   |   Next recruit: {_game.RecruitPrice} gold";
        BuildDynamicSections();
    }

    private void BuildDynamicSections()
    {
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free(); // immediate — Rebuild reuses names
        }

        var ships = _game.UnitsInEurope.Where(u => u.Type.IsCarrier && !u.IsAboard).ToList();
        var onDock = _game.UnitsInEurope.Where(u => u.Type.IsPerson && !u.IsAboard).ToList();

        // — Recruitment dock —
        dynamic.AddChild(SectionLabel("Recruitment dock"));
        int price = _game.RecruitPrice;
        IReadOnlyList<string> dock = _game.RecruitDock;
        for (int slot = 0; slot < dock.Count; slot++)
        {
            var row = new HBoxContainer();
            row.AddChild(Grow(new Label { Text = Short(dock[slot]) }));
            int s = slot;
            if (_game.CheckRecruit(slot).Allowed)
            {
                row.AddChild(ActionButton($"Recruit_{slot}", $"Recruit ({price})", () =>
                {
                    _game.Recruit(s);
                    Changed();
                }));
            }
            else
            {
                row.AddChild(new Label { Text = $"{price} gold" });
            }
            dynamic.AddChild(row);
        }

        // — Ships in port (with their passengers) —
        dynamic.AddChild(SectionLabel("Ships in port"));
        if (ships.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "(none)", HorizontalAlignment = HorizontalAlignment.Center });
        }
        foreach (Unit ship in ships)
        {
            var row = new HBoxContainer();
            row.AddChild(Grow(new Label
            {
                Text = $"{ship.Type.ShortName} — hold {_game.CargoSlotsUsed(ship)}/{_game.CargoCapacity(ship)}",
            }));
            Unit sh = ship;
            row.AddChild(ActionButton($"Sail_{ship.Id}", "Sail to New World", () =>
            {
                _game.SailToNewWorld(sh);
                Changed();
            }));
            dynamic.AddChild(row);

            foreach (Unit passenger in _game.Passengers(ship))
            {
                var prow = new HBoxContainer();
                prow.AddChild(Grow(new Label { Text = $"    • {passenger.Type.ShortName} (aboard)" }));
                Unit p = passenger;
                prow.AddChild(ActionButton($"Unload_{passenger.Id}", "Put on dock", () =>
                {
                    _game.DisembarkToDock(p);
                    Changed();
                }));
                dynamic.AddChild(prow);
            }

            // Cargo in the hold: sell each goods stack to the market.
            foreach ((string goodsId, int amount) in ship.Cargo.OrderBy(kv => kv.Key))
            {
                var crow = new HBoxContainer();
                crow.AddChild(Grow(new Label { Text = $"    {Short(goodsId)} {amount}" }));
                if (_game.Market.IsTradeable(goodsId))
                {
                    string g = goodsId;
                    int amt = amount;
                    crow.AddChild(ActionButton($"Sell_{ship.Id}_{Short(goodsId)}",
                        $"Sell ({_game.Market.BidPrice(goodsId)})", () =>
                        {
                            _game.SellShipCargo(sh, g, amt);
                            Changed();
                        }));
                }
                dynamic.AddChild(crow);
            }

            // Buy goods into this ship's hold (100 at a time, at the ask price).
            var buyOptions = new OptionButton { Name = $"Buy_{ship.Id}" };
            buyOptions.AddItem("Buy goods…");
            var buyable = _game.Ruleset.GoodsTypes.Where(g => _game.Market.IsTradeable(g.Id)).ToList();
            foreach (GoodsType g in buyable)
            {
                buyOptions.AddItem($"{g.ShortName} ×100 ({_game.Market.AskPrice(g.Id) * 100})");
            }
            buyOptions.ItemSelected += index =>
            {
                if (index > 0 && _game.CheckBuyEuropeGoods(sh, buyable[(int)index - 1].Id, 100).Allowed)
                {
                    _game.BuyEuropeGoods(sh, buyable[(int)index - 1].Id, 100);
                    Changed();
                }
            };
            dynamic.AddChild(buyOptions);
        }

        // — Colonists waiting on the dock —
        dynamic.AddChild(SectionLabel("On the dock"));
        if (onDock.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "(none)", HorizontalAlignment = HorizontalAlignment.Center });
        }
        foreach (Unit person in onDock)
        {
            var row = new HBoxContainer();
            row.AddChild(Grow(new Label { Text = person.Type.ShortName }));
            foreach (Unit ship in ships.Where(s => _game.CheckBoard(person, s).Allowed))
            {
                Unit pe = person;
                Unit sh = ship;
                row.AddChild(ActionButton($"Board_{person.Id}_{ship.Id}", $"Board {ship.Type.ShortName}", () =>
                {
                    _game.Board(pe, sh);
                    Changed();
                }));
            }
            dynamic.AddChild(row);
        }
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
