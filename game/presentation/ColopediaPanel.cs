using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The in-game reference panel (FreeCol's <c>Colopedia</c>), with switchable <b>category tabs</b> over the ruleset:
/// <list type="bullet">
/// <item><b>Goods</b> — every goods type with its kind (food / farmed raw / refined / manufactured) and, for a
/// market-tradeable good, its current sell (bid) / buy (ask) price from the human's market.</item>
/// <item><b>Terrain</b> — every terrain type with its move cost, defence bonus and what it can be worked for.</item>
/// <item><b>Units</b> — every unit type with its movement, combat power and how it is obtained (built / trained /
/// purchased in Europe).</item>
/// <item><b>Buildings</b> — every building type with the population it needs, its build cost and what it does.</item>
/// <item><b>Fathers</b> — every Founding Father with its category and the bonus it grants.</item>
/// <item><b>Nations</b> — every European nation with its advantage type and whether it is playable.</item>
/// <item><b>Resources</b> — every bonus resource with the yield bonus it grants.</item>
/// </list>
/// <para>
/// Pure presentation (ADR-006): reads <see cref="Game.Ruleset"/> and <see cref="Game.Market"/> only, never mutates.
/// Built programmatically into the fixed <c>VBox/Scroll/Dynamic</c> shell, with the category tab row above it.
/// Hidden by default; opened by the Colopedia button or the <b>C</b> key.
/// </para>
/// </summary>
public partial class ColopediaPanel : PanelContainer
{
    private enum Category { Goods, Terrain, Units, Buildings, Fathers, Nations, Resources }

    private Game _game = null!;
    private Category _category = Category.Goods;

    private static readonly Dictionary<Category, string> Titles = new()
    {
        [Category.Goods] = "Colopedia — Goods",
        [Category.Terrain] = "Colopedia — Terrain",
        [Category.Units] = "Colopedia — Units",
        [Category.Buildings] = "Colopedia — Buildings",
        [Category.Fathers] = "Colopedia — Founding Fathers",
        [Category.Nations] = "Colopedia — Nations",
        [Category.Resources] = "Colopedia — Resources",
    };

    /// <summary>Opens the Colopedia on the Goods category over the current ruleset / market.</summary>
    public void Open(Game game)
    {
        _game = game;
        _category = Category.Goods;
        Rebuild();
        Show();
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/ColopediaTitle").Text = Titles[_category];
        var dynamic = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free();
        }

        // Category tab row (named buttons for the L3 tests; the active category's button is disabled).
        var tabs = new HBoxContainer { Name = "Tabs" };
        tabs.AddChild(CategoryButton("Goods", Category.Goods));
        tabs.AddChild(CategoryButton("Terrain", Category.Terrain));
        tabs.AddChild(CategoryButton("Units", Category.Units));
        tabs.AddChild(CategoryButton("Buildings", Category.Buildings));
        tabs.AddChild(CategoryButton("Fathers", Category.Fathers));
        tabs.AddChild(CategoryButton("Nations", Category.Nations));
        tabs.AddChild(CategoryButton("Resources", Category.Resources));
        dynamic.AddChild(tabs);
        dynamic.AddChild(new HSeparator());

        switch (_category)
        {
            case Category.Goods: BuildGoods(dynamic); break;
            case Category.Terrain: BuildTerrain(dynamic); break;
            case Category.Units: BuildUnits(dynamic); break;
            case Category.Buildings: BuildBuildings(dynamic); break;
            case Category.Fathers: BuildFathers(dynamic); break;
            case Category.Nations: BuildNations(dynamic); break;
            case Category.Resources: BuildResources(dynamic); break;
        }
    }

    private Button CategoryButton(string label, Category category)
    {
        var b = new Button { Name = $"Cat_{category}", Text = label, Disabled = _category == category };
        b.Pressed += () => { _category = category; Rebuild(); };
        return b;
    }

    // ── Goods ────────────────────────────────────────────────────────────────────────────────────────────

    private void BuildGoods(VBoxContainer dynamic)
    {
        // FreeCol's Colopedia lists every goods type in ruleset order. One named row per good (Goods_{shortName})
        // so the L3 test can find a known good. Each row carries the good's facts; tradeable goods also show price.
        foreach (GoodsType g in _game.Ruleset.GoodsTypes)
        {
            string price = g.IsTradeable
                ? $"sell {_game.Market.BidPrice(g.Id)} / buy {_game.Market.AskPrice(g.Id)} gold"
                : "not traded in Europe";
            dynamic.AddChild(new Label
            {
                Name = $"Goods_{g.ShortName}",
                Text = $"{Title(g.ShortName)} — {GoodsKind(g)}  ·  {price}",
            });
        }
    }

    /// <summary>A short descriptor of the good's role: food, a farmed raw good, refined from another good, or manufactured.</summary>
    private static string GoodsKind(GoodsType g)
    {
        if (g.IsFood)
        {
            return "food";
        }
        if (g.MadeFrom is { } raw)
        {
            return $"refined from {Title(raw[(raw.LastIndexOf('.') + 1)..])}";
        }
        if (g.IsFarmed)
        {
            return g.IsNewWorldGoods ? "New World raw good" : "raw good";
        }
        return "manufactured";
    }

    // ── Terrain ──────────────────────────────────────────────────────────────────────────────────────────

    private void BuildTerrain(VBoxContainer dynamic)
    {
        foreach (TerrainType t in _game.Ruleset.TerrainTypes)
        {
            // The goods a colonist can be worked for here (the attended production outputs), or a dash for barren/water.
            IEnumerable<string> produces = t.Productions
                .Where(p => !p.Unattended)
                .SelectMany(p => p.Outputs)
                .Select(o => Title(_game.Ruleset.Goods(o.GoodsId).ShortName))
                .Distinct();
            string works = produces.Any() ? string.Join(", ", produces) : "—";
            string kind = t.IsWater ? "water" : t.IsForest ? "forest" : t.IsElevation ? "elevation" : "open land";
            string settle = t.CanSettle ? "" : ", no colony";
            dynamic.AddChild(new Label
            {
                Name = $"Terrain_{t.ShortName}",
                Text = $"{Title(t.ShortName)} — {kind}, move {t.MoveCost}, defence {Pct(t.DefenceBonus)}{settle}  ·  works: {works}",
            });
        }
    }

    // ── Units ────────────────────────────────────────────────────────────────────────────────────────────

    private void BuildUnits(VBoxContainer dynamic)
    {
        foreach (UnitType u in _game.Ruleset.UnitTypes)
        {
            dynamic.AddChild(new Label
            {
                Name = $"Units_{u.ShortName}",
                Text = $"{Title(u.ShortName)} — {UnitKind(u)}, move {u.Movement}, " +
                       $"attack {u.Offence:0.#} / defence {u.Defence:0.#}  ·  {UnitSource(u)}",
            });
        }
    }

    /// <summary>What kind of unit this is: ship, person (colonist) or other (artillery, wagon).</summary>
    private static string UnitKind(UnitType u) =>
        u.IsNaval ? "ship" : u.IsPerson ? "colonist" : "land";

    /// <summary>How this unit is obtained: built in a colony, trained or purchased in Europe, or otherwise (recruited/born).</summary>
    private static string UnitSource(UnitType u)
    {
        var ways = new List<string>();
        if (u.IsBuildable)
        {
            ways.Add("built in a colony");
        }
        if (u.IsTrainedInEurope)
        {
            ways.Add($"trained in Europe ({u.Price} gold)");
        }
        else if (u.IsPurchasedInEurope)
        {
            ways.Add($"purchased in Europe ({u.Price} gold)");
        }
        return ways.Count > 0 ? string.Join(", ", ways) : "recruited / born in the colonies";
    }

    // ── Buildings ────────────────────────────────────────────────────────────────────────────────────────

    private void BuildBuildings(VBoxContainer dynamic)
    {
        foreach (BuildingType b in _game.Ruleset.BuildingTypes)
        {
            string cost = b.BuildCost.Count > 0
                ? string.Join(", ", b.BuildCost.Select(c => $"{c.Amount} {_game.Ruleset.Goods(c.GoodsId).ShortName}"))
                : "free (starting building)";
            dynamic.AddChild(new Label
            {
                Name = $"Buildings_{b.ShortName}",
                Text = $"{Title(b.ShortName)} — needs pop {b.RequiredPopulation}, {b.Workplaces} workplaces  ·  cost: {cost}",
            });
        }
    }

    // ── Founding Fathers ─────────────────────────────────────────────────────────────────────────────────

    private void BuildFathers(VBoxContainer dynamic)
    {
        foreach (FoundingFather f in _game.Ruleset.FoundingFathers)
        {
            dynamic.AddChild(new Label
            {
                Name = $"Fathers_{f.ShortName}",
                Text = $"{Title(f.ShortName)} — {f.Type}  ·  {FatherEffect(f)}",
            });
        }
    }

    /// <summary>A short, plain summary of what electing this father does (free units/buildings, lifts boycotts, reveals colonies, or a modifier/ability count).</summary>
    private string FatherEffect(FoundingFather f)
    {
        var parts = new List<string>();
        if (f.LiftsBoycotts)
        {
            parts.Add("lifts all boycotts");
        }
        if (f.RevealsAllColonies)
        {
            parts.Add("reveals every colony");
        }
        foreach (string unitId in f.FreeUnits)
        {
            parts.Add($"a free {Title(_game.Ruleset.Unit(unitId).ShortName)}");
        }
        foreach (string buildingId in f.FreeBuildings)
        {
            parts.Add($"a free {Title(_game.Ruleset.Building(buildingId).ShortName)} per colony");
        }
        if (f.Modifiers.Count > 0)
        {
            parts.Add($"{f.Modifiers.Count} bonus{(f.Modifiers.Count == 1 ? "" : "es")}");
        }
        if (f.Abilities.Count > 0)
        {
            parts.Add($"{f.Abilities.Count} new abilit{(f.Abilities.Count == 1 ? "y" : "ies")}");
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "joins the Congress";
    }

    // ── Nations ──────────────────────────────────────────────────────────────────────────────────────────

    private void BuildNations(VBoxContainer dynamic)
    {
        // The colonial powers (and FreeCol's extras); the Royal Expeditionary Forces are not a player choice — skip them.
        foreach (EuropeanNation n in _game.Ruleset.EuropeanNations.Where(n => !n.IsRef))
        {
            string playable = n.Selectable ? "playable" : "non-playable";
            dynamic.AddChild(new Label
            {
                Name = $"Nations_{n.ShortName}",
                Text = $"{n.DisplayName} — {playable}, {Title(n.NationType.ShortName)} advantage",
            });
        }
    }

    // ── Resources ────────────────────────────────────────────────────────────────────────────────────────

    private void BuildResources(VBoxContainer dynamic)
    {
        foreach (ResourceType r in _game.Ruleset.ResourceTypes)
        {
            // The unscoped yield bonuses (those any colonist gets), summarised as "+N good" / "×N good".
            IEnumerable<string> bonuses = r.Modifiers
                .Where(m => m.IsUnscoped)
                .Select(m => $"{ModifierLabel(m.Type, m.Value)} {Title(_game.Ruleset.Goods(m.GoodsId).ShortName)}");
            string effect = bonuses.Any() ? string.Join(", ", bonuses) : "an expert-only bonus";
            dynamic.AddChild(new Label
            {
                Name = $"Resources_{r.ShortName}",
                Text = $"{Title(r.ShortName)} — boosts {effect}",
            });
        }
    }

    /// <summary>A compact modifier label: additive <c>+3</c>, multiplicative <c>×2</c>, percentage <c>+50%</c>.</summary>
    private static string ModifierLabel(ModifierType type, double value) => type switch
    {
        ModifierType.Multiplicative => $"×{value:0.#}",
        ModifierType.Percentage => $"{(value >= 0 ? "+" : "")}{value:0.#}%",
        _ => $"{(value >= 0 ? "+" : "")}{value:0.#}",
    };

    // ── Shared helpers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>A percentage label for a defence bonus (0 → "+0%").</summary>
    private static string Pct(double v) => $"{(v >= 0 ? "+" : "")}{v:0.#}%";

    /// <summary>Title-cases a short id for display (e.g. <c>tradeGoods</c> → <c>Trade Goods</c>, <c>food</c> → <c>Food</c>).</summary>
    private static string Title(string shortName)
    {
        // Split camelCase into words, then capitalise each (mirrors the colony screen's display helper, kept local).
        var words = new List<string>();
        int start = 0;
        for (int i = 1; i < shortName.Length; i++)
        {
            if (char.IsUpper(shortName[i]) && !char.IsUpper(shortName[i - 1]))
            {
                words.Add(shortName[start..i]);
                start = i;
            }
        }
        words.Add(shortName[start..]);
        return string.Join(" ", words.Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
