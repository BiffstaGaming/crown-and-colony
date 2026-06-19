using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Equipping a missionary (<c>86d3c9p3p</c>): the <c>model.role.missionary</c> role requires the colony to provide
/// <c>model.ability.dressMissionary</c>, which a <b>church</b> or <b>cathedral</b> grants (a chapel does not). So a
/// colonist can be ordained a missionary only at a colony with a church/cathedral — the player-facing entry point to
/// the missions system (then establish a mission + harvest converts, see <see cref="NativeMissionTests"/>).
/// </summary>
public class MissionaryEquipTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Missionary = "model.role.missionary";
    private const string Church = "model.building.church";
    private const string Cathedral = "model.building.cathedral";
    private const string Chapel = "model.building.chapel";

    [Fact]
    public void Spec_ChurchAndCathedralDressMissionaries_ButTheChapelDoesNot()
    {
        Assert.True(Classic.Building(Church).DressesMissionary);
        Assert.True(Classic.Building(Cathedral).DressesMissionary); // inherits it down the extends chain
        Assert.False(Classic.Building(Chapel).DressesMissionary);   // the chapel only makes crosses
    }

    [Fact]
    public void Spec_TheMissionaryRole_RequiresDressMissionary()
    {
        Assert.True(Classic.Role(Missionary).RequiresDressMissionary);
        Assert.False(Classic.Role("model.role.soldier").RequiresDressMissionary); // an ordinary equip role does not
    }

    [Fact]
    public void EquipMissionary_IsRefused_AtAColonyWithoutAChurch()
    {
        (Game game, Colony colony, Unit colonist) = ColonistAtColony();
        Assert.DoesNotContain(Church, colony.Buildings);    // a fresh colony has a chapel, not a church
        Assert.DoesNotContain(Cathedral, colony.Buildings);
        Assert.False(game.CheckEquipRole(colonist, colony, Missionary).Allowed);
    }

    [Fact]
    public void EquipMissionary_IsAllowed_AndOrdainsTheColonist_AtAChurch()
    {
        (Game game, Colony colony, Unit colonist) = ColonistAtColony();
        colony.AddBuilding(Church);
        Assert.True(game.CheckEquipRole(colonist, colony, Missionary).Allowed);

        game.EquipRole(colonist, colony, Missionary);
        Assert.Equal(Missionary, colonist.RoleId); // now a missionary, ready to establish a mission
    }

    [Fact]
    public void EquipMissionary_IsAllowed_AtACathedralToo()
    {
        (Game game, Colony colony, Unit colonist) = ColonistAtColony();
        colony.AddBuilding(Cathedral);
        Assert.True(game.CheckEquipRole(colonist, colony, Missionary).Allowed);
    }

    [Fact]
    public void EquipMissionary_ConsumesNoGoods()
    {
        (Game game, Colony colony, Unit colonist) = ColonistAtColony();
        colony.AddBuilding(Church);
        int storesBefore = colony.Stores.Values.Sum();

        game.EquipRole(colonist, colony, Missionary);
        Assert.Equal(storesBefore, colony.Stores.Values.Sum()); // the missionary role needs no muskets/horses/tools
    }

    /// <summary>A fresh human colony with a free colonist standing on its tile (ready to be equipped).</summary>
    private static (Game Game, Colony Colony, Unit Colonist) ColonistAtColony()
    {
        Game game = Game.New(Classic, seed: 42);
        Colony colony = game.FoundColony(game.Units[0]);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position);
        return (game, colony, colonist);
    }
}
