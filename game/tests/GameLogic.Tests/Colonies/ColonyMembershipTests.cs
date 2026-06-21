using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Colonists joining and leaving a colony (Phase 4 slice 9) — the payoff of
/// immigration: a colonist shipped home can grow an existing colony, and a colonist
/// can be detached back into a unit on the map.
/// </summary>
public class ColonyMembershipTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Colonist = "model.unit.freeColonist";
    private const string Caravel = "model.unit.caravel";
    private const string Grain = "model.goods.grain";
    private const string Ore = "model.goods.ore";
    private const string ExpertOreMiner = "model.unit.expertOreMiner";

    /// <summary>3×3: ocean corner (0,0), mountains corner (2,2), rest plains; colony at centre (1,1).</summary>
    private static Game Setup(int population, SavedUnit[] units)
    {
        string[] terrain =
        [
            "model.tile.ocean", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.mountains",
        ];
        return new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 3, MapHeight = 3, Terrain = terrain,
            Units = units, Explored = [],
            Colonies = [new SavedColony(1, "Capital", 1, 1, population)],
        }.Restore(Classic);
    }

    // ───────────────────────── join ─────────────────────────

    [Fact]
    public void JoinColony_GrowsThePopulation_ConsumesTheUnit_AndPutsItToWork()
    {
        Game game = Setup(population: 1, [new SavedUnit(1, Colonist, 0, 1, 3)]); // adjacent to (1,1)
        Colony colony = game.Colonies[0];
        Unit colonist = game.Units[0];

        game.JoinColony(colonist, colony);

        Assert.Equal(2, colony.Population);
        Assert.Empty(game.Units);                 // the colonist became part of the colony
        Assert.Equal(0, colony.IdleColonists);    // the newcomer was auto-assigned to a field
    }

    [Fact]
    public void CheckJoinColony_RequiresAPerson_OnOrNextToTheColony()
    {
        Game game = Setup(population: 1,
        [
            new SavedUnit(1, Caravel, 0, 0, 12),       // ship on the ocean corner (adjacent)
            new SavedUnit(2, Colonist, 1, 1, 3),       // colonist on the colony tile
        ]);
        Colony colony = game.Colonies[0];

        Assert.False(game.CheckJoinColony(game.Units[0], colony).Allowed); // a ship can't join
        Assert.True(game.CheckJoinColony(game.Units[1], colony).Allowed);  // on the colony tile is fine
    }

    [Fact]
    public void CheckJoinColony_Rejects_ANonAdjacentUnit()
    {
        // A 5-wide strip: a colonist at (4,0) is nowhere near the colony at (0,0).
        Game game = new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 5, MapHeight = 1,
            Terrain =
            [
                "model.tile.plains", "model.tile.plains", "model.tile.plains",
                "model.tile.plains", "model.tile.plains",
            ],
            Units = [new SavedUnit(1, Colonist, 4, 0, 3)],
            Explored = [],
            Colonies = [new SavedColony(1, "Far", 0, 0, 1)],
        }.Restore(Classic);

        Assert.False(game.CheckJoinColony(game.Units[0], game.Colonies[0]).Allowed); // (4,0) far from (0,0)
    }

    // ──────────────────── full expert-swap on join (86d3drn5j) ────────────────────

    [Fact]
    public void JoinColony_ExpertEvictsAFreeColonist_FromItsSpecialtyTile()
    {
        // A free colonist mining ore on the mountains tile (2,2); an expert ore miner then joins. Every food tile is
        // staffed, so the expert can't be seated idle-free → it bumps the free colonist off the ore tile and takes it.
        Game game = Setup(population: 1, [new SavedUnit(1, ExpertOreMiner, 2, 1, 3)]); // expert adjacent to (1,1)
        Colony colony = game.Colonies[0];
        var oreTile = new Position(2, 2);
        game.AssignWork(colony, oreTile, Ore);            // the founding free colonist works ore
        Assert.Equal(Colonist, colony.WorkerTypeAt(oreTile));

        game.JoinColony(game.Units[0], colony);

        Assert.Equal(2, colony.Population);
        Assert.Equal(Ore, colony.TileWorkers[oreTile]);                 // the tile still produces ore…
        Assert.Equal(ExpertOreMiner, colony.WorkerTypeAt(oreTile));     // …now worked by the expert (the swap)
        Assert.DoesNotContain(ExpertOreMiner, colony.IdleWorkerTypes);  // the expert is seated, not idle
        // The displaced free colonist was re-seated on a food tile (AutoAssignIdleToFood), so nobody is left idle.
        Assert.Equal(0, colony.IdleColonists);
    }

    [Fact]
    public void JoinColony_ExpertIsNotSwapped_WhenNoFreeColonistWorksItsGood()
    {
        // An expert ore miner joins a colony where nobody is mining ore (only food tiles worked). There is no specialty
        // slot to claim, so FreeCol leaves it un-swapped — here it lands on a food tile via the normal auto-assign.
        Game game = Setup(population: 1, [new SavedUnit(1, ExpertOreMiner, 2, 1, 3)]);
        Colony colony = game.Colonies[0];
        var oreTile = new Position(2, 2);

        game.JoinColony(game.Units[0], colony);

        Assert.Equal(2, colony.Population);
        Assert.False(colony.TileWorkers.ContainsKey(oreTile)); // nobody mines ore — no swap target existed
        Assert.Contains(ExpertOreMiner, colony.TileWorkerTypes.Values); // the expert was still seated (on a food tile)
    }

    // ───────────────────────── leave ─────────────────────────

    [Fact]
    public void LeaveColony_DetachesAFreeColonist_OntoTheColonyTile()
    {
        Game game = Setup(population: 2, []);
        Colony colony = game.Colonies[0];

        Unit unit = game.LeaveColony(colony);

        Assert.Equal(1, colony.Population);
        Assert.Equal(Colonist, unit.Type.Id);          // a generic free colonist
        Assert.Equal(colony.Position, unit.Position);
        Assert.True(unit.IsOnMap);
        Assert.Contains(unit, game.Units);
    }

    [Fact]
    public void LeaveColony_RejectedWhenItWouldEmptyTheColony()
    {
        Game game = Setup(population: 1, []);
        Colony colony = game.Colonies[0];

        Assert.False(game.CheckLeaveColony(colony).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.LeaveColony(colony));
    }

    [Fact]
    public void LeaveColony_TrimsAnAssignment_WhenEveryColonistWasWorking()
    {
        Game game = Setup(population: 2, []);
        Colony colony = game.Colonies[0];
        game.AssignWork(colony, new Position(0, 1), Grain);
        game.AssignWork(colony, new Position(1, 0), Grain);
        Assert.Equal(0, colony.IdleColonists);

        game.LeaveColony(colony);

        Assert.Equal(1, colony.Population);
        Assert.True(colony.TileWorkers.Count <= colony.Population, "assignments must fit the population");
    }

    // ───────────────────────── persistence ─────────────────────────

    [Fact]
    public void JoinThenLeave_RoundTrips()
    {
        Game game = Setup(population: 1, [new SavedUnit(1, Colonist, 0, 1, 3)]);
        game.JoinColony(game.Units[0], game.Colonies[0]);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(2, loaded.Colonies[0].Population);
        Assert.Empty(loaded.Units);
    }
}
