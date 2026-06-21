using System.Globalization;
using System.IO;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// The result of importing a map definition (<see cref="MapImporter"/>): the loaded <see cref="GameMap"/> — terrain
/// plus any imported overlays (bonus resources + their quantities, river/road improvements, lost-city rumours) — and
/// the native settlements the file places, as initial entities the caller installs into the game.
/// </summary>
/// <param name="Map">The imported world grid, carrying every overlay the definition declared (empty layers when it declared none).</param>
/// <param name="Settlements">
/// The native settlements the definition places (ids assigned from 1 in file order), or empty when it places none. The
/// caller wires these into the game (turning their nations into players, claiming their land); the importer only parses
/// their placement, mirroring FreeCol <c>FreeColMapLoader</c> importing a saved map's <c>IndianSettlement</c>s.
/// </param>
public sealed record MapImportResult(GameMap Map, IReadOnlyList<NativeSettlement> Settlements);

/// <summary>
/// Parses a map definition (a terrain grid plus optional resource/bonus, rumour and settlement overlays) into a
/// <see cref="GameMap"/> and its initial native settlements — a faithful subset of FreeCol's saved-map import
/// (<c>net.sf.freecol.server.generator.FreeColMapLoader</c>), which reads a saved game's tiles with their
/// <c>TileItemContainer</c> items (resources, rivers, lost-city rumours) and native ownership/settlements.
/// </summary>
/// <remarks>
/// <para>
/// <b>Engine-free</b> (ADR-006); the import is deterministic (a definition's bytes never change) — no RNG, so a fixed
/// map can be imported in a default-byte-identical way. A definition that declares <i>no</i> overlay sections (the
/// shipped <c>america.txt</c> is terrain-only) imports to a map with empty resource/improvement/rumour layers and no
/// settlements — exactly the terrain-only map <see cref="FixedMap"/> historically produced, so the default America game
/// stays byte-identical (its rivers/resources/natives are laid by the game-start generators as before).
/// </para>
/// <para>
/// <b>Definition format</b> (UTF-8 text; <c>#</c> begins a comment; blank lines ignored):
/// </para>
/// <list type="number">
/// <item><description>A header line <c>WIDTH HEIGHT</c>.</description></item>
/// <item><description><c>HEIGHT</c> terrain rows, each <c>WIDTH</c> terrain short names (row-major; <c>plains</c> → <c>model.tile.plains</c>).</description></item>
/// <item><description>
/// Then any of four optional, order-independent overlay sections, each introduced by a bracketed header:
/// <list type="bullet">
/// <item><description>
/// <c>[resources]</c> — rows <c>X Y resourceShortName [QUANTITY]</c> placing a bonus resource on a tile (a finite
/// resource may carry an explicit remaining quantity; FreeCol <c>Resource.quantity</c>).
/// </description></item>
/// <item><description>
/// <c>[improvements]</c> — rows <c>X Y improvementShortName [MAGNITUDE]</c> placing a tile improvement (river/road;
/// FreeCol <c>TileImprovement</c>). Magnitude defaults to the ruleset type's (a river's small/large band).
/// </description></item>
/// <item><description><c>[rumours]</c> — rows <c>X Y</c> marking a lost-city rumour tile (FreeCol <c>LostCityRumour</c> tile item).</description></item>
/// <item><description>
/// <c>[settlements]</c> — rows <c>X Y nationTypeShortName settlementTypeShortName capital|regular SIZE [skillUnitShortName]</c>
/// placing a native settlement (FreeCol <c>IndianSettlement</c>).
/// </description></item>
/// </list>
/// </description></item>
/// </list>
/// <para>
/// <b>Faithful-subset scope.</b> We import the overlay kinds the gameplay layers already model: bonus resources (+ a
/// finite quantity), river/road improvements, lost-city rumours and native settlements (placement + size + capital flag
/// + learnable skill). Deliberately <i>deferred</i> (documented in <c>docs/systems/map-terrain.md</c>): river
/// connection styling, per-tile region ids (we re-derive regions from terrain), tile ownership without a settlement,
/// European colonies/units and player start positions, and the binary FreeCol <c>.fsm</c>/Colonization <c>.MP</c>
/// containers (we read our extracted text form — see <c>game/data/maps/PROVENANCE.md</c>).
/// </para>
/// </remarks>
public static class MapImporter
{
    private const char SectionOpen = '[';

    /// <summary>The <c>[resources]</c> overlay section header (bonus resources + their finite quantity).</summary>
    private const string ResourcesSection = "[resources]";

