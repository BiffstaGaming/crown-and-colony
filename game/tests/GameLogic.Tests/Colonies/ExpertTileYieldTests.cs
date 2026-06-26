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
    private const string Lumber = "model.goods.lumber";
    private const string Free = "model.unit.freeColonist";
    private const string IndianConvert = "model.unit.indianConvert";

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

    [Fact]
    public void ExpertLumberJack_DoublesLumber()
    {
        // The colony-screen bug report (86d3f674f): an expert lumberjack must get FreeCol's 2× lumber bonus.
        // Its spec modifier is index-30 multiplicative ×2 on model.goods.lumber (specification.xml expertLumberJack).
        Game game = Game.New(Classic, Seed);
        Position tile = TileProducing(game, Lumber);
        int free = Yield(game, Free, tile, Lumber);
        Assert.True(free > 0);
        Assert.Equal(free * 2, Yield(game, "model.unit.expertLumberJack", tile, Lumber)); // multiplicative ×2
    }

    [Fact]
    public void AnExpertLumberJack_ProducesDoubleLumberPerTurn_ViaTheWorkerTypeOverlay()
    {
        // End-to-end through a real colony turn: the production calc must read the worker-type overlay (WorkerTypeAt)
        // and double the lumber a free colonist would make on the same tile — not produce as a free colonist. This is
        // the "no bonus" half of the bug report, asserted against the live RunColonyTurn (not just the TileYield helper).
        int LumberGainedInOneTurn(string workerType)
        {
            Game game = Game.New(Classic, Seed);
            Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
            // Put a colonist on a lumber-producing neighbour tile (release the auto-assigned founder onto it).
            Position lumberTile = colony.Position.Neighbours()
                .First(n => game.Map.InBounds(n) && game.TileWorkOptions(n).Any(o => o.GoodsId == Lumber));
            // Free the founder first (a fresh colony is pop 1, fully assigned), then assign it to make lumber.
            Position founder = colony.TileWorkers.Keys.First();
            game.UnassignWork(colony, founder);
            game.AssignWork(colony, lumberTile, Lumber);
            colony.SetWorker(lumberTile, Lumber, workerType); // retype the lumber worker
            int before = colony.StoreOf(Lumber);
            game.EndTurn();
            return colony.StoreOf(Lumber) - before;
        }

        int free = LumberGainedInOneTurn(Free);
        int expert = LumberGainedInOneTurn("model.unit.expertLumberJack");
        Assert.True(free > 0);
        Assert.Equal(free * 2, expert); // the live colony turn doubles lumber for the expert (worker-type overlay folded)
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

    // ---- Indian Convert food/raw-goods bonus (86d3fpx3h) ----

    [Theory] // the convert's index-30 +1 on each raw good it can work (FreeCol indianConvert modifiers)
    [InlineData(Grain)]
    [InlineData(Fish)]
    [InlineData(Furs)]
    public void IndianConvert_AddsOneRawGood_OverAFreeColonist(string good)
    {
        Game game = Game.New(Classic, Seed);
        Position tile = TileProducing(game, good);
        Assert.Equal(Yield(game, Free, tile, good) + 1, Yield(game, IndianConvert, tile, good)); // additive +1
    }

    [Fact]
    public void IndianConvert_ProducesOneMoreFoodPerTurn_ThanAFreeColonist_ViaTheLiveColonyTurn()
    {
        // End-to-end through a real colony turn: a convert working a grain tile banks one more food than a free
        // colonist on the same tile (its +1 grain folds via the worker-type overlay, like an expert's bonus).
        int FoodGainedInOneTurn(string workerType)
        {
            Game game = Game.New(Classic, Seed);
            Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
            Position tile = colony.TileWorkers.Keys.First(); // the founding colonist's auto-assigned grain tile
            colony.SetWorker(tile, colony.TileWorkers[tile], workerType);
            colony.AddGoods("model.goods.food", 50 - colony.Food); // below the 200 growth bar, above starvation
            int before = colony.Food;
            game.EndTurn();
            return colony.Food - before;
        }

        Assert.Equal(FoodGainedInOneTurn(Free) + 1, FoodGainedInOneTurn(IndianConvert));
    }

    [Fact]
    public void AnExpertWorkingTheWrongGood_GetsNoBonus()
    {
        Game game = Game.New(Classic, Seed);
        Position grain = TileProducing(game, Grain);
        // An expert ore miner on a grain tile produces the plain free-colonist grain yield (it's no farmer).
        Assert.Equal(Yield(game, Free, grain, Grain), Yield(game, "model.unit.expertOreMiner", grain, Grain));
    }

    // ---- Slice 4: expert-scoped bonus-resource modifiers ----

    [Fact]
    public void ExpertFarmer_AlsoGetsTheExpertScopedResourceBonus()
    {
        Game game = Game.New(Classic, Seed);
        Position tile = game.Map.AllPositions().First(p => game.TileYieldPotential(p, Grain) > 0 && game.Map.ResourceAt(p) is null);
        int plain = Yield(game, Free, tile, Grain); // no resource yet

        game.Map.SetResource(tile, "model.resource.game"); // grain +2 unscoped + grain +2 scoped to the expert farmer

        Assert.Equal(plain + 2, Yield(game, Free, tile, Grain));                          // free: the unscoped +2 only
        Assert.Equal(plain + 2, Yield(game, "model.unit.expertOreMiner", tile, Grain));   // wrong expert: unscoped +2, no scoped/unit
        Assert.Equal(plain + 2 + 2 + 2, Yield(game, "model.unit.expertFarmer", tile, Grain)); // unscoped +2, scoped +2, unit +2
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
