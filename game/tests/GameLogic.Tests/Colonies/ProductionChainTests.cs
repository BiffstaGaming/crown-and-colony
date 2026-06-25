using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// L2 cross-checks: the classic refining chains run end-to-end through the
/// building-production engine (raw goods in the warehouse + a staffed artisan
/// house → refined goods).
/// </summary>
public class ProductionChainTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Theory]
    [InlineData("model.building.distillerHouse", "model.goods.sugar", "model.goods.rum")]
    [InlineData("model.building.tobacconistHouse", "model.goods.tobacco", "model.goods.cigars")]
    [InlineData("model.building.weaverHouse", "model.goods.cotton", "model.goods.cloth")]
    [InlineData("model.building.furTraderHouse", "model.goods.furs", "model.goods.coats")]
    [InlineData("model.building.blacksmithHouse", "model.goods.ore", "model.goods.tools")]
    public void ArtisanHouse_RefinesRawIntoFinishedGoods(string building, string raw, string refined)
    {
        var game = Game.New(Classic, seed: 424242);
        game.Units[0].RoleId = RoleType.DefaultRoleId; // found a clean colony — the starting pioneer's banked tools would skew the refining baseline
        game.Units[0].RoleCount = 0;
        game.FoundColony(game.Units[0]);
        Colony colony = game.Colonies[0];
        foreach (var tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile);
        }

        Assert.True(colony.HasBuilding(building), $"{building} should be a free base building");
        game.AssignBuildingWork(colony, building);
        colony.AddGoods(raw, 10);

        // The colony square may itself produce the raw goods unattended.
        int centreYield = game.Map.TerrainAt(colony.Position).Productions
            .Where(p => p.Unattended)
            .SelectMany(p => p.Outputs)
            .Where(o => o.GoodsId == raw)
            .Sum(o => o.Amount);

        game.EndTurn();

        // Classic artisan houses convert 3 raw → 3 refined per worker.
        Assert.Equal(3, colony.StoreOf(refined));
        Assert.Equal(10 - 3 + centreYield, colony.StoreOf(raw));

        // And the spec's made-from data agrees with the chain we just ran.
        Assert.Equal(raw, Classic.Goods(refined).MadeFrom);
    }
}
