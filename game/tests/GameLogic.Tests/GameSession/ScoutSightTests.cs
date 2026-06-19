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

    // ── Hernando de Soto: +1 sight for every land unit (86d3...) ────────────────────────────────────────────────

    private const string DeSoto = "model.foundingFather.hernandoDeSoto";

    [Fact]
    public void Spec_DeSoto_GrantsAnAdditiveLineOfSightBonus()
    {
        FatherModifier los = Classic.Father(DeSoto).Modifiers
            .Single(m => m.TargetId == "model.modifier.lineOfSightBonus");
        Assert.Equal(ModifierType.Additive, los.Type);
        Assert.Equal(1, (int)los.Value);
    }

    [Fact]
    public void DeSoto_GivesAPlainLandUnitAnExtraTileOfSight()
    {
        Game game = EmptyPlainsMap();
        game.SpawnUnit(Classic.Unit(FreeColonist), new Position(2, 2)); // default role, sight 1
        var twoAway = new Position(4, 2);                               // Chebyshev distance 2
        Assert.False(game.IsVisible(twoAway));                          // sight 1 → not yet visible

        game.HumanPlayer.CongressList.Add(DeSoto);                      // elect Hernando de Soto
        Assert.True(game.IsVisible(twoAway));                           // +1 sight → the 2-away tile is revealed
    }

    [Fact]
    public void DeSoto_DoesNotExtendANavalUnitsSight()
    {
        Game game = EmptyOceanMap();
        game.SpawnUnit(Classic.Unit("model.unit.caravel"), new Position(2, 2)); // a ship, sight 1
        Assert.True(game.IsVisible(new Position(3, 2)));   // sees one tile out…
        var twoAway = new Position(4, 2);
        Assert.False(game.IsVisible(twoAway));             // …but not two

        game.HumanPlayer.CongressList.Add(DeSoto);
        Assert.False(game.IsVisible(twoAway));             // de Soto's bonus is land-only (navalUnit=false) → unchanged
    }

    private static Game EmptyPlainsMap() => EmptyMap("model.tile.plains");
    private static Game EmptyOceanMap() => EmptyMap("model.tile.ocean");

    private static Game EmptyMap(string terrainId) =>
        new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 5,
            MapHeight = 5,
            Terrain = [.. Enumerable.Repeat(terrainId, 25)],
            Units = [],
            Explored = [],
        }.Restore(Classic);
}
