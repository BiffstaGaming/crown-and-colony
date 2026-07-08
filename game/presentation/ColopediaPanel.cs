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
/// over a right detail pane, the in-game help surface (curated content from <see cref="ColopediaConcepts"/>).</item>
/// </list>
/// <para>
/// <b>Cross-links (<c>86d3fq0jx</c>):</b> entries reference each other — a building links to the goods it produces, a
/// good to what it is refined from / into, and a help topic to the entities it mentions. Clicking a cross-link
/// navigates to that entity's category, selects it (Concepts) and scrolls it into view with a brief highlight, all
/// within this panel (FreeCol's in-text help anchors / report→colopedia links). The <b>report → Colopedia</b>
/// deep-link (<c>86d3fymc5</c>) — a report entity cell (a colony's build, a trade good, a native settlement's skill, a
/// unit type) opening the Colopedia on the matching entry — arrives here through <see cref="OpenTo"/>, which
/// <c>ColonyReportPanel</c> is wired to call (FreeCol's <c>ReportPanel.showColopediaPanel</c>).
/// </para>
/// <para>
/// Pure presentation (ADR-006): reads <see cref="Game.Ruleset"/> and <see cref="Game.Market"/> only, never mutates.
/// Built programmatically into the fixed <c>VBox/Scroll/Dynamic</c> shell, with the category tab row above it.
/// Hidden by default; opened by the Colopedia button or the <b>C</b> key.
/// </para>
/// </summary>
public partial class ColopediaPanel : PanelContainer
{
    /// <summary>The Colopedia's top-level reference categories — one per ruleset collection (plus free-text Concepts).
    /// Public so a cross-panel deep-link (e.g. <see cref="ColonyReportPanel"/>'s report → Colopedia links) can target a
    /// category through <see cref="OpenTo"/>.</summary>
    public enum Category { Goods, Terrain, Units, Buildings, Fathers, Nations, Resources, Concepts }

    private Game _game = null!;
    private Category _category = Category.Goods;

    /// <summary>
    /// The active variant's help chrome — the words <see cref="ColopediaConcepts"/> weaves into the Concepts topics so
    /// they read in the variant's language (classic "Sons of Liberty / liberty bells / …", Australia "Federationists /
    /// Civic Voice / …"). Defaults to <see cref="HelpChrome.Classic"/> so a panel opened without a variant (or a classic
    /// game) shows byte-identical classic prose. Set in <see cref="Open"/>/<see cref="OpenTo"/>.
    /// </summary>
    private HelpChrome _chrome = HelpChrome.Classic;

    /// <summary>The Concepts help topics for the active variant's chrome (classic by default). Rebuilt whenever <see cref="_chrome"/> changes so the Concepts tab, cross-links and slug node names all read in the same variant's language.</summary>
    private IReadOnlyList<ColopediaConcepts.ColopediaConceptTopic> _concepts = ColopediaConcepts.BuildTopics(HelpChrome.Classic);

    /// <summary>The Concepts topic currently shown in the detail pane (the first topic by default).</summary>
    private string _concept = ColopediaConcepts.Topics[0].Title;

    /// <summary>
    /// The row a just-followed cross-link wants brought into view: the entity's node name (e.g. <c>Goods_bells</c>)
    /// within <c>Dynamic</c>, or null when nothing is pending. Set by <see cref="Navigate"/> and consumed once at the
    /// end of the next <see cref="Rebuild"/> — the row is scrolled visible and briefly highlighted, then this clears.
    /// </summary>
    private string? _focusNode;

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

    /// <summary>The active variant's display-name overrides (Australia renames goods/units/buildings; classic = none). Set in <see cref="Open"/>/<see cref="OpenTo"/>.</summary>
    private IReadOnlyDictionary<string, string>? _displayOverrides;

    /// <summary>Opens the Colopedia on the Goods category over the current ruleset / market.</summary>
    /// <param name="displayOverrides">The active variant's display-name overrides (<see cref="GameLogic.Specification.GameVariant.DisplayOverrides"/>) — Australia renames goods/units; <c>null</c>/empty (classic) leaves names classic.</param>
    /// <param name="chrome">The active variant's help chrome for the Concepts prose — Australia's "Federationists / Civic Voice / …"; <c>null</c> (classic, or no variant) shows byte-identical classic prose.</param>
    public void Open(Game game, IReadOnlyDictionary<string, string>? displayOverrides = null, HelpChrome? chrome = null)
    {
        ColonyArt.FramePanel(this, dense: true); // parchment image frame + dark-ink in-game theme (not Godot's default gray box)
        _game = game;
        _displayOverrides = displayOverrides;
        SetChrome(chrome);
        _category = Category.Goods;
        _focusNode = null;
        Rebuild();
        Show();
    }

