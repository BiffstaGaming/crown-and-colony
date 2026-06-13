using System.Reflection;
using System.Xml.Linq;

namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The parsed game ruleset. Loaded from a FreeCol-format <c>specification.xml</c>
/// (ADR-004); the classic ruleset ships embedded in this assembly so every host
/// (game, tests, CI) reads the identical bytes.
/// </summary>
public sealed class Ruleset
{
    private readonly Dictionary<string, TerrainType> _terrainById;
    private readonly Dictionary<string, UnitType> _unitById;
    private readonly Dictionary<string, GoodsType> _goodsById;
    private readonly Dictionary<string, BuildingType> _buildingById;

    private Ruleset(
        Dictionary<string, TerrainType> terrainById,
        Dictionary<string, UnitType> unitById,
        Dictionary<string, GoodsType> goodsById,
        Dictionary<string, BuildingType> buildingById)
    {
        _terrainById = terrainById;
        _unitById = unitById;
        _goodsById = goodsById;
        _buildingById = buildingById;
        TerrainTypes = _terrainById.Values.ToList();
        UnitTypes = _unitById.Values.ToList();
        GoodsTypes = _goodsById.Values.ToList();
        BuildingTypes = _buildingById.Values.ToList();
    }

    /// <summary>All terrain types, in specification order.</summary>
    public IReadOnlyList<TerrainType> TerrainTypes { get; }

    /// <summary>All concrete (non-abstract) unit types, in specification order.</summary>
    public IReadOnlyList<UnitType> UnitTypes { get; }

    /// <summary>Looks up a terrain type by ruleset id (e.g. <c>model.tile.plains</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public TerrainType Terrain(string id) =>
        _terrainById.TryGetValue(id, out var t)
            ? t
            : throw new KeyNotFoundException($"Unknown terrain type '{id}'.");

    /// <summary>Looks up a unit type by ruleset id (e.g. <c>model.unit.freeColonist</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public UnitType Unit(string id) =>
        _unitById.TryGetValue(id, out var u)
            ? u
            : throw new KeyNotFoundException($"Unknown unit type '{id}'.");

    /// <summary>All goods types, in specification order.</summary>
    public IReadOnlyList<GoodsType> GoodsTypes { get; }

    /// <summary>Looks up a goods type by ruleset id (e.g. <c>model.goods.sugar</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public GoodsType Goods(string id) =>
        _goodsById.TryGetValue(id, out var g)
            ? g
            : throw new KeyNotFoundException($"Unknown goods type '{id}'.");

    /// <summary>
    /// The warehouse id a goods type stores as (grain → food); unknown ids pass
    /// through unchanged so test rulesets without goods stay usable.
    /// </summary>
    public string StorageIdOf(string goodsId) =>
        _goodsById.TryGetValue(goodsId, out var g) ? g.StoredAs : goodsId;

    /// <summary>All building types, in specification order.</summary>
    public IReadOnlyList<BuildingType> BuildingTypes { get; }

    /// <summary>Looks up a building type by ruleset id (e.g. <c>model.building.townHall</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public BuildingType Building(string id) =>
        _buildingById.TryGetValue(id, out var b)
            ? b
            : throw new KeyNotFoundException($"Unknown building type '{id}'.");

