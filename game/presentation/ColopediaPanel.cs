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
/// <item><b>Concepts</b> — free-text help topics (FreeCol's <c>ConceptDetailPanel</c>): a left list of topic titles
/// over a right detail pane, the seed of an in-game help/tutorial surface.</item>
/// </list>
/// <para>
/// Pure presentation (ADR-006): reads <see cref="Game.Ruleset"/> and <see cref="Game.Market"/> only, never mutates.
/// Built programmatically into the fixed <c>VBox/Scroll/Dynamic</c> shell, with the category tab row above it.
/// Hidden by default; opened by the Colopedia button or the <b>C</b> key.
/// </para>
/// </summary>
public partial class ColopediaPanel : PanelContainer
{
    private enum Category { Goods, Terrain, Units, Buildings, Fathers, Nations, Resources, Concepts }

    private Game _game = null!;
    private Category _category = Category.Goods;

    /// <summary>The Concepts topic currently shown in the detail pane (the first topic by default).</summary>
    private string _concept = ConceptTopics[0].Title;

    private static readonly Dictionary<Category, string> Titles = new()
    {
        [Category.Goods] = "Colopedia — Goods",
        [Category.Terrain] = "Colopedia — Terrain",
        [Category.Units] = "Colopedia — Units",
        [Category.Buildings] = "Colopedia — Buildings",
        [Category.Fathers] = "Colopedia — Founding Fathers",
        [Category.Nations] = "Colopedia — Nations",
        [Category.Resources] = "Colopedia — Resources",
        [Category.Concepts] = "Colopedia — Concepts",
    };

    /// <summary>
    /// A free-text help topic in the Concepts tab: a short title and a couple of explanatory sentences.
    /// FreeCol pulls these from ruleset help-strings; we have none yet, so this is a small curated stub set
    /// (the seed of an in-game help/tutorial surface — presentation-only, no rules).
    /// </summary>
    private readonly record struct ConceptTopic(string Title, string Text);