    /// <summary>
    /// Opens the Colopedia straight to a specific entry (a cross-panel deep-link — FreeCol's report cells
    /// <c>showColopediaPanel(command)</c>): switches to <paramref name="category"/>, selects the concept when the target
    /// is a help topic, then rebuilds and shows with the target row scrolled into view and briefly highlighted (the same
    /// reveal path an in-panel cross-link uses). Called by <see cref="ColonyReportPanel"/> when the human clicks a
    /// report entity cell. Pure navigation — reads only <see cref="Game.Ruleset"/> / <see cref="Game.Market"/>, mutates
    /// no game state (ADR-006).
    /// </summary>
    /// <param name="game">The current game (the panel may have been closed since last shown, so re-seed it).</param>
    /// <param name="category">The category the linked entity lives in.</param>
    /// <param name="anchor">The entity's ruleset <c>ShortName</c>, or a concept <b>title</b> for the Concepts category.</param>
    /// <param name="displayOverrides">The active variant's display-name overrides (Australia renames goods/units; <c>null</c>/empty for classic).</param>
    /// <param name="chrome">The active variant's help chrome for the Concepts prose (Australia's words; <c>null</c> for classic).</param>
    public void OpenTo(Game game, Category category, string anchor, IReadOnlyDictionary<string, string>? displayOverrides = null, HelpChrome? chrome = null)
    {
        _game = game;
        _displayOverrides = displayOverrides;
        SetChrome(chrome);
        Navigate(category, anchor); // sets category + focus row, Rebuild()s
        Show();
    }

    /// <summary>
    /// Adopts the active variant's help chrome (or the classic default when <paramref name="chrome"/> is null) and
    /// rebuilds the Concepts topic set from it, so the tab list, the detail prose, the slug node names and the
    /// cross-link anchors all read in the same variant's language. Keeps the current topic selected if the (possibly
    /// renamed) variant still has a topic by that title, else falls back to the first topic.
    /// </summary>
    private void SetChrome(HelpChrome? chrome)
    {
        _chrome = chrome ?? HelpChrome.Classic;
        _concepts = ColopediaConcepts.BuildTopics(_chrome);
        if (!_concepts.Any(t => t.Title == _concept))
        {
            _concept = _concepts[0].Title;
        }
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/ColopediaTitle").Text = Titles[_category];
        var dynamic = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            // Detach immediately (no stale-vs-fresh tab-row collision) but QUEUE the free: a category/topic/cross-link
            // button's Pressed handler calls Rebuild(), so a synchronous Free() would free that button mid-signal
            // ("object freed while a signal is being emitted"). RemoveChild + QueueFree is the Godot-safe idiom.
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

        // A just-followed cross-link asked for a row to be revealed: scroll it into view and flash a highlight, then
        // clear the request. Deferred so the freshly-added children have a layout (size/position) to scroll to.
        if (_focusNode is { } focus)
        {
            _focusNode = null;
            CallDeferred(nameof(RevealRow), focus);
        }
    }

    private Button CategoryButton(string label, Category category)
    {
        var b = new Button { Name = $"Cat_{category}", Text = label, Disabled = _category == category };
        b.Pressed += () => { _category = category; Rebuild(); };
        return b;
    }

    // ── Cross-link navigation (86d3fq0jx) ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Follows a cross-link to another Colopedia entry: switches to the target <paramref name="category"/>, selects the
    /// concept (when the target is a help topic), and remembers the target row so the next <see cref="Rebuild"/> scrolls
    /// it into view and highlights it. Pure navigation within this panel — reads nothing, mutates no game state.
    /// </summary>
    /// <param name="category">The category the linked entity lives in.</param>
    /// <param name="anchor">The entity's ruleset <c>ShortName</c>, or a concept <b>title</b> for the Concepts category.</param>
    private void Navigate(Category category, string anchor)
    {
        _category = category;
        if (category == Category.Concepts)
        {
            // The concept list keys on title; only switch if the topic exists, else stay on whatever was shown.
            if (_concepts.Any(t => t.Title == anchor))
            {
                _concept = anchor;
            }
            _focusNode = $"ConceptsRow/ConceptList/Concept_{Slug(anchor)}";
        }
        else
        {
            _focusNode = $"{category}_{anchor}";
        }
        Rebuild();
    }