    /// <summary>Loads the classic (1994-faithful) ruleset embedded in this assembly.</summary>
    public static Ruleset LoadClassic()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resource = "CrownAndColony.GameLogic.Specification.classic.specification.xml";
        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded ruleset '{resource}' missing from assembly.");
        return Load(stream);
    }

    /// <summary>Parses a ruleset from FreeCol-format specification XML.</summary>
    /// <exception cref="RulesetFormatException">The XML is missing required elements or attributes.</exception>
    public static Ruleset Load(Stream xml)
    {
        XDocument doc = XDocument.Load(xml);
        XElement root = doc.Root
            ?? throw new RulesetFormatException("Specification has no root element.");

        XElement tileTypes = root.Element("tile-types")
            ?? throw new RulesetFormatException("Specification has no <tile-types> section.");

        var terrain = new Dictionary<string, TerrainType>();
        foreach (XElement el in tileTypes.Elements("tile-type"))
        {
            TerrainType parsed = ParseTileType(el);
            if (!terrain.TryAdd(parsed.Id, parsed))
            {
                throw new RulesetFormatException($"Duplicate tile-type id '{parsed.Id}'.");
            }
        }

        if (terrain.Count == 0)
        {
            throw new RulesetFormatException("Specification defines no tile types.");
        }

        XElement unitTypes = root.Element("unit-types")
            ?? throw new RulesetFormatException("Specification has no <unit-types> section.");
        Dictionary<string, UnitType> units = ParseUnitTypes(unitTypes);

        var goods = new Dictionary<string, GoodsType>();
        foreach (XElement el in root.Element("goods-types")?.Elements("goods-type") ?? [])
        {
            string id = RequiredAttribute(el, "id");
            XElement? market = el.Element("market");
            goods[id] = new GoodsType(
                Id: id,
                IsFood: (bool?)el.Attribute("is-food") ?? false,
                StoredAs: (string?)el.Attribute("stored-as") ?? id,
                MadeFrom: (string?)el.Attribute("made-from"),
                IsFarmed: (bool?)el.Attribute("is-farmed") ?? false,
                BreedingNumber: (int?)el.Attribute("breeding-number"),
                IsNewWorldGoods: (bool?)el.Attribute("new-world-goods") ?? false,
                Market: market is null ? null : new GoodsMarket(
                    InitialAmount: (int?)market.Attribute("initial-amount")
                        ?? throw new RulesetFormatException($"<market> in '{id}' lacks initial-amount."),
                    InitialPrice: (int?)market.Attribute("initial-price")
                        ?? throw new RulesetFormatException($"<market> in '{id}' lacks initial-price."),
                    PriceDifference: (int?)market.Attribute("price-difference")
                        ?? throw new RulesetFormatException($"<market> in '{id}' lacks price-difference.")));
        }

        var buildings = new Dictionary<string, BuildingType>();
        var buildingElements = new Dictionary<string, XElement>();
        foreach (XElement el in root.Element("building-types")?.Elements("building-type") ?? [])
        {
            buildingElements[RequiredAttribute(el, "id")] = el;
        }
        foreach ((string id, XElement el) in buildingElements)
        {
            // Productions: the building's own if defined, else inherited up the
            // extends chain. Attributes: nearest definition wins (FreeCol default
            // workplaces = 3).
            XElement? productionSource = el;
            while (productionSource is not null && !productionSource.Elements("production").Any())
            {
                string? parent = (string?)productionSource.Attribute("extends");
                productionSource = parent is not null ? buildingElements.GetValueOrDefault(parent) : null;
            }

            buildings[id] = new BuildingType(
                Id: id,
                UpgradesFrom: (string?)el.Attribute("upgrades-from"),
                Workplaces: ResolveIntAttribute(el, "workplaces", buildingElements) ?? 3,
                RequiredPopulation: ResolveIntAttribute(el, "required-population", buildingElements) ?? 1,
                Productions: (productionSource?.Elements("production") ?? [])
                    .Select(ParseProduction)
                    .ToList(),
                BuildCost: el.Elements("required-goods")
                    .Select(g => new GoodsOutput(
                        RequiredAttribute(g, "id"),
                        (int?)g.Attribute("value")
                            ?? throw new RulesetFormatException($"required-goods in '{id}' lacks value.")))
                    .ToList());
        }

        return new Ruleset(terrain, units, goods, buildings);
    }

    private static ProductionEntry ParseProduction(XElement p) => new(
        Unattended: (bool?)p.Attribute("unattended") ?? false,
        Outputs: p.Elements("output")
            .Select(o => new GoodsOutput(
                RequiredAttribute(o, "goods-type"),
                (int?)o.Attribute("value")
                    ?? throw new RulesetFormatException("<output> lacks a value.")))
            .ToList(),
        Inputs: p.Elements("input")
            .Select(o => new GoodsOutput(
                RequiredAttribute(o, "goods-type"),
                (int?)o.Attribute("value")
                    ?? throw new RulesetFormatException("<input> lacks a value.")))
            .ToList());

    /// <summary>
    /// Parses unit types, resolving the spec's <c>extends</c> inheritance chains
    /// (attributes: nearest definition wins; abilities: any ancestor may set them).
    /// Abstract types participate in resolution but are not exposed.
    /// </summary>
    private static Dictionary<string, UnitType> ParseUnitTypes(XElement unitTypes)
    {
        var elements = new Dictionary<string, XElement>();
        foreach (XElement el in unitTypes.Elements("unit-type"))
        {
            string id = RequiredAttribute(el, "id");
            if (!elements.TryAdd(id, el))
            {
                throw new RulesetFormatException($"Duplicate unit-type id '{id}'.");
            }
        }

        var units = new Dictionary<string, UnitType>();
        foreach ((string id, XElement el) in elements)
        {
            if ((bool?)el.Attribute("abstract") ?? false)
            {
                continue;
            }

            units[id] = new UnitType(
                Id: id,
                Movement: ResolveIntAttribute(el, "movement", elements) ?? 3,
                LineOfSight: ResolveIntAttribute(el, "line-of-sight", elements) ?? 1,
                IsNaval: ResolveAbility(el, "model.ability.navalUnit", elements),
                CanFoundColony: ResolveAbility(el, "model.ability.foundColony", elements));
        }

        if (units.Count == 0)
        {
            throw new RulesetFormatException("Specification defines no concrete unit types.");
        }
        return units;
    }

    /// <summary>Walks the extends chain until an element defines the attribute. Defaults match FreeCol's UnitType.</summary>
    private static int? ResolveIntAttribute(
        XElement el, string name, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            if ((int?)current.Attribute(name) is int value)
            {
                return value;
            }
        }
        return null;
    }

    /// <summary>True when any element in the extends chain sets the ability to true (nearest wins).</summary>
    private static bool ResolveAbility(
        XElement el, string abilityId, Dictionary<string, XElement> elements)
    {
        for (XElement? current = el; current is not null; current = ParentOf(current, elements))
        {
            XElement? ability = current.Elements("ability")
                .FirstOrDefault(a => (string?)a.Attribute("id") == abilityId);
            if (ability is not null)
            {
                return (bool?)ability.Attribute("value") ?? true;
            }
        }
        return false;
    }

    private static XElement? ParentOf(XElement el, Dictionary<string, XElement> elements)
    {
        string? parentId = (string?)el.Attribute("extends");
        if (parentId is null)
        {
            return null;
        }
        return elements.TryGetValue(parentId, out XElement? parent)
            ? parent
            : throw new RulesetFormatException(
                $"unit-type '{(string?)el.Attribute("id")}' extends unknown type '{parentId}'.");
    }

    private static TerrainType ParseTileType(XElement el)
    {
        string id = RequiredAttribute(el, "id");

        var productions = el.Elements("production").Select(ParseProduction).ToList();

        XElement? gen = el.Element("gen");

        return new TerrainType(
            Id: id,
            MoveCost: (int?)el.Attribute("basic-move-cost")
                ?? throw new RulesetFormatException($"Tile-type '{id}' lacks basic-move-cost."),
            WorkTurns: (int?)el.Attribute("basic-work-turns")
                ?? throw new RulesetFormatException($"Tile-type '{id}' lacks basic-work-turns."),
            IsForest: (bool?)el.Attribute("is-forest") ?? false,
            IsWater: (bool?)el.Attribute("is-water") ?? false,
            IsElevation: (bool?)el.Attribute("is-elevation") ?? false,
            CanSettle: (bool?)el.Attribute("can-settle") ?? true,
            IsConnected: (bool?)el.Attribute("is-connected") ?? false,
            Productions: productions,
            Gen: gen is null ? null : new GenRanges(
                HumidityMin: (int?)gen.Attribute("humidity-minimum") ?? 0,
                HumidityMax: (int?)gen.Attribute("humidity-maximum") ?? 100,
                TemperatureMin: (int?)gen.Attribute("temperature-minimum") ?? -20,
                TemperatureMax: (int?)gen.Attribute("temperature-maximum") ?? 40,
                AltitudeMin: (int?)gen.Attribute("altitude-minimum") ?? 0,
                AltitudeMax: (int?)gen.Attribute("altitude-maximum") ?? 30),
            Resources: el.Elements("resource")
                .Select(r => new ResourceChance(
                    RequiredAttribute(r, "type"),
                    (int?)r.Attribute("probability") ?? 100))
                .ToList());
    }

    private static string RequiredAttribute(XElement el, string name) =>
        el.Attribute(name)?.Value
            ?? throw new RulesetFormatException($"<{el.Name}> lacks required attribute '{name}'.");
}

/// <summary>Thrown when a ruleset specification file is malformed.</summary>
public sealed class RulesetFormatException : Exception
{
    /// <summary>Creates the exception with a description of the format problem.</summary>
    public RulesetFormatException(string message) : base(message) { }
}
