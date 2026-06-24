using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The colony screen, laid out after FreeCol's: a top production bar, an isometric view of the colony's surrounding
/// tiles (with colonists on the tiles they work), the buildings as images with their workers, the warehouse goods
/// bar, the construction panel, and the units outside the colony. All actions go through Game oracles (ADR-006);
/// the panel renders state (FreeCol GPL art via <see cref="ColonyArt"/>) and forwards clicks. Built programmatically
/// per open/refresh; the rebuild is deferred (see <see cref="Changed"/>) so a control is never freed mid-signal.
/// </summary>
public partial class ColonyPanel : PanelContainer
{
    private Game _game = null!;
    private Colony _colony = null!;
    private Action _onChange = () => { };

    /// <summary>The colony-screen commands that move goods between the warehouse and a docked carrier's holds (wired by <see cref="GameController.OpenColonyPanel"/>; guards + status reporting live there). Args: carrier, colony, goodsId, amount. Default no-ops keep a bare test scene safe.</summary>
    private Action<Unit, Colony, string, int> _loadCargo = (_, _, _, _) => { };
    private Action<Unit, Colony, string, int> _unloadCargo = (_, _, _, _) => { };

    /// <summary>The colony-screen command that sets a good's custom-house export setting (wired by <see cref="GameController.OpenColonyPanel"/>; engine guards + status reporting live there). Args: colony, goodsId, exported, retainLevel. Default no-op keeps a bare test scene safe.</summary>
    private Action<Colony, string, bool, int> _setExport = (_, _, _, _) => { };

    /// <summary>Goods are moved on/off a carrier one hold-slot (100 units) at a time, clamped to what's actually available — FreeCol's per-stack load lot.</summary>
    private const int CargoLot = 100;

    private static readonly Color Negative = new(0.9f, 0.3f, 0.25f);

    /// <summary>The roles a colonist standing in a colony can be armed into from the colony's stores.</summary>
    private static readonly string[] ArmRoles =
        ["model.role.soldier", "model.role.dragoon", "model.role.scout", "model.role.pioneer"];

    /// <summary>Wires the carved-wood frame (a sibling <c>NinePatchRect</c>) to follow the panel's visibility — it overlays the parchment edge, drawn on top with its centre cut out so the content shows through.</summary>
    public override void _Ready()
    {
        if (GetParent().GetNodeOrNull<NinePatchRect>("ColonyBorder") is { } border)
        {
            border.Texture = ColonyArt.ColonyBorder();
            border.Visible = Visible;
            VisibilityChanged += () => border.Visible = Visible;
        }
    }

    /// <summary>
    /// Opens the panel for a colony. <paramref name="onChange"/> runs after every action; <paramref name="loadCargo"/> /
    /// <paramref name="unloadCargo"/> are the host's load/unload-goods commands (carrier, goodsId, amount) for the cargo
    /// section, and <paramref name="setExport"/> is the host's set-custom-house-export command (colony, goodsId, exported,
    /// retainLevel) for the export section — they own the engine guards + status reporting (ADR-006), so the panel only
    /// chooses the carrier/good/lot or the toggle/level and forwards the click. The overload without them keeps existing
    /// callers/tests working with no-op commands.
    /// </summary>
    public void Open(Game game, Colony colony, Action onChange, Action<Unit, Colony, string, int> loadCargo, Action<Unit, Colony, string, int> unloadCargo, Action<Colony, string, bool, int> setExport)
    {
        _loadCargo = loadCargo;
        _unloadCargo = unloadCargo;
        _setExport = setExport;
        Open(game, colony, onChange);
    }

    /// <summary>Opens the panel for a colony. <paramref name="onChange"/> runs after every action.</summary>
    public void Open(Game game, Colony colony, Action onChange)
    {
        _game = game;
        _colony = colony;
        _onChange = onChange;
        _heldFrom = null;
        EnsureOpaqueBackground();
        Theme = ColonyTheme.Get(); // cohesive parchment/wood styling cascades to every child
        Rebuild();
        Show();
    }

    private static StyleBox? _panelBackground;

