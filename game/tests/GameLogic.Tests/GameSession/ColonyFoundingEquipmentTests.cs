using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Founding or joining a colony with an EQUIPPED unit returns its role goods to the colony's warehouse rather than
/// destroying them — a soldier's muskets, a dragoon's muskets+horses, a pioneer's tools — mirroring FreeCol
/// <c>InGameController.joinColony</c> → <c>colony.equipForRole(unit, defaultRole, 0)</c>. Regression for the
/// "muskets vanish when I found/join with a soldier" bug.
/// </summary>
public class ColonyFoundingEquipmentTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Muskets = "model.goods.muskets";
    private const string Horses = "model.goods.horses";

    private static Unit Foundable(Game game) =>
        game.PlayerUnits.First(u => game.CheckFoundColony(u).Allowed);

    private static int RoleGoods(string roleId, string goodsId) =>
        Classic.Role(roleId).RequiredGoods.Where(g => g.GoodsId == goodsId).Sum(g => g.Amount);

    [Fact]
    public void FoundingWithASoldier_BanksTheMusketsInTheNewColony()
    {
        var game = Game.New(Classic, Seed);
        Unit unit = Foundable(game);
        unit.RoleId = "model.role.soldier";
        unit.RoleCount = 1;
        int expected = RoleGoods("model.role.soldier", Muskets);

        Colony colony = game.FoundColony(unit);

        Assert.True(expected > 0); // sanity: the soldier role really does require muskets
        Assert.Equal(expected, colony.StoreOf(Muskets)); // returned to the warehouse, not destroyed
    }

    [Fact]
    public void FoundingWithADragoon_BanksMusketsAndHorses()
    {
        var game = Game.New(Classic, Seed);
        Unit unit = Foundable(game);
        unit.RoleId = "model.role.dragoon";
        unit.RoleCount = 1;

        Colony colony = game.FoundColony(unit);

        Assert.Equal(RoleGoods("model.role.dragoon", Muskets), colony.StoreOf(Muskets));
        Assert.Equal(RoleGoods("model.role.dragoon", Horses), colony.StoreOf(Horses));
    }

    [Fact]
    public void FoundingWithAFreeColonist_AddsNoEquipmentGoods()
    {
        var game = Game.New(Classic, Seed);
        Unit unit = Foundable(game);

        Colony colony = game.FoundColony(unit);

        Assert.Equal(0, colony.StoreOf(Muskets)); // an unequipped founder banks nothing
    }

    [Fact]
    public void JoiningWithASoldier_BanksTheMusketsInTheColony()
    {
        var game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(Foundable(game));

        // A second colonist, armed as a soldier, joins the existing colony.
        Unit soldier = game.PlayerUnits.First(u => u.IsOnMap && u.Type.IsPerson);
        soldier.Position = colony.Position; // stand at the colony so it may join
        soldier.RoleId = "model.role.soldier";
        soldier.RoleCount = 1;
        int before = colony.StoreOf(Muskets);

        game.JoinColony(soldier, colony);

        Assert.Equal(before + RoleGoods("model.role.soldier", Muskets), colony.StoreOf(Muskets));
    }
}
