using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Combat;

/// <summary>
/// The combat foundation (Phase 5 slice 5a): offence/defence + terrain-defence data parsing, and the
/// pure power/odds/resolution model — pinned to the classic FreeCol spec and SimpleCombatModel.
/// </summary>
public class CombatModelTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    /// <summary>Deterministic RNG returning a fixed NextDouble, for resolution tests.</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    // ---- Data parsing (pinned to the spec) ----

    [Theory]
    [InlineData("model.unit.freeColonist", 0, 1)]
    [InlineData("model.unit.brave", 1, 1)]
    [InlineData("model.unit.artillery", 7, 5)]
    [InlineData("model.unit.kingsRegular", 4, 5)] // base 0/1 + additive 4
    public void UnitCombatValues_MatchSpec(string id, double offence, double defence)
    {
        UnitType unit = Classic.Unit(id);
        Assert.Equal(offence, unit.Offence, 5);
        Assert.Equal(defence, unit.Defence, 5);
    }

    [Theory]
    [InlineData("model.unit.caravel", 6)]
    [InlineData("model.unit.frigate", 6)]
    [InlineData("model.unit.privateer", 6)]
    [InlineData("model.unit.galleon", 6)]
    public void ShipHitPoints_MatchSpec(string id, int hitPoints)
    {
        // Drives ship-repair time (MaxHitPoints − 1 turns); every classic ship is 6 → a 5-turn repair.
        Assert.Equal(hitPoints, Classic.Unit(id).MaxHitPoints);
    }

    [Theory]
    [InlineData("model.unit.frigate", true)]
    [InlineData("model.unit.privateer", true)]
    [InlineData("model.unit.manOWar", true)]
    [InlineData("model.unit.caravel", false)]
    [InlineData("model.unit.galleon", false)]
    public void CaptureGoodsAbility_MatchesSpec(string id, bool captures)
    {
        // The naval raiders (captureGoods) loot a beaten ship's hold; the pure transports do not.
        Assert.Equal(captures, Classic.Unit(id).CaptureGoods);
    }

    [Theory]
    [InlineData("model.unit.privateer", true)]
    [InlineData("model.unit.frigate", false)]
    [InlineData("model.unit.caravel", false)]
    public void PiracyAbility_MatchesSpec(string id, bool piracy)
    {
        // The privateer alone carries piracy (attack without war + hidden flag).
        Assert.Equal(piracy, Classic.Unit(id).Piracy);
    }

    [Fact]
    public void VeteranSoldier_FoldsItsPercentageModifier()
    {
        // Base 0/1, +50% on each → unarmed offence 0, defence 1.5 (its punch comes from a role, not the base).
        UnitType veteran = Classic.Unit("model.unit.veteranSoldier");
        Assert.Equal(0, veteran.Offence, 5);
        Assert.Equal(1.5, veteran.Defence, 5);
    }

    [Theory]
    [InlineData("model.tile.plains", 0)]
    [InlineData("model.tile.marsh", 25)]
    [InlineData("model.tile.rainForest", 75)]
    [InlineData("model.tile.hills", 100)]
    public void TerrainDefenceBonus_MatchesSpec(string id, double bonus)
    {
        Assert.Equal(bonus, Classic.Terrain(id).DefenceBonus, 5);
    }

    // ---- Power ----

    [Fact]
    public void AttackPower_AppliesAttackBonusAndPenalties()
    {
        Assert.Equal(1.5, CombatModel.AttackPower(1, new AttackContext()), 5);               // brave + attack bonus
        Assert.Equal(2.625, CombatModel.AttackPower(7, new AttackContext(ArtilleryInOpen: true)), 5); // 7×1.5×0.25
        Assert.Equal(0.51, CombatModel.AttackPower(1, new AttackContext(Movement: MovementPenalty.Big)), 5); // 1×1.5×0.34
    }

    [Fact]
    public void DefencePower_AppliesTerrainFortifyAndSettlement()
    {
        // Free colonist (1) on hills (+100%) fortified (+50%) = 1 × 2 × 1.5 = 3.
        Assert.Equal(3.0, CombatModel.DefencePower(1, new DefenceContext(TerrainDefenceBonus: 100, Fortified: true)), 5);
        // Brave (1) defending a camp (+50% settlement) = 1.5.
        Assert.Equal(1.5, CombatModel.DefencePower(1, new DefenceContext(SettlementDefenceBonus: 50)), 5);
    }

    [Fact]
    public void RolePower_FoldsIntoTheBase()
    {
        // A unit's role offence/defence adds to its type base before the situational percentages (index 30).
        double soldierAtk = Classic.Unit("model.unit.freeColonist").Offence + Classic.Role("model.role.soldier").Offence;
        Assert.Equal(2, soldierAtk, 5);
        Assert.Equal(3.0, CombatModel.AttackPower(soldierAtk, new AttackContext()), 5); // 2 × 1.5 attack bonus

        double dragoonDef = Classic.Unit("model.unit.freeColonist").Defence + Classic.Role("model.role.dragoon").Defence;
        Assert.Equal(3, dragoonDef, 5);
        Assert.Equal(4.5, CombatModel.DefencePower(dragoonDef, new DefenceContext(Fortified: true)), 5); // 3 × 1.5

        double armedBrave = Classic.Unit("model.unit.brave").Offence + Classic.Role("model.role.armedBrave").Offence;
        Assert.Equal(4.5, CombatModel.AttackPower(armedBrave, new AttackContext()), 5); // (1+2) × 1.5
    }

    // ---- Odds + resolution ----

    [Fact]
    public void WinProbability_IsAttackOverTotal()
    {
        Assert.Equal(1.5 / 4.5, CombatModel.WinProbability(1.5, 3.0), 5);
        Assert.Equal(0, CombatModel.WinProbability(0, 0)); // no power either side
    }

    [Theory]
    [InlineData(0.00, CombatResult.GreatWin)]  // r < 0.1·win (0.05)
    [InlineData(0.30, CombatResult.Win)]       // r < win (0.5)
    [InlineData(0.70, CombatResult.Loss)]      // win ≤ r < 0.95
    [InlineData(0.99, CombatResult.GreatLoss)] // r ≥ 0.1·win + 0.9 (0.95)
    public void Resolve_PartitionsTheRange(double roll, CombatResult expected)
    {
        Assert.Equal(expected, CombatModel.Resolve(0.5, new FixedRandom(roll)));
    }

    [Theory]
    [InlineData(0.00, CombatResult.GreatWin)]  // r < 0.1·win (0.05)
    [InlineData(0.30, CombatResult.Win)]       // r < win (0.5)
    [InlineData(0.55, CombatResult.Evade)]     // win ≤ r < 0.8·win+0.2 (0.6) — ships evade
    [InlineData(0.70, CombatResult.Loss)]      // 0.6 ≤ r < 0.1·win+0.9 (0.95)
    [InlineData(0.96, CombatResult.GreatLoss)] // r ≥ 0.95
    public void ResolveNaval_PartitionsTheRange_WithAnEvadeBand(double roll, CombatResult expected)
    {
        Assert.Equal(expected, CombatModel.ResolveNaval(0.5, new FixedRandom(roll)));
    }

    [Fact]
    public void CargoPenalty_WeakensBothOffenceAndDefence_PerSlot()
    {
        // FreeCol's −12.5% per goods slot, on both sides.
        Assert.Equal(8 * 1.5 * 0.75, CombatModel.AttackPower(8, new AttackContext(GoodsCarried: 2)), 5);  // 2 slots → ×0.75
        Assert.Equal(8 * 0.75, CombatModel.DefencePower(8, new DefenceContext(GoodsCarried: 2)), 5);
        Assert.Equal(0, CombatModel.DefencePower(8, new DefenceContext(GoodsCarried: 8)), 5);             // full hold → floored at 0
        Assert.Equal(8, CombatModel.DefencePower(8, new DefenceContext(GoodsCarried: 0)), 5);             // empty → no penalty
    }
}
