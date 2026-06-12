using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

public class RulesetTests
{
    // One parse shared by all tests in this class — the spec is immutable.
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void Classic_Defines23TerrainTypes()
    {
        // The classic FreeCol ruleset has exactly 23 tile types (8 base land,
        // 8 forests, hills, mountains, arctic, and 4 water types).
        Assert.Equal(23, Classic.TerrainTypes.Count);
    }

    [Fact]
    public void Plains_MatchesSpecificationValues()
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");

        Assert.Equal(3, plains.MoveCost);
        Assert.Equal(3, plains.WorkTurns);
        Assert.False(plains.IsForest);
        Assert.False(plains.IsWater);
        Assert.False(plains.IsElevation);
        Assert.True(plains.CanSettle);
        Assert.Equal("plains", plains.ShortName);

        // Unattended (colony-center) yield: grain 3 + cotton 2.
        ProductionEntry unattended = Assert.Single(plains.Productions, p => p.Unattended);
        Assert.Equal(
            [("model.goods.grain", 3), ("model.goods.cotton", 2)],
            unattended.Outputs.Select(o => (o.GoodsId, o.Amount)).ToArray());

        // Attended options include grain 5 (the plains farming yield).
        Assert.Contains(plains.Productions, p =>
            !p.Unattended && p.Outputs.Any(o => o is { GoodsId: "model.goods.grain", Amount: 5 }));
    }

    [Fact]
    public void Mountains_AreUnsettleableSlowElevation()
    {
        TerrainType mountains = Classic.Terrain("model.tile.mountains");

        Assert.Equal(9, mountains.MoveCost);
        Assert.True(mountains.IsElevation);
        Assert.False(mountains.CanSettle);
        Assert.False(mountains.IsWater);
    }

    [Theory]
    [InlineData("model.tile.ocean", true)]
    [InlineData("model.tile.highSeas", true)]
    [InlineData("model.tile.greatRiver", false)] // spec: rivers are not high-seas connected
    [InlineData("model.tile.lake", false)]
    public void WaterTypes_AreWater_WithExpectedConnectivity(string id, bool connected)
    {
        TerrainType water = Classic.Terrain(id);

        Assert.True(water.IsWater);
        Assert.False(water.CanSettle);
        Assert.Equal(connected, water.IsConnected);
    }

    [Fact]
    public void AllForestTypes_AreFlagged()
    {
        // 8 forest variants in the classic ruleset.
        Assert.Equal(8, Classic.TerrainTypes.Count(t => t.IsForest));
    }

    [Fact]
    public void EveryTerrainType_HasPositiveMovementAndWorkValues()
    {
        Assert.All(Classic.TerrainTypes, t =>
        {
            Assert.True(t.MoveCost > 0, $"{t.Id} has non-positive MoveCost");
            Assert.True(t.WorkTurns > 0, $"{t.Id} has non-positive WorkTurns");
        });
    }

    [Fact]
    public void UnknownTerrainId_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => Classic.Terrain("model.tile.atlantis"));
    }

    [Fact]
    public void MalformedSpecification_ThrowsFormatException()
    {
        static Stream Xml(string content)
        {
            var ms = new MemoryStream();
            using (var writer = new StreamWriter(ms, leaveOpen: true))
            {
                writer.Write(content);
            }
            ms.Position = 0;
            return ms;
        }

        // No tile-types section.
        Assert.Throws<RulesetFormatException>(() =>
            Ruleset.Load(Xml("<freecol-specification/>")));

        // Tile type without movement cost.
        Assert.Throws<RulesetFormatException>(() =>
            Ruleset.Load(Xml(
                "<freecol-specification><tile-types><tile-type id=\"x\" basic-work-turns=\"3\"/></tile-types></freecol-specification>")));

        // Duplicate ids.
        Assert.Throws<RulesetFormatException>(() =>
            Ruleset.Load(Xml(
                "<freecol-specification><tile-types>" +
                "<tile-type id=\"x\" basic-move-cost=\"3\" basic-work-turns=\"3\"/>" +
                "<tile-type id=\"x\" basic-move-cost=\"3\" basic-work-turns=\"3\"/>" +
                "</tile-types></freecol-specification>")));
    }
}