    private static readonly ConceptTopic[] ConceptTopics =
    {
        new("Founding a colony",
            "Move a colonist onto a buildable land tile and give the Build Colony order to found a new settlement. "
            + "A colony grows by gathering food, works the surrounding tiles, and is where you produce goods and train colonists."),
        new("Working tiles & production",
            "Each colonist in a colony works either a surrounding tile (for food or a raw good) or a building (to refine "
            + "goods or ring liberty bells). Terrain, bonus resources and the worker's expertise decide how much that tile or building yields each turn."),
        new("Trade & Europe",
            "Sail a ship loaded with goods across the high seas to your home port in Europe to sell them for gold, and buy goods or "
            + "recruit colonists to bring back. The Crown taxes your sales, and prices drift as the market is flooded or runs short."),
        new("Sons of Liberty & bells",
            "Statesmen working the town hall produce liberty bells, which raise a colony's Sons of Liberty membership. "
            + "High membership grants a production bonus; a discontented colony (too many Tories) suffers a penalty instead."),
        new("Founding Fathers",
            "Liberty bells also accrue toward your Continental Congress: spend enough and a Founding Father joins, granting a lasting "
            + "bonus. Each round you are offered one candidate per category (trade, exploration, military, political, religious) to recruit."),
        new("Combat basics",
            "A unit's strength comes from its base power, its role and equipment, plus bonuses from terrain, fortification and ambush. "
            + "The defender's modifiers matter as much as the attacker's, so fortified troops on good ground are hard to dislodge."),
        new("Native relations",
            "Indian settlements start wary and grow alarmed as your colonies and troops encroach on their land or you attack them. "
            + "Trade, gifts and missionaries calm them; raids and land-grabs provoke retaliation. Keep an eye on each tribe's mood."),
        new("Declaring Independence",
            "Once enough of your colonists support the rebellion, you may declare independence from the Crown. The King then sends his "
            + "Royal Expeditionary Force to crush you — survive the war of independence to win the game."),
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
            // Detach immediately (no stale-vs-fresh tab-row collision) but QUEUE the free: a category/topic button's
            // Pressed handler calls Rebuild(), so a synchronous Free() would free that button mid-signal ("object freed
            // while a signal is being emitted"). RemoveChild + QueueFree is the Godot-safe idiom.
            dynamic.RemoveChild(child);
            child.QueueFree();
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
        tabs.AddChild(CategoryButton("Concepts", Category.Concepts));
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
            case Category.Concepts: BuildConcepts(dynamic); break;
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
            // One row per father: their FreeCol portrait (if any) left of the existing name/category/effect label,
            // so the entry shows a face beside the text. The row keeps the Fathers_{shortName} node name the L3 test
            // looks up; the text lives on a child FatherLabel so the portrait is purely additive (degrades to label-only).
            var row = new HBoxContainer { Name = $"Fathers_{f.ShortName}" };
            if (FatherPortrait(f.ShortName) is { } control)
            {
                row.AddChild(control);
            }
            row.AddChild(new Label
            {
                Name = "FatherLabel",
                Text = $"{Title(f.ShortName)} — {f.Type}  ·  {FatherEffect(f)}",
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            dynamic.AddChild(row);
        }
    }

    /// <summary>
    /// A <see cref="TextureRect"/> showing a father's FreeCol portrait at a fixed thumbnail size (keeping the source
    /// 200×237 aspect), or null when that father has no portrait so the row renders label-only. Named
    /// <c>Portrait_&lt;shortName&gt;</c> so the L3 test can assert a portrait control is present beside the text.
    /// </summary>
    private static TextureRect? FatherPortrait(string shortName)
    {
        if (ColonyArt.FatherPortrait(shortName) is not { } tex)
        {
            return null;
        }
        return new TextureRect
        {
            Name = $"Portrait_{shortName}",
            Texture = tex,
            // The source art is 200×237; without IgnoreSize a TextureRect grows to that natural size (CustomMinimumSize
            // is only a floor). IgnoreSize makes it take exactly the min size we set, and KeepAspectCentered keeps the
            // face un-stretched inside that thumbnail box, so each row stays a compact face beside its text.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(PortraitWidth, PortraitHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
    }

    /// <summary>Portrait thumbnail width in the Fathers tab (the source art is 200×237, scaled to keep that aspect).</summary>
    private const int PortraitWidth = 48;

    /// <summary>Portrait thumbnail height in the Fathers tab (48 × 237/200 ≈ 57, rounded to keep the source aspect).</summary>
    private const int PortraitHeight = 57;

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

    // ── Concepts (free-text help topics) ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// FreeCol's <c>ConceptDetailPanel</c>: a master-detail help view — a left column of topic-title buttons
    /// (<c>Concept_{slug}</c>, a space-free slug of the title, the active one disabled) over a right detail pane that shows the selected topic's
    /// title (<c>ConceptTitle</c>) and its explanatory text (<c>ConceptText</c>, word-wrapped). Pure presentation —
    /// the topics are a small curated stub set (no ruleset help-strings yet), no game state read.
    /// </summary>
    private void BuildConcepts(VBoxContainer dynamic)
    {
        // If a previous tab/build left the selected topic unset (defensive), fall back to the first topic.
        if (!ConceptTopics.Any(t => t.Title == _concept))
        {
            _concept = ConceptTopics[0].Title;
        }

        var row = new HBoxContainer { Name = "ConceptsRow" };

        // Left: one named button per help topic; pressing it shows that topic in the detail pane.
        // The node name is a space-free slug of the title (Godot node-path lookups dislike spaces); Text keeps the title.
        var list = new VBoxContainer { Name = "ConceptList" };
        foreach (ConceptTopic topic in ConceptTopics)
        {
            var b = new Button
            {
                Name = $"Concept_{Slug(topic.Title)}",
                Text = topic.Title,
                Disabled = topic.Title == _concept,
            };
            string title = topic.Title;
            b.Pressed += () => { _concept = title; Rebuild(); };
            list.AddChild(b);
        }
        row.AddChild(list);
        row.AddChild(new VSeparator());

        // Right: the detail pane — the selected topic's title and its word-wrapped text.
        ConceptTopic selected = ConceptTopics.First(t => t.Title == _concept);
        var detail = new VBoxContainer { Name = "ConceptDetail", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        detail.AddChild(new Label { Name = "ConceptTitle", Text = selected.Title });
        detail.AddChild(new Label
        {
            Name = "ConceptText",
            Text = selected.Text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(360, 0),
        });
        row.AddChild(detail);

        dynamic.AddChild(row);
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

    /// <summary>A space-free node-name slug of a Concepts topic title (keeps letters/digits, drops everything else).</summary>
    private static string Slug(string title) =>
        new string(title.Where(char.IsLetterOrDigit).ToArray());

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
