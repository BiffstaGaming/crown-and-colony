using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The <see cref="Game.IsMilitaryUnit"/> classifier behind the unit report's Military group (FreeCol
/// <c>Unit.isOffensiveUnit</c>, faithful subset): a non-naval unit with an offensive type or role.
/// </summary>
public class UnitCategoryTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static Game Fixture() => new SaveGame
    {
        Turn = 1,
        RandomStateValue = 1,
        RandomIncrement = 1,
        MapWidth = 3,
        MapHeight = 1,
        Terrain = ["model.tile.plains", "model.tile.plains", "model.tile.ocean"],
        Explored = [0, 1, 2],
        Units =
        [
            new SavedUnit(1, "model.unit.freeColonist", 0, 0, 1),                                      // plain colonist
            new SavedUnit(2, "model.unit.freeColonist", 1, 0, 1, Role: "model.role.soldier", RoleCount: 50), // armed
            new SavedUnit(3, "model.unit.artillery", 0, 0, 1),                                          // offensive type
            new SavedUnit(4, "model.unit.caravel", 2, 0, 5),                                            // naval
        ],
        Colonies = [],
    }.Restore(Classic);

    [Fact]
    public void IsMilitaryUnit_CountsOffensiveTypeOrRole_ExcludesNavalAndPlainColonists()
    {
        Game game = Fixture();
        Unit colonist = First(game, 1);
        Unit soldier = First(game, 2);
        Unit artillery = First(game, 3);
        Unit ship = First(game, 4);

        Assert.False(game.IsMilitaryUnit(colonist)); // default role, no offence → labour, not military
        Assert.True(game.IsMilitaryUnit(soldier));   // soldier role is offensive
        Assert.True(game.IsMilitaryUnit(artillery)); // type offence > 0
        Assert.False(game.IsMilitaryUnit(ship));     // naval units never count as military
    }

    private static Unit First(Game game, int id)
    {
        foreach (Unit u in game.Units)
        {
            if (u.Id == id)
            {
                return u;
            }
        }
        throw new System.InvalidOperationException($"unit {id} not found");
    }
}
