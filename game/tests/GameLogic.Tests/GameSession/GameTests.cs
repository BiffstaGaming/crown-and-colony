using System.Text;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

public class GameTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void NewGame_StartsAtTurn1_WithAColonistOnSettleableLand()
    {
        var game = Game.New(Classic, seed: 42);

        Assert.Equal(1, game.Turn);
        Unit unit = Assert.Single(game.Units);
        Assert.Equal(Game.StartingUnitTypeId, unit.Type.Id);

        TerrainType startTerrain = game.Map.TerrainAt(unit.Position);
        Assert.False(startTerrain.IsWater);
        Assert.True(startTerrain.CanSettle);
        Assert.Equal(unit.Type.Movement, unit.MovementLeft);
    }

    [Fact]
    public void MoveUnit_ToAdjacentLand_MovesAndSpendsPoints()
    {
        var game = Game.New(Classic, seed: 42);
        Unit unit = game.Units[0];
        Position target = AdjacentLand(game, unit.Position);
        int terrainCost = game.Map.TerrainAt(target).MoveCost;

        game.MoveUnit(unit, target);

        Assert.Equal(target, unit.Position);
        // A 3-point colonist either pays the full cost or, when the terrain costs
        // more, spends everything (FreeCol partial-movement rule).
        Assert.Equal(Math.Max(0, unit.Type.Movement - terrainCost), unit.MovementLeft);
    }

    [Fact]
    public void MoveUnit_RejectsNonAdjacent_OffMap_AndExhausted()
    {
        var game = Game.New(Classic, seed: 42);
        Unit unit = game.Units[0];

        Assert.False(game.CheckMove(unit, new Position(unit.Position.X + 3, unit.Position.Y)).Allowed);
        Assert.False(game.CheckMove(unit, new Position(-1, -1)).Allowed);

        Position target = AdjacentLand(game, unit.Position);
        unit.MovementLeft = 0;
        MoveCheck check = game.CheckMove(unit, target);
        Assert.False(check.Allowed);
        Assert.Throws<InvalidMoveException>(() => game.MoveUnit(unit, target));
    }

    [Fact]
    public void LandUnit_RejectsWater_And_NavalUnit_RejectsLand()
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");
        TerrainType ocean = Classic.Terrain("model.tile.ocean");
        var map = new GameMap(2, 1, [plains, ocean]);

        // Colonist on land cannot enter the sea…
        Game game = RestoreOnMap(Classic, map,
            [new SavedUnit(1, "model.unit.freeColonist", 0, 0, 3)]);
        MoveCheck landCheck = game.CheckMove(game.Units[0], new Position(1, 0));
        Assert.False(landCheck.Allowed);
        Assert.Contains("water", landCheck.Reason, StringComparison.OrdinalIgnoreCase);

        // …and a caravel at sea cannot climb ashore.
        Game navalGame = RestoreOnMap(Classic, map,
            [new SavedUnit(1, "model.unit.caravel", 1, 0, 12)]);
        MoveCheck navalCheck = navalGame.CheckMove(navalGame.Units[0], new Position(0, 0));
        Assert.False(navalCheck.Allowed);
        Assert.Contains("land", navalCheck.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PartialMovement_SmallShortfall_AllowedForRemainder()
    {
        // FreeCol rule: cost <= movesLeft + 2 allows the move for all remaining
        // points. Caravel (12 MP) with 1 left entering ocean (cost 3): 3 <= 1+2.
        TerrainType ocean = Classic.Terrain("model.tile.ocean");
        var map = new GameMap(2, 1, [ocean, ocean]);
        Game game = RestoreOnMap(Classic, map, [new SavedUnit(1, "model.unit.caravel", 0, 0, 1)]);
        Unit caravel = game.Units[0];

        MoveCheck check = game.CheckMove(caravel, new Position(1, 0));

        Assert.True(check.Allowed);
        Assert.Equal(1, check.Cost); // spends the remainder, not the full 3
        game.MoveUnit(caravel, new Position(1, 0));
        Assert.Equal(0, caravel.MovementLeft);
    }

    [Fact]
    public void PartialMovement_BigShortfall_MidTurn_Rejected()
    {
        // The rejection branch needs terrain costing > movesLeft+2 and a unit
        // well below full movement — impossible with classic naval (max water
        // cost 3), so pin it with a synthetic ruleset: a 12-MP land unit at
        // 4 MP facing 9-cost mountains: 4+2 < 12 (not near-full) and 9 > 4+2.
        Ruleset rules = SyntheticRules();
        TerrainType flat = rules.Terrain("t.flat");
        TerrainType rough = rules.Terrain("t.rough");
        var map = new GameMap(2, 1, [flat, rough]);
        Game game = RestoreOnMap(rules, map, [new SavedUnit(1, "u.rider", 0, 0, 4)]);
        Unit rider = game.Units[0];

        MoveCheck check = game.CheckMove(rider, new Position(1, 0));

        Assert.False(check.Allowed);
        Assert.Contains("movement", check.Reason, StringComparison.OrdinalIgnoreCase);

        // But the same unit at (near-)full movement may overdraw:
        rider.MovementLeft = 10; // 10+2 >= 12 → near-full
        MoveCheck fullCheck = game.CheckMove(rider, new Position(1, 0));
        Assert.True(fullCheck.Allowed);
        Assert.Equal(9, fullCheck.Cost); // full cost fits anyway (9 <= 10)
    }

    [Fact]
    public void EndTurn_AdvancesTurn_AndRestoresMovement()
    {
        var game = Game.New(Classic, seed: 42);
        Unit unit = game.Units[0];
        game.MoveUnit(unit, AdjacentLand(game, unit.Position));
        Assert.True(unit.MovementLeft < unit.Type.Movement);

        game.EndTurn();

        Assert.Equal(2, game.Turn);
        Assert.Equal(unit.Type.Movement, unit.MovementLeft);
    }

    [Fact]
    public void SpawnUnit_RejectsMismatchedTerrainAndOffMap()
    {
        var game = Game.New(Classic, seed: 42);
        UnitType colonist = Classic.Unit(Game.StartingUnitTypeId);

        Assert.Throws<InvalidMoveException>(
            () => game.SpawnUnit(colonist, new Position(0, 0))); // high seas edge
        Assert.Throws<ArgumentOutOfRangeException>(
            () => game.SpawnUnit(colonist, new Position(-1, 0)));
    }

    [Fact]
    public void FogOfWar_NewGame_RevealsOnlyAroundStartingUnit()
    {
        var game = Game.New(Classic, seed: 42);
        Unit unit = game.Units[0];

        // Line of sight 1 → at most a 3×3 block is known.
        Assert.InRange(game.Explored.Count, 4, 9);
        Assert.True(game.IsExplored(unit.Position));
        Assert.True(game.Explored.Count < game.Map.Width * game.Map.Height);
    }

    [Fact]
    public void FogOfWar_Moving_RevealsNewGround()
    {
        var game = Game.New(Classic, seed: 42);
        Unit unit = game.Units[0];
        int before = game.Explored.Count;

        game.MoveUnit(unit, AdjacentLand(game, unit.Position));

        Assert.True(game.Explored.Count > before, "moving should reveal new tiles");
        Assert.True(game.IsExplored(unit.Position));
    }

    private static Position AdjacentLand(Game game, Position from) =>
        from.Neighbours().First(n => game.CheckMove(game.Units[0], n).Allowed);

    /// <summary>Builds a game on a hand-crafted map via the save/restore path.</summary>
    private static Game RestoreOnMap(Ruleset rules, GameMap map, SavedUnit[] units)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = map.Width,
            MapHeight = map.Height,
            Terrain = map.AllPositions().Select(p => map.TerrainAt(p).Id).ToList(),
            Units = units,
            Explored = [],
        };
        return save.Restore(rules);
    }

    /// <summary>Minimal ruleset: flat (cost 3) + rough (cost 9) terrain, a 12-MP land unit.</summary>
    private static Ruleset SyntheticRules()
    {
        const string xml = """
            <freecol-specification id="test" version="0">
              <tile-types>
                <tile-type id="t.flat" basic-move-cost="3" basic-work-turns="3"/>
                <tile-type id="t.rough" basic-move-cost="9" basic-work-turns="7"/>
              </tile-types>
              <unit-types>
                <unit-type id="u.rider" movement="12" line-of-sight="1"/>
              </unit-types>
            </freecol-specification>
            """;
        return Ruleset.Load(new MemoryStream(Encoding.UTF8.GetBytes(xml)));
    }
}
