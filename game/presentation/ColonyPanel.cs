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
            child.Free();
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
        card.AddChild(SectionLabel("Warehouse"));
        card.AddChild(WarehouseBar());

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

    /// <summary>A colony's per-turn net production: each tile worker's yield + the colony-centre auto-yield, less food eaten.</summary>
    private Dictionary<string, int> NetProduction()
    {
        var net = new Dictionary<string, int>();
        void Add(string good, int amount)
        {
            string stored = _game.Ruleset.StorageIdOf(good);
            net[stored] = net.GetValueOrDefault(stored) + amount;
        }
        foreach ((Position tile, string good) in _colony.TileWorkers)
        {
            Add(good, _game.TileYield(tile, good));
        }
        foreach (ProductionEntry p in _game.Map.TerrainAt(_colony.Position).Productions.Where(p => p.Unattended))
        {
            foreach (GoodsOutput o in p.Outputs)
            {
                Add(o.GoodsId, o.Amount);
            }
        }
        Add(Colony.FoodId, -_colony.Population * Colony.FoodPerColonist);
        return net;
    }

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
                else if (_colony.IdleColonists > 0 && _game.TileWorkOptions(tile) is { Count: > 0 } options)
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
                            _game.AssignWork(_colony, free, options[(int)index - 1].GoodsId);
                            Changed();
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
                else if (!_colony.TileWorkers.ContainsKey(tile) && _game.TileWorkOptions(tile) is { Count: > 0 } target)
                {
                    _game.UnassignWork(_colony, from);
                    _game.AssignWork(_colony, tile, target[0].GoodsId); // move → the new tile's best-yield good
                }
            }
            _heldFrom = null;
        }
        else if (_colony.TileWorkers.ContainsKey(tile))
        {
            _heldFrom = tile; // pick the colonist up
        }
        else if (!isCentre && _colony.IdleColonists > 0 && _game.TileWorkOptions(tile) is { Count: > 0 } free)
        {
            _game.AssignWork(_colony, tile, free[0].GoodsId); // no one held + an idle colonist → put it to work here
        }
        Changed();
    }

    private Control ConstructionPanel()
    {
        var box = new VBoxContainer();
        box.AddChild(SectionLabel("Construction"));
        if (_colony.CurrentBuild is not null)
        {
            BuildingType target = _game.Ruleset.Building(_colony.CurrentBuild);
            string cost = string.Join(", ", target.BuildCost
                .Select(c => $"{Display(Short(c.GoodsId))} {_colony.StoreOf(_game.Ruleset.StorageIdOf(c.GoodsId))}/{c.Amount}"));
            var row = new HBoxContainer();
            row.AddChild(IconRect(ColonyArt.BuildingImage(target.ShortName), 48, 36));
            row.AddChild(new Label { Text = $"Building {Display(target.ShortName)} ({cost})", SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var stop = new Button { Name = "StopBuild", Text = "Stop" };
            stop.Pressed += () => { _game.SetBuild(_colony, null); Changed(); };
            row.AddChild(stop);
            box.AddChild(row);
        }
        else
        {
            var options = new OptionButton { Name = "BuildOptions" };
            options.AddItem("Choose a building…");
            var buildables = _game.Buildables(_colony).ToList();
            foreach (BuildingType b in buildables)
            {
                string cost = string.Join(" + ", b.BuildCost.Select(c => $"{c.Amount} {Display(Short(c.GoodsId))}"));
                options.AddItem($"{Display(b.ShortName)} ({cost})");
            }
            options.ItemSelected += index =>
            {
                if (index > 0)
                {
                    _game.SetBuild(_colony, buildables[(int)index - 1].Id);
                    Changed();
                }
            };
            box.AddChild(options);
        }
        return box;
    }

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
