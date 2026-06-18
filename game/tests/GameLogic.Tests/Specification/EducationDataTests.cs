using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Schooling data layer (<c>86d3c9p7f</c> slice 1): the school buildings' teach ability + skill caps, a teacher's
/// <c>skill-taught</c>, and the <c>model.unitChange.education</c> table (recovered past the by-<c>from</c> collapse) +
/// its <see cref="Ruleset.GetTeachingType"/> / <see cref="Ruleset.NeededTurnsOfTraining"/> ports. No behaviour yet.
/// </summary>
public class EducationDataTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Schoolhouse = "model.building.schoolhouse";
    private const string College = "model.building.college";
    private const string University = "model.building.university";
    private const string PettyCriminal = "model.unit.pettyCriminal";
    private const string IndenturedServant = "model.unit.indenturedServant";
    private const string FreeColonist = "model.unit.freeColonist";
    private const string ExpertOreMiner = "model.unit.expertOreMiner";
    private const string MasterDistiller = "model.unit.masterDistiller";
    private const string ElderStatesman = "model.unit.elderStatesman";
    private const string ColonialRegular = "model.unit.colonialRegular";
    private const string VeteranSoldier = "model.unit.veteranSoldier";

    // ── School building data ──────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Schoolhouse, 1)]
    [InlineData(College, 2)]
    [InlineData(University, 4)]
    public void Schools_Teach_WithTheirSkillCap(string buildingId, int maximumSkill)
    {
        BuildingType school = Classic.Building(buildingId);
        Assert.True(school.Teaches); // college/university inherit the schoolhouse's teach ability down the extends chain
        Assert.Equal(maximumSkill, school.MaximumSkill);
    }

    [Fact]
    public void ANonSchool_DoesNotTeach()
    {
        BuildingType carpenter = Classic.Building("model.building.carpenterHouse");
        Assert.False(carpenter.Teaches);
        Assert.Equal(0, carpenter.MaximumSkill);
    }

    // ── skill-taught (non-inherited; the one classic override) ────────────────────────────────────────────────

    [Fact]
    public void ColonialRegular_TeachesVeteranSoldier_NotItself() =>
        Assert.Equal(VeteranSoldier, Classic.Unit(ColonialRegular).SkillTaughtOrSelf);

    [Fact]
    public void AnOrdinaryExpert_TeachesItself() =>
        Assert.Equal(ExpertOreMiner, Classic.Unit(ExpertOreMiner).SkillTaughtOrSelf);

    // ── The education ladder: GetTeachingType climbs one rung per cycle ───────────────────────────────────────

    [Fact]
    public void TeachingType_ClimbsOneRung_TowardTheTeachersExpertise()
    {
        Assert.Equal(IndenturedServant, Classic.GetTeachingType(ExpertOreMiner, PettyCriminal)?.Id);     // criminal → servant
        Assert.Equal(FreeColonist, Classic.GetTeachingType(ExpertOreMiner, IndenturedServant)?.Id);      // servant → free
        Assert.Equal(ExpertOreMiner, Classic.GetTeachingType(ExpertOreMiner, FreeColonist)?.Id);         // free → the teacher's expertise
    }

    [Fact]
    public void TeachingType_IsNull_WhenTheStudentIsAlreadyAtOrAboveTheTaughtSkill() =>
        Assert.Null(Classic.GetTeachingType(ExpertOreMiner, "model.unit.expertFarmer")); // a different skill-1 expert can't be taught ore mining

    [Fact]
    public void TeachingType_FollowsTheTeachersSkillTaught_NotItsType() =>
        Assert.Equal(VeteranSoldier, Classic.GetTeachingType(ColonialRegular, FreeColonist)?.Id); // colonial regular teaches veteran soldier

    // ── Needed turns (4/6/8), and the from-collapse is fixed (every expert row recoverable) ───────────────────

    [Theory]
    [InlineData(ExpertOreMiner, PettyCriminal, 4)]     // criminal rung
    [InlineData(ExpertOreMiner, IndenturedServant, 4)] // servant rung
    [InlineData(ExpertOreMiner, FreeColonist, 4)]      // a skill-1 expert
    [InlineData(MasterDistiller, FreeColonist, 6)]     // a skill-2 expert
    [InlineData(ElderStatesman, FreeColonist, 8)]      // a skill-3 expert
    public void NeededTurns_MatchTheSpec(string teacher, string student, int turns) =>
        Assert.Equal(turns, Classic.NeededTurnsOfTraining(teacher, student));

    [Theory] // distinct free→expert rows all survive (guards the by-`from` collapse regression)
    [InlineData("model.unit.expertFarmer", 4)]
    [InlineData("model.unit.masterCarpenter", 4)]
    [InlineData("model.unit.masterTobaccoPlanter", 6)]
    [InlineData("model.unit.firebrandPreacher", 8)]
    public void EveryExpertEducationRow_IsRecoverable(string expert, int turns) =>
        Assert.Equal(turns, Classic.NeededTurnsOfTraining(expert, FreeColonist));

    [Fact]
    public void NeededTurns_IsZero_WhenTheTeacherCannotTeachTheStudent() =>
        Assert.Equal(0, Classic.NeededTurnsOfTraining(ExpertOreMiner, "model.unit.expertFarmer"));
}
