using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Pioneer multi-load equipping (<c>86d3fpx80</c>): the pioneer role's <c>maximum-count="5"</c> means a freshly
/// equipped pioneer takes up to five tool-units (5 × 20 = 100 tools) — the highest count the colony's tool stock
/// affords, stepping down through 4/3/2/1 when it holds less. Mirrors FreeCol's <c>QuickActionMenu</c> equip loop
/// (<c>for (count = maximum-count; count &gt; 0; count--)</c> → first affordable count) feeding
/// <c>Settlement.equipForRole(unit, role, count)</c>.
/// </summary>
public class PioneerEquipTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Pioneer = "model.role.pioneer";
    private const string Tools = "model.goods.tools";

    private static Game GameWithColony(out Colony colony, out Unit colonist)
    {
        var game = Game.New(Classic, seed: 99);
        colony = game.FoundColony(game.PlayerUnits.First());
        colonist = game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position);
        return game;
    }

    /// <summary>Sets the colony's tool stock to exactly <paramref name="amount"/> (a fresh colony founds with some tools).</summary>
    private static void SetTools(Colony colony, int amount)
    {
        colony.AddGoods(Tools, -colony.StoreOf(Tools)); // zero it
        colony.AddGoods(Tools, amount);
    }

    [Fact]
    public void Spec_PioneerRoleMaxCountIsFive_TwentyToolsPerCount()
    {
        RoleType pioneer = Classic.Role(Pioneer);
        Assert.Equal(5, pioneer.MaximumCount);
        Assert.Equal(20, pioneer.RequiredGoods.Single(g => g.GoodsId == Tools).Amount);
    }

    [Fact]
    public void EquipPioneer_FromAFullyStockedColony_TakesAllFiveToolUnits()
    {
        Game game = GameWithColony(out Colony colony, out Unit colonist);
        SetTools(colony, 100); // five counts' worth

        game.EquipRole(colonist, colony, Pioneer);

        Assert.Equal(Pioneer, colonist.RoleId);
        Assert.Equal(5, colonist.RoleCount);   // armed to the role maximum
        Assert.Equal(0, colony.StoreOf(Tools)); // 5 × 20 = 100 tools consumed
    }

    [Theory] // the colony holds only enough for a lower count → the equip steps down to it
    [InlineData(20, 1, 0)]
    [InlineData(40, 2, 0)]
    [InlineData(60, 3, 0)]
    [InlineData(80, 4, 0)]
    [InlineData(90, 4, 10)]   // 90 tools → 4 counts (80), 10 left over (not enough for a 5th)
    [InlineData(140, 5, 40)]  // more than the maximum → caps at 5 counts (100), 40 left
    public void EquipPioneer_TakesTheHighestAffordableCount(int stocked, int expectedCount, int expectedLeft)
    {
        Game game = GameWithColony(out Colony colony, out Unit colonist);
        SetTools(colony, stocked);

        game.EquipRole(colonist, colony, Pioneer);

        Assert.Equal(expectedCount, colonist.RoleCount);
        Assert.Equal(expectedLeft, colony.StoreOf(Tools));
    }

    [Fact]
    public void DisarmingAPioneer_BanksAllItsToolsBackToTheColony()
    {
        Game game = GameWithColony(out Colony colony, out Unit colonist);
        SetTools(colony, 100);
        game.EquipRole(colonist, colony, Pioneer); // count 5, store now 0

        game.EquipRole(colonist, colony, RoleType.DefaultRoleId); // disarm

        Assert.Equal(RoleType.DefaultRoleId, colonist.RoleId);
        Assert.Equal(0, colonist.RoleCount);
        Assert.Equal(100, colony.StoreOf(Tools)); // all five counts' tools refunded
    }
}
