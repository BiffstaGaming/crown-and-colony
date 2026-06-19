using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Clear-skill (<c>86d3c7x5n</c>, FreeCol <c>InGameController.clearSpeciality</c>): a specialist colonist can
/// voluntarily revert to a plain free colonist via the <c>model.unitChange.clearSkill</c> unit-change — useful to
/// turn a surplus expert into a general worker. RNG-free, no save change (a unit type swap).
/// </summary>
public class ClearSkillTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string ExpertFarmer = "model.unit.expertFarmer";
    private const string MasterCarpenter = "model.unit.masterCarpenter";
    private const string FreeColonist = "model.unit.freeColonist";

    [Fact]
    public void Spec_ClearSkill_RevertsEachSpecialistToAFreeColonist()
    {
        Assert.Equal(FreeColonist, Classic.GetUnitChange(UnitChangeTypeIds.ClearSkill, ExpertFarmer)?.To);
        Assert.Equal(FreeColonist, Classic.GetUnitChange(UnitChangeTypeIds.ClearSkill, MasterCarpenter)?.To);
        Assert.Null(Classic.GetUnitChange(UnitChangeTypeIds.ClearSkill, FreeColonist)); // a free colonist has nothing to clear
    }

    [Fact]
    public void ClearSkill_TurnsAnExpertIntoAFreeColonist()
    {
        (Game game, Unit expert) = SpecialistOnMap(ExpertFarmer);
        int id = expert.Id;
        Assert.True(game.CheckClearSkill(expert).Allowed);

        game.ClearSkill(expert);

        Unit reverted = game.Units.Single(u => u.Id == id);
        Assert.Equal(FreeColonist, reverted.Type.Id); // same unit, now a plain colonist
    }

    [Fact]
    public void ClearSkill_IsRefused_ForAFreeColonist()
    {
        (Game game, Unit colonist) = SpecialistOnMap(FreeColonist);
        Assert.False(game.CheckClearSkill(colonist).Allowed); // nothing to clear
        Assert.Throws<InvalidMoveException>(() => game.ClearSkill(colonist));
    }

    [Fact]
    public void ClearSkill_IsRefused_ForANativeUnit()
    {
        Game game = Game.New(Classic, seed: 42);
        Unit? brave = game.NativeUnits.FirstOrDefault();
        Assert.NotNull(brave); // a fresh game seeds native braves
        Assert.False(game.CheckClearSkill(brave!).Allowed);
    }

    [Fact]
    public void ClearSkill_DrawsNoRandomness()
    {
        (Game game, Unit expert) = SpecialistOnMap(ExpertFarmer);
        var before = game.RandomState;
        game.ClearSkill(expert);
        Assert.Equal(before, game.RandomState); // a pure type swap (ADR-009)
    }

    /// <summary>A human unit of <paramref name="type"/> standing on a free land tile.</summary>
    private static (Game Game, Unit Unit) SpecialistOnMap(string type)
    {
        Game game = Game.New(Classic, seed: 42);
        Position land = game.Map.AllPositions().First(p => !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        return (game, game.SpawnUnit(Classic.Unit(type), land));
    }
}
