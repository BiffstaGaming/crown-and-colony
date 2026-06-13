using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Roles/equipment and unit-change data (Phase 5 slice 5b), pinned to the classic FreeCol spec
/// (<c>&lt;roles&gt;</c> and <c>&lt;unit-change-types&gt;</c> in specification.xml).
/// </summary>
public class RoleTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    // ---- Role parsing ----

    [Fact]
    public void Soldier_HasMusketsAndPlusTwoOffence()
    {
        RoleType soldier = Classic.Role("model.role.soldier");
        Assert.Equal(2, soldier.Offence, 5);
        Assert.Equal(1, soldier.Defence, 5);
        Assert.Equal(1, soldier.MaximumCount);
        Assert.Null(soldier.Downgrade); // disarms straight to the default role
        Assert.False(soldier.DisposeOnCombatLoss);
        Assert.False(soldier.RequiresNative);
        RoleRequiredGoods muskets = Assert.Single(soldier.RequiredGoods);
        Assert.Equal("model.goods.muskets", muskets.GoodsId);
        Assert.Equal(50, muskets.Amount);
    }

    [Fact]
    public void Dragoon_AddsHorsesAndDowngradesToSoldier()
    {
        RoleType dragoon = Classic.Role("model.role.dragoon");
        Assert.Equal(3, dragoon.Offence, 5);
        Assert.Equal(2, dragoon.Defence, 5);
        Assert.Equal("model.role.soldier", dragoon.Downgrade);
        Assert.Equal(
            new[] { ("model.goods.muskets", 50), ("model.goods.horses", 50) },
            dragoon.RequiredGoods.Select(g => (g.GoodsId, g.Amount)));
    }

    [Fact]
    public void Scout_IsDoomedOnCombatLoss()
    {
        RoleType scout = Classic.Role("model.role.scout");
        Assert.Equal(1, scout.Offence, 5);
        Assert.True(scout.DisposeOnCombatLoss);
        Assert.Null(scout.Downgrade);
    }

    [Theory]
    [InlineData("model.role.armedBrave", "model.goods.muskets", 25, 2, 1)]
    [InlineData("model.role.mountedBrave", "model.goods.horses", 25, 1, 1)]
    public void NativeRoles_AreNativeOnly_WithCheaperEquipment(
        string id, string goodsId, int amount, double offence, double defence)
    {
        RoleType role = Classic.Role(id);
        Assert.True(role.RequiresNative);
        Assert.True(role.DisposeOnCombatLoss);
        Assert.Equal(offence, role.Offence, 5);
        Assert.Equal(defence, role.Defence, 5);
        Assert.Equal(amount, role.RequiredGoods.Single(g => g.GoodsId == goodsId).Amount);
    }

    [Fact]
    public void DefaultRole_IsUnarmed()
    {
        RoleType def = Classic.Role(RoleType.DefaultRoleId);
        Assert.Equal(0, def.Offence, 5);
        Assert.Equal(0, def.Defence, 5);
        Assert.Empty(def.RequiredGoods);
        Assert.False(def.IsOffensive);
    }

    // ---- Equipment capture (role-change) ----

    [Theory]
    [InlineData("model.role.soldier", "model.role.armedBrave")]   // a native takes captured muskets
    [InlineData("model.role.dragoon", "model.role.mountedBrave")] // ... and captured horses
    public void NativeVictor_CapturesEquipmentIntoItsRole(string loserRole, string expectedWinnerRole)
    {
        RoleType? captured = Classic.CaptureRole(RoleType.DefaultRoleId, loserRole, winnerIsNative: true);
        Assert.Equal(expectedWinnerRole, captured?.Id);
    }

    [Fact]
    public void NonNativeVictor_HasNoNativeCaptureRole()
    {
        // No colonial role captures a soldier's muskets from the default role (only native braves do).
        Assert.Null(Classic.CaptureRole(RoleType.DefaultRoleId, "model.role.soldier", winnerIsNative: false));
    }

    // ---- Unit-change parsing ----

    [Theory]
    [InlineData("model.unit.pettyCriminal", "model.unit.indenturedServant")]
    [InlineData("model.unit.indenturedServant", "model.unit.freeColonist")]
    [InlineData("model.unit.freeColonist", "model.unit.veteranSoldier")]
    [InlineData("model.unit.veteranSoldier", "model.unit.colonialRegular")]
    public void Promotion_AdvancesTheLadder(string from, string to)
    {
        UnitChange? change = Classic.GetUnitChange(UnitChangeTypeIds.Promotion, from);
        Assert.Equal(to, change?.To);
        Assert.Equal(100, change?.Probability);
    }

    [Fact]
    public void Promotion_StopsAtColonialRegular()
    {
        Assert.Null(Classic.GetUnitChange(UnitChangeTypeIds.Promotion, "model.unit.colonialRegular"));
        Assert.Null(Classic.GetUnitChange(UnitChangeTypeIds.Promotion, "model.unit.artillery"));
    }

    [Theory]
    [InlineData("model.unit.veteranSoldier", "model.unit.freeColonist")]
    [InlineData("model.unit.artillery", "model.unit.damagedArtillery")]
    [InlineData("model.unit.colonialRegular", "model.unit.veteranSoldier")]
    public void Demotion_StepsDown(string from, string to) =>
        Assert.Equal(to, Classic.GetUnitChange(UnitChangeTypeIds.Demotion, from)?.To);

    [Fact]
    public void Capture_DowngradesVeteranButLeavesColonistsAsTheyAre()
    {
        Assert.Equal("model.unit.freeColonist",
            Classic.GetUnitChange(UnitChangeTypeIds.Capture, "model.unit.veteranSoldier")?.To);
        // A free colonist has no capture row → captured as the same type (owner change only).
        Assert.Null(Classic.GetUnitChange(UnitChangeTypeIds.Capture, "model.unit.freeColonist"));
    }

    // ---- Unit-type combat abilities ----

    [Fact]
    public void CombatAbilities_MatchSpec()
    {
        UnitType brave = Classic.Unit("model.unit.brave");
        Assert.True(brave.DisposeOnCombatLoss);
        Assert.True(brave.CaptureEquipment);
        Assert.False(brave.CaptureUnits);
        Assert.False(brave.CanBeCaptured);

        Assert.True(Classic.Unit("model.unit.freeColonist").CanBeCaptured);
        Assert.True(Classic.Unit("model.unit.artillery").Bombard);
        Assert.False(Classic.Unit("model.unit.artillery").DisposeOnCombatLoss);
    }
}
