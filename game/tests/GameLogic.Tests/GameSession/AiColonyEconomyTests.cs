using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// AI colony economy (<c>86d3c9vmr</c>, full-fidelity <c>ColonyPlan</c> port, built up in increments). Increment 2:
/// the <c>ColonyNetFood</c> production-query the worker planner uses to balance cash crops against starvation.
/// </summary>
public class AiColonyEconomyTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Grain = "model.goods.grain";

    /// <summary>A fresh game with a founded human colony whose tiles have all been freed (the founder left idle).</summary>
    private static (Game Game, Colony Colony) IdleColony()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (Position t in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, t);
        }
        return (game, colony);
    }

    [Fact]
    public void ColonyNetFood_IsCentrePlusFoodTiles_MinusConsumption()
    {
        (Game game, Colony colony) = IdleColony();
        int baseline = game.ColonyNetFood(game.HumanPlayer, colony); // centre food − population·2, no tiles worked

        // Put the idle colonist on the best grain tile; net food must rise by exactly that tile's (floored) yield.
        Position foodTile = colony.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && game.TileWorkOptions(n).Any(o => o.GoodsId == Grain));
        game.AssignWork(colony, foodTile, Grain);

        int tileFood = System.Math.Max(0, game.TileYield(foodTile, Grain) + colony.ProductionBonus);
        Assert.Equal(baseline + tileFood, game.ColonyNetFood(game.HumanPlayer, colony));
    }

    [Fact]
    public void ColonyNetFood_FallsAsThePopulationGrows()
    {
        (Game game, Colony colony) = IdleColony();
        int oneColonist = game.ColonyNetFood(game.HumanPlayer, colony);
        colony.Population += 1; // an extra mouth eats FoodPerColonist more
        Assert.Equal(oneColonist - Colony.FoodPerColonist, game.ColonyNetFood(game.HumanPlayer, colony));
    }

    [Fact]
    public void ColonyNetFood_IsAPureRead()
    {
        (Game game, Colony colony) = IdleColony();
        var before = game.RandomState;
        game.ColonyNetFood(game.HumanPlayer, colony);
        game.ColonyNetFood(game.HumanPlayer, colony);
        Assert.Equal(before, game.RandomState); // no RNG drawn
    }
}
