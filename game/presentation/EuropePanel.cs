using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The Europe screen, laid out as a <b>spatial harbour</b> (FreeCol/Colonization style) rather than a flat text list:
/// a header (treasury + immigration clock + next-recruit price), a two-column row of bordered cards (left a
/// recruit/train/purchase card — recruit portrait cards plus compact icon GRIDS for trainable specialists and
/// purchasable ships; right a goods-market card whose rows each carry the good's ICON), a ships-in-port zone (each ship
/// a CARD with its sprite and its hold drawn as a row of slot boxes that show goods icons / passenger portraits), a
/// bordered sail-to-the-New-World drop panel, and an on-the-docks row of colonist PORTRAIT chips (plus carried-home
/// treasure trains). Every unit/good is shown by its real FreeCol sprite (<see cref="ColonyArt.UnitIcon"/> /
/// <see cref="ColonyArt.GoodsIcon"/> / <see cref="UnitMarker.ResolveTexture"/>), not a text row. Like the colony screen
/// it only renders state and forwards button clicks to the <see cref="Game"/> oracles (ADR-006); all rules live in
/// GameLogic. The Phase 2 drag-and-drop layer (board / sail / buy / sell / disembark + the ship picker) is wired
/// additively over the buttons. Built programmatically per open/refresh; the rebuild is deferred (see
/// <see cref="Changed"/>) so a control is never freed mid-signal.
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

    /// <summary>The labourer/default sprite role used for a colonist portrait (no equipment), shared with the colony screen's worker portraits.</summary>
    private const string PortraitRole = "default";

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
    /// Selects the ship the goods market trades through (ship picker — driven by a single click on the ship card). The
    /// call is a no-op for an ineligible ship (gone / repairing). UI-only — it just records the id and rebuilds (no save
    /// change). Public so the L3 ship-picker test can drive the selection deterministically.
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
    /// FreeCol's <c>colonydocks</c> harbour scene (sky + sea, stretched to fill) so Europe reads as a place; falls back to
    /// the tiled brown parchment skin, then a warm solid fill, if the asset is absent (so it is opaque in CI even before
    /// the image is imported). The content cards carry their own opaque parchment backing on top, so their text stays
    /// readable over the scene. Built once and shared across opens.
    /// </summary>
    private void EnsureOpaqueBackground()
    {
        _panelBackground ??= BuildPanelBackground();
        AddThemeStyleboxOverride("panel", _panelBackground);
    }

    private static StyleBox BuildPanelBackground()
    {
        if (ColonyArt.HarbourBackdrop() is { } harbour)
        {
            // The harbour scene fills the whole panel (stretched, not tiled — it is one picture, not a repeating texture);
            // the content cards' own opaque parchment backing keeps text readable on top of it.
            var scene = new StyleBoxTexture { Texture = harbour };
            scene.SetContentMarginAll(20);
            return scene;
        }
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

    /// <summary>
    /// An <b>opaque</b> parchment backing for the content cards that sit over the harbour backdrop, so their text and
    /// icons stay readable on top of the sky/sea scene (the backdrop only shows around/behind the cards). Prefers the
    /// tiled brown parchment; falls back to a warm opaque solid fill when the asset is absent (CI). Built once, shared by
    /// the content card and the section cards.
    /// </summary>
    private static StyleBox CardBackingStyle()
    {
        if (ColonyArt.PanelParchment() is { } parchment)
        {
            var skin = new StyleBoxTexture
            {
                Texture = parchment,
                AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
                AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
            };
            skin.SetContentMarginAll(14); // StyleBoxTexture has no corner radius; the tiled parchment fills the rect
            return skin;
        }
        var flat = new StyleBoxFlat { BgColor = new Color(0.20f, 0.14f, 0.08f) };
        flat.SetContentMarginAll(14);
        flat.SetCornerRadiusAll(6);
        return flat;
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/EuropeTitle").Text = "Europe — the harbour";
        GetNode<Label>("VBox/EuropeInfo").Text =
            $"Treasury: {_game.Gold} gold   |   Next recruit: {_game.RecruitPrice} gold";

        var root = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in root.GetChildren())
        {
            root.RemoveChild(child); child.QueueFree(); // detach now (signal-safe), free deferred — avoids freed-while-emitting when a child button drives the rebuild
        }

        // A centred content card (ShrinkCenter) so on wide windows the zones sit centred with balanced margins rather
        // than spraying across the width; the Scroll's horizontal-auto mode (main.tscn) handles windows narrower than it.
        // It carries an OPAQUE parchment backing so the harbour backdrop (drawn behind the whole panel) shows around the
        // card while the card's own text/icons stay fully readable on the parchment on top.
        var backing = new PanelContainer { Name = "ContentBacking", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        backing.AddThemeStyleboxOverride("panel", CardBackingStyle());
        var card = new VBoxContainer { Name = "ContentCard", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        card.AddThemeConstantOverride("separation", 12);
        backing.AddChild(card);

        var ships = _game.UnitsInEurope.Where(u => u.Type.IsCarrier && !u.IsAboard).ToList();
        var onDock = _game.UnitsInEurope.Where(u => u.Type.IsPerson && !u.IsAboard).ToList();
        // Treasure trains carried home and put on the dock are neither carriers nor persons, so they fall through both
        // lists above — they get their own cash-in entry in the docks zone or the carry-it-home play dead-ends.
        var treasureOnDock = _game.UnitsInEurope.Where(u => u.Type.CarryTreasure && !u.IsAboard).ToList();

        card.AddChild(ImmigrationZone());

        // The two market/recruitment cards sit side by side on a wide screen (the existing topRow), top-aligned so
        // the differing card heights don't stretch each other; the Scroll handles windows narrower than they fit.
        var topRow = new HBoxContainer { Name = "TopRow", SizeFlagsHorizontal = SizeFlags.ShrinkCenter, Alignment = BoxContainer.AlignmentMode.Begin };
        topRow.AddThemeConstantOverride("separation", 16);
        topRow.AddChild(Card("Recruit & train", RecruitTrainPurchaseZone(), 400));
        topRow.AddChild(Card("Goods market", GoodsMarketZone(ships), 440));
        card.AddChild(topRow);

        card.AddChild(SectionLabel("Ships in port"));
        card.AddChild(ShipsInPortZone(ships));

        // The two in-transit lanes — ships crossing the high seas, so the player can see what's coming and going rather
        // than the ships simply vanishing until they dock. Each is read from a Game oracle (ADR-006).
        card.AddChild(SectionLabel("In transit"));
        card.AddChild(InTransitZone());

        card.AddChild(SectionLabel("Sail to the New World"));
        card.AddChild(SailZone(ships));

        card.AddChild(SectionLabel("On the docks"));
        card.AddChild(DocksZone(onDock, ships, treasureOnDock));

        root.AddChild(backing);
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
    /// The people-and-units card body: the three recruitment-dock slots as small <b>portrait cards</b> (the colonist's
    /// sprite + name + cost + a <c>Recruit_{slot}</c> button), then <b>Train a specialist</b> and <b>Purchase</b> as
    /// compact icon <b>grids</b> — each entry a small button drawn with the unit/ship <b>sprite</b> + its price and the
    /// full name in the tooltip (<see cref="Game.UnitTypesTrainedInEurope"/> → <see cref="Game.TrainUnit(string)"/> at a
    /// flat price; <see cref="Game.UnitTypesPurchasedInEurope"/> → <see cref="Game.BuyUnit(string)"/>, artillery
    /// escalating). Every action button is disabled (greyed) when its matching Check oracle refuses — chiefly when the
    /// player can't afford it (ADR-006: the panel only reads the gate). Replaces the old long vertical text lists.
    /// </summary>
    private Control RecruitTrainPurchaseZone()
    {
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 8);

        // — Recruitment dock: three colonist portrait cards side by side —
        col.AddChild(MutedLabelLeft("Recruitment dock"));
        int price = _game.RecruitPrice;
        IReadOnlyList<string> dock = _game.RecruitDock;
        var recruitRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.Fill };
        recruitRow.AddThemeConstantOverride("separation", 8);
        for (int slot = 0; slot < dock.Count; slot++)
        {
            int s = slot;
            string typeShort = Short(dock[slot]);
            // Each recruit slot is a portrait card sized wide enough that the wrapped colonist name and the Recruit button
            // fit without clipping in the 17px-bold theme (Chris's playtest). The full name shows on the card AND in the
            // button tooltip; the button itself is the compact price (the action is obvious from the card).
            var slotCard = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(116, 0) };
            slotCard.AddThemeConstantOverride("separation", 4);
            var framed = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            framed.AddThemeStyleboxOverride("panel", SlotStyle(filled: true));
            framed.AddChild(slotCard);

            slotCard.AddChild(Centered(UnitPortrait(typeShort, 44)));
            slotCard.AddChild(new Label { Text = Display(typeShort), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart });
            var recruit = GatedButton($"Recruit_{slot}", $"Recruit · {price}g", _game.CheckRecruit(slot).Allowed, () =>
            {
                if (_game.CheckRecruit(s).Allowed) { _game.Recruit(s); }
                Changed();
            });
            recruit.SizeFlagsHorizontal = SizeFlags.Fill;
            recruit.ClipText = true; // never let the price label spill past the card edge
            recruit.TooltipText = $"Recruit {Display(typeShort)} for {price} gold";
            slotCard.AddChild(recruit);
            recruitRow.AddChild(framed);
        }
        col.AddChild(recruitRow);

        // — Train specialists (flat price) — a compact 3-wide icon grid of sprite buttons —
        IReadOnlyList<UnitType> trainable = _game.UnitTypesTrainedInEurope();
        if (trainable.Count > 0)
        {
            col.AddChild(MutedLabelLeft("Train a specialist"));
            col.AddChild(UnitButtonGrid(trainable, train: true));
        }

        // — Purchase ships / artillery (artillery escalates) — a compact icon grid of sprite buttons —
        IReadOnlyList<UnitType> purchasable = _game.UnitTypesPurchasedInEurope();
        if (purchasable.Count > 0)
        {
            col.AddChild(MutedLabelLeft("Purchase"));
            col.AddChild(UnitButtonGrid(purchasable, train: false));
        }

        return col;
    }

    /// <summary>
    /// A compact icon grid of unit/ship buttons (3 columns): each button shows the type's <b>sprite</b> stacked over its
    /// price, with the full name in the tooltip — the spatial replacement for the old vertical text list. When
    /// <paramref name="train"/> is true the buttons are named <c>Train_{short}</c> and route to
    /// <see cref="Game.TrainUnit(string)"/> (gated on <see cref="Game.CheckTrain(string)"/>); otherwise they are
    /// <c>Purchase_{short}</c> routing to <see cref="Game.BuyUnit(string)"/> (gated on <see cref="Game.CheckBuyUnit"/>).
    /// Each is a <see cref="GatedButton"/> so an unaffordable action greys rather than vanishes (ADR-006).
    /// </summary>
    private Control UnitButtonGrid(IReadOnlyList<UnitType> types, bool train)
    {
        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.Fill };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (UnitType type in types)
        {
            string id = type.Id;
            string shortName = type.ShortName;
            int p = _game.EuropeUnitPrice(id);
            bool allowed = train ? _game.CheckTrain(id).Allowed : _game.CheckBuyUnit(id).Allowed;
            // Sized to its content with padding so neither the 44px sprite nor the price (in the 17px-bold in-game theme)
            // clips — the cell shows ONLY the sprite + price, the full name lives in the tooltip (Chris's playtest: don't
            // cram unit names into grid cells). The content VBox fills the button (FullRect) and centres vertically.
            var button = new Button
            {
                Name = train ? $"Train_{shortName}" : $"Purchase_{shortName}",
                Disabled = !allowed,
                TooltipText = $"{(train ? "Train" : "Buy")} {Display(shortName)} — {p} gold",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(118, 96),
            };
            var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            content.AddThemeConstantOverride("separation", 2);
            content.SetAnchorsPreset(LayoutPreset.FullRect);
            content.AddChild(Centered(IconRect(UnitSprite(shortName), 44, 44)));
            content.AddChild(new Label { Text = $"{p}g", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
            button.AddChild(content);
            button.Pressed += () =>
            {
                if (train) { if (_game.CheckTrain(id).Allowed) { _game.TrainUnit(id); } }
                else { if (_game.CheckBuyUnit(id).Allowed) { _game.BuyUnit(id); } }
                Changed();
            };
            grid.AddChild(button);
        }
        return grid;
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
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 4);
        drop.AddChild(col);

        // Trade is per-ship (the goods must go into a hold). The player picks which in-port ship via the ship picker
        // (click a ship card to select it); the market defaults to the first non-repairing ship when none is selected.
        Unit? tradeShip = TradeShip(ships);
        col.AddChild(new Label
        {
            Text = tradeShip is null
                ? "(no ship in port — prices only)"
                : $"Trading via {Display(tradeShip.Type.ShortName)} (hold {_game.CargoSlotsFree(tradeShip)} free) — Select a ship card to choose",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });

        // One row per tradeable good: [icon | name | bid/ask | Buy | Sell]. The icon+name pair is the buy drag source.
        foreach (GoodsType good in _game.Ruleset.GoodsTypes.Where(g => _game.Market.IsTradeable(g.Id)))
        {
            string id = good.Id;
            string shortName = good.ShortName;
            bool boycott = !_game.Market.CanTrade(id);

            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 6);

            // The good's icon + name form the buy drag source (drop #3): dragging it onto a ship card buys a 100-lot
            // into that ship. The payload carries no fromShipId (it comes from the market); a boycotted good is not
            // draggable (returns no payload).
            var labelBox = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
            labelBox.AddThemeConstantOverride("separation", 6);
            labelBox.AddChild(IconRect(ColonyArt.GoodsIcon(shortName), 28, 28));
            var name = new Label { Text = Display(shortName), SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, ClipText = true, MouseFilter = MouseFilterEnum.Pass };
            if (boycott)
            {
                name.AddThemeColorOverride("font_color", Negative);
                name.TooltipText = $"Boycotted — pay {_game.Market.Arrears(id)} gold in back taxes to trade again.";
            }
            labelBox.AddChild(name);
            labelBox.SetAnchorsPreset(LayoutPreset.FullRect);
            // A taller min row so the 17px-bold name + 28px icon sit with breathing room rather than clipping (playtest).
            row.AddChild(new EuropeDragSource { Name = $"MarketGood_{shortName}", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 34), SizeFlagsVertical = SizeFlags.Fill }
                .Configure(
                    () => boycott ? default : BuyPayload(id, GoodsLot),
                    () => $"Buy {GoodsLot} {Display(shortName)}")
                .WithChild(labelBox));

            // The bid/ask price (or the boycott/arrears state — no misleading sell price for a boycotted good, finding #4).
            if (boycott)
            {
                int arrears = _game.Market.Arrears(id);
                row.AddChild(MutedLabel(arrears > 0 ? $"boycotted (arrears {arrears})" : "boycotted"));
            }
            else
            {
                row.AddChild(new Label { Text = $"{_game.Market.BidPrice(id)}/{_game.Market.AskPrice(id)}", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(64, 0) });
            }

            // Buy a 100-lot into the trade ship's hold. A GatedButton greyed (with the refusal reason in its tooltip)
            // when the engine refuses — unaffordable, no room, boycotted — rather than vanishing (findings #5/#7).
            if (tradeShip is { } buyShip && !boycott)
            {
                MoveCheck buyCheck = _game.CheckBuyEuropeGoods(buyShip, id, GoodsLot);
                Button buy = GatedButton($"BuyGood_{shortName}", $"Buy ({_game.Market.BuyCost(id, GoodsLot)})", buyCheck.Allowed, () =>
                {
                    if (_game.CheckBuyEuropeGoods(buyShip, id, GoodsLot).Allowed)
                    {
                        _game.BuyEuropeGoods(buyShip, id, GoodsLot);
                    }
                    Changed();
                });
                buy.TooltipText = !buyCheck.Allowed && buyCheck.Reason is { } why ? why : $"Buy {GoodsLot} {Display(shortName)}";
                row.AddChild(buy);
            }

            // Sell the lot this ship is carrying of this good. The label and gate both come from CheckSellShipCargo,
            // whose cost is the AFTER-TAX proceeds (findings #1/#8) — never the raw pre-tax bid × amount.
            int aboard = tradeShip?.CargoOf(id) ?? 0;
            if (tradeShip is { } sellShip && aboard > 0 && !boycott && _game.CheckSellShipCargo(sellShip, id, aboard).Allowed)
            {
                int proceeds = _game.CheckSellShipCargo(sellShip, id, aboard).Cost;
                row.AddChild(ActionButton($"Sell_{sellShip.Id}_{shortName}", $"Sell {aboard} ({proceeds})", () =>
                {
                    if (_game.CheckSellShipCargo(sellShip, id, aboard).Allowed)
                    {
                        _game.SellShipCargo(sellShip, id, aboard);
                    }
                    Changed();
                }));
            }
            col.AddChild(row);
        }
        return drop;
    }

    // ── Zone 4: ships in port (each a card with its hold slots drawn out) ───────────────────────────────────────

    /// <summary>
    /// Each ship in port as a bordered <b>card</b>: its <b>sprite</b> (<see cref="ColonyArt.UnitIcon"/>) beside its name
    /// and hold occupancy, the hold's slots <b>drawn out</b> as a row of boxes (one per <see cref="Game.CargoCapacity"/>
    /// slot — a filled slot shows the goods icon + amount or a passenger portrait, an empty slot a faint empty box), and
    /// per-passenger actions (put on the dock; cash in a carried-home treasure train fee-free). A ship under repair shows
    /// its repair countdown instead of sail/trade controls (its sail button lives in the Sail zone). The whole card is the
    /// <c>ShipDrop_{id}</c> drop target; its title is the <c>ShipDrag_{id}</c> source, which both <b>selects the trade
    /// ship on a single click</b> and <b>sails on a drag</b> to the Sail zone (the old <c>Select_{id}</c> button is gone).
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
            // #3). Both accept/drop handlers re-check their Game oracle (ADR-006). A faint border frames each ship; the
            // drop target is the PARENT of the frame (DropZone) so the card's buttons are descendants (real clicks reach
            // them) while a drag-hover over the card's decorations falls through to the drop target. The frame and its
            // box are mouse-Ignore (decoration) so the hover reaches the drop target across the whole card.
            var cardDrop = new EuropeDropTarget { Name = $"ShipDrop_{ship.Id}" }
                .Configure(
                    data => BoardAllowed(data, ship) || BuyAllowed(data, ship),
                    data =>
                    {
                        if (BoardAllowed(data, ship)) { OnBoardDrop(data, ship); }
                        else if (BuyAllowed(data, ship)) { OnBuyDrop(data, ship); }
                    });
            var frame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            frame.AddThemeStyleboxOverride("panel", CardStyle(selected));
            var cardBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            cardBox.AddThemeConstantOverride("separation", 4);
            frame.AddChild(cardBox);

            string status = ship.IsUnderRepair
                ? $"under repair ({ship.RepairTurnsRemaining} turn{(ship.RepairTurnsRemaining == 1 ? "" : "s")})"
                : $"hold {_game.CargoSlotsUsed(ship)}/{capacity}";

            // The card title row: the ship SPRITE + name + status. A single CLICK on the card selects it as the trade
            // ship (the goods market's Buy/Sell then target it); a DRAG of the card onto the sail zone sails it (drop #2)
            // — both on the one ShipDrag source (no separate Select button; Chris's playtest). A repairing ship can't be
            // the trade ship and can't sail, so its title is plain (no drag, no select).
            // The title's sprite + name are decoration (mouse-Ignore) so the click/drag/hover reach the ShipDrag source.
            int selectId = ship.Id;
            var titleInner = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            titleInner.AddThemeConstantOverride("separation", 6);
            titleInner.AddChild(IconRect(UnitSprite(ship.Type.ShortName), 48, 40));
            titleInner.AddChild(new Label
            {
                Name = $"Ship_{ship.Id}",
                Text = $"{(selected ? "▶ " : "")}{Display(ship.Type.ShortName)} — {status}{(ship.IsUnderRepair ? "" : selected ? "  (trading)" : "  (click to trade)")}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

            // The title row is a decoration container (mouse-Ignore) so a hover over its gaps falls through to the card
            // drop target; the ShipDrag source (a descendant) keeps its own hover/click/drag.
            var titleRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            titleRow.AddThemeConstantOverride("separation", 6);
            if (!ship.IsUnderRepair)
            {
                titleInner.SetAnchorsPreset(LayoutPreset.FullRect); // fill the drag-source Control so the row reads at full width
                // A bare drag-source Control does NOT adopt its child's min size (only Containers do), so give it the
                // title's height explicitly — otherwise the row collapses to nothing now that the old Select button (which
                // used to lend the row its height) is gone.
                var titleSource = new EuropeDragSource { Name = $"ShipDrag_{ship.Id}", SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 40), TooltipText = "Click to trade through this ship; drag onto the Sail zone to depart" }
                    .Configure(() => ShipPayload(ship.Id), () => $"Sail {Display(ship.Type.ShortName)}")
                    .OnClick(() => SelectTradeShip(selectId))
                    .WithChild(titleInner);
                titleRow.AddChild(titleSource);
            }
            else
            {
                titleRow.AddChild(titleInner);
            }
            cardBox.AddChild(titleRow);

            cardBox.AddChild(HoldSlots(ship, capacity));

            // Passenger actions (put on the dock; cash in a carried-home treasure train straight from the hold).
            foreach (Unit passenger in _game.Passengers(ship))
            {
                Unit p = passenger;
                // The passenger row is a decoration container (mouse-Ignore) so a hover over its gaps reaches the card
                // drop target; the Passenger drag source and the CashIn / Put-on-dock buttons (descendants) keep theirs.
                var prow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
                prow.AddThemeConstantOverride("separation", 4);
                // The passenger portrait chip is a drag source — drag it onto the docks zone to put it ashore (drop #5).
                // Its inner sprite + label are decoration (mouse-Ignore) so the hover reaches the wrapping drag source.
                var pChip = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
                pChip.AddThemeConstantOverride("separation", 4);
                pChip.AddChild(IconRect(UnitSprite(p.Type.ShortName), 28, 28));
                pChip.AddChild(new Label { Text = $"{Display(p.Type.ShortName)} (aboard)", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
                pChip.SetAnchorsPreset(LayoutPreset.FullRect);
                prow.AddChild(new EuropeDragSource { Name = $"Passenger_{passenger.Id}", SizeFlagsHorizontal = SizeFlags.ExpandFill }
                    .Configure(() => ColonistPayload(p.Id), () => $"{Display(p.Type.ShortName)} → dock")
                    .WithChild(pChip));
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
            box.AddChild(DropZone(cardDrop, frame)); // the frame drives the card height; cardDrop overlays it for the drag/drop
        }
        return box;
    }

    /// <summary>A drawn-out hold slot's content: a goods stack (its icon + amount; the lead slot carries the drag-sell payload), a passenger portrait, or empty.</summary>
    private readonly record struct HoldFill(string? GoodsShort, int Amount, string? UnitShort, string? SellGoodsId);

    /// <summary>
    /// A ship's cargo hold drawn as a row of <paramref name="capacity"/> slot boxes — each goods stack fills
    /// ceil(amount/100) slots (a filled slot shows the <b>goods icon + amount</b>, its lead slot draggable to sell),
    /// each passenger fills its carry-slots (shown as a unit <b>portrait</b>), and the remaining slots are drawn as faint
    /// empty boxes. This is the at-a-glance slot view the original game shows. Pure presentation (ADR-006); the slot
    /// accounting mirrors <see cref="Game.CargoSlotsUsed"/> via <see cref="Game.GoodsSlotsFor"/>.
    /// </summary>
    private Control HoldSlots(Unit ship, int capacity)
    {
        var slots = new HBoxContainer { Name = $"Hold_{ship.Id}" };
        slots.AddThemeConstantOverride("separation", 4);

        // Each fill records what to draw AND, for the FIRST slot of a goods stack, the (goodsId, amount) to drag-sell —
        // so dragging that lead slot onto the market sells the whole stack (drop #4). Passenger slots and the trailing
        // slots of a multi-slot goods stack carry no sell payload.
        var fills = new List<HoldFill>();
        foreach ((string goodsId, int amount) in ship.Cargo.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            int used = _game.GoodsSlotsFor(amount); // the engine's per-stack slot rule (ADR-006), not a reimplemented literal
            for (int i = 0; i < used; i++)
            {
                fills.Add(new HoldFill(Short(goodsId), amount, null, i == 0 ? goodsId : null));
            }
        }
        foreach (Unit passenger in _game.Passengers(ship))
        {
            for (int i = 0; i < passenger.Type.CarrySlots; i++)
            {
                fills.Add(new HoldFill(null, 0, passenger.Type.ShortName, null));
            }
        }

        for (int i = 0; i < capacity; i++)
        {
            bool filled = i < fills.Count;
            // The slot box is decoration (mouse-Ignore) so a drag-hover over it falls through to the card drop target
            // (so a buy/board drop lands anywhere on the card); a goods lead-slot is re-wrapped below in a drag source
            // (which itself receives the hover to start a sell drag).
            var slot = new PanelContainer { CustomMinimumSize = new Vector2(60, 56), MouseFilter = Control.MouseFilterEnum.Ignore };
            slot.AddThemeStyleboxOverride("panel", SlotStyle(filled));

            if (filled && fills[i] is { GoodsShort: { } gs } gf)
            {
                // A goods stack: the goods icon with the amount overlaid beneath it.
                var content = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                content.AddThemeConstantOverride("separation", 0);
                content.AddChild(Centered(IconRect(ColonyArt.GoodsIcon(gs), 34, 34)));
                content.AddChild(new Label { Text = $"{gf.Amount}", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
                slot.AddChild(content);
            }
            else if (filled && fills[i] is { UnitShort: { } us })
            {
                slot.AddChild(Centered(IconRect(UnitSprite(us), 40, 40)));
            }
            else
            {
                slot.AddChild(new Control { MouseFilter = MouseFilterEnum.Ignore }); // empty slot — a faint box
            }

            // A goods stack's lead slot is a drag source — drag it onto the goods market to sell the whole stack. The
            // source carries the slot's min size (a bare drag-source Control doesn't adopt its child's min) and the slot
            // fills it (FullRect) so the box renders at full size.
            if (filled && fills[i] is { SellGoodsId: { } gid })
            {
                int amt = fills[i].Amount;
                slot.TooltipText = $"{Display(Short(gid))} {amt} — drag to the market to sell";
                slot.SetAnchorsPreset(LayoutPreset.FullRect);
                slots.AddChild(new EuropeDragSource { Name = $"Cargo_{ship.Id}_{Short(gid)}", CustomMinimumSize = new Vector2(60, 56) }
                    .Configure(() => SellPayload(gid, amt, ship.Id), () => $"Sell {amt} {Display(Short(gid))}")
                    .WithChild(slot));
            }
            else
            {
                slots.AddChild(slot);
            }
        }
        return slots;
    }

    /// <summary>
    /// A cargo-slot / chip skin. A <paramref name="filled"/> slot is a solid parchment-brown box (it holds a goods icon
    /// or a passenger portrait). An EMPTY slot is a faint parchment-toned outline — a barely-there transparent fill with
    /// a soft warm border — rather than the old near-black box that read as an ugly dark hole on the parchment backing
    /// (Chris's playtest). It still marks the slot's place without drawing attention to itself.
    /// </summary>
    private static StyleBox SlotStyle(bool filled)
    {
        var s = new StyleBoxFlat
        {
            BgColor = filled ? new Color(0.30f, 0.22f, 0.12f) : new Color(0.45f, 0.36f, 0.22f, 0.12f),
            BorderColor = filled ? new Color(0.45f, 0.36f, 0.22f) : new Color(0.45f, 0.36f, 0.22f, 0.45f),
        };
        s.SetBorderWidthAll(1);
        s.SetCornerRadiusAll(3);
        s.SetContentMarginAll(3);
        return s;
    }

    // ── Zone 4b: in-transit lanes (ships crossing the high seas) ────────────────────────────────────────────────

    /// <summary>
    /// The two in-transit lanes side by side: <b>Expected soon</b> (the human's ships
    /// <see cref="Game.ShipsSailingToEurope"/> — crossing towards Europe) and <b>Bound for the New World</b>
    /// (<see cref="Game.ShipsSailingToNewWorld"/> — crossing back). Each ship is its sprite + an "arrives in N turn(s)"
    /// line from <see cref="Unit.SailTurnsRemaining"/>. Pure read-only presentation (ADR-006) — no buttons; a ship in
    /// transit can't be acted on, it just shows where it is so the harbour reads as a living crossing.
    /// </summary>
    private Control InTransitZone()
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, Alignment = BoxContainer.AlignmentMode.Begin };
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(Card("Expected soon", TransitLane(_game.ShipsSailingToEurope, arriving: true), 300));
        row.AddChild(Card("Bound for the New World", TransitLane(_game.ShipsSailingToNewWorld, arriving: false), 300));
        return row;
    }

    /// <summary>
    /// One in-transit lane body: a column of ship chips (sprite + name + "arrives in N turn(s)"), or a muted "(none at
    /// sea)" when the lane is empty. <paramref name="arriving"/> only tunes the wording; both read the same
    /// <see cref="Unit.SailTurnsRemaining"/>.
    /// </summary>
    private Control TransitLane(IReadOnlyList<Unit> shipsAtSea, bool arriving)
    {
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 6);
        if (shipsAtSea.Count == 0)
        {
            col.AddChild(new Label { Text = "(none at sea)", HorizontalAlignment = HorizontalAlignment.Center });
            return col;
        }
        foreach (Unit ship in shipsAtSea)
        {
            int turns = ship.SailTurnsRemaining;
            var chip = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            chip.AddThemeConstantOverride("separation", 8);
            chip.AddChild(IconRect(UnitSprite(ship.Type.ShortName), 40, 32));
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            text.AddThemeConstantOverride("separation", 0);
            text.AddChild(new Label { Text = Display(ship.Type.ShortName), AutowrapMode = TextServer.AutowrapMode.WordSmart });
            text.AddChild(new Label
            {
                Text = $"arrives in {turns} turn{(turns == 1 ? "" : "s")}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            chip.AddChild(text);
            col.AddChild(chip);
        }
        return col;
    }

    // ── Zone 5: sail to the New World ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A visually distinct <b>bordered drop panel</b> with an arrow/label inviting the player to drag a loaded ship in to
    /// depart, plus a <c>Sail_{id}</c> button per ready ship as a fallback — pressing it sends the ship home to the New
    /// World (<see cref="Game.SailToNewWorld"/>), arriving beside the player's territory after the crossing. A repairing
    /// ship is listed but cannot sail until it is whole. The whole zone is the Phase-2 drop target (drop #2): dragging a
    /// ship card here sails it (gated + performed via <see cref="SailAllowed"/>/<see cref="OnSailDrop"/>).
    /// </summary>
    private Control SailZone(IReadOnlyList<Unit> ships)
    {
        var drop = new EuropeDropTarget { Name = "SailDrop" }
            .Configure(SailAllowed, OnSailDrop);
        // A distinct cool-bordered panel so the drop target reads as a "departure gate"; the drop is its PARENT
        // (DropZone) so a hover over the panel reaches the drop while the per-ship Sail buttons (descendants) keep their
        // clicks. The frame, its box and the arrow/label are decoration (mouse-Ignore) so the hover reaches the drop.
        var frame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        frame.AddThemeStyleboxOverride("panel", SailZoneStyle());
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 6);
        frame.AddChild(box);

        box.AddChild(new Label
        {
            Text = "⛵  ➜  Drag a loaded ship here to depart for the New World",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        });

        var ready = ships.Where(s => !s.IsUnderRepair).ToList();
        if (ready.Count == 0)
        {
            box.AddChild(new Label
            {
                Text = ships.Count == 0 ? "(no ships in port)" : "(all ships in port are under repair)",
                HorizontalAlignment = HorizontalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            });
            return DropZone(drop, frame);
        }
        foreach (Unit ship in ready)
        {
            Unit sh = ship;
            // The row + its sprite/label are decoration (mouse-Ignore) so a drag-hover over them reaches the sail drop
            // target; the Sail button (a descendant) keeps its own click regardless of its parent's filter.
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 6);
            row.AddChild(IconRect(UnitSprite(ship.Type.ShortName), 32, 26));
            var sailLabel = new Label { Text = $"{Display(ship.Type.ShortName)} — {_game.Passengers(ship).Count()} aboard", VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
            row.AddChild(Grow(sailLabel));
            row.AddChild(ActionButton($"Sail_{ship.Id}", "Sail to New World", () =>
            {
                _game.SailToNewWorld(sh);
                Changed();
            }));
            box.AddChild(row);
        }
        return DropZone(drop, frame);
    }

    // ── Zone 6: on the docks (waiting colonists + carried-home treasure) ────────────────────────────────────────

    /// <summary>
    /// The dock as a horizontal, wrapping <b>row of colonist portrait chips</b> — each chip the colonist's
    /// <b>portrait</b> + short label wrapped in a <c>DockColonist_{id}</c> drag source (drag onto a ship card to board,
    /// drop #1), with a small <c>Board_{person}_{ship}</c> button per ship that has room (<see cref="Game.Board"/>).
    /// Treasure trains carried home and put on the dock each get a chip with the fee-free <see cref="CashInButton"/>.
    /// The whole zone is the Phase-2 drop target (drop #5): dragging an aboard passenger here puts it ashore
    /// (<see cref="DisembarkAllowed"/>/<see cref="OnDisembarkDrop"/>).
    /// </summary>
    private Control DocksZone(IReadOnlyList<Unit> onDock, IReadOnlyList<Unit> ships, IReadOnlyList<Unit> treasureOnDock)
    {
        var drop = new EuropeDropTarget { Name = "DocksDrop" }
            .Configure(DisembarkAllowed, OnDisembarkDrop);

        if (onDock.Count == 0 && treasureOnDock.Count == 0)
        {
            var emptyBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            emptyBox.AddChild(new Label { Text = "(none — drag an aboard colonist here to put it ashore)", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
            return DropZone(drop, emptyBox);
        }

        // A wrapping flow of portrait chips — the docks read as a quay of waiting people, not a stacked text list. The
        // flow + chips + their decoration boxes are mouse-Ignore so a drag-hover falls through to the docks drop target;
        // the chips' DockColonist drag sources and Board/CashIn buttons (descendants) keep their own hover/clicks.
        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);

        foreach (Unit person in onDock)
        {
            Unit pe = person;
            // A bordered chip: the colonist portrait + name + per-ship Board buttons, the portrait+name a drag source.
            var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            chip.AddThemeStyleboxOverride("panel", SlotStyle(filled: true));
            var chipBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            chipBox.AddThemeConstantOverride("separation", 2);
            chip.AddChild(chipBox);

            // The portrait + name are decoration inside the drag source (mouse-Ignore) so the hover reaches that source.
            var portraitBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            portraitBox.AddThemeConstantOverride("separation", 0);
            portraitBox.AddChild(Centered(UnitPortrait(person.Type.ShortName, 44)));
            portraitBox.AddChild(new Label { Text = Display(person.Type.ShortName), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = MouseFilterEnum.Ignore });
            portraitBox.SetAnchorsPreset(LayoutPreset.FullRect);
            // The portrait chip is a drag source — drag it onto a ship card to board (drop #1). A fixed min size keeps the
            // bare drag-source Control (it doesn't adopt its child's min size) from collapsing inside the chip.
            chipBox.AddChild(new EuropeDragSource { Name = $"DockColonist_{person.Id}", MouseFilter = MouseFilterEnum.Pass, CustomMinimumSize = new Vector2(104, 78), SizeFlagsHorizontal = SizeFlags.ExpandFill }
                .Configure(() => ColonistPayload(pe.Id), () => $"{Display(pe.Type.ShortName)} → ship")
                .WithChild(portraitBox));

            foreach (Unit ship in ships.Where(s => _game.CheckBoard(person, s).Allowed))
            {
                Unit sh = ship;
                var board = ActionButton($"Board_{person.Id}_{ship.Id}", $"Board {Display(ship.Type.ShortName)}", () =>
                {
                    if (_game.CheckBoard(pe, sh).Allowed) { _game.Board(pe, sh); }
                    Changed();
                });
                board.SizeFlagsHorizontal = SizeFlags.Fill;
                chipBox.AddChild(board);
            }
            flow.AddChild(chip);
        }

        foreach (Unit train in treasureOnDock)
        {
            Unit tr = train;
            var chip = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            chip.AddThemeStyleboxOverride("panel", SlotStyle(filled: true));
            var chipBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            chipBox.AddThemeConstantOverride("separation", 2);
            chip.AddChild(chipBox);
            chipBox.AddChild(Centered(UnitPortrait(train.Type.ShortName, 44)));
            chipBox.AddChild(new Label { Text = $"{Display(train.Type.ShortName)} ({train.TreasureAmount}g)", HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore });
            if (_game.CheckCashInTreasureTrain(train).Allowed)
            {
                Button cashIn = CashInButton(tr);
                cashIn.SizeFlagsHorizontal = SizeFlags.Fill;
                chipBox.AddChild(cashIn);
            }
            flow.AddChild(chip);
        }
        return DropZone(drop, flow);
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

    /// <summary>A small muted left-aligned sub-heading used inside the two top cards (e.g. "Train a specialist").</summary>
    private static Label MutedLabelLeft(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", Muted);
        return label;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = $"— {text} —",
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>Title-cases a short id for display (<c>freeColonist</c> → "Free Colonist", <c>expertOreMiner</c> → "Expert Ore Miner") — splits on camelCase humps, capitalises the first letter.</summary>
    private static string Display(string shortName)
    {
        if (string.IsNullOrEmpty(shortName))
        {
            return shortName;
        }
        var sb = new System.Text.StringBuilder(shortName.Length + 4);
        sb.Append(char.ToUpperInvariant(shortName[0]));
        for (int i = 1; i < shortName.Length; i++)
        {
            char c = shortName[i];
            if (char.IsUpper(c) && !char.IsUpper(shortName[i - 1]))
            {
                sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Resolves a unit/ship sprite by type short name — the colony screen's portrait lookup (<see cref="UnitMarker.ResolveTexture"/> default role, falling back to <see cref="ColonyArt.UnitIcon"/>), so an expert/ship shows its own art.</summary>
    private static Texture2D? UnitSprite(string typeShortName) =>
        UnitMarker.ResolveTexture(typeShortName, PortraitRole) ?? ColonyArt.UnitIcon(typeShortName);

    /// <summary>A square unit portrait drawn at <paramref name="size"/>×<paramref name="size"/> via <see cref="UnitSprite"/> — used for the recruit/dock colonist chips.</summary>
    private static TextureRect UnitPortrait(string typeShortName, int size) => IconRect(UnitSprite(typeShortName), size, size);

    /// <summary>A non-stretching sprite/icon of fixed size (aspect-kept, mouse-transparent) — the colony screen's <c>IconRect</c> pattern reused so the Europe screen draws real FreeCol art rather than text.</summary>
    private static TextureRect IconRect(Texture2D? texture, int width, int height) => new()
    {
        Texture = texture,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(width, height),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Centres a single control horizontally inside an <see cref="HBoxContainer"/> so a fixed-size icon sits in the middle of its (wider) slot/card. Mouse-Ignore (pure layout around a decoration icon) so a drag-hover over it falls through to the wrapping drag source / drop target.</summary>
    private static Control Centered(Control child)
    {
        var box = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddChild(child);
        return box;
    }

    /// <summary>
    /// Wraps a zone body in a bordered, titled <b>card</b> (a parchment-toned panel with a centred title above the body),
    /// fixed to <paramref name="minWidth"/> so the two top cards line up. Top-aligned (<see cref="SizeFlags.ShrinkBegin"/>
    /// vertical) so a shorter card doesn't get stretched to match a taller neighbour.
    /// </summary>
    private static Control Card(string title, Control body, int minWidth)
    {
        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(minWidth, 0),
            SizeFlagsHorizontal = SizeFlags.Fill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
        };
        frame.AddThemeStyleboxOverride("panel", CardStyle(highlight: false));
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        frame.AddChild(col);
        col.AddChild(new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center });
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        col.AddChild(body);
        return frame;
    }

    /// <summary>
    /// Lays out a drop zone by making the <paramref name="drop"/> target the <b>PARENT</b> of its <paramref name="body"/>
    /// (the zone's card/panel): the body fills the target (<see cref="EuropeDropTarget.SetContent"/> anchors it FullRect
    /// and adopts its min size), so the body's interactive buttons are the target's DESCENDANTS — rendered on top of the
    /// <c>MouseFilter.Pass</c> target and therefore reached by real mouse clicks — while a drag-hover over the card's
    /// non-button area still reaches the target. (The earlier overlay-sibling structure left those buttons dead to real
    /// clicks because a Pass overlay forwards the click to the PARENT, not down to the sibling beneath it.)
    /// </summary>
    private static Control DropZone(EuropeDropTarget drop, Control body)
    {
        drop.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        drop.SetContent(body); // body becomes the drop's content child (FullRect, min-size adopted) → its buttons are descendants
        return drop;
    }

    /// <summary>The bordered-card skin — a slightly raised parchment-brown fill with a warm border; <paramref name="highlight"/> brightens the border (a selected ship card).</summary>
    private static StyleBox CardStyle(bool highlight)
    {
        var s = new StyleBoxFlat
        {
            BgColor = new Color(0.24f, 0.17f, 0.09f, 0.55f),
            BorderColor = highlight ? new Color(0.95f, 0.80f, 0.35f) : new Color(0.45f, 0.36f, 0.22f),
        };
        s.SetBorderWidthAll(highlight ? 3 : 1);
        s.SetCornerRadiusAll(4);
        s.SetContentMarginAll(8);
        return s;
    }

    /// <summary>The "Sail to the New World" drop-panel skin — a distinct cooler blue-tinged border so the departure gate stands apart from the cargo/recruit cards.</summary>
    private static StyleBox SailZoneStyle()
    {
        var s = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.16f, 0.22f, 0.55f),
            BorderColor = new Color(0.45f, 0.62f, 0.82f),
        };
        s.SetBorderWidthAll(2);
        s.SetCornerRadiusAll(4);
        s.SetContentMarginAll(8);
        return s;
    }

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
