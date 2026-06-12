using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

public class GameTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void NewGame_StartsAtTurn1_WithOneUnitOnSettleableLand()
    {
        var game = Game.New(Classic, seed: 42);

        Assert.Equal(1, game.Turn);
        Unit unit = Assert.Single(game.Units);

        TerrainType startTerrain = game.Map.TerrainAt(unit.Position);
        Assert.False(startTerrain.IsWater);
        Assert.True(startTerrain.CanSettle);
        Assert.Equal(Unit.BaseMovementPoints, unit.MovementLeft);
    }

    [Fact]
    public void MoveUnit_ToAdjacentLand_MovesAndSpendsPoints()
    {
        var game = Game.New(Classic, seed: 42);
        Unit unit = game.Units[0];
        Position target = AdjacentLand(game, unit.Position);
        int cost = game.Map.TerrainAt(target).MoveCost;

        game.MoveUnit(unit, target);

        Assert.Equal(target, unit.Position);
        Assert.Equal(Math.Max(0, Unit.BaseMovementPoints - cost), unit.MovementLeft);
    }

    [Fact]
    public void MoveUnit_RejectsNonAdjacent_Water_OffMap_AndExhausted()
    {
        var game = Game.New(Classic, seed: 42);
        Unit unit = game.Units[0];

        // Non-adjacent.
        Assert.False(game.CheckMove(unit, new Position(unit.Position.X + 3, unit.Position.Y)).Allowed);

        // Off-map.
        Assert.False(game.CheckMove(unit, new Position(-1, -1)).Allowed);

        // Water: the generated map's border is ocean — walk a probe to confirm rejection logic
        // using any adjacent water tile if one exists from a coastal position; at minimum the
        // rule itself is exercised via a hand-built map below.

        // Exhausted: drain movement, then any move is rejected.
        unit.MovementLeft = 0;
        Position target = AdjacentLand(game, unit.Position);
        MoveCheck check = game.CheckMove(unit, target);
        Assert.False(check.Allowed);
        Assert.Throws<InvalidMoveException>(() => game.MoveUnit(unit, target));
    }

    [Fact]
    public void MoveUnit_RejectsWater_OnHandBuiltCoast()
    {
        TerrainType plains = Classic.Terrain("model.tile.plains");
        TerrainType ocean = Classic.Terrain("model.tile.ocean");
        // 2x1 map: land next to water.
        var map = new GameMap(2, 1, [plains, ocean]);
        var game = RestoreOnMap(map, units: [(1, new Position(0, 0), Unit.BaseMovementPoints)]);
        Unit unit = game.Units[0];

        MoveCheck check = game.CheckMove(unit, new Position(1, 0));

        Assert.False(check.Allowed);
        Assert.Contains("water", check.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndTurn_AdvancesTurn_AndRestoresMovement()
    {
        var game = Game.New(Classic, seed: 42);
        Unit unit = game.Units[0];
        game.MoveUnit(unit, AdjacentLand(game, unit.Position));
        Assert.True(unit.MovementLeft < Unit.BaseMovementPoints);

        game.EndTurn();

        Assert.Equal(2, game.Turn);
        Assert.Equal(Unit.BaseMovementPoints, unit.MovementLeft);
    }

    [Fact]
    public void SpawnUnit_RejectsWaterAndOffMap()
    {
        var game = Game.New(Classic, seed: 42);

        Assert.Throws<InvalidMoveException>(() => game.SpawnUnit(new Position(0, 0))); // ocean border
        Assert.Throws<ArgumentOutOfRangeException>(() => game.SpawnUnit(new Position(-1, 0)));
    }

    private static Position AdjacentLand(Game game, Position from) =>
        from.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);

    private static Game RestoreOnMap(GameMap map, (int, Position, int)[] units)
    {
        // Build a game on a hand-crafted map via the save/restore path.
        var probe = Game.New(Classic, seed: 1);
        var save = CrownAndColony.GameLogic.Persistence.SaveGame.From(probe) with
        {
            MapWidth = map.Width,
            MapHeight = map.Height,
            Terrain = map.AllPositions().Select(p => map.TerrainAt(p).Id).ToList(),
            Units = units.Select(u => new CrownAndColony.GameLogic.Persistence.SavedUnit(
                u.Item1, u.Item2.X, u.Item2.Y, u.Item3)).ToList(),
        };
        return save.Restore(Classic);
    }
}
