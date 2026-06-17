using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Horse breeding (<c>86d3c9nwr</c>, FreeCol <c>BuildingProductionCalculator</c> auto-production): a pasture/stables
/// grows the herd by <c>((herd−1)/divisor + 1) × factor</c> each turn — faster the larger the herd, faster again with
/// a stables (divisor 50 → 25) — gated at the breeding number (2), stopping at the warehouse cap, eating only surplus
/// food. Replaces the old flat 1-horse/turn behaviour.
/// </summary>
public class HorseBreedingTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Country = "model.building.country";
    private const string Stables = "model.building.stables";
    private const string Horses = "model.goods.horses";
    private const string Food = "model.goods.food";

    private static (Game Game, Colony Colony) FoundedColony()
    {
        var game = Game.New(Classic, seed: 424242);
        Colony colony = game.FoundColony(game.Units[0]);
        return (game, colony);
    }

    /// <summary>
    /// A colony grown to several food-working colonists, so its per-turn food <em>production</em> is high enough that
    /// breeding's surplus limit (half this-turn's food production) never throttles the formula's small foal counts —
    /// used by the rate tests, which assert the unthrottled formula. (Breeding eats only produced food, not stored.)
    /// </summary>
    private static (Game Game, Colony Colony) WellFedColony()
    {
        var game = Game.New(Classic, seed: 424242);
        Colony colony = game.FoundColony(game.Units[0]);
        for (int i = 0; i < 6; i++)
        {
            colony.AddGoods(Food, 200); // carryover triggers growth → a new colonist auto-assigned to a food tile
            game.EndTurn();
        }
        return (game, colony);
    }

    // ---- Ruleset parse ----

    [Fact]
    public void BreedingModifiers_ParseOffTheExtendsChain()
    {
        Assert.Equal(50, Classic.Building(Country).BreedingDivisor); // additive 50
        Assert.Equal(2, Classic.Building(Country).BreedingFactor);
        Assert.Equal(25, Classic.Building(Stables).BreedingDivisor); // inherits 50, ×0.5 → 25
        Assert.Equal(2, Classic.Building(Stables).BreedingFactor);   // inherited
        Assert.Equal(0, Classic.Building("model.building.townHall").BreedingDivisor); // not a breeder
    }

    // ---- The formula (country: divisor 50, factor 2) ----

    [Theory]
    [InlineData(2, 2)]    // ((2-1)/50 + 1) * 2  = 2
    [InlineData(50, 2)]   // ((50-1)/50 + 1) * 2 = (0+1)*2 = 2
    [InlineData(51, 4)]   // ((51-1)/50 + 1) * 2 = (1+1)*2 = 4 — the herd-size step
    public void Pasture_BreedsByTheHerdSizeFormula(int herd, int expectedFoals)
    {
        (Game game, Colony colony) = WellFedColony(); // ample food production → the formula, not food, binds
        colony.AddGoods(Horses, herd);

        game.EndTurn();

        Assert.Equal(herd + expectedFoals, colony.StoreOf(Horses));
    }

    [Fact]
    public void OneHorse_IsBelowTheBreedingNumber_AndBreedsNothing()
    {
        (Game game, Colony colony) = FoundedColony();
        colony.AddGoods(Food, 60);
        colony.AddGoods(Horses, 1);

        game.EndTurn();

        Assert.Equal(1, colony.StoreOf(Horses)); // < breeding number 2 → no foals
    }

    // ---- Stables breeds twice as fast (divisor halved) ----

    [Fact]
    public void Stables_BreedsFasterThanThePasture_AtTheSameHerd()
    {
        (Game pastureGame, Colony pasture) = WellFedColony();
        pasture.AddGoods(Horses, 26);
        pastureGame.EndTurn();
        // country: ((26-1)/50 + 1) * 2 = (0+1)*2 = 2 foals
        Assert.Equal(28, pasture.StoreOf(Horses));

        (Game stablesGame, Colony stabled) = WellFedColony();
        stabled.ReplaceBuilding(Country, Stables); // upgrade the breeder (no double-breeding)
        stabled.AddGoods(Horses, 26);
        stablesGame.EndTurn();
        // stables: ((26-1)/25 + 1) * 2 = (1+1)*2 = 4 foals — twice the pasture's
        Assert.Equal(30, stabled.StoreOf(Horses));
    }

    // ---- Warehouse cap ----

    [Fact]
    public void Breeding_StopsAtTheWarehouseCap()
    {
        (Game game, Colony colony) = FoundedColony();
        colony.AddGoods(Food, 60);
        int cap = game.WarehouseCapacity(colony); // depot = 100

        colony.AddGoods(Horses, cap - 1); // one short of the cap
        game.EndTurn();
        Assert.Equal(cap, colony.StoreOf(Horses)); // bred only the single space left (formula 4 clamped to headroom 1)

        game.EndTurn();
        Assert.Equal(cap, colony.StoreOf(Horses)); // at the cap → no further foals
    }

    // ---- Surplus-food limit ----

    [Fact]
    public void Breeding_EatsOnlySurplusFood_NeverStarvingTheColony()
    {
        // A big herd wants several foals (4 at 1:1 = 4 food), but with the store held to exactly the colonists'
        // ration the only spare is this turn's centre-tile food — breeding takes only half of that, never the
        // ration, so the colony cannot starve and the foals are throttled below the formula's maximum.
        (Game game, Colony colony) = FoundedColony();
        colony.AddGoods(Horses, 51);                          // formula wants ((51-1)/50+1)*2 = 4 foals
        int pop = colony.Population;
        colony.AddGoods(Food, (pop * Colony.FoodPerColonist) - colony.Food); // hold the store to exactly the ration
        Assert.Equal(pop * Colony.FoodPerColonist, colony.Food);

        game.EndTurn();

        int foals = colony.StoreOf(Horses) - 51;
        Assert.InRange(foals, 0, 4);          // bounded by the formula, throttled by the scarce surplus
        Assert.Equal(pop, colony.Population); // breeding never ate the ration → no starvation
    }

    // ---- Determinism + persistence (the soak never holds horses, so it can't witness this) ----

    [Fact]
    public void Breeding_OverManyTurns_IsTwinDeterministic_AndSurvivesASaveRoundTrip()
    {
        // No soak colony ever acquires horses (no buy/trade path), so the soak never drives breeding. This pins the
        // determinism + round-trip stability of a drifting herd directly: two same-seed games stay identical, and a
        // save reloaded mid-drift breeds on exactly as the un-saved twin.
        (Game a, Colony ca) = WellFedColony();
        ca.AddGoods(Horses, 4);
        (Game b, Colony cb) = WellFedColony();
        cb.AddGoods(Horses, 4);

        for (int i = 0; i < 10; i++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        int herd = ca.StoreOf(Horses);
        Assert.True(herd > 4, "the herd should have grown over ten turns");
        Assert.Equal(herd, cb.StoreOf(Horses)); // twin-deterministic (no RNG in breeding)

        // Round-trip mid-drift, then keep breeding: identical to the un-saved game thereafter.
        Game restored = SaveGame.FromJson(SaveGame.From(a).ToJson()).Restore(Classic);
        for (int i = 0; i < 5; i++)
        {
            a.EndTurn();
            restored.EndTurn();
        }
        Assert.Equal(ca.StoreOf(Horses), restored.Colonies.Single(c => c.Id == ca.Id).StoreOf(Horses));
    }
}
