using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// Map regions (<c>86d3c9w12</c>) — slice 1: the <see cref="Region"/>/<see cref="RegionType"/> data model and the
/// <see cref="GameMap"/> region-layer storage (no assignment generator yet). Mirrors the resource/rumour sparse-layer
/// convention: a map built without regions reports an empty table and <see cref="GameMap.NoRegion"/> per tile.
/// </summary>
public class RegionTests
{
    [Fact]
    public void Region_RecordEquality_ByValue()
    {
        var a = new Region(0, RegionType.Land, 1000, "model.region.arctic");
        var b = new Region(0, RegionType.Land, 1000, "model.region.arctic");
        Assert.Equal(a, b);
        Assert.NotEqual(a, a with { ScoreValue = 5 });
    }

    [Fact]
    public void Region_DefaultsKeyAndParent_ToNull()
    {
        var r = new Region(3, RegionType.Mountain, 12);
        Assert.Null(r.Key);
        Assert.Null(r.ParentId);
    }

    [Fact]
    public void Region_OceanQuadrant_LinksToParentOcean()
    {
        var north = new Region(2, RegionType.Ocean, 0, "model.region.northAtlantic", ParentId: 1);
        Assert.Equal(1, north.ParentId);
        Assert.Equal("model.region.northAtlantic", north.Key);
    }

    [Fact]
    public void RegionType_OrdinalsAreStable_ForSaveFormat()
    {
        // Serialized as the ordinal — renumbering would silently corrupt saves.
        Assert.Equal(0, (int)RegionType.Ocean);
        Assert.Equal(1, (int)RegionType.Land);
        Assert.Equal(2, (int)RegionType.Mountain);
        Assert.Equal(3, (int)RegionType.River);
    }
}

/// <summary>The <see cref="GameMap"/> region-layer storage seam added in slice 1.</summary>
public class GameMapRegionTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static GameMap TwoByTwo(IReadOnlyList<int>? regionIds = null, IReadOnlyList<Region>? regions = null)
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");
        TerrainType ocean = Classic.Terrain("model.tile.ocean");
        return new GameMap(2, 2, [plains, ocean, ocean, plains], regionIds: regionIds, regions: regions);
    }

    [Fact]
    public void NoRegionLayer_ReportsEmptyTable_AndNoRegionPerTile()
    {
        GameMap map = TwoByTwo();

        Assert.Empty(map.Regions);
        Assert.Equal(GameMap.NoRegion, map.RegionIdAt(new Position(0, 0)));
        Assert.Null(map.RegionOf(new Position(1, 1)));
    }

    [Fact]
    public void RegionLayer_RoundTripsByPosition()
    {
        Region[] table = [new(0, RegionType.Land, 500), new(1, RegionType.Ocean, 0, "model.region.atlantic")];
        // row-major: (0,0)=land 0, (1,0)=ocean 1, (0,1)=ocean 1, (1,1)=land 0
        GameMap map = TwoByTwo([0, 1, 1, 0], table);

        Assert.Equal(0, map.RegionIdAt(new Position(0, 0)));
        Assert.Equal(1, map.RegionIdAt(new Position(1, 0)));
        Assert.Equal(RegionType.Land, map.RegionOf(new Position(1, 1))!.Type);
        Assert.Equal("model.region.atlantic", map.RegionOf(new Position(0, 1))!.Key);
        Assert.Equal(2, map.Regions.Count);
    }

    [Fact]
    public void RegionQueries_OffMap_Throw()
    {
        GameMap map = TwoByTwo([0, 0, 0, 0], [new(0, RegionType.Land, 1)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => map.RegionIdAt(new Position(2, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.RegionOf(new Position(-1, 0)));
    }

    [Fact]
    public void Constructor_RejectsMismatchedRegionArray()
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");
        Assert.Throws<ArgumentException>(() =>
            new GameMap(2, 2, [plains, plains, plains, plains], regionIds: [0, 0]));
    }

    [Fact]
    public void SetRegions_InstallsLayer_OverAnEmptyMap()
    {
        GameMap map = TwoByTwo();
        Region[] table = [new(0, RegionType.Ocean, 0, "model.region.atlantic")];

        map.SetRegions([0, 0, 0, 0], table);

        Assert.Single(map.Regions);
        Assert.Equal(0, map.RegionIdAt(new Position(1, 1)));
        Assert.Equal("model.region.atlantic", map.RegionOf(new Position(0, 0))!.Key);
    }

    [Fact]
    public void SetRegions_RejectsMismatchedArray()
    {
        GameMap map = TwoByTwo();
        Assert.Throws<ArgumentException>(() => map.SetRegions([0, 0], [new(0, RegionType.Land, 1)]));
    }
}
