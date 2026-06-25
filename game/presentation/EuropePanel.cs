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

    /// <summary>
    /// The ship the goods market trades through, chosen by the player (ship picker, Phase 2). Persisted across the
    /// deferred <see cref="Changed"/> rebuild within the open session (UI-only state — no save change); cleared when
    /// that ship is no longer a tradeable ship in port (sailed, sold, under repair). Null ⇒ fall back to the first
    /// non-repairing ship in port, matching Phase 1's default.
    /// </summary>
    private int? _selectedShipId;

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
        Theme = ColonyTheme.GetInGame(); // share the colony screen's cohesive parchment/wood styling (larger, bolder in-game body text)
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

    // ── Ship picker + drag-drop helpers (Phase 2) ───────────────────────────────────────────────────────────────

    /// <summary>Resolves a unit by id from the live game, or null if it has left (sailed/sold/consumed since the payload was built).</summary>
    private Unit? UnitById(int id) => _game.Units.FirstOrDefault(u => u.Id == id);

    /// <summary>
    /// Selects the ship the goods market trades through (ship picker). The press is a no-op for an ineligible ship
    /// (gone / repairing). UI-only — it just records the id and rebuilds (no save change). Public so the L3 ship-picker
    /// test can drive the selection deterministically.
    /// </summary>
    public void SelectTradeShip(int shipId)
    {
        if (UnitById(shipId) is { Type.IsCarrier: true, IsUnderRepair: false } ship && ship.Location == UnitLocation.InEurope)
        {
            _selectedShipId = shipId;
        }
        Changed();
    }

    /// <summary>
    /// The ship the goods market trades through: the player-selected one if it is still a tradeable ship in port,
    /// otherwise the first non-repairing in-port ship (Phase 1's default). Returns null when none can trade. A stale
    /// selection (the ship sailed/sold/started repairing) is cleared so it can't strand the market on a gone ship.
    /// </summary>
    private Unit? TradeShip(IReadOnlyList<Unit> ships)
    {
        if (_selectedShipId is { } id
            && ships.FirstOrDefault(s => s.Id == id && !s.IsUnderRepair) is { } selected)
        {
            return selected;
        }
        _selectedShipId = null; // selection no longer valid — fall back to the default
        return ships.FirstOrDefault(s => !s.IsUnderRepair);
    }

    /// <summary>
    /// Whether a docked ship can sail to the New World right now — the inline re-check the Sail drop target uses (there
    /// is no <c>CheckSailToNewWorld</c> oracle; this mirrors <see cref="Game.SailToNewWorld"/>'s guards: in Europe, not
    /// aboard, not under repair). ADR-006: this reads engine state only, encodes no NEW rule.
    /// </summary>
    private bool CanSailToNewWorld(Unit ship) =>
        ship.Location == UnitLocation.InEurope && !ship.IsAboard && !ship.IsUnderRepair;

    /// <summary>
    /// Whether an aboard passenger can be put on the dock now — the inline re-check the docks drop target uses (no
    /// <c>CheckDisembarkToDock</c> oracle exists; mirrors <see cref="Game.DisembarkToDock"/>'s guards: aboard a ship
    /// that is itself in Europe). ADR-006: reads engine state only.
    /// </summary>
    private bool CanDisembarkToDock(Unit unit) =>
        unit.IsAboard && UnitById(unit.CarrierId!.Value) is { Location: UnitLocation.InEurope };

    // ── Drag payload builders ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Builds a colonist/passenger drag payload (drop #1 Board / #5 Disembark).</summary>
    private static Variant ColonistPayload(int unitId) => new Godot.Collections.Dictionary
    {
        [EuropeDrag.Kind] = EuropeDrag.KindColonist,
        [EuropeDrag.UnitId] = unitId,
    };

    /// <summary>Builds a ship-card drag payload (drop #2 Sail).</summary>
    private static Variant ShipPayload(int shipId) => new Godot.Collections.Dictionary
    {
        [EuropeDrag.Kind] = EuropeDrag.KindShip,
        [EuropeDrag.ShipId] = shipId,
    };

    /// <summary>Builds a market-goods drag payload (drop #3 Buy) — no <c>fromShipId</c> (it comes from the market).</summary>
    private static Variant BuyPayload(string goodsId, int amount) => new Godot.Collections.Dictionary
    {
        [EuropeDrag.Kind] = EuropeDrag.KindGoods,
        [EuropeDrag.GoodsId] = goodsId,
        [EuropeDrag.Amount] = amount,
    };

    /// <summary>Builds a hold-cargo drag payload (drop #4 Sell) — carries <c>fromShipId</c> so the market knows which ship to sell from.</summary>
    private static Variant SellPayload(string goodsId, int amount, int fromShipId) => new Godot.Collections.Dictionary
    {
        [EuropeDrag.Kind] = EuropeDrag.KindGoods,
        [EuropeDrag.GoodsId] = goodsId,
        [EuropeDrag.Amount] = amount,
        [EuropeDrag.FromShipId] = fromShipId,
    };

    // ── Drop accept/perform handlers (each re-checks a Game oracle; refusal is a graceful no-op, ADR-006) ─────────

    /// <summary>Accept-test for a colonist dropped on a ship card (drop #1): the payload is a colonist and <see cref="Game.CheckBoard"/> allows it.</summary>
    private bool BoardAllowed(Variant data, Unit ship) =>
        EuropeDrag.KindOf(data) == EuropeDrag.KindColonist
        && data.AsGodotDictionary() is { } d
        && UnitById(d[EuropeDrag.UnitId].AsInt32()) is { } person
        && _game.CheckBoard(person, ship).Allowed;

    /// <summary>Performs a colonist-onto-ship drop (drop #1) — re-checks <see cref="Game.CheckBoard"/>, forwards <see cref="Game.Board"/>, refreshes.</summary>
    private void OnBoardDrop(Variant data, Unit ship)
    {
        if (data.AsGodotDictionary() is { } d
            && UnitById(d[EuropeDrag.UnitId].AsInt32()) is { } person
            && _game.CheckBoard(person, ship).Allowed)
        {
            _game.Board(person, ship);
        }
        Changed();
    }

    /// <summary>Accept-test for a ship dropped on the sail zone (drop #2): the payload is a ship and it can sail now (<see cref="CanSailToNewWorld"/>).</summary>
    private bool SailAllowed(Variant data) =>
        EuropeDrag.KindOf(data) == EuropeDrag.KindShip
        && data.AsGodotDictionary() is { } d
        && UnitById(d[EuropeDrag.ShipId].AsInt32()) is { } ship
        && CanSailToNewWorld(ship);

    /// <summary>Performs a ship-onto-sail-zone drop (drop #2) — re-checks <see cref="CanSailToNewWorld"/>, forwards <see cref="Game.SailToNewWorld"/>, refreshes.</summary>
    private void OnSailDrop(Variant data)
    {
        if (data.AsGodotDictionary() is { } d
            && UnitById(d[EuropeDrag.ShipId].AsInt32()) is { } ship
            && CanSailToNewWorld(ship))
        {
            _game.SailToNewWorld(ship);
        }
        Changed();
    }

    /// <summary>
    /// Accept-test for goods dropped on a ship card (drop #3 Buy): a market payload (no <c>fromShipId</c>) that
    /// <see cref="Game.CheckBuyEuropeGoods"/> allows for this ship. A hold→ship move (a payload WITH a <c>fromShipId</c>)
    /// is rejected here — the market is the sell target, not another ship.
    /// </summary>
    private bool BuyAllowed(Variant data, Unit ship) =>
        EuropeDrag.KindOf(data) == EuropeDrag.KindGoods
        && data.AsGodotDictionary() is { } d
        && !d.ContainsKey(EuropeDrag.FromShipId)
        && _game.CheckBuyEuropeGoods(ship, d[EuropeDrag.GoodsId].AsString(), d[EuropeDrag.Amount].AsInt32()).Allowed;

    /// <summary>Performs a goods-onto-ship drop (drop #3 Buy) — re-checks <see cref="Game.CheckBuyEuropeGoods"/>, forwards <see cref="Game.BuyEuropeGoods"/>, refreshes.</summary>
    private void OnBuyDrop(Variant data, Unit ship)
    {
        if (data.AsGodotDictionary() is { } d && !d.ContainsKey(EuropeDrag.FromShipId))
        {
            string goodsId = d[EuropeDrag.GoodsId].AsString();
            int amount = d[EuropeDrag.Amount].AsInt32();
            if (_game.CheckBuyEuropeGoods(ship, goodsId, amount).Allowed)
            {
                _game.BuyEuropeGoods(ship, goodsId, amount);
            }
        }
        Changed();
    }

    /// <summary>
    /// Accept-test for hold cargo dropped on the market (drop #4 Sell): a goods payload WITH a <c>fromShipId</c> that
    /// <see cref="Game.CheckSellShipCargo"/> allows. A market-row payload (no <c>fromShipId</c>) is rejected — you don't
    /// "sell" the market to itself.
    /// </summary>
    private bool SellPayloadAllowed(Variant data) =>
        EuropeDrag.KindOf(data) == EuropeDrag.KindGoods
        && data.AsGodotDictionary() is { } d
        && d.ContainsKey(EuropeDrag.FromShipId)
        && UnitById(d[EuropeDrag.FromShipId].AsInt32()) is { } ship
        && _game.CheckSellShipCargo(ship, d[EuropeDrag.GoodsId].AsString(), d[EuropeDrag.Amount].AsInt32()).Allowed;

    /// <summary>Performs a cargo-onto-market drop (drop #4 Sell) — re-checks <see cref="Game.CheckSellShipCargo"/>, forwards <see cref="Game.SellShipCargo"/>, refreshes.</summary>
    private void OnSellDrop(Variant data)
    {
        if (data.AsGodotDictionary() is { } d && d.ContainsKey(EuropeDrag.FromShipId)
            && UnitById(d[EuropeDrag.FromShipId].AsInt32()) is { } ship)
        {
            string goodsId = d[EuropeDrag.GoodsId].AsString();
            int amount = d[EuropeDrag.Amount].AsInt32();
            if (_game.CheckSellShipCargo(ship, goodsId, amount).Allowed)
            {
                _game.SellShipCargo(ship, goodsId, amount);
            }
        }
        Changed();
    }

    /// <summary>Accept-test for a passenger dropped on the docks zone (drop #5 Disembark): a colonist payload that <see cref="CanDisembarkToDock"/> allows.</summary>
    private bool DisembarkAllowed(Variant data) =>
        EuropeDrag.KindOf(data) == EuropeDrag.KindColonist
        && data.AsGodotDictionary() is { } d
        && UnitById(d[EuropeDrag.UnitId].AsInt32()) is { } unit
        && CanDisembarkToDock(unit);

    /// <summary>Performs a passenger-onto-docks drop (drop #5 Disembark) — re-checks <see cref="CanDisembarkToDock"/>, forwards <see cref="Game.DisembarkToDock"/>, refreshes.</summary>
    private void OnDisembarkDrop(Variant data)
    {
        if (data.AsGodotDictionary() is { } d
            && UnitById(d[EuropeDrag.UnitId].AsInt32()) is { } unit
            && CanDisembarkToDock(unit))
        {
            _game.DisembarkToDock(unit);
        }
        Changed();
    }

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
        // The whole market is a drop target so cargo dragged onto it sells (drop #4). The accept/drop both read
        // CheckSellShipCargo (ADR-006) and resolve the source ship from the payload — so a sell drop works regardless
        // of which ship is the trade ship.
        var drop = new EuropeDropTarget { Name = "GoodsMarketDrop", SizeFlagsHorizontal = SizeFlags.Fill }
            .Configure(SellPayloadAllowed, OnSellDrop);
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0), SizeFlagsHorizontal = SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 4);
        drop.AddChild(col);
        col.AddChild(SectionLabel("Goods market"));

        // Trade is per-ship (the goods must go into a hold). The player picks which in-port ship via the ship picker
        // (click a ship card to select it); the market defaults to the first non-repairing ship when none is selected.
        Unit? tradeShip = TradeShip(ships);
        col.AddChild(new Label
        {
            Text = tradeShip is null
                ? "(no ship in port — prices only)"
                : $"Trading via {tradeShip.Type.ShortName} (hold {_game.CargoSlotsFree(tradeShip)} free) — click a ship card to choose",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        var grid = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 4);
        foreach (GoodsType good in _game.Ruleset.GoodsTypes.Where(g => _game.Market.IsTradeable(g.Id)))
        {
            string id = good.Id;
            bool boycott = !_game.Market.CanTrade(id);

            var name = new Label { Text = good.ShortName, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass };
            if (boycott)
            {
                name.AddThemeColorOverride("font_color", Negative);
                name.TooltipText = $"Boycotted — pay {_game.Market.Arrears(id)} gold in back taxes to trade again.";
            }
            // Dragging the goods name onto a ship card buys a 100-lot into that ship (drop #3). The payload carries no
            // fromShipId (it comes from the market); a boycotted good is not draggable (returns no payload).
            grid.AddChild(new EuropeDragSource { Name = $"MarketGood_{good.ShortName}", SizeFlagsHorizontal = SizeFlags.ExpandFill }
                .Configure(
                    () => boycott ? default : BuyPayload(id, GoodsLot),
                    () => $"Buy {GoodsLot} {good.ShortName}")
                .WithChild(name));

            grid.AddChild(new Label { Text = boycott ? "boycott" : $"buy {_game.Market.AskPrice(id)}", HorizontalAlignment = HorizontalAlignment.Right });

            // Buy a 100-lot into the trade ship's hold. Show the button greyed (GatedButton) when the engine refuses —
            // unaffordable, no room, boycotted, no ship — rather than vanishing it, matching the recruit/train zone
            // (findings #5/#7). The label carries the chunked BuyCost; the tooltip carries the refusal reason.
            if (tradeShip is { } buyShip && !boycott)
            {
                MoveCheck buyCheck = _game.CheckBuyEuropeGoods(buyShip, id, GoodsLot);
                Button buy = GatedButton($"BuyGood_{good.ShortName}", $"Buy ({_game.Market.BuyCost(id, GoodsLot)})", buyCheck.Allowed, () =>
                {
                    if (_game.CheckBuyEuropeGoods(buyShip, id, GoodsLot).Allowed)
                    {
                        _game.BuyEuropeGoods(buyShip, id, GoodsLot);
                    }
                    Changed();
                });
                if (!buyCheck.Allowed && buyCheck.Reason is { } why) { buy.TooltipText = why; }
                grid.AddChild(buy);
            }
            else if (boycott)
            {
                // Boycotted: do not advertise a sell price (the old "sell {bid}" was both wrong — you can't sell — and a
                // dead ternary whose arms were identical, finding #4). Show the boycott/arrears state instead.
                int arrears = _game.Market.Arrears(id);
                grid.AddChild(MutedLabel(arrears > 0 ? $"boycotted (arrears {arrears})" : "boycotted"));
            }
            else
            {
                grid.AddChild(new Control()); // no ship in port — nothing to buy into; keep the 4-column grid aligned
            }

            // Sell the lot this ship is carrying of this good (one button per stack actually aboard). The label and gate
            // both come from CheckSellShipCargo, whose cost is the AFTER-TAX proceeds (findings #1/#8) — the price slides
            // as the sale floods the market and the King withholds tax per chunk — never the raw pre-tax bid × amount.
            int aboard = tradeShip?.CargoOf(id) ?? 0;
            if (tradeShip is { } sellShip && aboard > 0 && !boycott && _game.CheckSellShipCargo(sellShip, id, aboard).Allowed)
            {
                int proceeds = _game.CheckSellShipCargo(sellShip, id, aboard).Cost;
                grid.AddChild(ActionButton($"Sell_{sellShip.Id}_{good.ShortName}", $"Sell {aboard} ({proceeds})", () =>
                {
                    if (_game.CheckSellShipCargo(sellShip, id, aboard).Allowed)
                    {
                        _game.SellShipCargo(sellShip, id, aboard);
                    }
                    Changed();
                }));
            }
            else
            {
                grid.AddChild(new Control()); // keep the 4-column grid aligned
            }
        }
        col.AddChild(grid);
        return drop;
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

        foreach (Unit shipUnit in ships)
        {
            Unit ship = shipUnit;
            int capacity = _game.CargoCapacity(ship);
            bool selected = _selectedShipId == ship.Id;

            // The whole card is a drop target: a colonist dropped here boards (drop #1), goods dropped here buy (drop
            // #3). Both accept/drop handlers re-check their Game oracle (ADR-006).
            var cardDrop = new EuropeDropTarget { Name = $"ShipDrop_{ship.Id}", SizeFlagsHorizontal = SizeFlags.ExpandFill }
                .Configure(
                    data => BoardAllowed(data, ship) || BuyAllowed(data, ship),
                    data =>
                    {
                        if (BoardAllowed(data, ship)) { OnBoardDrop(data, ship); }
                        else if (BuyAllowed(data, ship)) { OnBuyDrop(data, ship); }
                    });
            var cardBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            cardBox.AddThemeConstantOverride("separation", 4);
            cardDrop.AddChild(cardBox);

            string status = ship.IsUnderRepair
                ? $" — under repair ({ship.RepairTurnsRemaining} turn{(ship.RepairTurnsRemaining == 1 ? "" : "s")})"
                : $" — hold {_game.CargoSlotsUsed(ship)}/{capacity}";

            // The card title row: a Select button (the ship picker — its goods Buy/Sell target the selected ship) and
            // the title as a drag source (drag the card onto the sail zone to sail it — drop #2). A repairing ship can't
            // be the trade ship and can't sail, so it offers neither.
            var titleRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            titleRow.AddThemeConstantOverride("separation", 6);
            var title = new Label
            {
                Name = $"Ship_{ship.Id}",
                Text = $"{(selected ? "▶ " : "")}{ship.Type.ShortName}{status}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Pass,
            };
            if (!ship.IsUnderRepair)
            {
                titleRow.AddChild(new EuropeDragSource { Name = $"ShipDrag_{ship.Id}", SizeFlagsHorizontal = SizeFlags.ExpandFill }
                    .Configure(() => ShipPayload(ship.Id), () => $"Sail {ship.Type.ShortName}")
                    .WithChild(title));
                titleRow.AddChild(ActionButton($"Select_{ship.Id}", selected ? "Selected" : "Select", () => SelectTradeShip(ship.Id)));
            }
            else
            {
                titleRow.AddChild(title);
            }
            cardBox.AddChild(titleRow);

            cardBox.AddChild(HoldSlots(ship, capacity));

            // Passenger actions (put on the dock; cash in a carried-home treasure train straight from the hold).
            foreach (Unit passenger in _game.Passengers(ship))
            {
                Unit p = passenger;
                var prow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                // The passenger label is a drag source — drag it onto the docks zone to put it ashore (drop #5).
                prow.AddChild(new EuropeDragSource { Name = $"Passenger_{passenger.Id}", SizeFlagsHorizontal = SizeFlags.ExpandFill }
                    .Configure(() => ColonistPayload(p.Id), () => $"{p.Type.ShortName} → dock")
                    .WithChild(new Label { Text = $"    {passenger.Type.ShortName} (aboard)", SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass }));
                if (passenger.Type.CarryTreasure && _game.CheckCashInTreasureTrain(passenger).Allowed)
                {
                    prow.AddChild(CashInButton(p));
                }
                prow.AddChild(ActionButton($"Unload_{passenger.Id}", "Put on dock", () =>
                {
                    _game.DisembarkToDock(p);
                    Changed();
                }));
                cardBox.AddChild(prow);
            }
            box.AddChild(cardDrop);
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

        // Each fill records the label AND, for the FIRST slot of a goods stack, the (goodsId, amount) to drag-sell —
        // so dragging that lead slot onto the market sells the whole stack (drop #4). Passenger slots and the trailing
        // slots of a multi-slot goods stack carry no sell payload.
        var fills = new List<(string Label, string? GoodsId, int Amount)>();
        foreach ((string goodsId, int amount) in ship.Cargo.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            int used = _game.GoodsSlotsFor(amount); // the engine's per-stack slot rule (ADR-006), not a reimplemented literal
            for (int i = 0; i < used; i++)
            {
                fills.Add(i == 0 ? ($"{Short(goodsId)} {amount}", goodsId, amount) : (Short(goodsId), null, 0));
            }
        }
        foreach (Unit passenger in _game.Passengers(ship))
        {
            for (int i = 0; i < passenger.Type.CarrySlots; i++)
            {
                fills.Add((passenger.Type.ShortName, null, 0));
            }
        }

        for (int i = 0; i < capacity; i++)
        {
            bool filled = i < fills.Count;
            var slot = new PanelContainer { CustomMinimumSize = new Vector2(78, 40), MouseFilter = Control.MouseFilterEnum.Pass };
            slot.AddThemeStyleboxOverride("panel", SlotStyle(filled));
            slot.AddChild(new Label
            {
                Text = filled ? fills[i].Label : "",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });

            // A goods stack's lead slot is a drag source — drag it onto the goods market to sell the whole stack.
            if (filled && fills[i] is { GoodsId: { } gid, Amount: var amt })
            {
                slots.AddChild(new EuropeDragSource { Name = $"Cargo_{ship.Id}_{Short(gid)}" }
                    .Configure(() => SellPayload(gid, amt, ship.Id), () => $"Sell {amt} {Short(gid)}")
                    .WithChild(slot));
            }
            else
            {
                slots.AddChild(slot);
            }
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
    /// A repairing ship is listed but cannot sail until it is whole. The whole zone is also a <b>drop target</b> (Phase 2,
    /// drop #2): dragging a ship card here sails it (gated + performed via <see cref="SailAllowed"/>/<see cref="OnSailDrop"/>).
    /// </summary>
    private Control SailZone(IReadOnlyList<Unit> ships)
    {
        var drop = new EuropeDropTarget { Name = "SailDrop", SizeFlagsHorizontal = SizeFlags.ExpandFill }
            .Configure(SailAllowed, OnSailDrop);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        drop.AddChild(box);
        var ready = ships.Where(s => !s.IsUnderRepair).ToList();
        if (ready.Count == 0)
        {
            box.AddChild(new Label
            {
                Text = ships.Count == 0 ? "(no ships in port)" : "(all ships in port are under repair)",
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            return drop;
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
        return drop;
    }

    // ── Zone 6: on the docks (waiting colonists + carried-home treasure) ────────────────────────────────────────

    /// <summary>
    /// The dock: colonists waiting to sail (each with a Board button per ship that has room — <see cref="Game.Board"/>),
    /// and treasure trains carried home and put on the dock (each cashed in fee-free — <see cref="CashInButton"/>). These
    /// are the off-map persons/trains in Europe not aboard a ship. The whole zone is also a <b>drop target</b> (Phase 2,
    /// drop #5): dragging an aboard passenger here puts it ashore (<see cref="DisembarkAllowed"/>/<see cref="OnDisembarkDrop"/>);
    /// each waiting colonist label is itself a drag source (drop #1: drag it onto a ship card to board).
    /// </summary>
    private Control DocksZone(IReadOnlyList<Unit> onDock, IReadOnlyList<Unit> ships, IReadOnlyList<Unit> treasureOnDock)
    {
        var drop = new EuropeDropTarget { Name = "DocksDrop", SizeFlagsHorizontal = SizeFlags.ExpandFill }
            .Configure(DisembarkAllowed, OnDisembarkDrop);
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        drop.AddChild(box);

        if (onDock.Count == 0 && treasureOnDock.Count == 0)
        {
            box.AddChild(new Label { Text = "(none — drag an aboard colonist here to put it ashore)", HorizontalAlignment = HorizontalAlignment.Center });
            return drop;
        }

        foreach (Unit person in onDock)
        {
            Unit pe = person;
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            // The colonist label is a drag source — drag it onto a ship card to board (drop #1).
            row.AddChild(new EuropeDragSource { Name = $"DockColonist_{person.Id}", SizeFlagsHorizontal = SizeFlags.ExpandFill }
                .Configure(() => ColonistPayload(pe.Id), () => $"{pe.Type.ShortName} → ship")
                .WithChild(new Label { Text = person.Type.ShortName, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Pass }));
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
        return drop;
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
