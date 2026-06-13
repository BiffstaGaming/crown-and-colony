using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The colony screen (Phase 3 economy UI): population, stores, and interactive
/// management — staff/unstaff buildings, release field workers, auto-assign
/// idle colonists, and choose what to construct. All actions go through the
/// Game oracles (ADR-006); the panel only renders state and forwards clicks.
/// </summary>
public partial class ColonyPanel : PanelContainer
{
    private Game _game = null!;
    private Colony _colony = null!;
    private Action _onChange = () => { };

    /// <summary>Opens the panel for a colony. <paramref name="onChange"/> runs after every action.</summary>
    public void Open(Game game, Colony colony, Action onChange)
    {
        _game = game;
        _colony = colony;
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
        GetNode<Label>("VBox/ColonyTitle").Text = _colony.Name;

        TerrainType terrain = _game.Map.TerrainAt(_colony.Position);
        string stores = _colony.Stores.Where(kv => kv.Value > 0)
            .Select(kv => $"{Short(kv.Key)} {kv.Value}")
            .DefaultIfEmpty("(empty)")
            .Aggregate((a, b) => $"{a}, {b}");
        GetNode<Label>("VBox/ColonyInfo").Text =
            $"Population: {_colony.Population} ({_colony.IdleColonists} idle)   |   " +
            $"Terrain: {terrain.ShortName}\n" +
            $"Stores: {stores}\n" +
            $"Food for next colonist: {_colony.Food}/{Colony.FoodForGrowth}";

        BuildDynamicSections();
    }

    private void BuildDynamicSections()
    {
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free(); // immediate — Rebuild reuses names
        }

        // — Field workers —
        dynamic.AddChild(SectionLabel("Fields"));
        foreach ((Position tile, string goods) in _colony.TileWorkers.OrderBy(w => (w.Key.Y, w.Key.X)))
        {
            var row = new HBoxContainer();
            row.AddChild(new Label
            {
                Text = $"({tile.X},{tile.Y}) {Short(goods)} {_game.TileYield(tile, goods)}/turn",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            Position t = tile;
            row.AddChild(ActionButton($"Release_{tile.X}_{tile.Y}", "Release", () =>
            {
                _game.UnassignWork(_colony, t);
                Changed();
            }));
            dynamic.AddChild(row);
        }
        if (_colony.IdleColonists > 0)
        {
            dynamic.AddChild(ActionButton("AutoAssign", "Send idle colonists to the fields", () =>
            {
                _game.AutoAssignIdleToFood(_colony);
                Changed();
            }));
        }

        // — Buildings —
        dynamic.AddChild(SectionLabel("Buildings"));
        foreach (string buildingId in _colony.Buildings)
        {
            BuildingType building = _game.Ruleset.Building(buildingId);
            int workers = _colony.BuildingWorkers.GetValueOrDefault(buildingId);
            var row = new HBoxContainer();
            row.AddChild(new Label
            {
                Text = $"{building.ShortName} ({workers}/{building.Workplaces})",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            string id = buildingId;
            if (_game.CheckAssignBuildingWork(_colony, buildingId).Allowed)
            {
                row.AddChild(ActionButton($"Staff_{building.ShortName}", "+", () =>
                {
                    _game.AssignBuildingWork(_colony, id);
                    Changed();
                }));
            }
            if (workers > 0)
            {
                row.AddChild(ActionButton($"Unstaff_{building.ShortName}", "−", () =>
                {
                    _game.UnassignBuildingWork(_colony, id);
                    Changed();
                }));
            }
            dynamic.AddChild(row);
        }

        // — Construction —
        dynamic.AddChild(SectionLabel("Construction"));
        if (_colony.CurrentBuild is not null)
        {
            BuildingType target = _game.Ruleset.Building(_colony.CurrentBuild);
            string cost = target.BuildCost
                .Select(c => $"{Short(c.GoodsId)} {_colony.StoreOf(_game.Ruleset.StorageIdOf(c.GoodsId))}/{c.Amount}")
                .Aggregate((a, b) => $"{a}, {b}");
            var row = new HBoxContainer();
            row.AddChild(new Label
            {
                Text = $"Building {target.ShortName} ({cost})",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            });
            row.AddChild(ActionButton("StopBuild", "Stop", () =>
            {
                _game.SetBuild(_colony, null);
                Changed();
            }));
            dynamic.AddChild(row);
        }
        else
        {
            var options = new OptionButton { Name = "BuildOptions" };
            options.AddItem("Choose a building…");
            var buildables = _game.Buildables(_colony).ToList();
            foreach (BuildingType b in buildables)
            {
                string cost = b.BuildCost.Select(c => $"{c.Amount} {Short(c.GoodsId)}")
                    .Aggregate((a, x) => $"{a} + {x}");
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
            dynamic.AddChild(options);
        }
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
