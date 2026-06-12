using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Persistence;

public class SaveGameTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void JsonRoundTrip_PreservesEverything()
    {
        var game = Game.New(Classic, seed: 99);
        Unit unit = game.Units[0];
        game.MoveUnit(unit, AdjacentLand(game, unit.Position));
        game.EndTurn();

        string json = SaveGame.From(game).ToJson();
        Game loaded = SaveGame.FromJson(json).Restore(Classic);

        Assert.Equal(game.Turn, loaded.Turn);
        Assert.Equal(game.Map.Width, loaded.Map.Width);
        Assert.Equal(game.Map.Height, loaded.Map.Height);
        Assert.Equal(
            game.Map.AllPositions().Select(p => game.Map.TerrainAt(p).Id),
            loaded.Map.AllPositions().Select(p => loaded.Map.TerrainAt(p).Id));
        Assert.Equal(game.Units.Count, loaded.Units.Count);
        Assert.Equal(game.Units[0].Id, loaded.Units[0].Id);
        Assert.Equal(game.Units[0].Type.Id, loaded.Units[0].Type.Id);
        Assert.Equal(game.Units[0].Position, loaded.Units[0].Position);
        Assert.Equal(game.Units[0].MovementLeft, loaded.Units[0].MovementLeft);
    }

    [Fact]
    public void RoundTrip_PreservesExploredTilesExactly()
    {
        var game = Game.New(Classic, seed: 99);
        game.MoveUnit(game.Units[0], AdjacentLand(game, game.Units[0].Position));

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(
            game.Explored.OrderBy(p => (p.Y, p.X)),
            loaded.Explored.OrderBy(p => (p.Y, p.X)));
    }

    [Fact]
    public void LoadedGame_ContinuesIdenticalRandomSequence()
    {
        var original = Game.New(Classic, seed: 7);
        string json = SaveGame.From(original).ToJson();
        Game loaded = SaveGame.FromJson(json).Restore(Classic);

        Assert.Equal(original.RandomState, loaded.RandomState);
    }

    [Fact]
    public void V1Save_WithoutFogOrUnitTypes_LoadsWithDefaults()
    {
        // Format v1 (Phase 1) had no Explored list and no unit TypeId. Loading
        // must still work: units default to free colonists, fog reveals around them.
        var game = Game.New(Classic, seed: 5);
        SaveGame v1 = SaveGame.From(game) with
        {
            Version = 1,
            Explored = null,
            Units = game.Units.Select(u =>
                new SavedUnit(u.Id, null, u.Position.X, u.Position.Y, u.MovementLeft)).ToList(),
        };

        Game loaded = SaveGame.FromJson(v1.ToJson()).Restore(Classic);

        Assert.Equal(Game.StartingUnitTypeId, loaded.Units[0].Type.Id);
        Assert.True(loaded.IsExplored(loaded.Units[0].Position));
        Assert.InRange(loaded.Explored.Count, 4, 9);
    }

    [Fact]
    public void Load_WithUnknownTerrainId_Throws()
    {
        var game = Game.New(Classic, seed: 1);
        SaveGame save = SaveGame.From(game);
        SaveGame corrupted = save with
        {
            Terrain = save.Terrain.Select((id, i) => i == 0 ? "model.tile.atlantis" : id).ToList(),
        };

        Assert.Throws<KeyNotFoundException>(() => corrupted.Restore(Classic));
    }

    private static Position AdjacentLand(Game game, Position from) =>
        from.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
}
