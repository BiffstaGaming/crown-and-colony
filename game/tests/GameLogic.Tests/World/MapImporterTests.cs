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

        Assert.Equal(40, result.Map.Width);
        Assert.Equal(180, result.Map.Height);
        Assert.Empty(result.Map.Resources);
        Assert.Empty(result.Map.AllImprovements());
        Assert.Empty(result.Map.Rumours);
        Assert.Empty(result.Settlements);
    }
}
