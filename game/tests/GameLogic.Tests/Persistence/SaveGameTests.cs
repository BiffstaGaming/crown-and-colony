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
        Assert.Equal(game.Units[0].Position, loaded.Units[0].Position);
        Assert.Equal(game.Units[0].MovementLeft, loaded.Units[0].MovementLeft);
    }

    [Fact]
    public void LoadedGame_ContinuesIdenticalRandomSequence()
    {
        // The determinism contract end-to-end (ADR-009): a saved-and-loaded game
        // must behave identically to the original from the save point onward.
        var original = Game.New(Classic, seed: 7);
        string json = SaveGame.From(original).ToJson();
        Game loaded = SaveGame.FromJson(json).Restore(Classic);

        Assert.Equal(original.RandomState, loaded.RandomState);
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