    /// <summary>The <c>[improvements]</c> overlay section header (river/road tile improvements).</summary>
    private const string ImprovementsSection = "[improvements]";

    /// <summary>The <c>[rumours]</c> overlay section header (lost-city rumour tiles).</summary>
    private const string RumoursSection = "[rumours]";

    /// <summary>The <c>[settlements]</c> overlay section header (native settlements).</summary>
    private const string SettlementsSection = "[settlements]";

    /// <summary>The settlement-row keyword marking a nation's capital (vs. <c>regular</c>).</summary>
    private const string CapitalKeyword = "capital";

    /// <summary>The settlement-row keyword marking an ordinary (non-capital) settlement.</summary>
    private const string RegularKeyword = "regular";

    /// <summary>
    /// Imports a map definition from <paramref name="reader"/> into a <see cref="MapImportResult"/> (the
    /// <see cref="GameMap"/> with every declared overlay, plus the native settlements it places).
    /// </summary>
    /// <param name="reader">The definition text (header + terrain rows + optional overlay sections). Not disposed here.</param>
    /// <param name="ruleset">The ruleset whose terrain/resource/improvement/nation/settlement ids the definition resolves against.</param>
    /// <param name="sourceName">A name for the definition (e.g. the resource/file name) used in parse-error messages.</param>
    /// <exception cref="InvalidDataException">The definition is malformed (bad header/row, off-map coordinate, unknown id).</exception>
    public static MapImportResult Import(TextReader reader, Ruleset ruleset, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(ruleset);

        var lines = new LineCursor(reader, sourceName);

        (int width, int height) = ReadHeader(lines);
        TerrainType[] terrain = ReadTerrainGrid(lines, ruleset, width, height);

        var resources = new Dictionary<Position, string>();
        var resourceQuantities = new Dictionary<Position, int>();
        var improvements = new Dictionary<Position, List<TileImprovementType>>();
        var rumours = new HashSet<Position>();
        var settlements = new List<NativeSettlement>();

        // Optional overlay sections follow the grid in any order; each begins with a '[name]' header. A terrain-only
        // file (america.txt) has none, so every layer stays empty — the historical terrain-only import.
        for (string? line = lines.NextContent(); line is not null; line = lines.NextContent())
        {
            switch (line.ToLowerInvariant())
            {
                case ResourcesSection:
                    ReadResources(lines, ruleset, width, height, resources, resourceQuantities);
                    break;
                case ImprovementsSection:
                    ReadImprovements(lines, ruleset, width, height, improvements);
                    break;
                case RumoursSection:
                    ReadRumours(lines, width, height, rumours);
                    break;
                case SettlementsSection:
                    ReadSettlements(lines, ruleset, width, height, settlements);
                    break;
                default:
                    throw lines.Error($"expected an overlay section header ('{ResourcesSection}', "
                        + $"'{ImprovementsSection}', '{RumoursSection}', '{SettlementsSection}'), got '{line}'");
            }
        }

        var map = new GameMap(
            width, height, terrain,
            resources: resources.Count > 0 ? resources : null,
            rumours: rumours.Count > 0 ? rumours : null,
            resourceQuantities: resourceQuantities.Count > 0 ? resourceQuantities : null,
            improvements: improvements.Count > 0
                ? improvements.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<TileImprovementType>)kv.Value)
                : null);
        return new MapImportResult(map, settlements);
    }

    private static (int Width, int Height) ReadHeader(LineCursor lines)
    {
        string header = lines.NextContent() ?? throw lines.Error("the definition is empty");
        string[] dims = Split(header);
        if (dims.Length != 2 || !TryPositive(dims[0], out int width) || !TryPositive(dims[1], out int height))
        {
            throw lines.Error($"the header must be 'WIDTH HEIGHT' (positive integers), got '{header}'");
        }
        return (width, height);
    }

    private static TerrainType[] ReadTerrainGrid(LineCursor lines, Ruleset ruleset, int width, int height)
    {
        var terrain = new TerrainType[width * height];
        for (int y = 0; y < height; y++)
        {
            string row = lines.NextContent() ?? throw lines.Error($"ended at terrain row {y}; expected {height} rows");
            string[] cells = Split(row);
            if (cells.Length != width)
            {
                throw lines.Error($"terrain row {y} has {cells.Length} tiles; expected {width}");
            }
            for (int x = 0; x < width; x++)
            {
                terrain[(y * width) + x] = ResolveTerrain(lines, ruleset, cells[x]);
            }
        }
        return terrain;
    }

    private static void ReadResources(
        LineCursor lines, Ruleset ruleset, int width, int height,
        Dictionary<Position, string> resources, Dictionary<Position, int> resourceQuantities)
    {
        foreach (string[] fields in ReadSectionRows(lines, min: 3, max: 4, "resource"))
        {
            Position p = ParsePosition(lines, fields, width, height);
            ResourceType resource = ResolveResource(lines, ruleset, fields[2]);
            resources[p] = resource.Id;
            if (fields.Length == 4)
            {
                if (!TryNonNegative(fields[3], out int quantity))
                {
                    throw lines.Error($"resource quantity must be a non-negative integer, got '{fields[3]}'");
                }
                resourceQuantities[p] = quantity;
            }
        }
    }

    private static void ReadImprovements(
        LineCursor lines, Ruleset ruleset, int width, int height,
        Dictionary<Position, List<TileImprovementType>> improvements)
    {
        foreach (string[] fields in ReadSectionRows(lines, min: 3, max: 4, "improvement"))
        {
            Position p = ParsePosition(lines, fields, width, height);
            TileImprovementType type = ResolveImprovement(lines, ruleset, fields[2]);
            if (fields.Length == 4)
            {
                if (!TryPositive(fields[3], out int magnitude))
                {
                    throw lines.Error($"improvement magnitude must be a positive integer, got '{fields[3]}'");
                }
                type = type with { Magnitude = magnitude };
            }
            if (!improvements.TryGetValue(p, out List<TileImprovementType>? list))
            {
                improvements[p] = list = [];
            }
            list.RemoveAll(i => i.Id == type.Id); // one improvement of each type per tile (FreeCol TileItemContainer)
            list.Add(type);
        }
    }

    private static void ReadRumours(LineCursor lines, int width, int height, HashSet<Position> rumours)
    {
        foreach (string[] fields in ReadSectionRows(lines, min: 2, max: 2, "rumour"))
        {
            rumours.Add(ParsePosition(lines, fields, width, height));
        }
    }

    private static void ReadSettlements(
        LineCursor lines, Ruleset ruleset, int width, int height, List<NativeSettlement> settlements)
    {
        foreach (string[] fields in ReadSectionRows(lines, min: 6, max: 7, "settlement"))
        {
            // X Y nationType settlementType capital|regular SIZE [skillUnit]
            Position p = ParsePosition(lines, fields, width, height);
            NativeNationType nation = ResolveNation(lines, ruleset, fields[2]);
            SettlementType type = ResolveSettlement(lines, ruleset, fields[3]);
            bool capital = ParseCapital(lines, fields[4]);
            if (!TryPositive(fields[5], out int size))
            {
                throw lines.Error($"settlement size must be a positive integer, got '{fields[5]}'");
            }
            string? skill = fields.Length == 7 ? ResolveSkillUnit(lines, ruleset, fields[6]) : null;
            settlements.Add(new NativeSettlement(
                id: settlements.Count + 1,
                nationTypeId: nation.Id,
                settlementTypeId: type.Id,
                isCapital: capital,
                position: p,
                size: size,
                learnableSkill: skill));
        }
    }

    // --- shared row reading -------------------------------------------------

    /// <summary>
    /// Yields the data rows of an overlay section (until the next section header or end of file), each split into its
    /// whitespace-separated fields and validated to hold between <paramref name="min"/> and <paramref name="max"/> of
    /// them. A line starting <c>[</c> is the next section header: it ends this section and is pushed back for the
    /// caller's top-level section loop.
    /// </summary>
    private static IEnumerable<string[]> ReadSectionRows(LineCursor lines, int min, int max, string rowKind)
    {
        for (string? line = lines.NextContent(); line is not null; line = lines.NextContent())
        {
            if (line.StartsWith(SectionOpen))
            {
                lines.PushBack(line); // a new section header — hand control back to the section loop
                yield break;
            }
            string[] fields = Split(line);
            if (fields.Length < min || fields.Length > max)
            {
                throw lines.Error(min == max
                    ? $"a {rowKind} row needs {min} fields, got {fields.Length}"
                    : $"a {rowKind} row needs {min}-{max} fields, got {fields.Length}");
            }
            yield return fields;
        }
    }

    // --- field parsing ------------------------------------------------------

    private static Position ParsePosition(LineCursor lines, string[] fields, int width, int height)
    {
        if (!TryNonNegative(fields[0], out int x) || !TryNonNegative(fields[1], out int y))
        {
            throw lines.Error($"coordinates must be non-negative integers, got '{fields[0]} {fields[1]}'");
        }
        if (x >= width || y >= height)
        {
            throw lines.Error($"coordinate ({x},{y}) is off the {width}x{height} map");
        }
        return new Position(x, y);
    }

    private static bool ParseCapital(LineCursor lines, string flag) => flag.ToLowerInvariant() switch
    {
        CapitalKeyword => true,
        RegularKeyword => false,
        _ => throw lines.Error($"settlement flag must be '{CapitalKeyword}' or '{RegularKeyword}', got '{flag}'"),
    };

    private static TerrainType ResolveTerrain(LineCursor lines, Ruleset ruleset, string shortName)
    {
        try
        {
            return ruleset.Terrain(Qualify("model.tile.", shortName));
        }
        catch (KeyNotFoundException ex)
        {
            throw lines.Error($"unknown terrain '{shortName}'", ex);
        }
    }

    private static ResourceType ResolveResource(LineCursor lines, Ruleset ruleset, string shortName)
    {
        try
        {
            return ruleset.Resource(Qualify("model.resource.", shortName));
        }
        catch (KeyNotFoundException ex)
        {
            throw lines.Error($"unknown resource '{shortName}'", ex);
        }
    }

    private static TileImprovementType ResolveImprovement(LineCursor lines, Ruleset ruleset, string shortName)
    {
        try
        {
            return ruleset.Improvement(Qualify("model.improvement.", shortName));
        }
        catch (KeyNotFoundException ex)
        {
            throw lines.Error($"unknown improvement '{shortName}'", ex);
        }
    }

    private static NativeNationType ResolveNation(LineCursor lines, Ruleset ruleset, string shortName)
    {
        try
        {
            return ruleset.NativeNation(Qualify("model.nationType.", shortName));
        }
        catch (KeyNotFoundException ex)
        {
            throw lines.Error($"unknown native nation '{shortName}'", ex);
        }
    }

    private static SettlementType ResolveSettlement(LineCursor lines, Ruleset ruleset, string shortName)
    {
        try
        {
            return ruleset.Settlement(Qualify("model.settlement.", shortName));
        }
        catch (KeyNotFoundException ex)
        {
            throw lines.Error($"unknown settlement type '{shortName}'", ex);
        }
    }

    private static string ResolveSkillUnit(LineCursor lines, Ruleset ruleset, string shortName)
    {
        string id = Qualify("model.unit.", shortName);
        try
        {
            _ = ruleset.Unit(id); // validate the learnable-skill unit exists (its yield is just the id)
            return id;
        }
        catch (KeyNotFoundException ex)
        {
            throw lines.Error($"unknown learnable-skill unit '{shortName}'", ex);
        }
    }

    /// <summary>Qualifies a short name with its ruleset prefix unless it is already a fully-qualified <c>model.*</c> id.</summary>
    private static string Qualify(string prefix, string token) =>
        token.StartsWith("model.", StringComparison.Ordinal) ? token : prefix + token;

    private static string[] Split(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool TryPositive(string token, out int value) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;

    private static bool TryNonNegative(string token, out int value) =>
        int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0;

    /// <summary>
    /// A forward-only line reader that skips blank and <c>#</c>-comment lines, tracks the 1-based line number for parse
    /// errors, lower-cases nothing (callers decide), and supports a one-line push-back (so a section's row loop can hand
    /// a following section header back to the top-level section loop).
    /// </summary>
    private sealed class LineCursor(TextReader reader, string sourceName)
    {
        private int _lineNumber;
        private string? _pushedBack;
        private int _pushedBackLineNumber;

        /// <summary>The next non-blank, non-comment, trimmed line (inline <c>#</c> comment stripped), or null at end of file.</summary>
        public string? NextContent()
        {
            if (_pushedBack is not null)
            {
                _lineNumber = _pushedBackLineNumber;
                string back = _pushedBack;
                _pushedBack = null;
                return back;
            }

            for (string? line = reader.ReadLine(); line is not null; line = reader.ReadLine())
            {
                _lineNumber++;
                string content = StripComment(line).Trim();
                if (content.Length > 0)
                {
                    return content;
                }
            }
            return null;
        }

        /// <summary>Returns a line to the cursor so the next <see cref="NextContent"/> re-yields it (single-slot).</summary>
        public void PushBack(string line)
        {
            _pushedBack = line;
            _pushedBackLineNumber = _lineNumber;
        }

        /// <summary>Builds a parse error naming the source and the current line number.</summary>
        public InvalidDataException Error(string message, Exception? inner = null) =>
            new($"Map '{sourceName}' line {_lineNumber}: {message}.", inner);

        private static string StripComment(string line)
        {
            int hash = line.IndexOf('#');
            return hash < 0 ? line : line[..hash];
        }
    }
}
