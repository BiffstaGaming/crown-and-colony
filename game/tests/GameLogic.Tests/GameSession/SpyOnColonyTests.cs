using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Spy-on-colony (<c>86d3drn4m</c>, FreeCol <c>InGameController.spySettlement</c> + <c>model.ability.spyOnColony</c>):
/// a scout-role unit adjacent to a <b>rival</b> colony may spy — <b>always succeeding</b> (FreeCol-exact: the spy never
/// fails) to get a one-shot <see cref="ColonyInteriorSnapshot"/> of the colony's interior (buildings / worker layout /
/// stockpile / Sons-of-Liberty). The scout's turn ends and the colony tile is revealed. Spying draws <b>no RNG</b> at
/// all, so the human's economy stream 0 is never shifted (ADR-006/009).
/// </summary>
public class SpyOnColonyTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string ScoutRole = "model.role.scout";
    private const string FreeColonist = "model.unit.freeColonist";

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>The human founds a colony, it is handed to a rival, and a human scout (scout role) stands beside it.</summary>
    private static (Game game, Colony colony, Unit scout, int foreignId) StageScoutBesideRivalColony(string scoutType = FreeColonist)
    {
        Game game = Game.New(Classic, Seed);
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony);
        Colony colony = game.FoundColony(founder);
        colony.OwnerId = foreignId; // now a rival colony (the founding colonist became its population)

        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit scout = game.SpawnUnit(Classic.Unit(scoutType), adj); // human-owned (OwnerId 0)
        scout.RoleId = ScoutRole;
        return (game, colony, scout, foreignId);
    }

    // ---- Gate (CheckSpyOnColony) ----

    [Fact]
    public void Check_AllowsAScoutAdjacentToARivalColony()
    {
        (Game game, Colony colony, Unit scout, _) = StageScoutBesideRivalColony();
        Assert.True(game.CheckSpyOnColony(scout, colony.Position).Allowed);
    }

    [Fact]
    public void Check_RejectsANonScoutColonist()
    {
        (Game game, Colony colony, Unit scout, _) = StageScoutBesideRivalColony();
        scout.RoleId = RoleType.DefaultRoleId; // strip the scout role → no spyOnColony ability

        MoveCheck check = game.CheckSpyOnColony(scout, colony.Position);

        Assert.False(check.Allowed);
        Assert.Contains("only a scout", check.Reason);
    }

    [Fact]
    public void Check_RejectsSpyingOnYourOwnColony()
    {
        (Game game, Colony colony, Unit scout, _) = StageScoutBesideRivalColony();
        colony.OwnerId = scout.OwnerId; // make it the scout's own colony

        MoveCheck check = game.CheckSpyOnColony(scout, colony.Position);

        Assert.False(check.Allowed);
        Assert.Contains("no rival colony", check.Reason);
    }

    [Fact]
    public void Check_RejectsWhenNotAdjacent()
    {
        (Game game, Colony colony, Unit scout, _) = StageScoutBesideRivalColony();
        // Move the scout two tiles away from the colony (still on free land).
        Position far = colony.Position.Neighbours()
            .SelectMany(n => n.Neighbours())
            .First(p => !p.IsAdjacentTo(colony.Position) && p != colony.Position && FreeLand(game, p));
        scout.Position = far;

        MoveCheck check = game.CheckSpyOnColony(scout, colony.Position);

        Assert.False(check.Allowed);
        Assert.Contains("next to the colony", check.Reason);
    }

    [Fact]
    public void Check_RejectsWithNoMovementLeft()
    {
        (Game game, Colony colony, Unit scout, _) = StageScoutBesideRivalColony();
        scout.MovementLeft = 0;
        Assert.False(game.CheckSpyOnColony(scout, colony.Position).Allowed);
    }

    // ---- Command (SpyOnColony) ----

    [Fact]
    public void Spy_AlwaysRevealsTheColonyInterior()
    {
        (Game game, Colony colony, Unit scout, int foreignId) = StageScoutBesideRivalColony();
        colony.AddBuilding("model.building.townHall");
        colony.AddGoods("model.goods.muskets", 42);

        // FreeCol's spySettlement never fails — the spy always succeeds (no RNG drawn).
        SpyResult result = game.SpyOnColony(game.HumanPlayer, scout, colony.Position);

        Assert.Equal(colony.Id, result.Snapshot.ColonyId);
        Assert.Equal(foreignId, result.Snapshot.OwnerId);
        Assert.Equal(colony.Population, result.Snapshot.Population);
        Assert.Contains("model.building.townHall", result.Snapshot.Buildings);
        Assert.Equal(42, result.Snapshot.Stores["model.goods.muskets"]);
    }

    [Fact]
    public void Spy_EndsTheScoutsTurn_AndRevealsTheColonyTile()
    {
        (Game game, Colony colony, Unit scout, _) = StageScoutBesideRivalColony();

        game.SpyOnColony(game.HumanPlayer, scout, colony.Position);

        Assert.Equal(0, scout.MovementLeft);              // the spy ends the turn (FreeCol setMovesLeft(0))
        Assert.True(game.IsExplored(colony.Position));    // the scout learns the colony's tile
    }

    [Fact]
    public void Spy_SnapshotIsADefensiveCopy_NotALiveLink()
    {
        (Game game, Colony colony, Unit scout, _) = StageScoutBesideRivalColony();
        colony.AddGoods("model.goods.food", 10);

        SpyResult result = game.SpyOnColony(game.HumanPlayer, scout, colony.Position);
        int seen = result.Snapshot.Stores["model.goods.food"];

        colony.AddGoods("model.goods.food", 100); // the colony changes after the glimpse

        Assert.Equal(seen, result.Snapshot.Stores["model.goods.food"]); // the snapshot is frozen at the spy instant
    }

    [Fact]
    public void Spy_DoesNotDrawTheHumansStream0()
    {
        // The spy draws no randomness at all (FreeCol-exact), so the economy stream 0 is never shifted — a default
        // game stays byte-identical (ADR-009).
        (Game game, Colony colony, Unit scout, _) = StageScoutBesideRivalColony();
        RandomState before = game.RandomState;

        game.SpyOnColony(scout, colony.Position); // the public human entry point

        Assert.Equal(before, game.RandomState); // stream 0 untouched
    }
}
