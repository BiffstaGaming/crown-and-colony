using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Scout line-of-sight (<c>86d3c9upk</c>, FreeCol <c>model.modifier.lineOfSightBonus</c>): the scout role grants
/// +1 sight radius, folded into the fog reveal so a scout sees one tile further than a plain colonist.
/// </summary>
public class ScoutSightTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Scout = "model.role.scout";

    [Fact]
    public void TheScoutRole_GrantsAOneTileSightBonus_TheDefaultRoleNone()
    {
        Assert.Equal(1, Classic.Role(Scout).LineOfSightBonus);
        Assert.Equal(0, Classic.Role(RoleType.DefaultRoleId).LineOfSightBonus);
    }

    [Fact]
    public void AScout_SeesOneTileFurtherThanAPlainColonist()
    {
        Game game = EmptyPlainsMap();
        Unit unit = game.SpawnUnit(Classic.Unit(FreeColonist), new Position(2, 2)); // base line-of-sight 1
        var twoAway = new Position(4, 2); // Chebyshev distance 2 from the unit

        Assert.False(game.IsVisible(twoAway)); // a plain colonist (sight 1) can't see two tiles out

        unit.RoleId = Scout; // mount it as a scout (sight 1 + 1 = 2)
        unit.RoleCount = 1;
        Assert.True(game.IsVisible(twoAway)); // now the tile two away is in sight
        Assert.Contains(twoAway, game.CurrentlyVisible);
    }

    private static Game EmptyPlainsMap() =>
        new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 5,
            MapHeight = 5,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", 25)],
            Units = [],
            Explored = [],
        }.Restore(Classic);
}
