using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The Europe screen, laid out as a zoned harbour (FreeCol/Colonization style) rather than a flat text list: a header
/// (treasury + immigration clock + next-recruit price), a recruit/train/purchase zone, a goods-market zone, a
/// ships-in-port zone (each ship a card with its hold slots drawn out), a sail-to-the-New-World zone, and an
/// on-the-docks zone (waiting colonists + carried-home treasure trains). Like the colony screen it only renders state
/// and forwards button clicks to the <see cref="Game"/> oracles (ADR-006); all rules live in GameLogic. Phase 1 is
/// click/button interactions only — drag-and-drop comes later. Built programmatically per open/refresh; the rebuild is
/// deferred (see <see cref="Changed"/>) so a control is never freed mid-signal.
/// </summary>
public partial class EuropePanel : PanelContainer
{
    private Game _game = null!;
    private Action _onChange = () => { };

    /// <summary>Goods are bought into a ship's hold one slot (100 units) at a time, at the market ask — FreeCol's per-stack lot.</summary>
    private const int GoodsLot = 100;

    private static readonly Color Negative = new(0.9f, 0.3f, 0.25f);
    private static readonly Color Muted = new(0.62f, 0.55f, 0.42f);

    /// <summary>Opens the panel. <paramref name="onChange"/> runs after every action.</summary>
    public void Open(Game game, Action onChange)
    {
        _game = game;
        _onChange = onChange;
        EnsureOpaqueBackground();
        Theme = ColonyTheme.Get(); // share the colony screen's cohesive parchment/wood styling
        Rebuild();
        Show();
    }

    /// <summary>
    /// Signals a finished action. The rebuild is <b>deferred</b> so a control is never freed inside its own signal
    /// callback (freeing an <see cref="OptionButton"/> mid-<c>ItemSelected</c>, popup still closing, crashes Godot). The
    /// game-state change has already happened synchronously.
    /// </summary>
    private void Changed() => Callable.From(ApplyChange).CallDeferred();

    private void ApplyChange()
    {
        _onChange();
        Rebuild();
    }

    private static string Short(string id) => id[(id.LastIndexOf('.') + 1)..];

    // ── Opaque background (shared with ColonyPanel's parchment skin) ────────────────────────────────────────────

    private static StyleBox? _panelBackground;

    /// <summary>
    /// Gives the panel an opaque background so the (dimmed) map behind the UI layer never shows through. Prefers
    /// FreeCol's tiled brown parchment skin; falls back to a warm solid fill if the asset is absent (so it is opaque in
    /// CI even before the parchment is imported). Built once and shared with the colony screen.
    /// </summary>
    private void EnsureOpaqueBackground()
    {
        _panelBackground ??= BuildPanelBackground();
        AddThemeStyleboxOverride("panel", _panelBackground);
    }

    private static StyleBox BuildPanelBackground()
    {
        if (ColonyArt.PanelParchment() is { } parchment)
        {
            var skin = new StyleBoxTexture
            {
                Texture = parchment,
                AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
                AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
            };
            skin.SetContentMarginAll(20);
            return skin;
        }
        var flat = new StyleBoxFlat { BgColor = new Color(0.18f, 0.12f, 0.07f) };
        flat.SetContentMarginAll(20);
        return flat;
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/EuropeTitle").Text = "Europe";
        GetNode<Label>("VBox/EuropeInfo").Text =
            $"Treasury: {_game.Gold} gold   |   Next recruit: {_game.RecruitPrice} gold";

        var root = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in root.GetChildren())
        {
            root.RemoveChild(child); child.QueueFree(); // detach now (signal-safe), free deferred — avoids freed-while-emitting when a child button drives the rebuild
        }

