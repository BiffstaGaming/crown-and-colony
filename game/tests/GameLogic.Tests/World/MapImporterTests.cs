using System.IO;
using System.Linq;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// The faithful map importer (<see cref="MapImporter"/>): parses a map definition (terrain grid + optional
/// resource/bonus, improvement, lost-city-rumour and native-settlement overlays) into a <see cref="GameMap"/> plus its
/// initial settlements — mirroring FreeCol's saved-map import (<c>FreeColMapLoader</c>). A terrain-only definition (the
/// shipped <c>america.txt</c>) imports with empty overlays, so the default America game stays byte-identical.
/// </summary>
public class MapImporterTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static MapImportResult Import(string definition) =>
        MapImporter.Import(new StringReader(definition), Classic, "test-map");

    // A small definition exercising all four overlay sections (also embedded as example-overlays.txt).
    private const string OverlaidMap = """
        4 3
        ocean plains hills ocean
        ocean plains prairie ocean
        ocean ocean ocean ocean
        [resources]
        2 0 minerals 250
        1 1 grain
        [improvements]
        1 0 river 2
        2 1 road
        [rumours]
        1 1
        [settlements]
        1 0 apache camp capital 5 expertOreMiner
        2 1 apache village regular 3
        """;

    [Fact]
    public void ImportsTerrainGrid_RowMajor()
    {
        GameMap map = Import(OverlaidMap).Map;

        Assert.Equal(4, map.Width);
        Assert.Equal(3, map.Height);
        Assert.Equal("model.tile.ocean", map.TerrainAt(new Position(0, 0)).Id);
        Assert.Equal("model.tile.plains", map.TerrainAt(new Position(1, 0)).Id);
        Assert.Equal("model.tile.hills", map.TerrainAt(new Position(2, 0)).Id);
        Assert.Equal("model.tile.prairie", map.TerrainAt(new Position(2, 1)).Id);
    }

    [Fact]
    public void ImportsBonusResource_WithAndWithoutAnExplicitQuantity()
    {
        GameMap map = Import(OverlaidMap).Map;

        // A finite minerals deposit carries the explicit remaining quantity from the file.
        Assert.Equal("model.resource.minerals", map.ResourceAt(new Position(2, 0)));
        Assert.Equal(250, map.ResourceQuantityAt(new Position(2, 0)));

        // A bonus declared without a quantity column is placed with no persisted quantity (limitless).
        Assert.Equal("model.resource.grain", map.ResourceAt(new Position(1, 1)));
        Assert.Null(map.ResourceQuantityAt(new Position(1, 1)));

        Assert.Equal(2, map.Resources.Count);
    }

    [Fact]
    public void ImportsTileImprovements_WithOptionalMagnitude()
    {
        GameMap map = Import(OverlaidMap).Map;

        // A large river (magnitude 2 from the file) and a pioneer road land as tile improvements.
        TileImprovementType? river = map.RiverAt(new Position(1, 0));
        Assert.NotNull(river);
        Assert.Equal(2, river!.Magnitude);
        Assert.True(map.HasRiver(new Position(1, 0)));

        Assert.True(map.HasImprovement(new Position(2, 1), TileImprovementType.RoadId));
    }

    [Fact]
    public void ImportsLostCityRumour()
    {
        GameMap map = Import(OverlaidMap).Map;

        Assert.True(map.HasRumour(new Position(1, 1)));
        Assert.Single(map.Rumours);
    }

    [Fact]
    public void ImportsNativeSettlements_WithIdsSizeCapitalFlagAndSkill()
    {
        var settlements = Import(OverlaidMap).Settlements;

        Assert.Equal(2, settlements.Count);

        var capital = settlements[0];
        Assert.Equal(1, capital.Id);
        Assert.Equal(new Position(1, 0), capital.Position);
        Assert.Equal("model.nationType.apache", capital.NationTypeId);
        Assert.Equal("model.settlement.camp", capital.SettlementTypeId);
        Assert.True(capital.IsCapital);
        Assert.Equal(5, capital.Size);
        Assert.Equal("model.unit.expertOreMiner", capital.LearnableSkill);

        var village = settlements[1];
        Assert.Equal(2, village.Id);
        Assert.False(village.IsCapital);
        Assert.Equal(3, village.Size);
        Assert.Null(village.LearnableSkill); // no skill column → teaches nothing
    }

    // A definition exercising the two new optional sections: fixed start tiles ([starts]) and a per-tile region layer
    // ([regions]) declaring two regions and assigning every tile to one of them.
    private const string StartsAndRegionsMap = """
        2 2
        plains plains
        ocean ocean
        [starts]
        human 0 0
        ref 1 1
        [regions]
        region 0 Land 500
        region 1 Ocean 0 model.region.atlantic
        0 0 0
        1 0 0
        0 1 1
        1 1 1
        """;

    [Fact]
    public void ImportsStarts_HumanAndRefEntryTiles()
    {
        MapImportResult result = Import(StartsAndRegionsMap);

        Assert.Equal(new Position(0, 0), result.HumanStart);
        Assert.Equal(new Position(1, 1), result.RefEntry);
    }

    [Fact]
    public void ImportsRegions_PerTileIdsAndRegionTable()
    {
        GameMap map = Import(StartsAndRegionsMap).Map;

        // The two declared regions populate the table (id == index), with their type/score/key.
        Assert.Equal(2, map.Regions.Count);
        Assert.Equal(RegionType.Land, map.Regions[0].Type);
        Assert.Equal(500, map.Regions[0].ScoreValue);
        Assert.Equal(RegionType.Ocean, map.Regions[1].Type);
        Assert.Equal("model.region.atlantic", map.Regions[1].Key);

        // Every tile carries the imported id (not a re-derived one): the two plains tiles → Land, the two ocean → Ocean.
        Assert.Equal(0, map.RegionIdAt(new Position(0, 0)));
        Assert.Equal(0, map.RegionIdAt(new Position(1, 0)));
        Assert.Equal(1, map.RegionIdAt(new Position(0, 1)));
        Assert.Equal(1, map.RegionIdAt(new Position(1, 1)));
    }

    [Fact]
    public void StartsSection_EitherEntryMayBeOmitted()
    {
        // Only a human start declared → RefEntry stays null (the caller falls back to the nearest water tile).
        MapImportResult result = Import("""
            2 2
            plains plains
            ocean ocean
            [starts]
            human 1 0
            """);

        Assert.Equal(new Position(1, 0), result.HumanStart);
        Assert.Null(result.RefEntry);
    }

    [Fact]
    public void DefinitionWithoutStartsOrRegions_LeavesThemUnset_AndNoRegionLayer()
    {
        // No [starts]/[regions] → no fixed starts and no imported region layer (the generator re-derives regions). This
        // is the america.txt shape: the new sections must not perturb a definition that omits them.
        MapImportResult result = Import("""
            2 2
            plains plains
            ocean ocean
            [rumours]
            0 0
            """);

        Assert.Null(result.HumanStart);
        Assert.Null(result.RefEntry);
        Assert.Empty(result.Map.Regions); // no region layer imported — left for the generator
    }

    [Theory]
    [InlineData("2 2\nplains plains\nocean ocean\n[starts]\nhuman 9 9", "off the")] // off-map start tile
    [InlineData("2 2\nplains plains\nocean ocean\n[starts]\nelf 0 0", "must begin")] // bad start keyword
    [InlineData("2 2\nplains plains\nocean ocean\n[starts]\nhuman 0 0\nhuman 1 1", "more than once")] // duplicate human start
    [InlineData("2 2\nplains plains\nocean ocean\n[regions]\nregion 0 Bogus\n0 0 0\n1 0 0\n0 1 0\n1 1 0", "unknown region type")]
    [InlineData("2 2\nplains plains\nocean ocean\n[regions]\nregion 1 Land\n0 0 1", "densely from 0")] // first id must be 0
    [InlineData("2 2\nplains plains\nocean ocean\n[regions]\nregion 0 Land\n0 0 5", "not a declared region")] // unknown tile region id
    [InlineData("2 2\nplains plains\nocean ocean\n[regions]\nregion 0 Land\n0 0 0", "no region")] // a tile left unassigned
    public void RejectsMalformedStartsAndRegions(string definition, string expectedMessageFragment)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Import(definition.Replace("\n", "\r\n")));
        Assert.Contains(expectedMessageFragment, ex.Message);
    }

    [Fact]
    public void TerrainOnlyDefinition_ImportsWithEmptyOverlays_AndNoSettlements()
    {
        // No overlay sections at all — exactly the shape of america.txt. Every overlay layer must be empty and no
        // settlement placed, so a terrain-only import is byte-identical to the historical terrain-only loader.
        MapImportResult result = Import("""
            3 2
            plains plains plains
            ocean ocean ocean
            """);

        Assert.Equal(3, result.Map.Width);
        Assert.Empty(result.Map.Resources);
        Assert.Empty(result.Map.ResourceQuantities);
        Assert.Empty(result.Map.AllImprovements());
        Assert.Empty(result.Map.Rumours);
        Assert.Empty(result.Settlements);
    }

    [Fact]
    public void IgnoresCommentsAndBlankLines()
    {
        MapImportResult result = Import("""
            # a comment before the header
            2 2

            plains plains   # inline comment after a terrain row
            ocean ocean
            # ---
            [rumours]
            0 0
            """);

        Assert.Equal(2, result.Map.Width);
        Assert.True(result.Map.HasRumour(new Position(0, 0)));
    }

    [Fact]
    public void SectionsMayAppearInAnyOrder()
    {
        MapImportResult result = Import("""
            2 2
            plains plains
            ocean ocean
            [settlements]
            0 0 apache camp regular 2
            [rumours]
            1 0
            [resources]
            1 0 grain
            """);

        Assert.Single(result.Settlements);
        Assert.True(result.Map.HasRumour(new Position(1, 0)));
        Assert.Equal("model.resource.grain", result.Map.ResourceAt(new Position(1, 0)));
    }

    [Theory]
    [InlineData("not a header", "header")]
    [InlineData("2 2\nplains plains", "terrain row")] // truncated grid
    [InlineData("2 2\nplains nope\nocean ocean", "unknown terrain")]
    [InlineData("2 2\nplains plains\nocean ocean\n[resources]\n9 9 grain", "off the")] // off-map coordinate
    [InlineData("2 2\nplains plains\nocean ocean\n[resources]\n0 0 unobtainium", "unknown resource")]
    [InlineData("2 2\nplains plains\nocean ocean\n[bogus]\n0 0", "overlay section header")]
    [InlineData("2 2\nplains plains\nocean ocean\n[settlements]\n0 0 apache camp maybe 2", "capital")]
    public void RejectsMalformedDefinitions(string definition, string expectedMessageFragment)
    {
        var ex = Assert.Throws<InvalidDataException>(() => Import(definition.Replace("\n", "\r\n")));
        Assert.Contains(expectedMessageFragment, ex.Message);
    }

    [Fact]
    public void EmbeddedExampleOverlayMap_ImportsAllOverlays()
    {
        // The embedded example-overlays.txt exercises the importer end-to-end through the real embedded-resource path
        // (the same one america.txt uses). Assert one of each overlay lands.
        MapImportResult result = FixedMap.ImportExampleOverlays(Classic);

        Assert.Equal(6, result.Map.Width);
        Assert.Equal(5, result.Map.Height);
        Assert.Equal(250, result.Map.ResourceQuantityAt(new Position(4, 0)));       // a finite minerals deposit
        Assert.True(result.Map.HasRiver(new Position(2, 1)));                        // a river improvement
        Assert.True(result.Map.HasRumour(new Position(3, 2)));                       // a lost-city rumour
        Assert.Equal(2, result.Settlements.Count);                                   // two native settlements
        Assert.Contains(result.Settlements, s => s.IsCapital && s.LearnableSkill == "model.unit.expertOreMiner");
        Assert.Equal(new Position(1, 1), result.HumanStart);                          // a fixed human landing tile
        Assert.Equal(new Position(0, 2), result.RefEntry);                            // a fixed REF entry tile
    }

    [Fact]
    public void America_ImportsAsTerrainOnly_NoOverlaysOrSettlements()
    {
        // The shipped america.txt is terrain-only; importing it must produce no overlays and no settlements, so the
        // America new game remains byte-identical (its rivers/resources/natives are laid by the game-start generators).
        MapImportResult result = FixedMap.ImportAmerica(Classic);

        Assert.Equal(80, result.Map.Width);
        Assert.Equal(90, result.Map.Height);
        Assert.Empty(result.Map.Resources);
        Assert.Empty(result.Map.AllImprovements());
        Assert.Empty(result.Map.Rumours);
        Assert.Empty(result.Settlements);
    }

    // ── Colonization .MP import (86d3fpxjc, FreeCol ColonizationMapLoader port) ─────────────────────────────────────

    /// <summary>
    /// A synthesized 4×3 .MP fixture (header {4,0,3,0,4,0} + 12 terrain bytes) covering every decode branch: base
    /// terrain, both forest blocks, ocean/high-seas/arctic, hills/mountains overlays, minor/major rivers on a base
    /// terrain, and a river on a hills-overlay tile. Built in test code — never a real original-game file (licensing).
    /// </summary>
    private static byte[] ColonizationFixture() =>
    [
        4, 0, 3, 0, 4, 0,             // header: width 4 (UInt16 LE @0), height 3 (UInt16 LE @2), fixed tail 4,0
        2,                            // (0,0) plains
        10,                           // (1,0) mixedForest (first forest block)
        25,                           // (2,0) ocean
        26,                           // (3,0) highSeas
        (1 << 5) | 31,                // (0,1) hills (terrain 31 = no base type, overlay 1)
        (5 << 5) | 31,                // (1,1) mountains (overlay 5)
        (2 << 5) | 2,                 // (2,1) plains + minor river (overlay 2 → magnitude 1)
        (6 << 5) | 2,                 // (3,1) plains + major river (overlay 6 → magnitude 2)
        (3 << 5) | 31,                // (0,2) hills + minor river (overlay 3 → hills AND magnitude-1 river)
        0,                            // (1,2) tundra
        24,                           // (2,2) arctic
        16,                           // (3,2) borealForest (duplicate forest block, code 16 ≡ code 8)
    ];

    private static MapImportResult ImportMp(byte[] bytes) =>
        MapImporter.ImportColonization(new MemoryStream(bytes), Classic, "test.mp");

    [Fact]
    public void Colonization_DecodesTerrain_OverlaysAndBothForestBlocks()
    {
        GameMap map = ImportMp(ColonizationFixture()).Map;

        Assert.Equal(4, map.Width);
        Assert.Equal(3, map.Height);
        Assert.Equal("model.tile.plains", map.TerrainAt(new Position(0, 0)).Id);
        Assert.Equal("model.tile.mixedForest", map.TerrainAt(new Position(1, 0)).Id);
        Assert.Equal("model.tile.ocean", map.TerrainAt(new Position(2, 0)).Id);
        Assert.Equal("model.tile.highSeas", map.TerrainAt(new Position(3, 0)).Id);
        Assert.Equal("model.tile.hills", map.TerrainAt(new Position(0, 1)).Id);
        Assert.Equal("model.tile.mountains", map.TerrainAt(new Position(1, 1)).Id);
        Assert.Equal("model.tile.tundra", map.TerrainAt(new Position(1, 2)).Id);
        Assert.Equal("model.tile.arctic", map.TerrainAt(new Position(2, 2)).Id);
        // Codes 8-15 and 16-23 are the SAME eight forests (FreeCol's duplicated tiletypes block).
        Assert.Equal("model.tile.borealForest", map.TerrainAt(new Position(3, 2)).Id);
    }

    [Fact]
    public void Colonization_LaysRivers_MinorAndMajorMagnitude_IncludingOnHills()
    {
        GameMap map = ImportMp(ColonizationFixture()).Map;

        // Overlay 2 → minor river (magnitude 1); overlay 6 → major river (magnitude 2); base terrain kept (plains,
        // because a terrain code < 27 wins over the overlay bits, exactly as FreeCol decodes).
        Assert.Equal("model.tile.plains", map.TerrainAt(new Position(2, 1)).Id);
        Assert.Equal(1, map.RiverAt(new Position(2, 1))!.Magnitude);
        Assert.Equal("model.tile.plains", map.TerrainAt(new Position(3, 1)).Id);
        Assert.Equal(2, map.RiverAt(new Position(3, 1))!.Magnitude);
        // Overlay 3 = hills + minor river on the same tile (extremely rare in real maps, FreeCol decodes both).
        Assert.Equal("model.tile.hills", map.TerrainAt(new Position(0, 2)).Id);
        Assert.Equal(1, map.RiverAt(new Position(0, 2))!.Magnitude);
        // No other tile gained an improvement.
        Assert.Equal(3, map.AllImprovements().Count());
    }

    [Fact]
    public void Colonization_CarriesNoOverlaysTheFormatCannotExpress()
    {
        // FreeCol's loader reads terrain + river magnitude ONLY — no bonuses/rumours/settlements/starts. Our port is
        // exactly that subset: everything else must come out empty/null so the game-start generators lay it.
        MapImportResult result = ImportMp(ColonizationFixture());

        Assert.Empty(result.Map.Resources);
        Assert.Empty(result.Map.Rumours);
        Assert.Empty(result.Map.Regions);
        Assert.Empty(result.Settlements);
        Assert.Null(result.HumanStart);
        Assert.Null(result.RefEntry);
    }

    [Fact]
    public void Colonization_EveryTiletypeCode_ResolvesAgainstTheClassicRuleset()
    {
        // A 29×1 map whose tiles are the 27 base terrain codes 0..26 plus a hills (overlay 1) and a mountains
        // (overlay 5) tile — every id the decoder can produce must exist in the classic ruleset (the 29-id sanity
        // check: 27 tiletypes entries + hills + mountains).
        byte[] bytes = new byte[6 + 29];
        bytes[0] = 29; bytes[2] = 1; bytes[4] = 4; // header {29,0,1,0,4,0}
        for (int code = 0; code < 27; code++)
        {
            bytes[6 + code] = (byte)code;
        }
        bytes[6 + 27] = (1 << 5) | 31; // hills
        bytes[6 + 28] = (5 << 5) | 31; // mountains

        GameMap map = ImportMp(bytes).Map; // any unknown id would have thrown
        Assert.Equal("model.tile.tundra", map.TerrainAt(new Position(0, 0)).Id);
        Assert.Equal("model.tile.highSeas", map.TerrainAt(new Position(26, 0)).Id);
        Assert.Equal("model.tile.hills", map.TerrainAt(new Position(27, 0)).Id);
        Assert.Equal("model.tile.mountains", map.TerrainAt(new Position(28, 0)).Id);
        // The duplicate forest block decodes to the identical id (code 8 ≡ code 16, … 15 ≡ 23).
        for (int i = 0; i < 8; i++)
        {
            Assert.Equal(map.TerrainAt(new Position(8 + i, 0)).Id, map.TerrainAt(new Position(16 + i, 0)).Id);
        }
    }

    [Fact]
    public void Colonization_IgnoresTrailingLayers()
    {
        // A real .MP carries three same-size layers; FreeCol reads only the first, and so do we — trailing bytes
        // (layers 2-3, whatever their content) must not affect the import.
        byte[] withLayers = [.. ColonizationFixture(), .. new byte[24]]; // + two more 12-byte layers of zeros
        GameMap map = MapImporter.ImportColonization(new MemoryStream(withLayers), Classic, "layered.mp").Map;

        Assert.Equal(4, map.Width);
        Assert.Equal("model.tile.plains", map.TerrainAt(new Position(0, 0)).Id);
    }

    public static TheoryData<byte[], string> MalformedColonizationMaps() => new()
    {
        // 5-byte header (shorter than the 6-byte fixed header).
        { new byte[] { 4, 0, 3, 0, 4 }, "header" },
        // Zero width in the header.
        { new byte[] { 0, 0, 3, 0, 4, 0, 2, 2, 2 }, "at least 1" },
        // Truncated terrain layer: header declares 4×3 = 12 tiles, only 5 bytes follow.
        { new byte[] { 4, 0, 3, 0, 4, 0, 2, 2, 2, 2, 2 }, "truncated" },
        // Undefined code: terrain 31 (no base type) with overlay 0 (not hills/mountains) — FreeCol's latent
        // reuse-the-previous-tile bug; we throw cleanly instead (documented deviation).
        { new byte[] { 1, 0, 1, 0, 4, 0, 31 }, "undefined" },
        // Undefined code: terrain 27 with overlay 4 ("nothing") — same branch.
        { new byte[] { 1, 0, 1, 0, 4, 0, (4 << 5) | 27 }, "undefined" },
    };

    [Theory]
    [MemberData(nameof(MalformedColonizationMaps))]
    public void Colonization_RejectsMalformedMaps(byte[] bytes, string expectedMessageFragment)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ImportMp(bytes));
        Assert.Contains(expectedMessageFragment, ex.Message);
        Assert.Contains("test.mp", ex.Message); // errors name the source, like the text importer's
    }

    [Fact]
    public void ImportFile_DispatchesMpToTheColonizationLoader_CaseInsensitively()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".MP");
        try
        {
            File.WriteAllBytes(path, ColonizationFixture());
            MapImportResult result = MapImporter.ImportFile(path, Classic);

            Assert.Equal(4, result.Map.Width);
            Assert.Equal("model.tile.mixedForest", result.Map.TerrainAt(new Position(1, 0)).Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportFile_ReadsAnythingElseAsATextDefinition()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        try
        {
            File.WriteAllText(path, "2 2\nplains plains\nocean ocean\n");
            MapImportResult result = MapImporter.ImportFile(path, Classic);

            Assert.Equal(2, result.Map.Width);
            Assert.Equal("model.tile.plains", result.Map.TerrainAt(new Position(0, 0)).Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportFile_ParseErrors_NameTheFile()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mp");
        try
        {
            File.WriteAllBytes(path, [4, 0]); // far too short
            var ex = Assert.Throws<InvalidDataException>(() => MapImporter.ImportFile(path, Classic));
            Assert.Contains(Path.GetFileName(path), ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
