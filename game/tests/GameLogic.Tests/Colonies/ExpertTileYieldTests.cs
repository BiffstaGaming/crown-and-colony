using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Expert tile yields (<c>86d3b6nrz</c> slice 3): a colony worker's unit type now folds its index-30 production
/// modifier into the tile yield — expert farmer +2 grain, fisherman +3 fish, fur trapper ×2 furs — applied after the
/// bonus-resource (index 10) and before founding-father modifiers (index 40). Indentured/petty have no raw-tile
/// modifier (their penalty is building-only), so their tile yield equals a free colonist's.
/// </summary>
public class ExpertTileYieldTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Grain = "model.goods.grain";
    private const string Fish = "model.goods.fish";
    private const string Furs = "model.goods.furs";
    private const string Free = "model.unit.freeColonist";

    private static int Yield(Game game, string workerType, Position tile, string goods) =>
        game.TileYield(game.HumanPlayer, workerType, tile, goods);

    private static Position TileProducing(Game game, string goods) =>
        game.Map.AllPositions().First(p => game.TileYieldPotential(p, goods) > 0);

    [Fact]
    public void ExpertFarmer_AddsTwoGrain_OverAFreeColonist()
    {
        Game game = Game.New(Classic, Seed);
        Position tile = TileProducing(game, Grain);
        Assert.Equal(Yield(game, Free, tile, Grain) + 2, Yield(game, "model.unit.expertFarmer", tile, Grain));
    }

    [Fact]
    public void ExpertFisherman_AddsThreeFish()
    {
        Game game = Game.New(Classic, Seed);
        Position tile = TileProducing(game, Fish);
        Assert.Equal(Yield(game, Free, tile, Fish) + 3, Yield(game, "model.unit.expertFisherman", tile, Fish));
    }

    [Fact]
    public void ExpertFurTrapper_DoublesFurs()
    {
        Game game = Game.New(Classic, Seed);
        Position tile = TileProducing(game, Furs);
        int free = Yield(game, Free, tile, Furs);
        Assert.True(free > 0);
        Assert.Equal(free * 2, Yield(game, "model.unit.expertFurTrapper", tile, Furs)); // multiplicative ×2
    }

    [Theory]
    [InlineData("model.unit.indenturedServant")]
    [InlineData("model.unit.pettyCriminal")]
    public void LesserColonists_HaveNoRawTilePenalty(string workerType)
    {
        Game game = Game.New(Classic, Seed);
        Position grain = TileProducing(game, Grain);
        Position furs = TileProducing(game, Furs);
        Assert.Equal(Yield(game, Free, grain, Grain), Yield(game, workerType, grain, Grain));
        Assert.Equal(Yield(game, Free, furs, Furs), Yield(game, workerType, furs, Furs));
    }

    [Fact]
    public void AnExpertWorkingTheWrongGood_GetsNoBonus()
    {
        Game game = Game.New(Classic, Seed);
        Position grain = TileProducing(game, Grain);
        // An expert ore miner on a grain tile produces the plain free-colonist grain yield (it's no farmer).
        Assert.Equal(Yield(game, Free, grain, Grain), Yield(game, "model.unit.expertOreMiner", grain, Grain));
    }

    // ---- End-to-end: the colony turn folds the worker type into production ----

    [Fact]
    public void AnExpertFarmer_ProducesTwoMoreFoodPerTurn_ThanAFreeColonist()
    {
        int FoodGainedInOneTurn(string workerType)
        {
            Game game = Game.New(Classic, Seed);
            Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
            Position tile = colony.TileWorkers.Keys.First(); // the founding colonist's auto-assigned grain tile
            colony.SetWorker(tile, colony.TileWorkers[tile], workerType); // retype that worker
            colony.AddGoods("model.goods.food", 50 - colony.Food);        // below the 200 growth bar, above starvation
            int before = colony.Food;
            game.EndTurn();
            return colony.Food - before;
        }

        // Identical colony, identical seed — the only difference is the tile worker's type, so the delta is the +2.
        Assert.Equal(FoodGainedInOneTurn(Free) + 2, FoodGainedInOneTurn("model.unit.expertFarmer"));
    }
}
