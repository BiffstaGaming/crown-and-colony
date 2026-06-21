using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// National-advantage colony-site ranking (86d3drn5d, FreeCol <c>Player.getColonyValue</c> tilted by
/// <c>AIPlayer.getAIAdvantage</c>): a foreign power weights candidate colony sites by its nation's advantage — a
/// <b>trade</b> power (Dutch) values goods by their European <b>sale price</b> and prizes coastal sites, while a
/// <b>production</b> power (Spanish, <c>conquest</c>) values <b>raw yield volume</b>. So the two advantages rank the
/// same pair of sites in OPPOSITE order. A power with no nation (or a neutral advantage) scores the un-tilted
/// price-weighted base, so the human — who plans no sites here — and the classic default game are unaffected
/// (stream 0 byte-identical, ADR-009).
/// </summary>
public class AiColonySitePlanningTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Dutch = "model.nation.dutch";    // model.nationType.trade
    private const string Spanish = "model.nation.spanish"; // model.nationType.conquest
    private const string French = "model.nation.french";   // model.nationType.cooperation (neutral for site scoring)

    /// <summary>Builds an empty (no colony) game over a custom terrain grid — the map a site is scored against.</summary>
    private static Game MapFrom(int width, int height, string[] terrain)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = width,
            MapHeight = height,
            Terrain = terrain,
            Units = [],
            Explored = [],
            Colonies = [],
        };
        return save.Restore(Classic);
    }

    /// <summary>A foreign colonial power with the given nation id (null = no nation / no advantage), id 1, its own market.</summary>
    private static Player Power(string? nationId) =>
        new(playerId: 1, nationId: nationId, isHuman: false, PlayerType.Colonial, new Market(Classic));

    /// <summary>
    /// A 5×3 map with two distinct candidate sites whose neighbourhoods do not overlap (a plains column at x=2 splits them):
    /// <list type="bullet">
    /// <item><b>Coastal high-PRICE site (1,1)</b> — ringed by mountains (ore @ price 4 + silver @ price 16: high value
    /// per unit, low volume) with an ocean neighbour, so it is a coastal, high-value trade neighbourhood.</item>
    /// <item><b>Inland high-VOLUME site (3,1)</b> — ringed by savannah (grain + sugar: high raw output, low price per
    /// unit), wholly inland.</item>
    /// </list>
    /// A trade power (price-weighted) should prefer the coastal mountains site; a production power (volume-weighted)
    /// should prefer the savannah site — the same two tiles ranked in opposite order.
    /// </summary>
    private static Game TwoSiteMap()
    {
        string[] terrain =
        [
            "model.tile.ocean",     "model.tile.mountains", "model.tile.plains", "model.tile.savannah", "model.tile.savannah",
            "model.tile.mountains", "model.tile.mountains", "model.tile.plains", "model.tile.savannah", "model.tile.savannah",
            "model.tile.ocean",     "model.tile.mountains", "model.tile.plains", "model.tile.savannah", "model.tile.savannah",
        ];
        return MapFrom(5, 3, terrain);
    }

    private static readonly Position CoastalPriceSite = new(1, 1);
    private static readonly Position InlandVolumeSite = new(3, 1);

    [Fact]
    public void NoNationPower_ScoresTheUntiltedBase_SameAsANeutralAdvantage()
    {
        Game game = TwoSiteMap();

        double noNation = game.ScoreColonySite(Power(null), CoastalPriceSite);
        double neutral = game.ScoreColonySite(Power(French), CoastalPriceSite); // cooperation: not a site advantage

        Assert.True(noNation > 0, "a goods-rich site should score above zero");
        Assert.Equal(noNation, neutral, precision: 6); // both apply multiplier 1.0 — the price-weighted base, un-tilted
    }

    [Fact]
    public void TradePower_ScoresACoastalSite_AboveANoNationPower()
    {
        Game game = TwoSiteMap();

        double baseScore = game.ScoreColonySite(Power(null), CoastalPriceSite);
        double tradeScore = game.ScoreColonySite(Power(Dutch), CoastalPriceSite);

        // The trade tilt (×1.2 on tradeable value, ×1.2 again for the coast) lifts the coastal site above the base.
        Assert.True(tradeScore > baseScore,
            $"trade power should value a coastal high-value site above the base ({tradeScore} vs {baseScore})");
    }

    [Fact]
    public void TwoDifferentAdvantages_RankTheSameTwoSitesInOppositeOrder()
    {
        Game game = TwoSiteMap();

        double tradePrice = game.ScoreColonySite(Power(Dutch), CoastalPriceSite);
        double tradeVolume = game.ScoreColonySite(Power(Dutch), InlandVolumeSite);
        double conquestPrice = game.ScoreColonySite(Power(Spanish), CoastalPriceSite);
        double conquestVolume = game.ScoreColonySite(Power(Spanish), InlandVolumeSite);

        // The trade power (price-weighted) prefers the coastal high-value site; the production power (volume-weighted)
        // prefers the high-output savannah site — the SAME pair of tiles ranked in OPPOSITE order by the two advantages.
        Assert.True(tradePrice > tradeVolume,
            $"trade power should prefer the coastal high-value site ({tradePrice} vs {tradeVolume})");
        Assert.True(conquestVolume > conquestPrice,
            $"production power should prefer the high-output site ({conquestVolume} vs {conquestPrice})");
    }

    [Fact]
    public void ProductionPower_GivesAHighYieldSite_TheExtraTiltOverAThinOne()
    {
        // A high-output savannah neighbourhood (best single yield ≥ the good-production band) vs a thin desert one
        // (yields below it). The production power's volume base + the ×1.2 good-production tilt rank the rich site above.
        string[] terrain =
        [
            "model.tile.savannah", "model.tile.savannah", "model.tile.plains", "model.tile.desert", "model.tile.desert",
            "model.tile.savannah", "model.tile.savannah", "model.tile.plains", "model.tile.desert", "model.tile.desert",
            "model.tile.savannah", "model.tile.savannah", "model.tile.plains", "model.tile.desert", "model.tile.desert",
        ];
        Game game = MapFrom(5, 3, terrain);
        Player conquest = Power(Spanish);

        double rich = game.ScoreColonySite(conquest, new Position(1, 1));  // ringed by savannah (high yield)
        double thin = game.ScoreColonySite(conquest, new Position(3, 1));  // ringed by desert (low yield)

        Assert.True(rich > thin, $"production power should value the high-yield site above the thin one ({rich} vs {thin})");
    }
}
