using System;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Colony lifecycle commands (<c>86d3c9nzc</c>): rename a colony, and abandon it — the last colonist walks out
/// as a free colonist and the colony is removed (FreeCol <c>renameObject</c> / <c>abandonSettlement</c>). A
/// fortified colony (stockade/fort/fortress) or one with more than one colonist cannot be abandoned.
/// </summary>
public class ColonyLifecycleTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Stockade = "model.building.stockade";

    private static (Game game, Colony colony) FoundedColony(ulong seed = 424242)
    {
        var game = Game.New(Classic, seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First());
        return (game, colony);
    }

    // ---- Rename ----

    [Fact]
    public void RenameColony_SetsTheName_AndRoundTrips()
    {
        (Game game, Colony colony) = FoundedColony();

        game.RenameColony(colony, "  New Plymouth  ");
        Assert.Equal("New Plymouth", colony.Name); // trimmed

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal("New Plymouth", restored.Colonies.Single(c => c.Id == colony.Id).Name);
    }

    [Fact]
    public void RenameColony_RejectsABlankName()
    {
        (Game game, Colony colony) = FoundedColony();
        string original = colony.Name;

        Assert.Throws<ArgumentException>(() => game.RenameColony(colony, "   "));
        Assert.Equal(original, colony.Name); // unchanged
    }

    // ---- Abandon ----

    [Fact]
    public void AbandonColony_RemovesTheColony_AndFreesItsLastColonist()
    {
        (Game game, Colony colony) = FoundedColony();
        Position where = colony.Position;
        Assert.True(game.CheckAbandonColony(colony).Allowed);

        Unit freed = game.AbandonColony(colony);

        Assert.DoesNotContain(colony, game.Colonies);          // the colony is gone
        Assert.Equal(where, freed.Position);                   // the colonist stands where it was
        Assert.Equal("model.unit.freeColonist", freed.Type.Id);
        Assert.Contains(freed, game.PlayerUnits);
    }

    [Fact]
    public void AbandonColony_RejectedWhileMoreThanOneColonistRemains()
    {
        Game game = TwoColonistColony(out Colony colony);

        MoveCheck check = game.CheckAbandonColony(colony);

        Assert.False(check.Allowed); // send the others out first
        Assert.Throws<InvalidMoveException>(() => game.AbandonColony(colony));
        Assert.Contains(colony, game.Colonies);
    }

    [Fact]
    public void AbandonColony_RejectedForAFortifiedColony()
    {
        (Game game, Colony colony) = FoundedColony();
        colony.AddBuilding(Stockade); // a wall blocks abandonment
        Assert.True(game.ColonyDefenceBonus(colony) > 0);

        MoveCheck check = game.CheckAbandonColony(colony);

        Assert.False(check.Allowed);
        Assert.Throws<InvalidMoveException>(() => game.AbandonColony(colony));
    }

    [Fact]
    public void AbandonedColony_IsAbsentAfterSaveLoad()
    {
        (Game game, Colony colony) = FoundedColony();
        int colonyId = colony.Id;
        Unit freed = game.AbandonColony(colony);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.DoesNotContain(restored.Colonies, c => c.Id == colonyId);
        Assert.Contains(restored.Units, u => u.Id == freed.Id); // the freed colonist persists
    }

    /// <summary>A pop-2 colony on a 1×1 plains map (no fortification).</summary>
    private static Game TwoColonistColony(out Colony colony)
    {
        var game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [0],
            Colonies = [new SavedColony(1, "Twoville", 0, 0, 2)],
        }.Restore(Classic);
        colony = game.Colonies[0];
        return game;
    }
}
