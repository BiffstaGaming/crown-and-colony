using System.Globalization;
using System.IO;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World.Improvements;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// The result of importing a map definition (<see cref="MapImporter"/>): the loaded <see cref="GameMap"/> — terrain
/// plus any imported overlays (bonus resources + their quantities, river/road improvements, lost-city rumours, a
/// region layer) — the native settlements the file places, and any fixed start positions, as initial entities the
/// caller installs into the game.
/// </summary>
/// <param name="Map">The imported world grid, carrying every overlay the definition declared (empty layers when it declared none).</param>
/// <param name="Settlements">
/// The native settlements the definition places (ids assigned from 1 in file order), or empty when it places none. The
/// caller wires these into the game (turning their nations into players, claiming their land); the importer only parses
/// their placement, mirroring FreeCol <c>FreeColMapLoader</c> importing a saved map's <c>IndianSettlement</c>s.
/// </param>
/// <param name="HumanStart">
/// The tile the human player is fixed to land on (the <c>human</c> entry in a <c>[starts]</c> section), or null when
/// the definition declares none — then the caller falls back to its coastal-start heuristic. Mirrors FreeCol fixing a
/// player's start tile in a saved scenario (<c>Player.entryTile</c> / the European starting-positions generator).
/// </param>
/// <param name="RefEntry">
/// The tile the Royal Expeditionary Force enters at (the <c>ref</c> entry in a <c>[starts]</c> section), or null when
/// the definition declares none — then the caller falls back to the nearest water tile to the human's start. Mirrors
/// FreeCol storing the REF player's <c>entryTile</c> (<c>ourREF.setEntryTile</c>).
/// </param>
public sealed record MapImportResult(
    GameMap Map,
    IReadOnlyList<NativeSettlement> Settlements,
    Position? HumanStart = null,
    Position? RefEntry = null);

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
/// Then any of six optional, order-independent overlay sections, each introduced by a bracketed header:
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
/// <item><description>
/// <c>[starts]</c> — rows <c>human X Y</c> and/or <c>ref X Y</c> fixing the human's landing tile and the REF's entry
/// tile (FreeCol <c>Player.entryTile</c>). Either may be omitted; a missing one falls back to the caller's heuristic.
/// </description></item>
/// <item><description>
/// <c>[regions]</c> — a region layer: <c>region ID TYPE [SCORE [KEY [PARENT]]]</c> rows declaring the region table
/// (id from 0, dense, in order) and <c>X Y REGION_ID</c> rows assigning tiles to a declared region (FreeCol saved-map
/// <c>tile.setRegion</c>). Every in-bounds tile must be assigned; an unassigned tile is a parse error.
/// </description></item>
/// </list>
/// </description></item>
/// </list>
/// <para>
/// <b>Faithful-subset scope.</b> We import the overlay kinds the gameplay layers already model: bonus resources (+ a
/// finite quantity), river/road improvements, lost-city rumours, native settlements (placement + size + capital flag
/// + learnable skill), fixed player start positions (<c>[starts]</c>) and a per-tile region layer (<c>[regions]</c>).
/// A map that declares no <c>[regions]</c> section leaves the region layer for the generator to re-derive from terrain;
/// one that declares no <c>[starts]</c> section leaves the start tiles to the caller's heuristic — so a definition
/// declaring neither (the shipped <c>america.txt</c>) is unchanged. The binary Colonization <c>.MP</c> container is
/// <i>also</i> read (<see cref="ImportColonization"/>, a port of FreeCol <c>ColonizationMapLoader</c> — terrain +
/// river magnitude only, exactly FreeCol's subset); <see cref="ImportFile"/> dispatches on the file extension.
/// Deliberately <i>deferred</i> (documented in <c>docs/systems/map-terrain.md</c>): river connection styling, tile
/// ownership without a settlement, European colonies/units, and the binary FreeCol <c>.fsm</c> container (we read our
/// extracted text form — see <c>game/data/maps/PROVENANCE.md</c>).
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

    /// <summary>The <c>[starts]</c> overlay section header (fixed human + REF start tiles; FreeCol <c>Player.entryTile</c>).</summary>
    private const string StartsSection = "[starts]";

    /// <summary>The <c>[regions]</c> overlay section header (the per-tile region layer).</summary>
    private const string RegionsSection = "[regions]";

    /// <summary>The settlement-row keyword marking a nation's capital (vs. <c>regular</c>).</summary>
    private const string CapitalKeyword = "capital";

    /// <summary>The settlement-row keyword marking an ordinary (non-capital) settlement.</summary>
    private const string RegularKeyword = "regular";

    /// <summary>The <c>[starts]</c>-row keyword for the human player's landing tile.</summary>
    private const string HumanStartKeyword = "human";

    /// <summary>The <c>[starts]</c>-row keyword for the Royal Expeditionary Force's entry tile.</summary>
    private const string RefStartKeyword = "ref";

    /// <summary>The <c>[regions]</c>-row keyword opening a region-table declaration (vs. a tile-assignment row).</summary>
    private const string RegionDeclarationKeyword = "region";

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
        Position? humanStart = null;
        Position? refEntry = null;
        RegionLayer? regionLayer = null;

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
                case StartsSection:
                    ReadStarts(lines, width, height, ref humanStart, ref refEntry);
                    break;
                case RegionsSection:
                    regionLayer = ReadRegions(lines, width, height);
                    break;
                default:
                    throw lines.Error($"expected an overlay section header ('{ResourcesSection}', "
                        + $"'{ImprovementsSection}', '{RumoursSection}', '{SettlementsSection}', '{StartsSection}', "
                        + $"'{RegionsSection}'), got '{line}'");
            }
        }

        var map = new GameMap(
            width, height, terrain,
            resources: resources.Count > 0 ? resources : null,
            rumours: rumours.Count > 0 ? rumours : null,
            resourceQuantities: resourceQuantities.Count > 0 ? resourceQuantities : null,
            improvements: improvements.Count > 0
                ? improvements.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<TileImprovementType>)kv.Value)
                : null,
            regionIds: regionLayer?.Ids,
            regions: regionLayer?.Regions);
        return new MapImportResult(map, settlements, humanStart, refEntry);
    }

    // --- Colonization .MP import (86d3fpxjc) --------------------------------

    /// <summary>
    /// The 27 FreeCol tile short-names a Colonization <c>.MP</c> terrain code (the byte's low five bits) maps to —
    /// ported <b>verbatim</b> from FreeCol <c>ColonizationMapLoader.tiletypes</c>, including its duplicated forest
    /// block (codes 8–15 and 16–23 name the same eight forests; the two blocks differ only in the original game's
    /// hidden data, which FreeCol — and we — decode identically). Codes 24–26 are arctic / ocean / high seas; codes
    /// 27–31 have no base terrain and are legal only with a hills/mountains overlay (see
    /// <see cref="ImportColonization"/>).
    /// </summary>
    private static readonly string[] ColonizationTileTypes =
    {
        "tundra", "desert", "plains", "prairie", "grassland", "savannah", "marsh", "swamp",
        "borealForest", "scrubForest", "mixedForest", "broadleafForest",
        "coniferForest", "tropicalForest", "wetlandForest", "rainForest",
        "borealForest", "scrubForest", "mixedForest", "broadleafForest",
        "coniferForest", "tropicalForest", "wetlandForest", "rainForest",
        "arctic", "ocean", "highSeas",
    };

    /// <summary>Byte length of the Colonization <c>.MP</c> header (width/height at offsets 0/2; bytes 4–5 have an unknown, fixed function and are ignored — FreeCol ignores them too).</summary>
    private const int ColonizationHeaderLength = 6;

    /// <summary>
    /// Imports a map definition from <paramref name="path"/>, dispatching on the file extension: a <c>.mp</c> file
    /// (case-insensitive) is read as a binary Colonization map via <see cref="ImportColonization"/>; anything else is
    /// read as our UTF-8 text definition via <see cref="Import"/>. The seam the New-Game import UI uses (86d3fq1cg).
    /// </summary>
    /// <param name="path">Path of the map file to import.</param>
    /// <param name="ruleset">The ruleset whose ids the definition resolves against.</param>
    /// <exception cref="InvalidDataException">The definition is malformed (see <see cref="Import"/> / <see cref="ImportColonization"/>).</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static MapImportResult ImportFile(string path, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(ruleset);
        string sourceName = Path.GetFileName(path);
        using FileStream stream = File.OpenRead(path);
        if (Path.GetExtension(path).Equals(".mp", StringComparison.OrdinalIgnoreCase))
        {
            return ImportColonization(stream, ruleset, sourceName);
        }
        using var reader = new StreamReader(stream);
        return Import(reader, ruleset, sourceName);
    }

    /// <summary>
    /// Imports an original-Colonization binary map (<c>.MP</c>) — a faithful port of FreeCol
    /// <c>ColonizationMapLoader</c> (GPL v2). Format: a 6-byte header whose bytes 0/2 carry the map width/height
    /// (read as UInt16 little-endian — identical to FreeCol's signed-byte read for every file FreeCol itself can
    /// load, and additionally correct for widths over 127), then <c>width×height</c> terrain bytes (the first of the
    /// file's three same-size layers; FreeCol reads only this first layer and so do we — layers 2–3 are ignored).
    /// Each terrain byte decodes as <c>terrain = b &amp; 0b11111</c>, <c>overlay = b &gt;&gt; 5</c>: a terrain code
    /// below 27 is the base tile (<see cref="ColonizationTileTypes"/>); otherwise overlay 1/3 is hills and 5/7
    /// mountains. Overlay 2/3 lays a minor river (magnitude 1) and 6/7 a major river (magnitude 2), with no
    /// connection styling (FreeCol's loader leaves connections a TODO).
    /// </summary>
    /// <remarks>
    /// <b>Faithful-subset scope:</b> exactly FreeCol's — terrain plus river magnitude. The format carries no
    /// bonus resources, rumours, settlements or start positions, so the result's settlement list is empty and its
    /// start tiles null (the caller's generators lay rivers'-worth of gameplay on top, as for any terrain-only map).
    /// <b>Documented deviation:</b> a terrain code ≥ 27 whose overlay is not hills/mountains (1/3/5/7) makes FreeCol
    /// silently reuse the <i>previous</i> tile's type (and throw on tile 0) — a latent bug; we throw a clean
    /// <see cref="InvalidDataException"/> instead.
    /// </remarks>
    /// <param name="stream">The <c>.MP</c> bytes. Not disposed here.</param>
    /// <param name="ruleset">The ruleset whose terrain/river ids the decoded codes resolve against.</param>
    /// <param name="sourceName">A name for the map (e.g. the file name) used in parse-error messages.</param>
    /// <exception cref="InvalidDataException">The map is malformed (short header, zero dimension, truncated terrain layer, undefined terrain code).</exception>
    public static MapImportResult ImportColonization(Stream stream, Ruleset ruleset, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(ruleset);

        static InvalidDataException Error(string source, string message) =>
            new($"Map '{source}': {message}.");

        byte[] header = new byte[ColonizationHeaderLength];
        if (ReadUpTo(stream, header) < ColonizationHeaderLength)
        {
            throw Error(sourceName, $"a Colonization .MP map starts with a {ColonizationHeaderLength}-byte header; the file is shorter");
        }
        int width = header[0] | (header[1] << 8);   // UInt16 LE at offset 0
        int height = header[2] | (header[3] << 8);  // UInt16 LE at offset 2
        if (width < 1 || height < 1)
        {
            throw Error(sourceName, $"the header declares an empty {width}x{height} map (dimensions must be at least 1)");
        }

        byte[] layer1 = new byte[width * height];
        int read = ReadUpTo(stream, layer1);
        if (read < layer1.Length)
        {
            throw Error(sourceName, $"the terrain layer is truncated: the header declares {width}x{height} = {layer1.Length} tiles, but only {read} bytes follow");
        }

        var terrain = new TerrainType[width * height];
        var improvements = new Dictionary<Position, IReadOnlyList<TileImprovementType>>();
        TileImprovementType? riverType = null; // resolved on first river tile (a river-less map never needs it)
        for (int index = 0; index < layer1.Length; index++)
        {
            int x = index % width;
            int y = index / width;
            int decimalValue = layer1[index]; // FreeCol's `b & 0xff` — C# bytes are already unsigned
            int terrainCode = decimalValue & 0b11111;
            int overlay = decimalValue >> 5;

            // Base terrain: a code below 27 IS the tile (even under a hill/river overlay — FreeCol checks the code
            // first); 27-31 have no base type and are only legal as hills (overlay 1/3) or mountains (overlay 5/7).
            string shortName;
            if (terrainCode < ColonizationTileTypes.Length)
            {
                shortName = ColonizationTileTypes[terrainCode];
            }
            else if (overlay is 1 or 3)
            {
                shortName = "hills";
            }
            else if (overlay is 5 or 7)
            {
                shortName = "mountains";
            }
            else
            {
                // FreeCol silently reuses the previous tile's type here (NPE on tile 0) — we fail cleanly instead.
                throw Error(sourceName, $"tile ({x},{y}) has undefined Colonization terrain code 0x{decimalValue:X2} (terrain {terrainCode} with overlay {overlay})");
            }
            try
            {
                terrain[index] = ruleset.Terrain(Qualify("model.tile.", shortName));
            }
            catch (KeyNotFoundException ex)
            {
                throw new InvalidDataException($"Map '{sourceName}': tile ({x},{y}) decodes to terrain '{shortName}', which this ruleset does not define.", ex);
            }

            // Rivers: overlay 2/3 = minor (magnitude 1), 6/7 = major (magnitude 2). No connection styling (FreeCol
            // EMPTY_RIVER_STYLE, "//TODO: connections!").
            if (overlay is 2 or 3 or 6 or 7)
            {
                try
                {
                    riverType ??= ruleset.Improvement(TileImprovementType.RiverId);
                }
                catch (KeyNotFoundException ex)
                {
                    throw new InvalidDataException($"Map '{sourceName}': tile ({x},{y}) carries a river, but this ruleset defines no '{TileImprovementType.RiverId}'.", ex);
                }
                improvements[new Position(x, y)] = [riverType with { Magnitude = overlay <= 3 ? 1 : 2 }];
            }
        }

        var map = new GameMap(width, height, terrain,
            improvements: improvements.Count > 0 ? improvements : null);
        return new MapImportResult(map, []);
    }

    /// <summary>Reads up to <paramref name="buffer"/>.Length bytes from <paramref name="stream"/> (looping over partial reads); returns how many were actually read (less than the buffer length only at end of stream).</summary>
    private static int ReadUpTo(Stream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer, total, buffer.Length - total);
            if (n == 0)
            {
                break;
            }
            total += n;
        }
        return total;
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

    private static void ReadStarts(
        LineCursor lines, int width, int height, ref Position? humanStart, ref Position? refEntry)
    {
        // human X Y  /  ref X Y — either may be present (at most once each); a missing one is the caller's heuristic.
        foreach (string[] fields in ReadSectionRows(lines, min: 3, max: 3, "start"))
        {
            // The keyword leads, so the X/Y pair sits in fields[1..2]; reuse ParsePosition by shifting past it.
            Position p = ParsePosition(lines, fields[1..], width, height);
            switch (fields[0].ToLowerInvariant())
            {
                case HumanStartKeyword:
                    if (humanStart is not null)
                    {
                        throw lines.Error($"the '{HumanStartKeyword}' start tile is declared more than once");
                    }
                    humanStart = p;
                    break;
                case RefStartKeyword:
                    if (refEntry is not null)
                    {
                        throw lines.Error($"the '{RefStartKeyword}' start tile is declared more than once");
                    }
                    refEntry = p;
                    break;
                default:
                    throw lines.Error(
                        $"a start row must begin '{HumanStartKeyword}' or '{RefStartKeyword}', got '{fields[0]}'");
            }
        }
    }

    /// <summary>The parsed region layer: the region table plus a dense row-major region id per tile (FreeCol saved-map regions).</summary>
    private sealed record RegionLayer(IReadOnlyList<Region> Regions, int[] Ids);

    private static RegionLayer ReadRegions(LineCursor lines, int width, int height)
    {
        var regions = new List<Region>();
        var ids = new int[width * height];
        Array.Fill(ids, GameMap.NoRegion);

        // Two row kinds: 'region ID TYPE [SCORE [KEY [PARENT]]]' declares a table entry (ids dense from 0, in order);
        // 'X Y REGION_ID' assigns a tile to a declared region. Declarations may interleave with assignments, but every
        // assignment must reference an already-declared id and every in-bounds tile must end up assigned.
        foreach (string[] fields in ReadSectionRows(lines, min: 3, max: 6, "region"))
        {
            if (fields[0].Equals(RegionDeclarationKeyword, StringComparison.OrdinalIgnoreCase))
            {
                regions.Add(ParseRegionDeclaration(lines, fields, regions.Count));
            }
            else
            {
                AssignTileRegion(lines, fields, width, height, regions.Count, ids);
            }
        }

        if (regions.Count == 0)
        {
            throw lines.Error($"a '{RegionsSection}' section must declare at least one '{RegionDeclarationKeyword}'");
        }
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] == GameMap.NoRegion)
            {
                throw lines.Error($"tile ({i % width},{i / width}) was left with no region (every tile must be assigned)");
            }
        }
        return new RegionLayer(regions, ids);
    }

    private static Region ParseRegionDeclaration(LineCursor lines, string[] fields, int expectedId)
    {
        // region ID TYPE [SCORE [KEY [PARENT]]] — ID must be the next dense id (0,1,2,…) so Region.Id indexes the table.
        if (!TryNonNegative(fields[1], out int id) || id != expectedId)
        {
            throw lines.Error($"region ids must be declared densely from 0 in order; expected {expectedId}, got '{fields[1]}'");
        }
        if (!Enum.TryParse(fields[2], ignoreCase: true, out RegionType type))
        {
            throw lines.Error($"unknown region type '{fields[2]}' (one of {string.Join(", ", Enum.GetNames<RegionType>())})");
        }
        int score = 0;
        if (fields.Length >= 4 && (!TryNonNegative(fields[3], out score)))
        {
            throw lines.Error($"region score must be a non-negative integer, got '{fields[3]}'");
        }
        string? key = fields.Length >= 5 && fields[4] != "-" ? fields[4] : null;
        int? parent = null;
        if (fields.Length == 6)
        {
            if (!TryNonNegative(fields[5], out int parentId) || parentId >= expectedId)
            {
                throw lines.Error($"region parent must be an already-declared id (< {expectedId}), got '{fields[5]}'");
            }
            parent = parentId;
        }
        return new Region(id, type, score, key, parent);
    }

    private static void AssignTileRegion(
        LineCursor lines, string[] fields, int width, int height, int regionCount, int[] ids)
    {
        // X Y REGION_ID — a tile-assignment row (exactly three fields, none the 'region' keyword).
        if (fields.Length != 3)
        {
            throw lines.Error($"a region tile-assignment row needs 3 fields (X Y REGION_ID), got {fields.Length}");
        }
        Position p = ParsePosition(lines, fields, width, height);
        if (!TryNonNegative(fields[2], out int regionId) || regionId >= regionCount)
        {
            throw lines.Error($"region id '{fields[2]}' is not a declared region (declare it with '{RegionDeclarationKeyword}' first)");
        }
        ids[p.Y * width + p.X] = regionId;
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
