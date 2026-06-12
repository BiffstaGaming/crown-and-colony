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

    private Ruleset(Dictionary<string, TerrainType> terrainById)
    {
        _terrainById = terrainById;
        TerrainTypes = _terrainById.Values.ToList();
    }

    /// <summary>All terrain types, in specification order.</summary>
    public IReadOnlyList<TerrainType> TerrainTypes { get; }

    /// <summary>Looks up a terrain type by ruleset id (e.g. <c>model.tile.plains</c>).</summary>
    /// <exception cref="KeyNotFoundException">Unknown id.</exception>
    public TerrainType Terrain(string id) =>
        _terrainById.TryGetValue(id, out var t)
            ? t
            : throw new KeyNotFoundException($"Unknown terrain type '{id}'.");

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

        return new Ruleset(terrain);
    }

    private static TerrainType ParseTileType(XElement el)
    {
        string id = RequiredAttribute(el, "id");

        var productions = el.Elements("production")
            .Select(p => new ProductionEntry(
                Unattended: (bool?)p.Attribute("unattended") ?? false,
                Outputs: p.Elements("output")
                    .Select(o => new GoodsOutput(
                        RequiredAttribute(o, "goods-type"),
                        (int?)o.Attribute("value")
                            ?? throw new RulesetFormatException($"<output> in '{id}' lacks a value.")))
                    .ToList()))
            .ToList();

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
            Productions: productions);
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