    /// <summary>
    /// Gives the panel an opaque background so the map (drawn behind the UI layer) never shows through. The default
    /// <see cref="PanelContainer"/> stylebox is effectively transparent here, which let the map bleed across the
    /// colony screen. Prefers FreeCol's tiled brown parchment skin; falls back to a warm solid fill if the asset is
    /// absent (so it is opaque in CI even before the parchment is imported). Built once and shared.
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
                // Tile, don't stretch: bg_paper_brown is only 291×295; stretched across a ~1900px panel it blurs.
                AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
                AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
            };
            skin.SetContentMarginAll(26); // inset so the content clears the 23px carved-wood frame
            return skin;
        }
        var flat = new StyleBoxFlat { BgColor = new Color(0.18f, 0.12f, 0.07f) }; // warm parchment-brown fallback
        flat.SetContentMarginAll(26);
        return flat;
    }

    /// <summary>
    /// Signals a finished action. The rebuild is <b>deferred</b> so a control is never freed inside its own signal
    /// callback (freeing an OptionButton mid-<c>ItemSelected</c>, popup still closing, crashes Godot). The game-state
    /// change has already happened synchronously.
    /// </summary>
    private void Changed() => Callable.From(ApplyChange).CallDeferred();

    private void ApplyChange()
    {
        _onChange();
        Rebuild();
    }

    /// <summary>
    /// Rebuilds the panel only, deferred (signal-safe) — for actions whose command has <b>already</b> refreshed the host
    /// HUD and set the status notice (the cargo load/unload commands, 86d3f5y8r). Calling <see cref="Changed"/> here would
    /// run the host refresh a second time and clear the just-set notice before the player sees it; this re-renders the
    /// panel's own hold occupancy + cargo rows without disturbing the status bar.
    /// </summary>
    private void RebuildDeferred() => Callable.From(Rebuild).CallDeferred();

    private void Act(int unitId, Action<Unit> action)
    {
        if (_game.Units.FirstOrDefault(u => u.Id == unitId) is { } unit)
        {
            action(unit);
        }
        Changed();
    }

    private static string Short(string id) => id[(id.LastIndexOf('.') + 1)..];

    /// <summary>A tile's per-worker yield as the colony actually banks it — the base plus the Sons-of-Liberty production bonus, floored at 0 (mirrors the production turn).</summary>
    private int EffectiveYield(int baseYield) => Math.Max(0, baseYield + _colony.ProductionBonus);

    /// <summary>
    /// A human display name for a ruleset short-name, by splitting camelCase and capitalising — pure presentation
    /// (ADR-006: no model data). e.g. <c>tobacconistHouse</c> → "Tobacconist House". Used for label text only; the
    /// control <c>Name</c>s the tests query still use <see cref="Short"/>.
    /// </summary>
    private static string Display(string shortName)
    {
        var sb = new System.Text.StringBuilder(shortName.Length + 4);
        for (int i = 0; i < shortName.Length; i++)
        {
            char c = shortName[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(shortName[i - 1]))
            {
                sb.Append(' ');
            }
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/ColonyTitle").Text = _colony.Name;
        GetNode<Label>("VBox/ColonyInfo").Text =
            $"Population: {_colony.Population} ({_colony.IdleColonists} idle)   |   " +
            $"Food: {_colony.Food}/{Colony.FoodForGrowth}   |   Defence: +{_game.ColonyDefenceBonus(_colony)}%";

        var root = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        root.AddThemeConstantOverride("separation", 8);
        foreach (Node child in root.GetChildren())
        {
            root.RemoveChild(child); child.QueueFree(); // detach now (signal-safe), free deferred — avoids freed-while-emitting when a child button's handler drives the rebuild
        }

        // All bands live inside a content "card" that sizes to its content and CENTRES (ShrinkCenter): on wide
        // windows it sits centred with balanced parchment margins instead of spraying across the width; on windows
        // narrower than the content the Scroll's horizontal-auto mode (main.tscn) gives a scrollbar rather than a
        // hard right-edge clip. This is the resize fix — the grid stays a fixed 4 columns (test-locked).
        var card = new VBoxContainer { Name = "ContentCard", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        card.AddThemeConstantOverride("separation", 10);

        card.AddChild(ProductionBar());

        var middle = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        middle.AddThemeConstantOverride("separation", 16);
        middle.AddChild(LeftColumn());      // fixed-width band (tiles + construction)
        middle.AddChild(BuildingsColumn()); // the 4-wide buildings grid
        card.AddChild(middle);

        card.AddChild(SectionLabel("Outside the colony"));
        card.AddChild(OutsideArea());
        // The cargo section only renders when a carrier (ship/wagon) is at or beside the colony, so a fresh colony's
        // screen (and its visual golden) is unchanged. Goods load/unload between the warehouse and the holds (86d3f5y8r).
        if (DockedCarriers() is { Count: > 0 } carriers)
        {
            card.AddChild(SectionLabel("Cargo"));
            card.AddChild(CargoArea(carriers));
        }
        card.AddChild(SectionLabel("Warehouse"));
        card.AddChild(WarehouseBar());

        // The custom-house export section only renders when the colony actually has a custom house (the engine's
        // auto-sell likewise no-ops without one) — so a fresh colony's screen and its visual golden are unchanged.
        if (_game.ColonyHasCustomHouse(_colony))
        {
            card.AddChild(SectionLabel("Custom house — exports"));
            card.AddChild(CustomHouseArea());
        }

        root.AddChild(card);
    }

    // ── Top: the colony's net production, FreeCol's production row ───────────────────────────────────────────

    private Control ProductionBar()
    {
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddChild(new Label { Text = "Producing: " });
        foreach ((string good, int net) in NetProduction().OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (net == 0)
            {
                continue;
            }
            var cell = new VBoxContainer();
            cell.AddChild(IconRect(ColonyArt.GoodsIcon(Short(good)), 28, 28));
            var amount = new Label { Text = (net > 0 ? "+" : "") + net, HorizontalAlignment = HorizontalAlignment.Center };
            if (net < 0)
            {
                amount.AddThemeColorOverride("font_color", Negative); // theme ink is dark; flag shortfalls in red
            }
            cell.AddChild(amount);
            bar.AddChild(cell);
        }
        return bar;
    }

    /// <summary>
    /// A colony's per-turn net production (tile yields + colony-centre auto-yield, less food eaten), via the shared
    /// <see cref="Game.ColonyNetProduction"/> oracle so the colony screen and the empire report show one tested figure.
    /// </summary>
    private IReadOnlyDictionary<string, int> NetProduction() => _game.ColonyNetProduction(_colony);

    // ── Left: the isometric surrounding tiles + the construction panel ──────────────────────────────────────

    // The isometric tile diamonds, enlarged 1.25× from the map's 128×64 so the colony view reads like FreeCol's.
    private const int TileW = 160;
    private const int TileH = 80;

    private Control LeftColumn()
    {
        // A fixed-width band (NOT expand) so the buildings column to its right takes the elastic width — one
        // horizontal expander in the row, mirroring FreeCol's [fill][474!] columns.
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(500, 0), SizeFlagsHorizontal = SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 8);
        col.AddChild(SonsOfLibertyBar());
        Control tiles = IsometricTiles();
        tiles.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; // centre the fixed-size tile view in the column
        col.AddChild(tiles);
        col.AddChild(ConstructionPanel());
        return col;
    }

    /// <summary>
    /// FreeCol's population / Sons-of-Liberty band: Rebels (count + SoL%) · Population (+ the production bonus) ·
    /// Royalists (count + 100−SoL%), over a two-segment SoL meter. Reads the colony's computed SoL properties only
    /// (ADR-006 — the rules live in <see cref="Colony"/>); the rebel/royalist nation shields are a deferred follow-up
    /// (no coat-of-arms art imported yet).
    /// </summary>
    private Control SonsOfLibertyBar()
    {
        int sol = _colony.SonsOfLiberty;
        int bonus = _colony.ProductionBonus;

        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 3);

        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 24);
        row.AddChild(StatCell($"Rebels: {_colony.RebelCount}", "RebelCount", $"{sol}%", "RebelPercent"));

        var centre = new VBoxContainer();
        centre.AddChild(new Label { Name = "PopulationCount", Text = $"Population: {_colony.Population}", HorizontalAlignment = HorizontalAlignment.Center });
        var bonusLabel = new Label { Name = "ProductionBonus", Text = $"Bonus: {(bonus >= 0 ? "+" : "")}{bonus}", HorizontalAlignment = HorizontalAlignment.Center };
        if (bonus < 0)
        {
            bonusLabel.AddThemeColorOverride("font_color", Negative);
        }
        centre.AddChild(bonusLabel);
        row.AddChild(centre);

        row.AddChild(StatCell($"Royalists: {_colony.ToryCount}", "RoyalistCount", $"{100 - sol}%", "RoyalistPercent"));
        box.AddChild(row);
        box.AddChild(SolMeter(sol));
        return box;
    }

    private static Control StatCell(string topText, string topName, string bottomText, string bottomName)
    {
        var cell = new VBoxContainer();
        cell.AddChild(new Label { Name = topName, Text = topText, HorizontalAlignment = HorizontalAlignment.Center });
        cell.AddChild(new Label { Name = bottomName, Text = bottomText, HorizontalAlignment = HorizontalAlignment.Center });
        return cell;
    }

    /// <summary>A thin two-segment Sons-of-Liberty meter: a gold rebel fill proportioned to the SoL%, a dark royalist remainder.</summary>
    private static Control SolMeter(int solPercent)
    {
        var meter = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 10) };
        meter.AddThemeConstantOverride("separation", 0);
        meter.AddChild(new ColorRect { Color = new Color(0.79f, 0.64f, 0.29f), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = Math.Max(solPercent, 0) });
        meter.AddChild(new ColorRect { Color = new Color(0.29f, 0.18f, 0.10f), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = Math.Max(100 - solPercent, 0) });
        return meter;
    }

    /// <summary>The tile a picked-up colonist was lifted from (click-to-move), or null when nothing is held.</summary>
    private Position? _heldFrom;

    /// <summary>
    /// The colony's 3×3 surrounding tiles as overlapping FreeCol diamonds: the colony at the centre, a colonist on
    /// each worked tile. <b>Click-to-move</b> (FreeCol's drag-a-colonist gesture, click-based so it stays testable):
    /// click a worked tile to pick its colonist up (it highlights), then click a free tile to send it there (the
    /// tile's best-yield good) or the colony centre to send it idle. The per-tile ✕ release and the *Work…* good
    /// picker remain for explicit control / choosing a specific good.
    /// </summary>
    private Control IsometricTiles()
    {
        var view = new Control { Name = "TilesView", CustomMinimumSize = new Vector2(500, 344) };
        var centre = new Vector2(250, 152);
        var half = new Vector2(TileW / 2, TileH / 2);
        if (_heldFrom is not null)
        {
            Place(view, new Label { Text = "Click a tile to move the colonist — the colony centre sends it idle" }, new Vector2(8, 0));
        }
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Position tile = new(_colony.Position.X + dx, _colony.Position.Y + dy);
                Vector2 topLeft = centre + new Vector2((dx - dy) * (TileW / 2), (dx + dy) * (TileH / 2)) - half;
                if (!_game.Map.InBounds(tile))
                {
                    continue;
                }
                foreach (Texture2D tex in ColonyArt.TerrainTextures(_game.Map.TerrainAt(tile).ShortName))
                {
                    Place(view, IconRect(tex, TileW, TileH), topLeft);
                }

                // A transparent whole-tile hit button drives click-to-move. Added before the small ✕/picker controls
                // so those (placed after → higher z) still receive their own clicks; clicks anywhere else on the tile
                // fall through to this.
                Position clicked = tile;
                var hit = new Button
                {
                    Name = $"Tile_{tile.X}_{tile.Y}",
                    Flat = true,
                    Modulate = new Color(1, 1, 1, 0),
                    CustomMinimumSize = new Vector2(TileW, TileH),
                };
                hit.Pressed += () => OnTileClicked(clicked);
                Place(view, hit, topLeft);

                if (dx == 0 && dy == 0)
                {
                    Place(view, IconRect(ColonyArt.ColonyIcon(), 120, 80), topLeft + new Vector2(20, 0));
                    continue;
                }
                if (_colony.TileWorkers.TryGetValue(tile, out string? good))
                {
                    TextureRect colonist = IconRect(ColonyArt.UnitIcon("freeColonist"), 56, 56);
                    if (_heldFrom == tile)
                    {
                        colonist.Modulate = new Color(1f, 0.9f, 0.3f); // picked up — highlight the held colonist
                    }
                    Place(view, colonist, topLeft + new Vector2(52, 8));
                    Place(view, Badge($"{Display(Short(good))} {EffectiveYield(_game.TileYield(tile, good))}"), topLeft + new Vector2(44, 0));
                    Position worked = tile;
                    var release = new Button { Name = $"Release_{tile.X}_{tile.Y}", Text = "✕", CustomMinimumSize = new Vector2(24, 20) };
                    release.Pressed += () => { _game.UnassignWork(_colony, worked); _heldFrom = null; Changed(); };
                    Place(view, release, topLeft + new Vector2(64, 50));
                }
                else if (_colony.IdleColonists > 0 && _game.ColonyCanWorkTile(_colony, tile)
                         && _game.TileWorkOptions(tile) is { Count: > 0 } options)
                {
                    var picker = new OptionButton { Name = $"Work_{tile.X}_{tile.Y}", CustomMinimumSize = new Vector2(104, 24) };
                    picker.AddItem("Work…");
                    foreach ((string goodsId, int yield) in options)
                    {
                        picker.AddItem($"{Display(Short(goodsId))} {EffectiveYield(yield)}");
                    }
                    Position free = tile;
                    picker.ItemSelected += index =>
                    {
                        if (index > 0)
                        {
                            AssignWorkWithClaim(free, options[(int)index - 1].GoodsId);
                        }
                    };
                    Place(view, picker, topLeft + new Vector2(28, 28));
                }
            }
        }
        return view;
    }

    /// <summary>Click-to-move worker management for a clicked surrounding tile (see <see cref="IsometricTiles"/>).</summary>
    private void OnTileClicked(Position tile)
    {
        bool isCentre = tile == _colony.Position;
        if (_heldFrom is { } from)
        {
            if (tile != from)
            {
                if (isCentre)
                {
                    _game.UnassignWork(_colony, from); // drop on the town → send the colonist idle
                }
                else if (!_colony.TileWorkers.ContainsKey(tile) && _game.ColonyCanWorkTile(_colony, tile)
                         && _game.TileWorkOptions(tile) is { Count: > 0 } target)
                {
                    _game.UnassignWork(_colony, from);
                    AssignWorkWithClaim(tile, target[0].GoodsId); // move → the new tile's best-yield good
                    return; // AssignWorkWithClaim refreshes (possibly after the claim dialog)
                }
            }
            _heldFrom = null;
        }
        else if (_colony.TileWorkers.ContainsKey(tile))
        {
            _heldFrom = tile; // pick the colonist up
        }
        else if (!isCentre && _colony.IdleColonists > 0 && _game.ColonyCanWorkTile(_colony, tile)
                 && _game.TileWorkOptions(tile) is { Count: > 0 } free)
        {
            AssignWorkWithClaim(tile, free[0].GoodsId); // no one held + an idle colonist → put it to work here
            return; // AssignWorkWithClaim refreshes (possibly after the claim dialog)
        }
        Changed();
    }

    /// <summary>
    /// Assigns a colonist to <paramref name="tile"/> to produce <paramref name="goodsId"/>, first resolving the forced
    /// buy-or-steal-or-abandon claim if the tile is native-owned (86d3e4bj7): on free land it assigns immediately; on
    /// native land it raises the choice modal and routes through the <see cref="Game.AssignWork(Colony, Position, string, LandClaimChoice)"/>
    /// overload (Abandon leaves the colonist idle). No pending state is stored — the (tile, good) context is held by the
    /// closure and the claim is resolved synchronously on the click. Always <see cref="Changed"/>s afterward.
    /// </summary>
    private void AssignWorkWithClaim(Position tile, string goodsId)
    {
        Game.ForcedLandClaim forced = _game.RequiredLandClaim(tile);
        if (!forced.Required)
        {
            _game.AssignWork(_colony, tile, goodsId);
            Changed();
            return;
        }

        string nation = NationLabel(forced.OwningNation!);
        bool canAfford = _game.HumanPlayer.Gold >= forced.BuyPrice;
        var dialog = new ConfirmationDialog
        {
            Title = "Native land",
            DialogText = $"The {nation} own this tile.\nBuy it for {forced.BuyPrice} gold, take it by force (angering them), or abandon working it?",
            OkButtonText = canAfford ? $"Buy ({forced.BuyPrice}g)" : $"Buy ({forced.BuyPrice}g — can't afford)",
            CancelButtonText = "Abandon",
        };
        Button stealButton = dialog.AddButton("Steal", right: true, action: "steal");
        if (!canAfford)
        {
            dialog.GetOkButton().Disabled = true;
        }

        void Finish(LandClaimChoice? choice)
        {
            dialog.QueueFree();
            if (choice is not null)
            {
                _game.AssignWork(_colony, tile, goodsId, choice.Value);
            }
            Changed();
        }

        dialog.Confirmed += () => Finish(LandClaimChoice.Buy);
        dialog.Canceled += () => Finish(null); // Abandon → colonist stays idle
        stealButton.Pressed += () => Finish(LandClaimChoice.Steal);
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>Human-readable nation name from a nation-type id (e.g. <c>model.nationType.apache</c> → "Apache").</summary>
    private static string NationLabel(string nationId)
    {
        string shortName = nationId[(nationId.LastIndexOf('.') + 1)..];
        return shortName.Length == 0 ? shortName : char.ToUpperInvariant(shortName[0]) + shortName[1..];
    }

    private Control ConstructionPanel()
    {
        var box = new VBoxContainer();
        box.AddChild(SectionLabel("Construction"));

        // — The build queue (the front item is built first; each row can be reordered or removed). A queued id the
        //   ruleset no longer knows is skipped here (the engine drops it at build time). Buildings AND units appear,
        //   each rendered from DescribeBuildable so a queued unit no longer mis-renders as a building. —
        IReadOnlyList<string> queue = _colony.BuildQueue;
        if (queue.Count == 0)
        {
            box.AddChild(new Label { Text = "(nothing under construction)", HorizontalAlignment = HorizontalAlignment.Center });
        }
        for (int i = 0; i < queue.Count; i++)
        {
            if (_game.DescribeBuildable(queue[i]) is not { } info)
            {
                continue;
            }
            bool front = i == 0;
            string detail = front
                ? string.Join(", ", info.BuildCost.Select(c =>
                    $"{Display(Short(c.GoodsId))} {_colony.StoreOf(_game.Ruleset.StorageIdOf(c.GoodsId))}/{c.Amount}"))
                : CostLabel(info.BuildCost);
            Texture2D? icon = info.IsUnit ? ColonyArt.UnitIcon(info.ShortName) : ColonyArt.BuildingImage(info.ShortName);

            var row = new HBoxContainer();
            row.AddChild(IconRect(icon, 48, 36));
            row.AddChild(new Label
            {
                Text = $"{(front ? "▶ " : "")}{Display(info.ShortName)} ({detail})",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });

            int index = i;
            var up = new Button { Name = $"Up_{index}", Text = "▲", Disabled = index == 0 };
            up.Pressed += () => { _game.MoveBuildQueueItem(_colony, index, -1); Changed(); };
            row.AddChild(up);
            var down = new Button { Name = $"Down_{index}", Text = "▼", Disabled = index == queue.Count - 1 };
            down.Pressed += () => { _game.MoveBuildQueueItem(_colony, index, +1); Changed(); };
            row.AddChild(down);
            var remove = new Button { Name = $"RemoveBuild_{index}", Text = "✕" };
            remove.Pressed += () => { _game.RemoveFromBuildQueue(_colony, index); Changed(); };
            row.AddChild(remove);

            box.AddChild(row);
        }

        // — Add a building or a unit to the end of the queue (FreeCol's BuildQueuePanel offers both) —
        var options = new OptionButton { Name = "BuildOptions" };
        options.AddItem("Add to queue…");
        var addable = new List<string>();
        foreach (BuildingType b in _game.Buildables(_colony))
        {
            addable.Add(b.Id);
            options.AddItem($"{Display(b.ShortName)} ({CostLabel(b.BuildCost)})");
        }
        foreach (UnitType u in _game.BuildableUnits(_colony))
        {
            addable.Add(u.Id);
            options.AddItem($"{Display(u.ShortName)} ({CostLabel(u.BuildCostOrEmpty)}) — unit");
        }
        options.ItemSelected += selected =>
        {
            if (selected > 0)
            {
                _game.EnqueueBuild(_colony, addable[(int)selected - 1]);
                Changed();
            }
        };
        box.AddChild(options);

        if (queue.Count > 0)
        {
            var clear = new Button { Name = "StopBuild", Text = "Clear queue" };
            clear.Pressed += () => { _game.SetBuild(_colony, null); Changed(); };
            box.AddChild(clear);
        }
        return box;
    }

    private string CostLabel(IReadOnlyList<GoodsOutput> cost) =>
        string.Join(" + ", cost.Select(c => $"{c.Amount} {Display(Short(c.GoodsId))}"));

    // ── Right: the colony's buildings as FreeCol images, with their workers ─────────────────────────────────

    private Control BuildingsColumn()
    {
        // The 4-wide buildings grid, sized to its content (the content card centres the whole row). The screen already
        // scrolls (VBox/Scroll), so no inner scroll is needed.
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        var grid = new GridContainer { Columns = 4, Name = "BuildingsGrid", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 8);
        foreach (string buildingId in _colony.Buildings)
        {
            grid.AddChild(BuildingCell(buildingId));
        }
        col.AddChild(grid);
        return col;
    }

    private Control BuildingCell(string buildingId)
    {
        BuildingType building = _game.Ruleset.Building(buildingId);
        int workers = _colony.BuildingWorkers.GetValueOrDefault(buildingId);
        var cell = new PanelContainer { ThemeTypeVariation = "BuildingCell", CustomMinimumSize = new Vector2(142, 0) };
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 2);
        cell.AddChild(box);

        box.AddChild(IconRect(ColonyArt.BuildingImage(building.ShortName), 124, 70));
        // Display name wraps to (at most) two reserved lines so long names like "Tobacconist House" don't spill the
        // cell, and every cell stays the same height.
        var label = new Label
        {
            Text = $"{Display(building.ShortName)} ({workers}/{building.Workplaces})",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(124, 36),
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(label);

        var controls = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        if (_game.CheckAssignBuildingWork(_colony, buildingId).Allowed)
        {
            var add = new Button { Name = $"Staff_{building.ShortName}", Text = "+" };
            add.Pressed += () => { _game.AssignBuildingWork(_colony, buildingId); Changed(); };
            controls.AddChild(add);
        }
        if (workers > 0)
        {
            var remove = new Button { Name = $"Unstaff_{building.ShortName}", Text = "−" };
            remove.Pressed += () => { _game.UnassignBuildingWork(_colony, buildingId); Changed(); };
            controls.AddChild(remove);
        }
        box.AddChild(controls);
        return cell;
    }

    // ── Bottom: units outside the colony, and the warehouse goods bar ───────────────────────────────────────

    private Control OutsideArea()
    {
        var box = new VBoxContainer();
        if (_game.CheckLeaveColony(_colony).Allowed)
        {
            var leave = new Button { Name = "LeaveColony", Text = "Send a colonist out" };
            leave.Pressed += () => { _game.LeaveColony(_colony); Changed(); };
            box.AddChild(leave);
        }
        foreach (Unit unit in _game.PlayerUnits
            .Where(u => u.IsOnMap && (u.Position == _colony.Position || u.Position.IsAdjacentTo(_colony.Position)))
            .OrderBy(u => u.Id))
        {
            var buttons = new List<Button>();
            int uid = unit.Id;
            if (_game.CheckJoinColony(unit, _colony).Allowed)
            {
                buttons.Add(MakeButton($"Join_{uid}", "Join colony", () => Act(uid, u => _game.JoinColony(u, _colony))));
            }
            foreach (string roleId in ArmRoles)
            {
                if (_game.CheckEquipRole(unit, _colony, roleId).Allowed)
                {
                    string r = roleId;
                    buttons.Add(MakeButton($"Equip_{uid}_{Short(roleId)}", $"Arm {Display(Short(roleId))}", () => Act(uid, u => _game.EquipRole(u, _colony, r))));
                }
            }
            if (!unit.HasDefaultRole && _game.CheckEquipRole(unit, _colony, RoleType.DefaultRoleId).Allowed)
            {
                buttons.Add(MakeButton($"Disarm_{uid}", "Disarm", () => Act(uid, u => _game.EquipRole(u, _colony, RoleType.DefaultRoleId))));
            }
            if (buttons.Count == 0)
            {
                continue;
            }
            var row = new HBoxContainer();
            row.AddChild(IconRect(ColonyArt.UnitIcon(unit.Type.ShortName), 36, 36));
            string role = unit.HasDefaultRole ? "" : $" ({Display(Short(unit.RoleId))})";
            row.AddChild(new Label { Text = $"{Display(unit.Type.ShortName)}{role} at ({unit.Position.X},{unit.Position.Y})", SizeFlagsHorizontal = SizeFlags.ExpandFill });
            foreach (Button button in buttons)
            {
                row.AddChild(button);
            }
            box.AddChild(row);
        }
        return box;
    }

    private Control WarehouseBar()
    {
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var stored = _colony.Stores.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        if (stored.Count == 0)
        {
            bar.AddChild(new Label { Text = "(empty)" });
            return bar;
        }
        foreach ((string good, int amount) in stored)
        {
            var cell = new VBoxContainer();
            cell.AddChild(IconRect(ColonyArt.GoodsIcon(Short(good)), 28, 28));
            cell.AddChild(new Label { Text = amount.ToString(), HorizontalAlignment = HorizontalAlignment.Center });
            bar.AddChild(cell);
        }
        return bar;
    }

    // ── Cargo: load/unload goods between the warehouse and a docked carrier's holds (86d3f5y8r) ────────────────

    /// <summary>
    /// The player's carriers (a ship <em>or</em> a wagon train — <see cref="UnitType.IsCarrier"/>) standing on or next to
    /// the colony, ordered by id. These are the units that can take cargo on/off the warehouse here; the same
    /// at-or-adjacent reach the <see cref="Game.LoadFromColony"/>/<see cref="Game.UnloadToColony"/> oracles enforce, so a
    /// carrier the panel offers is one the engine will accept. An empty list hides the whole cargo section.
    /// </summary>
    private List<Unit> DockedCarriers() => _game.PlayerUnits
        .Where(u => u.IsOnMap && u.Type.IsCarrier
                    && (u.Position == _colony.Position || u.Position.IsAdjacentTo(_colony.Position)))
        .OrderBy(u => u.Id)
        .ToList();

    /// <summary>
    /// For each docked/adjacent carrier: a row showing its type and <b>hold occupancy</b> ("hold 2/4"), a <em>Load goods…</em>
    /// picker drawing from the warehouse (each good with stock, loaded a <see cref="CargoLot"/> at a time, clamped to stock),
    /// and one <em>Unload</em> button per goods stack already aboard (unloading a lot, clamped to what's carried). Each control
    /// forwards to the host's load/unload command, which owns the engine guards + status reporting (ADR-006). The picker amount
    /// is pre-clamped to hold/stock so the common case never trips a guard, but the engine remains the gate (a hold-full / out-of-stock
    /// attempt is refused there and surfaced in the status bar, never thrown to the UI).
    /// </summary>
    private Control CargoArea(IReadOnlyList<Unit> carriers)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 6);
        foreach (Unit carrier in carriers)
        {
            int uid = carrier.Id;
            var header = new HBoxContainer();
            header.AddChild(IconRect(ColonyArt.UnitIcon(carrier.Type.ShortName), 36, 36));
            header.AddChild(new Label
            {
                Name = $"CargoHold_{uid}",
                Text = $"{Display(carrier.Type.ShortName)} — hold {_game.CargoSlotsUsed(carrier)}/{_game.CargoCapacity(carrier)}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            box.AddChild(header);

            // Load: pick a warehouse good (with stock) to load a lot onto this carrier.
            var stored = _colony.Stores.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
            var loadable = new List<string>();
            var loadPicker = new OptionButton { Name = $"Load_{uid}" };
            loadPicker.AddItem("Load goods…");
            foreach ((string good, int amount) in stored)
            {
                loadable.Add(good);
                loadPicker.AddItem($"{Display(Short(good))} ({amount})");
            }
            loadPicker.Disabled = loadable.Count == 0 || _game.CargoSlotsFree(carrier) <= 0; // nothing to load / no room
            loadPicker.ItemSelected += index =>
            {
                if (index > 0)
                {
                    string good = loadable[(int)index - 1];
                    _loadCargo(carrier, _colony, good, Math.Min(CargoLot, _colony.StoreOf(good))); // a lot, clamped to stock
                    RebuildDeferred(); // the command already refreshed the HUD + set the status notice
                }
            };
            box.AddChild(loadPicker);

            // Unload: one button per goods stack aboard, returning a lot to the warehouse.
            foreach ((string good, int amount) in carrier.Cargo.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                string g = good;
                var row = new HBoxContainer();
                row.AddChild(IconRect(ColonyArt.GoodsIcon(Short(good)), 28, 28));
                row.AddChild(new Label { Text = $"{Display(Short(good))} {amount}", SizeFlagsHorizontal = SizeFlags.ExpandFill });
                var unload = new Button { Name = $"Unload_{uid}_{Short(good)}", Text = "Unload" };
                unload.Pressed += () =>
                {
                    _unloadCargo(carrier, _colony, g, Math.Min(CargoLot, carrier.CargoOf(g))); // a lot, clamped to what's carried
                    RebuildDeferred(); // the command already refreshed the HUD + set the status notice
                };
                row.AddChild(unload);
                box.AddChild(row);
            }
        }
        return box;
    }

    // ── Custom house: per-good export toggle + retain level (86d3f62q8) ────────────────────────────────────────

    /// <summary>
    /// The colony's storable, tradeable goods (those that have a European market) in stable id order — the goods a
    /// custom house can auto-export. Mirrors the engine's eligibility gate in <see cref="Game.SetColonyExport"/>
    /// (storable + tradeable; food is included — FreeCol's custom house can export food, it just defaults off), so a
    /// good the panel offers is one the engine will accept.
    /// </summary>
    private IEnumerable<GoodsType> ExportableGoods() => _game.Ruleset.GoodsTypes
        .Where(g => g.IsStorable && g.IsTradeable)
        .OrderBy(g => g.Id, StringComparer.Ordinal);

    /// <summary>
    /// FreeCol's custom-house export controls: one row per exportable good with an <b>Exported</b> on/off
    /// <see cref="CheckBox"/> and a <b>retain-level</b> <see cref="SpinBox"/> (the warehouse amount to keep before the
    /// surplus auto-sells each turn). Both forward to the host's <see cref="_setExport"/> command, which owns the engine
    /// guards + status reporting (ADR-006); the panel only reads <see cref="Colony.ExportOf"/> for the current setting and
    /// forwards the change. Toggling a good on (or off) sets it with the current level; changing the level keeps the
    /// current on/off flag. The engine then auto-sells each exported good over its retain level on End Turn — only goods
    /// flagged here sell (the default per-good mode), which is why this UI is what makes a custom house actually trade.
    /// </summary>
    private Control CustomHouseArea()
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        box.AddChild(new Label
        {
            Text = "Tick a good to auto-sell its surplus over the retain level each turn.",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        foreach (GoodsType good in ExportableGoods())
        {
            string goodsId = good.Id;
            Colony.ExportSetting setting = _colony.ExportOf(goodsId);

            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(IconRect(ColonyArt.GoodsIcon(good.ShortName), 28, 28));

            var toggle = new CheckBox
            {
                Name = $"Export_{good.ShortName}",
                Text = Display(good.ShortName),
                ButtonPressed = setting.Exported,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            // Set with the level shown in the (about-to-be-rebuilt) row's spinner so a toggle never resets the level.
            toggle.Toggled += on => _setExport(_colony, goodsId, on, setting.ExportLevel);
            row.AddChild(toggle);

            row.AddChild(new Label { Text = "keep" });
            var level = new SpinBox
            {
                Name = $"Retain_{good.ShortName}",
                MinValue = 0,
                MaxValue = _game.ColonyWarehouseCapacity(_colony),
                Step = CargoLot, // one hold-lot per click; the player can still type any value
                Value = setting.ExportLevel,
                CustomMinimumSize = new Vector2(96, 0),
            };
            // Keep the current on/off flag when only the retain level changes.
            level.ValueChanged += value => _setExport(_colony, goodsId, setting.Exported, (int)value);
            row.AddChild(level);

            box.AddChild(row);
        }
        return box;
    }

    // ── Small UI helpers ────────────────────────────────────────────────────────────────────────────────────

    private static TextureRect IconRect(Texture2D? texture, int width, int height) => new()
    {
        Texture = texture,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(width, height),
        Size = new Vector2(width, height),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    /// <summary>Places a free-positioned child at <paramref name="pos"/> inside a layout-free <see cref="Control"/>; a control with no explicit size (a button/picker) is grown to its minimum so it renders.</summary>
    private static void Place(Control parent, Control child, Vector2 pos)
    {
        parent.AddChild(child);
        child.Position = pos;
        if (child.Size == Vector2.Zero)
        {
            child.Size = child.GetCombinedMinimumSize();
        }
    }

    // A tile overlay caption (e.g. "Grain 4"): forced light text with a dark halo so it reads on any terrain — the
    // theme's default dark-ink Label colour would vanish against the diamonds.
    private static Label Badge(string text)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    private static Button MakeButton(string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        return button;
    }

    /// <summary>A section divider: an engraved wood rule above a centred, header-styled caption.</summary>
    private static Control SectionLabel(string text)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(new HSeparator());
        box.AddChild(new Label { Text = text, ThemeTypeVariation = "SectionHeader", HorizontalAlignment = HorizontalAlignment.Center });
        return box;
    }
}