        // A centred content card (ShrinkCenter) so on wide windows the zones sit centred with balanced margins rather
        // than spraying across the width; the Scroll's horizontal-auto mode (main.tscn) handles windows narrower than it.
        var card = new VBoxContainer { Name = "ContentCard", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        card.AddThemeConstantOverride("separation", 12);

        var ships = _game.UnitsInEurope.Where(u => u.Type.IsCarrier && !u.IsAboard).ToList();
        var onDock = _game.UnitsInEurope.Where(u => u.Type.IsPerson && !u.IsAboard).ToList();
        // Treasure trains carried home and put on the dock are neither carriers nor persons, so they fall through both
        // lists above — they get their own cash-in entry in the docks zone or the carry-it-home play dead-ends.
        var treasureOnDock = _game.UnitsInEurope.Where(u => u.Type.CarryTreasure && !u.IsAboard).ToList();

        card.AddChild(ImmigrationZone());

        // The two market/recruitment columns sit side by side on a wide screen, stacking only on a narrow one.
        var topRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        topRow.AddThemeConstantOverride("separation", 16);
        topRow.AddChild(RecruitTrainPurchaseZone());
        topRow.AddChild(GoodsMarketZone(ships));
        card.AddChild(topRow);

        card.AddChild(SectionLabel("Ships in port"));
        card.AddChild(ShipsInPortZone(ships));

        card.AddChild(SectionLabel("Sail to the New World"));
        card.AddChild(SailZone(ships));

        card.AddChild(SectionLabel("On the docks"));
        card.AddChild(DocksZone(onDock, ships, treasureOnDock));

        root.AddChild(card);
    }

    // ── Zone 1: the immigration clock (a progress bar) ──────────────────────────────────────────────────────────

