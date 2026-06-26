using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// On-the-job experience upgrades (<c>86d3c9pgj</c>, FreeCol <c>model.unitChange.experience</c>): a free colonist
/// working a tile accrues that turn's production as experience (capped at the type's <c>maximum-experience</c>, 200 for
/// a free colonist) and rolls a per-turn chance — <c>experience / (100·maxExp/probability)</c>, peaking at the spec
/// 4% — to upgrade in place to the tile good's matching expert. Saved additively in v31 (omitted when 0).
/// </summary>
public class ExperienceUpgradeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Free = "model.unit.freeColonist";
    private const string ExpertFarmer = "model.unit.expertFarmer";
    private const string Indentured = "model.unit.indenturedServant";
    private const string Petty = "model.unit.pettyCriminal";
    private const string Grain = "model.goods.grain";

    /// <summary>A fixed-draw RNG that records the bound it was asked for and how many times it was drawn.</summary>
    private sealed class CountingRandom(int returns) : IGameRandom
    {
        public int Calls { get; private set; }
        public int LastMax { get; private set; }
        public int Next(int maxExclusive) { Calls++; LastMax = maxExclusive; return returns; }
        public int Next(int minInclusive, int maxExclusive) { Calls++; return minInclusive; }
        public double NextDouble() => 0;
        public RandomState SaveState() => new(0, 0);
    }

    private static (Game game, Colony colony, Position tile, string good) FreeColonistOnGrain()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        Position tile = colony.TileWorkers.Keys.First(); // founder auto-assigns to the best food (grain) tile
        return (game, colony, tile, colony.TileWorkers[tile]);
    }

    // ── Ruleset data (step 0: maximum-experience parsed; the from-collapse bug fixed) ───────────────────────────

    [Fact]
    public void FreeColonist_HasMaximumExperience200() => Assert.Equal(200, Classic.Unit(Free).MaximumExperience);

    [Theory]
    [InlineData(ExpertFarmer)]
    [InlineData(Indentured)]
    [InlineData(Petty)]
    public void NonUpgradingTypes_HaveNoMaximumExperience(string type) =>
        Assert.Equal(0, Classic.Unit(type).MaximumExperience);

    [Theory] // every experience row survives — they all share from=freeColonist (the from-key collapse is fixed)
    [InlineData(ExpertFarmer)]
    [InlineData("model.unit.expertFisherman")]
    [InlineData("model.unit.masterCottonPlanter")]
    [InlineData("model.unit.masterTobaccoPlanter")]
    [InlineData("model.unit.expertLumberJack")]
    public void ExperienceProbability_IsFour_ForEveryFreeColonistExpertRow(string expert) =>
        Assert.Equal(4, Classic.ExperienceUpgradeProbability(Free, expert));

    [Fact]
    public void ExperienceProbability_IsZero_ForANonExperiencePair() =>
        Assert.Equal(0, Classic.ExperienceUpgradeProbability(Free, "model.unit.veteranSoldier"));

    [Theory]
    [InlineData(Grain, ExpertFarmer)]
    [InlineData("model.goods.fish", "model.unit.expertFisherman")]
    [InlineData("model.goods.cotton", "model.unit.masterCottonPlanter")]
    [InlineData("model.goods.ore", "model.unit.expertOreMiner")]
    [InlineData("model.goods.lumber", "model.unit.expertLumberJack")]
    public void ExpertForProducing_MapsAGoodToItsExpert(string good, string expert) =>
        Assert.Equal(expert, Classic.ExpertForProducing(good));

    [Fact]
    public void ExpertForProducing_IsNull_ForAGoodWithNoExpert() =>
        Assert.Null(Classic.ExpertForProducing("model.goods.food")); // grain/fish have experts; "food" (storage) does not

    // ── Accrual ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FreeColonistFarmer_AccruesProductionAsExperience_ClampedAtTheCap()
    {
        (Game game, Colony colony, Position tile, string good) = FreeColonistOnGrain();
        Assert.Equal(Grain, good);
        var rng = new CountingRandom(4999); // a draw this high never upgrades (xp ≤ 200)

        game.AccrueAndRollExperience(colony, tile, good, 3, rng);
        Assert.Equal(3, colony.TileWorkerExperienceAt(tile));
        game.AccrueAndRollExperience(colony, tile, good, 3, rng);
        Assert.Equal(6, colony.TileWorkerExperienceAt(tile));

        colony.AddTileWorkerExperience(tile, 1000, 200); // shoot for over the cap
        Assert.Equal(200, colony.TileWorkerExperienceAt(tile));
        game.AccrueAndRollExperience(colony, tile, good, 50, rng);
        Assert.Equal(200, colony.TileWorkerExperienceAt(tile)); // stays clamped
    }

    // ── Eligibility (a non-free / wrong / already-expert worker draws no RNG and never upgrades) ──────────────────

    [Theory]
    [InlineData(Indentured)]
    [InlineData(Petty)]
    [InlineData(ExpertFarmer)] // already the grain expert
    public void IneligibleWorker_AccruesNothing_DrawsNoRng_AndNeverUpgrades(string type)
    {
        (Game game, Colony colony, Position tile, string good) = FreeColonistOnGrain();
        colony.SetWorker(tile, good, type);
        var rng = new CountingRandom(0); // would upgrade anyone eligible

        game.AccrueAndRollExperience(colony, tile, good, 3, rng);

        Assert.Equal(0, rng.Calls);                       // determinism: no draw consumed for an ineligible tile
        Assert.Equal(type, colony.WorkerTypeAt(tile));    // unchanged
        Assert.Equal(0, colony.TileWorkerExperienceAt(tile));
    }

    [Fact]
    public void FreeColonistOnAGoodWithNoExpert_AccruesNothing_AndDrawsNoRng()
    {
        (Game game, Colony colony, Position tile, _) = FreeColonistOnGrain();
        var rng = new CountingRandom(0);
        game.AccrueAndRollExperience(colony, tile, "model.goods.food", 3, rng); // "food" has no expert
        Assert.Equal(0, rng.Calls);
        Assert.Equal(0, colony.TileWorkerExperienceAt(tile));
    }

    // ── Upgrade roll (maxValue = 100·200/4 = 5000; upgrade iff draw < experience) ─────────────────────────────────

    [Fact]
    public void Upgrades_WhenTheDrawIsBelowExperience()
    {
        (Game game, Colony colony, Position tile, string good) = FreeColonistOnGrain();
        colony.AddTileWorkerExperience(tile, 199, 200);
        var rng = new CountingRandom(199); // accrue +1 → xp 200; 199 < 200 → upgrade

        game.AccrueAndRollExperience(colony, tile, good, 1, rng);

        Assert.Equal(5000, rng.LastMax);                 // the FreeCol maxValue
        Assert.Equal(1, rng.Calls);
        Assert.Equal(ExpertFarmer, colony.WorkerTypeAt(tile));
        Assert.Equal(0, colony.TileWorkerExperienceAt(tile)); // experience cleared on upgrade
    }

    [Fact]
    public void DoesNotUpgrade_WhenTheDrawEqualsExperience() // strict <
    {
        (Game game, Colony colony, Position tile, string good) = FreeColonistOnGrain();
        colony.AddTileWorkerExperience(tile, 200, 200);
        var rng = new CountingRandom(200); // 200 < 200 is false

        game.AccrueAndRollExperience(colony, tile, good, 0, rng);

        Assert.Equal(Free, colony.WorkerTypeAt(tile));
        Assert.Equal(200, colony.TileWorkerExperienceAt(tile));
    }

    [Fact]
    public void Upgrade_LeavesTheCountModelUntouched()
    {
        (Game game, Colony colony, Position tile, string good) = FreeColonistOnGrain();
        int populationBefore = colony.Population;
        int idleBefore = colony.IdleColonists;
        colony.AddTileWorkerExperience(tile, 200, 200);

        game.AccrueAndRollExperience(colony, tile, good, 0, new CountingRandom(0)); // 0 < 200 → upgrade

        Assert.Equal(ExpertFarmer, colony.WorkerTypeAt(tile));
        Assert.Equal(populationBefore, colony.Population);
        Assert.Equal(idleBefore, colony.IdleColonists);
        Assert.True(colony.TileWorkers.ContainsKey(tile)); // still working the same tile
    }

    // ── Experience is per-(tile, occupant): cleared whenever the worker leaves or changes ─────────────────────────

    [Fact]
    public void Experience_IsCleared_OnReassignAndRemoval()
    {
        (_, Colony colony, Position tile, string good) = FreeColonistOnGrain();

        colony.AddTileWorkerExperience(tile, 50, 200);
        colony.SetWorker(tile, good); // a (re)assignment resets the new occupant to 0
        Assert.Equal(0, colony.TileWorkerExperienceAt(tile));

        colony.AddTileWorkerExperience(tile, 50, 200);
        colony.RemoveWorker(tile); // the worker leaves — experience goes with it
        Assert.Equal(0, colony.TileWorkerExperienceAt(tile));
    }

    // ── Persistence (v31, additive) ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Experience_RoundTripsThroughSave_V31()
    {
        (Game game, Colony colony, Position tile, _) = FreeColonistOnGrain();
        colony.AddTileWorkerExperience(tile, 137, 200);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(63, SaveGame.CurrentVersion);
        Colony r = restored.Colonies.Single(c => c.Id == colony.Id);
        Assert.Equal(137, r.TileWorkerExperienceAt(tile));
    }

    [Fact]
    public void ZeroExperience_IsOmittedFromTheSave()
    {
        Game game = Game.New(Classic, Seed);
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony)); // fresh: no accrued experience
        Assert.DoesNotContain("Experience", SaveGame.From(game).ToJson()); // additive: omitted → byte-identical to v30
    }

    // ── End-to-end: the colony turn accrues + the run is twin-deterministic and survives a save/load ──────────────

    [Fact]
    public void TheColonyTurn_AccruesExperience_ForAFreeColonistFarmer()
    {
        (Game game, Colony colony, Position tile, _) = FreeColonistOnGrain();
        Assert.Equal(Free, colony.WorkerTypeAt(tile));

        game.EndTurn();

        Assert.True(
            colony.TileWorkerExperienceAt(tile) > 0 || colony.WorkerTypeAt(tile) != Free,
            "a free-colonist farmer should accrue experience (or upgrade) over a colony turn");
    }

    [Fact]
    public void ExperienceUpgrades_AreTwinDeterministic_AndSurviveSaveLoad()
    {
        static Game Run()
        {
            Game g = Game.New(Classic, Seed);
            g.FoundColony(g.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
            return g;
        }
        static string Composition(Game g) => string.Join("|", g.Colonies
            .OrderBy(c => c.Id)
            .SelectMany(c => c.TileWorkers.Keys
                .OrderBy(p => p.Y).ThenBy(p => p.X)
                .Select(p => $"{c.Id}:{p.X},{p.Y}={c.WorkerTypeAt(p)}#{c.TileWorkerExperienceAt(p)}")));

        Game a = Run();
        Game b = Run();
        for (int i = 0; i < 30; i++) { a.EndTurn(); b.EndTurn(); }
        Assert.Equal(Composition(a), Composition(b)); // same seed → identical worker composition + experience

        Game restored = SaveGame.FromJson(SaveGame.From(a).ToJson()).Restore(Classic);
        Assert.Equal(Composition(a), Composition(restored));
        for (int i = 0; i < 10; i++) { a.EndTurn(); restored.EndTurn(); }
        Assert.Equal(Composition(a), Composition(restored)); // continues identically after a round-trip
    }
}
