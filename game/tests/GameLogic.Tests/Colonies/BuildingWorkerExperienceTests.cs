using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Building on-the-job learning (<c>86d3kgbpd</c> — the original 1994 Colonization let a colonist become a building's
/// master by WORKING it; FreeCol teaches building experts only in schools). A <b>free</b> colonist working a building
/// accrues that turn's output as shared experience (capped at the free colonist's <c>maximum-experience</c>, 200) and
/// rolls a per-turn chance — <c>experience / (100·maxExp/4)</c>, peaking at 4% — to upgrade one free occupant in place to
/// the building's expert. Saved additively in v70 (omitted when 0). Mirrors the tile path (<c>ExperienceUpgradeTests</c>).
/// </summary>
public class BuildingWorkerExperienceTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 3UL;
    private const string Carpenter = "model.building.carpenterHouse";
    private const string MasterCarpenter = "model.unit.masterCarpenter";
    private const string Hammers = "model.goods.hammers";
    private const string Lumber = "model.goods.lumber";
    private const string Food = "model.goods.food";
    private const string Free = "model.unit.freeColonist";
    private const string Indentured = "model.unit.indenturedServant";
    private const string Petty = "model.unit.pettyCriminal";

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

    /// <summary>A fresh colony whose only workers are the given types, all in the carpenter's house, well-fed.</summary>
    private static (Game game, Colony colony) CarpenterColony(params string[] workerTypes)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (Position tile in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, tile); // pull the founder out of the fields so only building work runs
        }
        colony.Population = workerTypes.Length;
        foreach (string type in workerTypes)
        {
            colony.AssignBuildingWorker(Carpenter, type);
        }
        colony.AddGoods(Food, 100); // no starvation / growth perturbing the count this turn
        return (game, colony);
    }

    // ── Ruleset data: a building expert has NO experience row (we mirror FreeCol's classic spec byte-for-byte) ───────

    [Fact]
    public void BuildingExpert_HasNoExperienceRow_InTheClassicSpec() =>
        // FreeCol teaches building experts only in schools; we DON'T add a spec row (keeping our "classic" a faithful
        // mirror). Col1's building-learning rate is supplied in code (Game.ClassicBuildingExperienceProbability) instead.
        Assert.Equal(0, Classic.ExperienceUpgradeProbability(Free, MasterCarpenter));

    [Fact]
    public void ExpertForProducing_MapsABuildingGoodToItsExpert() =>
        Assert.Equal(MasterCarpenter, Classic.ExpertForProducing(Hammers));

    // ── Accrual + eligibility ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FreeColonistInABuilding_AccruesProductionAsExperience_ClampedAtTheCap()
    {
        (Game game, Colony colony) = CarpenterColony(Free);
        var rng = new CountingRandom(4999); // a draw this high never upgrades (xp ≤ 200)

        game.AccrueAndRollBuildingExperience(colony, Carpenter, MasterCarpenter, 3, rng);
        Assert.Equal(3, colony.BuildingWorkerExperienceAt(Carpenter));
        game.AccrueAndRollBuildingExperience(colony, Carpenter, MasterCarpenter, 3, rng);
        Assert.Equal(6, colony.BuildingWorkerExperienceAt(Carpenter));

        colony.AddBuildingWorkerExperience(Carpenter, 1000, 200); // shoot for over the cap
        Assert.Equal(200, colony.BuildingWorkerExperienceAt(Carpenter)); // stays clamped
    }

    [Theory]
    [InlineData(Indentured)]
    [InlineData(Petty)]
    [InlineData(MasterCarpenter)] // already the expert
    public void ABuildingWithNoFreeColonist_AccruesNothing_DrawsNoRng_AndNeverUpgrades(string type)
    {
        (Game game, Colony colony) = CarpenterColony(type);
        var rng = new CountingRandom(0); // would upgrade anyone eligible

        game.AccrueAndRollBuildingExperience(colony, Carpenter, MasterCarpenter, 3, rng);

        Assert.Equal(0, rng.Calls); // determinism: no draw consumed when no free colonist can learn
        Assert.Equal(0, colony.BuildingWorkerExperienceAt(Carpenter));
        Assert.Equal(type, colony.BuildingOccupants(Carpenter).Single()); // unchanged
    }

    // ── Upgrade roll (maxValue = 100·200/4 = 5000; upgrade iff draw < experience) ──────────────────────────────────

    [Fact]
    public void Upgrades_OneFreeColonist_WhenTheDrawIsBelowExperience()
    {
        (Game game, Colony colony) = CarpenterColony(Free, Free);
        colony.AddBuildingWorkerExperience(Carpenter, 199, 200);
        var rng = new CountingRandom(199); // accrue +1 → xp 200; 199 < 200 → upgrade

        game.AccrueAndRollBuildingExperience(colony, Carpenter, MasterCarpenter, 1, rng);

        Assert.Equal(5000, rng.LastMax); // the FreeCol maxValue
        Assert.Equal(1, rng.Calls);
        Assert.Equal(1, colony.BuildingOccupants(Carpenter).Count(t => t == MasterCarpenter)); // exactly one graduated
        Assert.Equal(1, colony.BuildingOccupants(Carpenter).Count(t => t == Free));            // the other stays free
        Assert.Equal(0, colony.BuildingWorkerExperienceAt(Carpenter));                          // pool cleared on graduation
    }

    [Fact]
    public void DoesNotUpgrade_WhenTheDrawEqualsExperience() // strict <
    {
        (Game game, Colony colony) = CarpenterColony(Free);
        colony.AddBuildingWorkerExperience(Carpenter, 200, 200);
        var rng = new CountingRandom(200); // 200 < 200 is false

        game.AccrueAndRollBuildingExperience(colony, Carpenter, MasterCarpenter, 0, rng);

        Assert.Equal(Free, colony.BuildingOccupants(Carpenter).Single());
        Assert.Equal(200, colony.BuildingWorkerExperienceAt(Carpenter));
    }

    [Fact]
    public void Upgrade_LeavesThePopulationAndWorkerCountUntouched()
    {
        (Game game, Colony colony) = CarpenterColony(Free);
        int populationBefore = colony.Population;
        colony.AddBuildingWorkerExperience(Carpenter, 200, 200);

        game.AccrueAndRollBuildingExperience(colony, Carpenter, MasterCarpenter, 0, new CountingRandom(0)); // 0 < 200 → upgrade

        Assert.Equal(MasterCarpenter, colony.BuildingOccupants(Carpenter).Single());
        Assert.Equal(populationBefore, colony.Population);
        Assert.Equal(1, colony.BuildingWorkers.GetValueOrDefault(Carpenter)); // still one worker in the building
    }

    // ── Persistence (v70, additive) ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Experience_RoundTripsThroughSave_V70()
    {
        (Game game, Colony colony) = CarpenterColony(Free);
        colony.AddBuildingWorkerExperience(Carpenter, 137, 200);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(76, SaveGame.CurrentVersion);
        Colony r = restored.Colonies.Single(c => c.Id == colony.Id);
        Assert.Equal(137, r.BuildingWorkerExperienceAt(Carpenter));
    }

    [Fact]
    public void ZeroExperience_IsOmittedFromTheSave()
    {
        Game game = Game.New(Classic, Seed);
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony)); // fresh: no accrued building experience
        Assert.DoesNotContain("BuildingWorkerExperience", SaveGame.From(game).ToJson()); // additive: omitted → byte-identical to v69
    }

    [Fact]
    public void AZeroExperiencePool_IsOmitted_AndRoundTripsByteIdentically()
    {
        // Regression: accruing 0 (a building that produced none of its expert good this turn) creates a 0-value pool
        // entry; the save must OMIT it (like the per-tile filter) so a save→load→save stays byte-identical — otherwise
        // restore's >0 skip drops it and the round-trip diverges (caught first by the 25-seed soak).
        (Game game, Colony colony) = CarpenterColony(Free);
        colony.AddBuildingWorkerExperience(Carpenter, 0, 200); // a 0 gain creates a 0-value pool entry in memory

        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("BuildingWorkerExperience", json);                                 // a 0 pool is not written
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());     // byte-identical round-trip
    }

    // ── End-to-end: the colony turn accrues + the run is twin-deterministic and survives a save/load ──────────────

    [Fact]
    public void TheColonyTurn_AccruesBuildingExperience_ForAFreeColonistCarpenter()
    {
        (Game game, Colony colony) = CarpenterColony(Free);
        colony.AddGoods(Lumber, 100); // feed the carpenter so it makes hammers (the experience gain)

        game.EndTurn();

        Assert.True(
            colony.BuildingWorkerExperienceAt(Carpenter) > 0 || colony.BuildingOccupants(Carpenter).Single() != Free,
            "a free-colonist carpenter should accrue building experience (or upgrade) over a colony turn");
    }

    [Fact]
    public void BuildingUpgrades_AreTwinDeterministic_AndSurviveSaveLoad()
    {
        static (Game game, Colony colony) Run()
        {
            (Game g, Colony c) = CarpenterColony(Free, Free);
            c.AddGoods(Lumber, 800);
            c.AddGoods(Food, 900); // keep the colony fed over the run so the worker count is stable
            return (g, c);
        }
        static string State(Colony c) =>
            string.Join(",", c.BuildingOccupants(Carpenter).OrderBy(t => t)) + $"#{c.BuildingWorkerExperienceAt(Carpenter)}";

        (Game a, Colony ca) = Run();
        (Game b, Colony cb) = Run();
        for (int i = 0; i < 30; i++) { a.EndTurn(); b.EndTurn(); }
        Assert.Equal(State(ca), State(cb)); // same seed → identical building composition + experience

        Game restored = SaveGame.FromJson(SaveGame.From(a).ToJson()).Restore(Classic);
        Colony cr = restored.Colonies.Single(c => c.Id == ca.Id);
        Assert.Equal(State(ca), State(cr)); // survives a save/load round-trip
    }
}