    /// <summary>
    /// The immigration clock as a progress bar — <see cref="Game.Immigration"/> crosses out of
    /// <see cref="Game.ImmigrationRequired"/> "crosses" to deliver the next free emigrant (see immigration.md). Read-only;
    /// the recruitment dock alongside is where the player pays to recruit early.
    /// </summary>
    private Control ImmigrationZone()
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        box.AddChild(new Label
        {
            Text = $"Immigration: {_game.Immigration}/{_game.ImmigrationRequired} crosses",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        int required = Math.Max(1, _game.ImmigrationRequired); // never divide by zero on a degenerate fixture
        box.AddChild(new ProgressBar
        {
            Name = "ImmigrationBar",
            MinValue = 0,
            MaxValue = required,
            Value = Math.Clamp(_game.Immigration, 0, required),
            CustomMinimumSize = new Vector2(360, 16),
            ShowPercentage = false,
        });
        return box;
    }

    // ── Zone 2: recruit / train / purchase ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The people-and-units column: the three recruitment-dock slots (each with its current price), a <b>Train</b> list of
    /// priced specialists (<see cref="Game.UnitTypesTrainedInEurope"/> → <see cref="Game.TrainUnit(string)"/>, flat price),
    /// and a <b>Purchase</b> list of ships/artillery (<see cref="Game.UnitTypesPurchasedInEurope"/> →
    /// <see cref="Game.BuyUnit(string)"/>, artillery escalating). Every action button is disabled (greyed) when its
    /// matching Check oracle refuses — chiefly when the player can't afford it (ADR-006: the panel only reads the gate).
    /// </summary>
    private Control RecruitTrainPurchaseZone()
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0), SizeFlagsHorizontal = SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 6);

        col.AddChild(SectionLabel("Recruitment dock"));
        int price = _game.RecruitPrice;
        IReadOnlyList<string> dock = _game.RecruitDock;
        for (int slot = 0; slot < dock.Count; slot++)
        {
            int s = slot;
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(Grow(new Label { Text = Short(dock[slot]) }));
            row.AddChild(GatedButton($"Recruit_{slot}", $"Recruit ({price})", _game.CheckRecruit(slot).Allowed, () =>
            {
                if (_game.CheckRecruit(s).Allowed) { _game.Recruit(s); }
                Changed();
            }));
            col.AddChild(row);
        }

        // — Train specialists (flat price) —
        IReadOnlyList<UnitType> trainable = _game.UnitTypesTrainedInEurope();
        if (trainable.Count > 0)
        {
            col.AddChild(SectionLabel("Train a specialist"));
            foreach (UnitType type in trainable)
            {
                string id = type.Id;
                int p = _game.EuropeUnitPrice(id);
                var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddChild(Grow(new Label { Text = type.ShortName }));
                row.AddChild(GatedButton($"Train_{type.ShortName}", $"Train ({p})", _game.CheckTrain(id).Allowed, () =>
                {
                    if (_game.CheckTrain(id).Allowed) { _game.TrainUnit(id); }
                    Changed();
                }));
                col.AddChild(row);
            }
        }

        // — Purchase ships / artillery (artillery escalates) —
        IReadOnlyList<UnitType> purchasable = _game.UnitTypesPurchasedInEurope();
        if (purchasable.Count > 0)
        {
            col.AddChild(SectionLabel("Purchase"));
            foreach (UnitType type in purchasable)
            {
                string id = type.Id;
                int p = _game.EuropeUnitPrice(id);
                var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                row.AddChild(Grow(new Label { Text = type.ShortName }));
                row.AddChild(GatedButton($"Purchase_{type.ShortName}", $"Buy ({p})", _game.CheckBuyUnit(id).Allowed, () =>
                {
                    if (_game.CheckBuyUnit(id).Allowed) { _game.BuyUnit(id); }
                    Changed();
                }));
                col.AddChild(row);
            }
        }

        return col;
    }

    // ── Zone 3: the goods market ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The European goods market: every tradeable good with its live <b>bid</b> (sell) and <b>ask</b> (buy) prices,
    /// boycott state (<see cref="Market.CanTrade"/> false / <see cref="Market.Arrears"/> &gt; 0), and — for the chosen
    /// in-port ship — a Buy button (loads a 100-lot via <see cref="Game.BuyEuropeGoods"/>) and one Sell button per goods
    /// stack actually aboard (<see cref="Game.SellShipCargo"/>). Trading needs a ship in port to hold the goods; without
    /// one the market is shown read-only (prices only). A repairing ship can't take on cargo, so its buys are gated off.
    /// </summary>
    private Control GoodsMarketZone(IReadOnlyList<Unit> ships)
    {
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0), SizeFlagsHorizontal = SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 4);
        col.AddChild(SectionLabel("Goods market"));

        // Trade is per-ship (the goods must go into a hold). Pick the first tradeable ship in port; a repairing ship
        // can't trade. Phase 2 will let the player choose which ship; Phase 1 uses the first available one.
        Unit? tradeShip = ships.FirstOrDefault(s => !s.IsUnderRepair);
        col.AddChild(new Label
        {
            Text = tradeShip is null
                ? "(no ship in port — prices only)"
                : $"Trading via {tradeShip.Type.ShortName} (hold {_game.CargoSlotsFree(tradeShip)} free)",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (GoodsType good in _game.Ruleset.GoodsTypes.Where(g => _game.Market.IsTradeable(g.Id)))
        {
            string id = good.Id;
            bool boycott = !_game.Market.CanTrade(id);

            var name = new Label { Text = good.ShortName, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            if (boycott)
            {
                name.AddThemeColorOverride("font_color", Negative);
                name.TooltipText = $"Boycotted — pay {_game.Market.Arrears(id)} gold in back taxes to trade again.";
            }
            grid.AddChild(name);

            grid.AddChild(new Label { Text = boycott ? "boycott" : $"buy {_game.Market.AskPrice(id)}", HorizontalAlignment = HorizontalAlignment.Right });

            // Buy a 100-lot into the trade ship's hold (gated on affordability + room + not boycotted).
            if (tradeShip is { } buyShip && !boycott && _game.CheckBuyEuropeGoods(buyShip, id, GoodsLot).Allowed)
            {
                grid.AddChild(ActionButton($"BuyGood_{good.ShortName}", $"Buy ({_game.Market.BuyCost(id, GoodsLot)})", () =>
                {
                    if (_game.CheckBuyEuropeGoods(buyShip, id, GoodsLot).Allowed)
                    {
                        _game.BuyEuropeGoods(buyShip, id, GoodsLot);
                    }
                    Changed();
                }));
            }
            else
            {
                grid.AddChild(MutedLabel(boycott ? $"sell {_game.Market.BidPrice(id)}" : $"sell {_game.Market.BidPrice(id)}"));
            }

            // Sell the lot this ship is carrying of this good (one button per stack actually aboard).
            int aboard = tradeShip?.CargoOf(id) ?? 0;
            if (tradeShip is { } sellShip && aboard > 0 && !boycott)
            {
                grid.AddChild(ActionButton($"Sell_{sellShip.Id}_{good.ShortName}", $"Sell {aboard} ({_game.Market.BidPrice(id) * aboard})", () =>
                {
                    _game.SellShipCargo(sellShip, id, aboard);
                    Changed();
                }));
            }
            else
            {
                grid.AddChild(new Control()); // keep the 4-column grid aligned
            }
        }
        col.AddChild(grid);
        return col;
    }

    // ── Zone 4: ships in port (each a card with its hold slots drawn out) ───────────────────────────────────────

    /// <summary>
    /// Each ship in port as a card: its name and hold occupancy, the hold's slots <b>drawn out</b> (one box per
    /// <see cref="Game.CargoCapacity"/> slot — filled slots labelled with the good/passenger they hold, empty slots
    /// blank), and per-passenger actions (put on the dock; cash in a carried-home treasure train fee-free). A ship under
    /// repair shows its repair countdown instead of sail/trade controls (its sail button lives in the Sail zone).
    /// </summary>
    private Control ShipsInPortZone(IReadOnlyList<Unit> ships)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 10);
        if (ships.Count == 0)
        {
            box.AddChild(new Label { Text = "(none)", HorizontalAlignment = HorizontalAlignment.Center });
            return box;
        }

        foreach (Unit ship in ships)
        {
            int capacity = _game.CargoCapacity(ship);
            string status = ship.IsUnderRepair
                ? $" — under repair ({ship.RepairTurnsRemaining} turn{(ship.RepairTurnsRemaining == 1 ? "" : "s")})"
                : $" — hold {_game.CargoSlotsUsed(ship)}/{capacity}";
            box.AddChild(new Label
            {
                Name = $"Ship_{ship.Id}",
                Text = $"{ship.Type.ShortName}{status}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });

            box.AddChild(HoldSlots(ship, capacity));

            // Passenger actions (put on the dock; cash in a carried-home treasure train straight from the hold).
            foreach (Unit passenger in _game.Passengers(ship))
            {
                Unit p = passenger;
                var prow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                prow.AddChild(Grow(new Label { Text = $"    {passenger.Type.ShortName} (aboard)" }));
                if (passenger.Type.CarryTreasure && _game.CheckCashInTreasureTrain(passenger).Allowed)
                {
                    prow.AddChild(CashInButton(p));
                }
                prow.AddChild(ActionButton($"Unload_{passenger.Id}", "Put on dock", () =>
                {
                    _game.DisembarkToDock(p);
                    Changed();
                }));
                box.AddChild(prow);
            }
        }
        return box;
    }

    /// <summary>
    /// A ship's cargo hold drawn as a row of <paramref name="capacity"/> slot boxes — each goods stack fills
    /// ceil(amount/100) slots (labelled "good ×N"), each passenger fills its carry-slots (labelled with the unit), and the
    /// remaining slots are drawn empty. This replaces the old "hold 1/2" text with the at-a-glance slot view the original
    /// game shows. Pure presentation (ADR-006); the slot accounting mirrors <see cref="Game.CargoSlotsUsed"/>.
    /// </summary>
    private Control HoldSlots(Unit ship, int capacity)
    {
        var slots = new HBoxContainer { Name = $"Hold_{ship.Id}" };
        slots.AddThemeConstantOverride("separation", 4);

        var fills = new List<string>();
        foreach ((string goodsId, int amount) in ship.Cargo.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            int used = (amount + 99) / 100; // 100 units per slot, rounded up — FreeCol GoodsContainer.CARGO_SIZE
            for (int i = 0; i < used; i++)
            {
                fills.Add(i == 0 ? $"{Short(goodsId)} {amount}" : Short(goodsId));
            }
        }
        foreach (Unit passenger in _game.Passengers(ship))
        {
            for (int i = 0; i < passenger.Type.CarrySlots; i++)
            {
                fills.Add(passenger.Type.ShortName);
            }
        }

        for (int i = 0; i < capacity; i++)
        {
            bool filled = i < fills.Count;
            var slot = new PanelContainer { CustomMinimumSize = new Vector2(78, 40) };
            slot.AddThemeStyleboxOverride("panel", SlotStyle(filled));
            slot.AddChild(new Label
            {
                Text = filled ? fills[i] : "",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            slots.AddChild(slot);
        }
        return slots;
    }

    private static StyleBox SlotStyle(bool filled)
    {
        var s = new StyleBoxFlat
        {
            BgColor = filled ? new Color(0.30f, 0.22f, 0.12f) : new Color(0.12f, 0.09f, 0.05f),
            BorderColor = new Color(0.45f, 0.36f, 0.22f),
        };
        s.SetBorderWidthAll(1);
        s.SetCornerRadiusAll(3);
        s.SetContentMarginAll(3);
        return s;
    }

    // ── Zone 5: sail to the New World ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A Sail button for every ship in port that is ready to sail (not under repair) — pressing it sends the ship home
    /// to the New World (<see cref="Game.SailToNewWorld"/>), arriving beside the player's territory after the crossing.
    /// A repairing ship is listed but cannot sail until it is whole. Phase 2 turns this into a drop target.
    /// </summary>
    private Control SailZone(IReadOnlyList<Unit> ships)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        var ready = ships.Where(s => !s.IsUnderRepair).ToList();
        if (ready.Count == 0)
        {
            box.AddChild(new Label
            {
                Text = ships.Count == 0 ? "(no ships in port)" : "(all ships in port are under repair)",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return box;
        }
        foreach (Unit ship in ready)
        {
            Unit sh = ship;
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(Grow(new Label { Text = $"{ship.Type.ShortName} — {_game.Passengers(ship).Count()} aboard" }));
            row.AddChild(ActionButton($"Sail_{ship.Id}", "Sail to New World", () =>
            {
                _game.SailToNewWorld(sh);
                Changed();
            }));
            box.AddChild(row);
        }
        return box;
    }

    // ── Zone 6: on the docks (waiting colonists + carried-home treasure) ────────────────────────────────────────

    /// <summary>
    /// The dock: colonists waiting to sail (each with a Board button per ship that has room — <see cref="Game.Board"/>),
    /// and treasure trains carried home and put on the dock (each cashed in fee-free — <see cref="CashInButton"/>). These
    /// are the off-map persons/trains in Europe not aboard a ship.
    /// </summary>
    private Control DocksZone(IReadOnlyList<Unit> onDock, IReadOnlyList<Unit> ships, IReadOnlyList<Unit> treasureOnDock)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);

        if (onDock.Count == 0 && treasureOnDock.Count == 0)
        {
            box.AddChild(new Label { Text = "(none)", HorizontalAlignment = HorizontalAlignment.Center });
            return box;
        }

        foreach (Unit person in onDock)
        {
            Unit pe = person;
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(Grow(new Label { Text = person.Type.ShortName }));
            foreach (Unit ship in ships.Where(s => _game.CheckBoard(person, s).Allowed))
            {
                Unit sh = ship;
                row.AddChild(ActionButton($"Board_{person.Id}_{ship.Id}", $"Board {ship.Type.ShortName}", () =>
                {
                    if (_game.CheckBoard(pe, sh).Allowed) { _game.Board(pe, sh); }
                    Changed();
                }));
            }
            box.AddChild(row);
        }

        foreach (Unit train in treasureOnDock)
        {
            Unit tr = train;
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddChild(Grow(new Label { Text = $"{train.Type.ShortName} — {train.TreasureAmount} gold" }));
            if (_game.CheckCashInTreasureTrain(train).Allowed)
            {
                row.AddChild(CashInButton(tr));
            }
            box.AddChild(row);
        }
        return box;
    }

    /// <summary>
    /// A "Cash in treasure (Ng)" button for a treasure <paramref name="train"/> in Europe (docked itself, or aboard a
    /// galleon docked there) — N is the fee-free net (<see cref="Game.CashInValue(Unit)"/>). Pressing it forwards to
    /// <see cref="Game.CashInTreasureTrain"/>, which banks the gold and consumes the train, then refreshes the panel.
    /// Gated on <see cref="Game.CheckCashInTreasureTrain"/> at build time (ADR-006); the press re-checks so a stale button
    /// is a no-op rather than a throw.
    /// </summary>
    private Button CashInButton(Unit train) =>
        ActionButton($"CashIn_{train.Id}", $"Cash in treasure ({_game.CashInValue(train)}g)", () =>
        {
            if (_game.CheckCashInTreasureTrain(train).Allowed)
            {
                _game.CashInTreasureTrain(train);
            }
            Changed();
        });

    // ── Small UI helpers ────────────────────────────────────────────────────────────────────────────────────────

    private static Label Grow(Label label)
    {
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return label;
    }

    private static Label MutedLabel(string text)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Right };
        label.AddThemeColorOverride("font_color", Muted);
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

    /// <summary>An action button that is created disabled (greyed) when <paramref name="enabled"/> is false — the panel's way of showing an unaffordable/blocked action without removing it (ADR-006: the gate is read from the engine Check oracle).</summary>
    private static Button GatedButton(string name, string text, bool enabled, Action onPressed)
    {
        Button button = ActionButton(name, text, onPressed);
        button.Disabled = !enabled;
        return button;
    }
}
