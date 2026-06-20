using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Schoolhouse teaching (<c>86d3c9p7f</c> slice 2): an expert in a school raises the colony's least-skilled colonist
/// one rung per training cycle (petty criminal → indentured servant → free colonist → the teacher's expertise) over
/// the spec turns (4/6/8, reduced by the Sons-of-Liberty bonus, floor 1). Deterministic (no RNG); save v32.
/// </summary>
public class SchoolTeachingTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Schoolhouse = "model.building.schoolhouse";
    private const string University = "model.building.university";
    private const string TownHall = "model.building.townHall";
    private const string ExpertOreMiner = "model.unit.expertOreMiner"; // skill 1
    private const string ElderStatesman = "model.unit.elderStatesman"; // skill 3
    private const string Petty = "model.unit.pettyCriminal";
    private const string Indentured = "model.unit.indenturedServant";
    private const string Free = "model.unit.freeColonist";
    private const string Grain = "model.goods.grain";
    private const string Ore = "model.goods.ore";

    /// <summary>A colony with the given school building staffed by <paramref name="teacher"/> (pop = 1, the teacher).</summary>
    private static (Game game, Colony colony) School(string building, string teacher)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (Position t in colony.TileWorkers.Keys.ToList())
        {
            game.UnassignWork(colony, t); // free the founder off the fields
        }
        colony.AddBuilding(building);
        colony.Population = 1;
        colony.AssignBuildingWorker(building, teacher);
        colony.AddGoods("model.goods.food", 200);
        return (game, colony);
    }

    /// <summary>Adds one idle colonist of <paramref name="type"/> (a student) to the colony.</summary>
    private static void AddIdleStudent(Colony colony, string type)
    {
        colony.Population++;
        colony.AddIdleColonist(type); // a free colonist is implicit in the idle count
    }

    // ── Colony mutators ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SchoolTrainingTurns_AccrueResetAndRestore()
    {
        var colony = new Colony(1, "Test", new Position(0, 0), 1);
        colony.AddSchoolTrainingTurn(Schoolhouse);
        colony.AddSchoolTrainingTurn(Schoolhouse);
        Assert.Equal(2, colony.SchoolTrainingTurnsAt(Schoolhouse));
        colony.ResetSchoolTraining(Schoolhouse);
        Assert.Equal(0, colony.SchoolTrainingTurnsAt(Schoolhouse));
        colony.RestoreSchoolTraining(Schoolhouse, 3);
        Assert.Equal(3, colony.SchoolTrainingTurnsAt(Schoolhouse));
        colony.RestoreSchoolTraining(Schoolhouse, 0); // 0 stays sparse (not stored)
        Assert.Equal(3, colony.SchoolTrainingTurnsAt(Schoolhouse));
    }

    [Fact]
    public void UpgradeIdleWorker_PromotesAnImplicitFreeColonist_AndBack()
    {
        var colony = new Colony(1, "Test", new Position(0, 0), 1); // one implicit free idle colonist
        colony.UpgradeIdleWorker(Free, ExpertOreMiner);          // free → expert: adds an overlay entry
        Assert.Contains(ExpertOreMiner, colony.IdleWorkerTypes);
        colony.UpgradeIdleWorker(ExpertOreMiner, Free);          // expert → free: removes it
        Assert.Empty(colony.IdleWorkerTypes);
    }

    [Fact]
    public void ReconcileWorkerTypes_DropsTrainingForARemovedSchool()
    {
        var colony = new Colony(1, "Test", new Position(0, 0), 1);
        colony.RestoreSchoolTraining(Schoolhouse, 2); // school not actually present
        colony.ReconcileWorkerTypes();
        Assert.Equal(0, colony.SchoolTrainingTurnsAt(Schoolhouse)); // swept (building absent)
    }

    // ── The teaching ladder ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Teacher_RaisesACriminal_OneRungPerFourTurns_UpToTheExpertise()
    {
        (Game game, Colony colony) = School(Schoolhouse, ExpertOreMiner);
        AddIdleStudent(colony, Petty);
        Assert.Equal(0, colony.ProductionBonus); // needed turns = 4 each

        void Teach(int turns) { for (int i = 0; i < turns; i++) game.RunSchoolTeaching(colony); }

        Teach(3);
        Assert.Contains(Petty, colony.IdleWorkerTypes); // not yet (3 < 4)
        Teach(1);
        Assert.Contains(Indentured, colony.IdleWorkerTypes); // criminal → servant at turn 4
        Assert.Equal(0, colony.SchoolTrainingTurnsAt(Schoolhouse)); // counter reset on graduation
        Teach(4);
        Assert.DoesNotContain(Indentured, colony.IdleWorkerTypes); // servant → free (free is implicit)
        Assert.Empty(colony.IdleWorkerTypes);
        Teach(4);
        Assert.Contains(ExpertOreMiner, colony.IdleWorkerTypes); // free → the teacher's expertise
    }

    [Fact]
    public void NoFurtherTraining_OnceEveryColonistIsAnExpert()
    {
        (Game game, Colony colony) = School(Schoolhouse, ExpertOreMiner);
        AddIdleStudent(colony, ExpertOreMiner); // the only other colonist is already an expert
        for (int i = 0; i < 6; i++) game.RunSchoolTeaching(colony);
        Assert.Equal(0, colony.SchoolTrainingTurnsAt(Schoolhouse)); // no eligible student → counter never advances
    }

    [Fact]
    public void TeachesTheLeastSkilledFirst()
    {
        (Game game, Colony colony) = School(Schoolhouse, ExpertOreMiner);
        AddIdleStudent(colony, Free);
        AddIdleStudent(colony, Petty);        // the least-skilled
        AddIdleStudent(colony, Indentured);
        for (int i = 0; i < 4; i++) game.RunSchoolTeaching(colony);
        Assert.DoesNotContain(Petty, colony.IdleWorkerTypes);   // the criminal was taught first → servant
        Assert.Equal(2, colony.IdleWorkerTypes.Count(t => t == Indentured)); // original servant + the promoted criminal
        Assert.Contains(Indentured, colony.IdleWorkerTypes);
    }

    [Fact]
    public void AnOverSkilledTeacher_CannotTeachInASchoolBelowItsCap()
    {
        // An elder statesman (skill 3) cannot teach in a schoolhouse (max-skill 1); a university (max-skill 4) accepts it.
        (Game school, Colony schoolColony) = School(Schoolhouse, ElderStatesman);
        AddIdleStudent(schoolColony, Free);
        for (int i = 0; i < 10; i++) school.RunSchoolTeaching(schoolColony);
        Assert.Equal(0, schoolColony.SchoolTrainingTurnsAt(Schoolhouse)); // never teaches

        (Game uni, Colony uniColony) = School(University, ElderStatesman);
        AddIdleStudent(uniColony, Free);
        for (int i = 0; i < 8; i++) uni.RunSchoolTeaching(uniColony); // free → elder statesman takes 8 turns
        Assert.Contains(ElderStatesman, uniColony.IdleWorkerTypes);
    }

    [Fact]
    public void AColonistSittingInTheSchool_IsNotTaught()
    {
        // A non-expert co-occupying a school is staff, not a student (FreeCol keeps students out via minimum-skill),
        // so it is never taught in place and the school doesn't train it as a free graduation.
        (Game game, Colony colony) = School(University, ExpertOreMiner);
        colony.Population++;
        colony.AssignBuildingWorker(University, Free); // a free colonist co-occupies the university (not the teacher)

        for (int i = 0; i < 8; i++) game.RunSchoolTeaching(colony);

        Assert.Equal(0, colony.SchoolTrainingTurnsAt(University));         // no eligible student outside the school → no training
        Assert.Contains(Free, colony.BuildingOccupants(University));       // the in-school free colonist was not upgraded
    }

    [Fact]
    public void TeachesATileStudentInPlace()
    {
        (Game game, Colony colony) = School(Schoolhouse, ExpertOreMiner);
        colony.Population++;
        Position tile = colony.Position.Neighbours().First(n => game.Map.InBounds(n) && game.TileWorkOptions(n).Any(o => o.GoodsId == Grain));
        colony.SetWorker(tile, Grain, Free); // a free colonist working a grain tile
        for (int i = 0; i < 4; i++) game.RunSchoolTeaching(colony);
        Assert.Equal(ExpertOreMiner, colony.WorkerTypeAt(tile)); // taught in place, still on its tile
        Assert.True(colony.TileWorkers.ContainsKey(tile));
    }

    [Fact]
    public void TieBreak_PrefersTheEquallyLeastSkilledStudentWorkingTheTeachersExpertGood()
    {
        // Two free colonists (equal skill 0): one mining the teacher's good (ore), one farming grain. The expert ore
        // miner teaches the ore worker first — FreeCol findStudent's trade tie-break — even though the grain worker
        // comes EARLIER in the stable enumeration order (so the old order-only rule would have picked grain).
        (Game game, Colony colony) = School(Schoolhouse, ExpertOreMiner); // ExpertProduction = model.goods.ore
        colony.Population += 2;
        var tiles = colony.Position.Neighbours().Where(n => game.Map.InBounds(n))
            .OrderBy(p => p.Y).ThenBy(p => p.X).Take(2).ToList();
        Position grainTile = tiles[0]; // row-major first → would win the tie under the old stable-order rule
        Position oreTile = tiles[1];   // later, but works the teacher's good → wins via the trade tie-break
        colony.SetWorker(grainTile, Grain, Free);
        colony.SetWorker(oreTile, Ore, Free);

        for (int i = 0; i < 4; i++) game.RunSchoolTeaching(colony);

        Assert.Equal(ExpertOreMiner, colony.WorkerTypeAt(oreTile)); // the ore worker won the tie and graduated in place
        Assert.Equal(Free, colony.WorkerTypeAt(grainTile));         // the grain worker was not chosen
    }

    [Fact]
    public void NeededTurns_AreReducedByTheSonsOfLibertyBonus()
    {
        (Game game, Colony colony) = School(Schoolhouse, ExpertOreMiner);
        AddIdleStudent(colony, Free);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // SoL 100 → ProductionBonus +2
        Assert.Equal(2, colony.ProductionBonus);
        for (int i = 0; i < 2; i++) game.RunSchoolTeaching(colony); // needed = max(1, 4 − 2) = 2
        Assert.Contains(ExpertOreMiner, colony.IdleWorkerTypes);
    }

    // ── Determinism / save ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AColonyWithNoSchool_IsAPureNoOp()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        string before = SaveGame.From(game).ToJson();
        game.RunSchoolTeaching(colony);
        Assert.Equal(before, SaveGame.From(game).ToJson()); // no school → no state change, no draw
    }

    [Fact]
    public void TrainingProgress_RoundTripsThroughSave_V32()
    {
        (Game game, Colony colony) = School(Schoolhouse, ExpertOreMiner);
        AddIdleStudent(colony, Petty);
        game.RunSchoolTeaching(colony);
        game.RunSchoolTeaching(colony);
        Assert.Equal(2, colony.SchoolTrainingTurnsAt(Schoolhouse));

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Colony r = restored.Colonies.Single(c => c.Id == colony.Id);

        Assert.Equal(47, SaveGame.CurrentVersion);
        Assert.Equal(2, r.SchoolTrainingTurnsAt(Schoolhouse)); // mid-training progress survives
        restored.RunSchoolTeaching(r);
        restored.RunSchoolTeaching(r);
        Assert.Contains(Indentured, r.IdleWorkerTypes); // completes at the same turn 4 after the round-trip
    }

    [Fact]
    public void NoTrainingInProgress_IsOmittedFromTheSave()
    {
        Game game = Game.New(Classic, Seed);
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        Assert.DoesNotContain("SchoolTraining", SaveGame.From(game).ToJson()); // additive: omitted → byte-identical to v31
    }
}
