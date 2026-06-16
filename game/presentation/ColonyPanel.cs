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

    /// <summary>Opens the panel for a colony. <paramref name="onChange"/> runs after every action.</summary>
    public void Open(Game game, Colony colony, Action onChange)
    {
        _game = game;
        _colony = colony;
        _onChange = onChange;
        _heldFrom = null;
        EnsureOpaqueBackground();
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
            skin.SetContentMarginAll(16);
            return skin;
        }
        var flat = new StyleBoxFlat { BgColor = new Color(0.18f, 0.12f, 0.07f) }; // warm parchment-brown fallback
        flat.SetContentMarginAll(16);
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

        // Three bands stacked in the Dynamic VBox (which fills the panel — main.tscn sets its vertical ExpandFill):
        //  1) the production strip (natural height),
        //  2) the MIDDLE row — the ONLY child with vertical ExpandFill, so it absorbs all the slack and the bands
        //     below it can never be stranded in a void,
        //  3) the outside-units + warehouse bars (natural height) pinned beneath it.
        root.AddChild(ProductionBar());

        // The middle row takes its natural height; with no in-port/cargo band to fill a tall middle (FreeCol has one,
        // we don't yet), the content stacks compactly from the top and the panel's parchment fills the space below the
        // warehouse — cleaner than a gap floating mid-screen.
        var middle = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 12);
        middle.AddChild(LeftColumn());      // fixed-width band (tiles + construction)
        middle.AddChild(BuildingsColumn()); // expands to fill the remaining width
        root.AddChild(middle);

        root.AddChild(SectionLabel("Outside the colony"));
        root.AddChild(OutsideArea());
        root.AddChild(SectionLabel("Warehouse"));
        root.AddChild(WarehouseBar());
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
            cell.AddChild(new Label { Text = (net > 0 ? "+" : "") + net, HorizontalAlignment = HorizontalAlignment.Center, Modulate = net < 0 ? Negative : Colors.White });
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
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0), SizeFlagsHorizontal = SizeFlags.Fill };
        col.AddThemeConstantOverride("separation", 8);
        col.AddChild(PopulationStrip());
        Control tiles = IsometricTiles();
        tiles.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; // centre the fixed-size tile view in the column
        col.AddChild(tiles);
        col.AddChild(ConstructionPanel());
        return col;
    }

    /// <summary>
    /// FreeCol's population strip is a row of colonist portraits, not text — we mirror that here (the population
    /// <em>count</em> already shows in the info line). One sprite per colonist, capped so a big colony doesn't
    /// overflow. Sons-of-Liberty / Rebels / Royalists are deliberately omitted: the model has no per-colony liberty
    /// data yet (ADR-006 — presentation must not invent rules), tracked as a separate follow-up.
    /// </summary>
    private Control PopulationStrip()
    {
        const int cap = 12;
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        for (int i = 0; i < Math.Min(_colony.Population, cap); i++)
        {
            row.AddChild(IconRect(ColonyArt.UnitIcon("freeColonist"), 32, 40));
        }
        if (_colony.Population > cap)
        {
            row.AddChild(new Label { Text = $"+{_colony.Population - cap}", VerticalAlignment = VerticalAlignment.Center });
        }
        return row;
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
        var view = new Control { Name = "TilesView", CustomMinimumSize = new Vector2(540, 344) };
        var centre = new Vector2(270, 152);
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
                    Place(view, Badge($"{Short(good)} {_game.TileYield(tile, good)}"), topLeft + new Vector2(44, 0));
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
                        picker.AddItem($"{Short(goodsId)} {yield}");
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
                .Select(c => $"{Short(c.GoodsId)} {_colony.StoreOf(_game.Ruleset.StorageIdOf(c.GoodsId))}/{c.Amount}"));
            var row = new HBoxContainer();
            row.AddChild(IconRect(ColonyArt.BuildingImage(target.ShortName), 48, 36));
            row.AddChild(new Label { Text = $"Building {target.ShortName} ({cost})", SizeFlagsHorizontal = SizeFlags.ExpandFill });
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
                string cost = string.Join(" + ", b.BuildCost.Select(c => $"{c.Amount} {Short(c.GoodsId)}"));
                options.AddItem($"{b.ShortName} ({cost})");
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
        // The elastic column: it takes the width left by the fixed LeftColumn, with the 4-wide buildings grid centred
        // in it (ShrinkCenter — not ExpandFill, which would stretch the cells into a void). The whole screen already
        // scrolls (VBox/Scroll), so no inner scroll is needed; a bottom spacer absorbs this column's vertical slack.
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
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
        var cell = new PanelContainer { CustomMinimumSize = new Vector2(150, 132) };
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cell.AddChild(box);

        box.AddChild(IconRect(ColonyArt.BuildingImage(building.ShortName), 144, 80));
        box.AddChild(new Label { Text = $"{building.ShortName} ({workers}/{building.Workplaces})", HorizontalAlignment = HorizontalAlignment.Center });

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
                    buttons.Add(MakeButton($"Equip_{uid}_{Short(roleId)}", $"Arm {Short(roleId)}", () => Act(uid, u => _game.EquipRole(u, _colony, r))));
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
            string role = unit.HasDefaultRole ? "" : $" ({Short(unit.RoleId)})";
            row.AddChild(new Label { Text = $"{unit.Type.ShortName}{role} at ({unit.Position.X},{unit.Position.Y})", SizeFlagsHorizontal = SizeFlags.ExpandFill });
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

    private static Label Badge(string text) => new()
    {
        Text = text,
        Modulate = Colors.White,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private static Button MakeButton(string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        return button;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = $"— {text} —",
        HorizontalAlignment = HorizontalAlignment.Center,
    };
}
