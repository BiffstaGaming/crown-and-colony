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
        EnsureOpaqueBackground();
        Rebuild();
        Show();
    }

    /// <summary>
    /// Gives the panel a solid, opaque background so the map (drawn behind the UI layer) never shows through. The
    /// default <see cref="PanelContainer"/> stylebox is effectively transparent here, which let the map bleed across
    /// the colony screen. A warm dark fill with inner padding stands in until the FreeCol parchment skin is adopted.
    /// </summary>
    private void EnsureOpaqueBackground()
    {
        var bg = new StyleBoxFlat { BgColor = new Color(0.12f, 0.10f, 0.08f) };
        bg.SetContentMarginAll(16);
        AddThemeStyleboxOverride("panel", bg);
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
        foreach (Node child in root.GetChildren())
        {
            child.Free();
        }

        root.AddChild(ProductionBar());

        // Two equal halves that fill the panel width — tiles + construction on the left, buildings on the right.
        // No vertical expand here: the bottom bars (units + warehouse) must sit directly beneath this row, not be
        // shoved to the foot of a tall scroll viewport.
        var main = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        main.AddChild(LeftColumn());
        main.AddChild(BuildingsColumn());
        root.AddChild(main);

        root.AddChild(SectionLabel("Outside the colony"));
        root.AddChild(OutsideArea());
        root.AddChild(SectionLabel("Warehouse"));
        root.AddChild(WarehouseBar());
    }

    // ── Top: the colony's net production, FreeCol's production row ───────────────────────────────────────────

    private Control ProductionBar()
    {
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
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
        var col = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Control tiles = IsometricTiles();
        tiles.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; // centre the fixed-size tile view in the half-width column
        col.AddChild(tiles);
        col.AddChild(ConstructionPanel());
        return col;
    }

    /// <summary>The colony's 3×3 surrounding tiles drawn as overlapping FreeCol diamonds, the colony at the centre, a colonist on each worked tile, with its yield and a tiny work control.</summary>
    private Control IsometricTiles()
    {
        var view = new Control { Name = "TilesView", CustomMinimumSize = new Vector2(540, 344) };
        var centre = new Vector2(270, 152);
        var half = new Vector2(TileW / 2, TileH / 2);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Position tile = new(_colony.Position.X + dx, _colony.Position.Y + dy);
                Vector2 topLeft = centre + new Vector2((dx - dy) * (TileW / 2), (dx + dy) * (TileH / 2)) - half;
                if (_game.Map.InBounds(tile))
                {
                    foreach (Texture2D tex in ColonyArt.TerrainTextures(_game.Map.TerrainAt(tile).ShortName))
                    {
                        Place(view, IconRect(tex, TileW, TileH), topLeft);
                    }
                }
                if (dx == 0 && dy == 0)
                {
                    Place(view, IconRect(ColonyArt.ColonyIcon(), 120, 80), topLeft + new Vector2(20, 0));
                    continue;
                }
                if (!_game.Map.InBounds(tile))
                {
                    continue;
                }
                if (_colony.TileWorkers.TryGetValue(tile, out string? good))
                {
                    Place(view, IconRect(ColonyArt.UnitIcon("freeColonist"), 56, 56), topLeft + new Vector2(52, 8));
                    Place(view, Badge($"{Short(good)} {_game.TileYield(tile, good)}"), topLeft + new Vector2(44, 0));
                    Position worked = tile;
                    var release = new Button { Name = $"Release_{tile.X}_{tile.Y}", Text = "✕", CustomMinimumSize = new Vector2(24, 20) };
                    release.Pressed += () => { _game.UnassignWork(_colony, worked); Changed(); };
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
        // A half-width column with the buildings grid centred in it; the whole screen already scrolls (VBox/Scroll),
        // so no inner scroll is needed.
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var grid = new GridContainer { Columns = 4, Name = "BuildingsGrid", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
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
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
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