    /// <summary>Maps a concept cross-link category (presentation data) to this panel's own <see cref="Category"/>.</summary>
    private static Category ToCategory(ColopediaConcepts.LinkCategory c) => c switch
    {
        ColopediaConcepts.LinkCategory.Goods => Category.Goods,
        ColopediaConcepts.LinkCategory.Terrain => Category.Terrain,
        ColopediaConcepts.LinkCategory.Units => Category.Units,
        ColopediaConcepts.LinkCategory.Buildings => Category.Buildings,
        ColopediaConcepts.LinkCategory.Fathers => Category.Fathers,
        ColopediaConcepts.LinkCategory.Nations => Category.Nations,
        ColopediaConcepts.LinkCategory.Resources => Category.Resources,
        _ => Category.Concepts,
    };

    /// <summary>
    /// A small underlined link button that navigates to <paramref name="category"/> / <paramref name="anchor"/> when
    /// pressed (FreeCol's in-text help anchor). Named <c>Link_{Category}_{slug}</c> so the L3 test can find and click a
    /// known cross-link. Flat styling keeps it reading as a hyperlink inside a row, not a chunky button.
    /// </summary>
    private Button LinkButton(string label, Category category, string anchor)
    {
        var b = new Button
        {
            Name = $"Link_{category}_{Slug(anchor)}",
            Text = label,
            Flat = true,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            TooltipText = $"Open the Colopedia entry for {label}",
        };
        b.Pressed += () => Navigate(category, anchor);
        return b;
    }

    /// <summary>
    /// Scrolls the named row of <c>Dynamic</c> into view in the surrounding <c>Scroll</c> container and briefly tints it,
    /// so a freshly-followed cross-link lands on the right entry. No-op if the row is gone (defensive). Deferred from
    /// <see cref="Rebuild"/> so the row already has a layout rect to scroll to.
    /// </summary>
    private void RevealRow(string nodeName)
    {
        var dynamic = GetNodeOrNull<VBoxContainer>("VBox/Scroll/Dynamic");
        if (dynamic?.GetNodeOrNull<Control>(nodeName) is not { } row)
        {
            return;
        }
        GetNode<ScrollContainer>("VBox/Scroll").EnsureControlVisible(row);
        // A one-shot highlight: tint the row toward the accent, then ease back to normal so the eye catches the target.
        row.Modulate = new Color(1f, 1f, 0.55f);
        CreateTween().TweenProperty(row, "modulate", Colors.White, 0.8);
    }

    // ── Goods ────────────────────────────────────────────────────────────────────────────────────────────

    private void BuildGoods(VBoxContainer dynamic)
    {
        // FreeCol's Colopedia lists every goods type in ruleset order. One named row per good (Goods_{shortName})
        // so the L3 test (and a cross-link) can find a known good. Each row carries the good's facts; tradeable goods
        // also show price, and cross-links the good to what it is refined FROM and (if any) refined INTO.
        foreach (GoodsType g in _game.Ruleset.GoodsTypes)
        {
            string price = g.IsTradeable
                ? $"sell {_game.Market.BidPrice(g.Id)} / buy {_game.Market.AskPrice(g.Id)} gold"
                : "not traded in Europe";

            var row = new HBoxContainer { Name = $"Goods_{g.ShortName}" };
            AddIcon(row, ColonyArt.GoodsIcon(g.ShortName));
            row.AddChild(new Label { Name = "Fact", Text = $"{Title(g.ShortName)} — {GoodsKind(g)}  ·  {price}" });

            // Refined good → a link to its raw input (its "made from"); raw good → links to whatever is refined from it.
            if (g.MadeFrom is { } rawId)
            {
                GoodsType raw = _game.Ruleset.Goods(rawId);
                row.AddChild(new Label { Text = "  ← from " });
                row.AddChild(LinkButton(Title(raw.ShortName), Category.Goods, raw.ShortName));
            }
            foreach (GoodsType refined in _game.Ruleset.GoodsTypes.Where(o => o.MadeFrom == g.Id))
            {
                row.AddChild(new Label { Text = "  → makes " });
                row.AddChild(LinkButton(Title(refined.ShortName), Category.Goods, refined.ShortName));
            }
            dynamic.AddChild(row);
        }
    }

