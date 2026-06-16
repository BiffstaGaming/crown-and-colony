using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// L1 unit tests for per-colony Sons of Liberty (FreeCol <c>Colony.calculateSoLPercentage</c> /
/// <c>calculateRebelCount</c> / <c>calculateProductionBonus</c>): the membership %, rebel/tory split, the
/// production-bonus tiers (medium-difficulty government limits 6/10), liberty accumulation + the 100%-SoL cap, the
/// v22 save round-trip, and (Slice B) the bonus reaching tile + building output (+N per attended worker, floored).
/// </summary>
public class SonsOfLibertyTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static Colony ColonyWith(int population, int liberty)
    {
        var colony = new Colony(1, "Test", new Position(0, 0), population);
        colony.Liberty = liberty;
        return colony;
    }

    // ── A. SoL% from liberty + population ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 0, 0)]      // no liberty → 0%
    [InlineData(5, 500, 50)]   // exactly half of 200×pop → 50%
    [InlineData(5, 1000, 100)] // full (200×pop) → 100%
    [InlineData(5, 5000, 100)] // over-full clamps to 100%
    [InlineData(3, 199, 33)]   // floor(199·100/600)=33 — truncates, never rounds
    [InlineData(0, 999, 0)]    // empty colony → 0% (no divide-by-zero)
    public void SonsOfLiberty_IsLibertyOverTwoHundredTimesPop_FlooredAndClamped(int pop, int liberty, int expected) =>
        Assert.Equal(expected, ColonyWith(pop, liberty).SonsOfLiberty);

    // ── B. Rebel / tory counts ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RebelCount_IsFloorOfSoLTimesPopulation()
    {
        Colony colony = ColonyWith(population: 7, liberty: 700); // SoL 50
        Assert.Equal(50, colony.SonsOfLiberty);
        Assert.Equal(3, colony.RebelCount); // floor(0.5·7)
        Assert.Equal(4, colony.ToryCount);
    }

    [Theory]
    [InlineData(7, 700)]
    [InlineData(11, 0)]
    [InlineData(5, 5000)]
    [InlineData(3, 199)]
    public void RebelsPlusTories_AlwaysEqualPopulation(int pop, int liberty)
    {
        Colony colony = ColonyWith(pop, liberty);
        Assert.Equal(pop, colony.RebelCount + colony.ToryCount);
    }

    [Fact]
    public void AtFullSoL_EveryoneIsARebel()
    {
        Colony colony = ColonyWith(population: 4, liberty: 800); // SoL 100
        Assert.Equal(4, colony.RebelCount);
        Assert.Equal(0, colony.ToryCount);
    }

    // ── C. Production-bonus tiers (medium-difficulty limits 6/10 — the penalty pins these) ───────────────────

    [Fact]
    public void SoL100_GivesPlusTwo() => Assert.Equal(2, ColonyWith(5, 1000).ProductionBonus);

    [Fact]
    public void SoL50_GivesPlusOne() => Assert.Equal(1, ColonyWith(5, 500).ProductionBonus);

    [Fact]
    public void SoL49_DoesNotGivePlusOne() => Assert.Equal(0, ColonyWith(5, 490).ProductionBonus); // SoL 49, 3 tories

    [Fact]
    public void SmallColony_LowSoL_HasNoPenalty() => Assert.Equal(0, ColonyWith(6, 0).ProductionBonus); // 6 tories, not > 6

    [Fact]
    public void SevenTories_TriggerBadGovernment() => Assert.Equal(-1, ColonyWith(7, 0).ProductionBonus); // > 6 (medium limit)

    [Fact]
    public void ElevenTories_TriggerVeryBadGovernment() => Assert.Equal(-2, ColonyWith(11, 0).ProductionBonus); // > 10 (medium limit)

    [Fact]
    public void HighSoL_OverridesTheToryPenalty() => Assert.Equal(1, ColonyWith(11, 1100).ProductionBonus); // SoL 50 wins over 6 tories

    // ── D. AddLiberty floor + 100%-SoL cap ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AddLiberty_Negative_FloorsAtZero()
    {
        Colony colony = ColonyWith(population: 5, liberty: 50);
        colony.AddLiberty(-100);
        Assert.Equal(0, colony.Liberty);
    }

    [Fact]
    public void AddLiberty_PastFullSoL_CapsAtTwoHundredTimesPopulation()
    {
        Colony colony = ColonyWith(population: 5, liberty: 0);
        colony.AddLiberty(5000); // would be SoL 500%
        Assert.Equal(1000, colony.Liberty); // capped to 200×5
        Assert.Equal(100, colony.SonsOfLiberty);
    }

    // ── E. Accumulation over a turn: the colony banks the same figure as the player's founding-father pool ───

    [Fact]
    public void EndTurn_BanksColonyLiberty_TrackingThePlayerPool()
    {
        Game game = ColonyOnPlains(population: 1); // a town hall produces bells unattended
        Colony colony = game.Colonies[0];
        Assert.Equal(0, colony.Liberty);
        int playerBefore = game.Liberty;

        game.EndTurn();

        int gained = game.Liberty - playerBefore;
        Assert.True(gained > 0, "the town hall should bank some bells into liberty");
        Assert.Equal(gained, colony.Liberty); // colony liberty rose by the same figure the player pool got

        int colonyAfterOne = colony.Liberty;
        game.EndTurn();
        Assert.True(colony.Liberty > colonyAfterOne, "liberty keeps climbing while bells are produced");
    }

    // ── F. Save round-trip (v22, additive) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Liberty_SurvivesSaveLoad()
    {
        Game game = ColonyOnPlains(population: 3);
        game.Colonies[0].Liberty = 350;

        Game reloaded = SaveGame.From(game).Restore(Classic);

        Assert.Equal(350, reloaded.Colonies[0].Liberty);
    }

    [Fact]
    public void ZeroLiberty_IsOmittedFromTheSave()
    {
        Game game = ColonyOnPlains(population: 1); // freshly founded, no banked liberty
        Assert.Equal(0, game.Colonies[0].Liberty);
        Assert.Null(SaveGame.From(game).Colonies![0].Liberty); // additive: omitted → byte-identical to v21
    }

    [Fact]
    public void PreV22Colony_LoadsLibertyZero()
    {
        // A SavedColony without the v22 field (Liberty defaulted to null) restores to 0.
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [],
            Colonies = [new SavedColony(1, "Old", 0, 0, 1)],
        };
        Assert.Equal(0, save.Restore(Classic).Colonies[0].Liberty);
    }

    // ── G. The production bonus reaches output (Slice B) ────────────────────────────────────────────────────

    [Fact]
    public void ProductionBonus_PlusTwoAtFullSoL_AddsToATileWorkersOutput()
    {
        // Same colony + worker; the only variable is the SoL bonus (compare a base run vs a full-SoL run).
        int CottonGained(int liberty)
        {
            Game game = ColonyOnPlains(population: 1);
            Colony colony = game.Colonies[0];
            Position tile = colony.Position.Neighbours().First(n => game.TileWorkOptions(n).Any(o => o.GoodsId == "model.goods.cotton"));
            game.AssignWork(colony, tile, "model.goods.cotton");
            colony.Liberty = liberty;
            int before = colony.StoreOf("model.goods.cotton");
            game.EndTurn();
            return colony.StoreOf("model.goods.cotton") - before;
        }

        int baseCotton = CottonGained(liberty: 0);                       // SoL 0 → +0
        int boosted = CottonGained(liberty: Colony.LibertyPerRebel);     // pop 1 → SoL 100 → +2
        Assert.True(baseCotton > 0, "the worker should produce cotton");
        Assert.Equal(baseCotton + 2, boosted);
    }

    [Fact]
    public void ProductionBonus_AddsPerWorker_ToBuildingOutput()
    {
        // One carpenter, plenty of lumber + food; the only variable is the SoL bonus.
        int HammersGained(int liberty)
        {
            Game game = ColonyOnPlains(population: 2);
            SaveGame save = SaveGame.From(game);
            SavedColony c = save.Colonies!.Single() with
            {
                Population = 2,
                Workers = null,
                BuildingWorkers = new Dictionary<string, int> { ["model.building.carpenterHouse"] = 1 },
                Stores = new Dictionary<string, int> { ["model.goods.lumber"] = 200, ["model.goods.food"] = 200 },
                Liberty = liberty,
            };
            Game restored = (save with { Colonies = [c] }).Restore(Classic);
            Colony colony = restored.Colonies[0];
            int before = colony.StoreOf("model.goods.hammers");
            restored.EndTurn();
            return colony.StoreOf("model.goods.hammers") - before;
        }

        int baseHammers = HammersGained(liberty: 0);     // SoL 0 → +0
        int boosted = HammersGained(liberty: 400);       // pop 2 → SoL 100 → +2 per worker (1 worker)
        Assert.True(baseHammers > 0, "the carpenter should produce hammers");
        Assert.Equal(baseHammers + 2, boosted);
    }

    private static Game ColonyOnPlains(int population)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain =
            [
                "model.tile.plains", "model.tile.plains", "model.tile.plains",
                "model.tile.plains", "model.tile.plains", "model.tile.plains",
                "model.tile.plains", "model.tile.plains", "model.tile.plains",
            ],
            Units = [],
            Explored = [],
            Colonies = [new SavedColony(1, "Liberty City", 1, 1, population)],
        };
        return save.Restore(Classic);
    }
}