    /// <summary>A short descriptor of the good's role: food, a farmed raw good, refined from another good, or manufactured (the "refined from" clause honours the active variant's <see cref="_displayOverrides"/>).</summary>
    private string GoodsKind(GoodsType g)
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
            // The goods a colonist can be worked for here (the attended production outputs), or empty for barren/water.
            List<GoodsType> produces = t.Productions
                .Where(p => !p.Unattended)
                .SelectMany(p => p.Outputs)
                .Select(o => _game.Ruleset.Goods(o.GoodsId))
                .DistinctBy(g => g.Id)
                .ToList();
            string kind = t.IsWater ? "water" : t.IsForest ? "forest" : t.IsElevation ? "elevation" : "open land";
            string settle = t.CanSettle ? "" : ", no colony";

            var row = new HBoxContainer { Name = $"Terrain_{t.ShortName}" };
            row.AddChild(new Label
            {
                Text = $"{Title(t.ShortName)} — {kind}, move {t.MoveCost}, defence {Pct(t.DefenceBonus)}{settle}  ·  works:",
            });
            if (produces.Count == 0)
            {
                row.AddChild(new Label { Text = " —" });
            }
            else
            {
                // Each worked-for good is a cross-link into the Goods category.
                foreach (GoodsType g in produces)
                {
                    row.AddChild(LinkButton(Title(g.ShortName), Category.Goods, g.ShortName));
                }
            }
            dynamic.AddChild(row);
        }
    }

    // ── Units ────────────────────────────────────────────────────────────────────────────────────────────

    private void BuildUnits(VBoxContainer dynamic)
    {
        foreach (UnitType u in _game.Ruleset.UnitTypes)
        {
            var row = new HBoxContainer { Name = $"Units_{u.ShortName}" };
            AddIcon(row, ColonyArt.UnitIcon(u.ShortName));
            row.AddChild(new Label
            {
                Name = "Fact",
                Text = $"{Title(u.ShortName)} — {UnitKind(u)}, move {u.Movement}, " +
                       $"attack {u.Offence:0.#} / defence {u.Defence:0.#}  ·  {UnitSource(u)}",
            });
            dynamic.AddChild(row);
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
                ? string.Join(", ", b.BuildCost.Select(c => $"{c.Amount} {Title(_game.Ruleset.Goods(c.GoodsId).ShortName)}"))
                : "free (starting building)";

            // The distinct goods this building's attended production yields (lumber mill → hammers, weaver's → cloth…).
            List<GoodsType> makes = b.Productions
                .Where(p => !p.Unattended)
                .SelectMany(p => p.Outputs)
                .Select(o => _game.Ruleset.Goods(o.GoodsId))
                .DistinctBy(g => g.Id)
                .ToList();

            var row = new HBoxContainer { Name = $"Buildings_{b.ShortName}" };
            AddIcon(row, ColonyArt.BuildingImage(b.ShortName));
            row.AddChild(new Label
            {
                Text = $"{Title(b.ShortName)} — needs pop {b.RequiredPopulation}, {b.Workplaces} workplaces  ·  cost: {cost}",
            });
            if (makes.Count > 0)
            {
                row.AddChild(new Label { Text = "  ·  makes" });
                // Each produced good cross-links into the Goods category — the building→goods link FreeCol surfaces.
                foreach (GoodsType g in makes)
                {
                    row.AddChild(LinkButton(Title(g.ShortName), Category.Goods, g.ShortName));
                }
            }
            dynamic.AddChild(row);
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
            // A father granting a free unit/building cross-links to that entity's entry.
            foreach (string unitId in f.FreeUnits)
            {
                UnitType u = _game.Ruleset.Unit(unitId);
                row.AddChild(LinkButton(Title(u.ShortName), Category.Units, u.ShortName));
            }
            foreach (string buildingId in f.FreeBuildings)
            {
                BuildingType b = _game.Ruleset.Building(buildingId);
                row.AddChild(LinkButton(Title(b.ShortName), Category.Buildings, b.ShortName));
            }
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
            // The unscoped yield bonuses (those any colonist gets) — each cross-links the boosted good into Goods.
            List<(string Label, GoodsType Goods)> bonuses = r.Modifiers
                .Where(m => m.IsUnscoped)
                .Select(m => ($"{ModifierLabel(m.Type, m.Value)} {Title(_game.Ruleset.Goods(m.GoodsId).ShortName)}",
                              _game.Ruleset.Goods(m.GoodsId)))
                .ToList();

            var row = new HBoxContainer { Name = $"Resources_{r.ShortName}" };
            row.AddChild(new Label { Text = $"{Title(r.ShortName)} — boosts" });
            if (bonuses.Count == 0)
            {
                row.AddChild(new Label { Text = " an expert-only bonus" });
            }
            else
            {
                foreach ((string label, GoodsType g) in bonuses)
                {
                    row.AddChild(LinkButton(label, Category.Goods, g.ShortName));
                }
            }
            dynamic.AddChild(row);
        }
    }

    // ── Concepts (free-text help topics) ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// FreeCol's <c>ConceptDetailPanel</c>: a master-detail help view — a left column of topic-title buttons
    /// (<c>Concept_{slug}</c>, a space-free slug of the title, the active one disabled) over a right detail pane that
    /// shows the selected topic's title (<c>ConceptTitle</c>), its explanatory text (<c>ConceptText</c>, word-wrapped)
    /// and a row of cross-link buttons (<c>ConceptLinks</c>) to the entries it mentions. Pure presentation — the topics
    /// are the curated <see cref="ColopediaConcepts"/> set (no ruleset help-strings yet), no game state read.
    /// </summary>
    private void BuildConcepts(VBoxContainer dynamic)
    {
        // If a previous tab/build left the selected topic unset (defensive), fall back to the first topic.
        if (!_concepts.Any(t => t.Title == _concept))
        {
            _concept = _concepts[0].Title;
        }

        var row = new HBoxContainer { Name = "ConceptsRow" };

        // Left: one named button per help topic; pressing it shows that topic in the detail pane.
        // The node name is a space-free slug of the title (Godot node-path lookups dislike spaces); Text keeps the title.
        var list = new VBoxContainer { Name = "ConceptList" };
        foreach (ColopediaConcepts.ColopediaConceptTopic topic in _concepts)
        {
            var b = new Button
            {
                Name = $"Concept_{Slug(topic.Title)}",
                Text = topic.Title,
                Disabled = topic.Title == _concept,
            };
            string title = topic.Title;
            b.Pressed += () => { _concept = title; _focusNode = null; Rebuild(); };
            list.AddChild(b);
        }
        row.AddChild(list);
        row.AddChild(new VSeparator());

        // Right: the detail pane — the selected topic's title, its word-wrapped text, and its cross-link buttons.
        ColopediaConcepts.ColopediaConceptTopic selected = _concepts.First(t => t.Title == _concept);
        var detail = new VBoxContainer { Name = "ConceptDetail", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        detail.AddChild(new Label { Name = "ConceptTitle", Text = selected.Title });
        detail.AddChild(new Label
        {
            Name = "ConceptText",
            Text = selected.Text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(360, 0),
        });
        if (selected.Links.Count > 0)
        {
            // "See also:" cross-links — each jumps to the referenced entry (a good/building/unit or another topic).
            var links = new HBoxContainer { Name = "ConceptLinks" };
            links.AddChild(new Label { Text = "See also:", SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            foreach (ColopediaConcepts.ConceptLink link in selected.Links)
            {
                links.AddChild(LinkButton(link.Label, ToCategory(link.Category), link.Anchor));
            }
            detail.AddChild(links);
        }
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

    /// <summary>Entry-icon side length (px) — a small goods/unit/building thumbnail at the left of a Colopedia row.</summary>
    private const int IconSize = 30;

    /// <summary>Prepends a small entry icon to a row when its art exists (goods icon / unit sprite / building image), else no-op so the row is label-only.</summary>
    private static void AddIcon(HBoxContainer row, Texture2D? tex)
    {
        if (tex is null)
        {
            return;
        }
        row.AddChild(new TextureRect
        {
            Texture = tex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(IconSize, IconSize),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
    }

    /// <summary>A percentage label for a defence bonus (0 → "+0%").</summary>
    private static string Pct(double v) => $"{(v >= 0 ? "+" : "")}{v:0.#}%";

    /// <summary>A space-free node-name slug of a Concepts topic title (keeps letters/digits, drops everything else).</summary>
    private static string Slug(string title) =>
        new string(title.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Title-cases a short id for display (e.g. <c>tradeGoods</c> → <c>Trade Goods</c>) — the shared <see cref="Naming.Humanize"/> (one humaniser everywhere, so display overrides like country → "Pasture" apply uniformly; this had its own duplicate copy).</summary>
    private string Title(string shortName) => Naming.Humanize(shortName, _displayOverrides);
}
