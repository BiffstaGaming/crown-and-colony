using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Native nation / settlement parsing, pinned to the classic FreeCol spec
/// (<c>freecol/data/rules/classic/specification.xml</c>, <c>&lt;indian-nation-types&gt;</c>).
/// </summary>
public class NativeNationTypeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void Classic_DefinesEightNativeNations()
    {
        // Apache, Sioux, Tupi (camp); Arawak, Cherokee, Iroquois (village); Inca, Aztec (city).
        Assert.Equal(8, Classic.NativeNationTypes.Count);
        Assert.All(
            new[]
            {
                "model.nationType.apache", "model.nationType.sioux", "model.nationType.tupi",
                "model.nationType.arawak", "model.nationType.cherokee", "model.nationType.iroquois",
                "model.nationType.inca", "model.nationType.aztec",
            },
            id => Assert.Contains(Classic.NativeNationTypes, n => n.Id == id));
    }

    [Fact]
    public void AbstractNationTypes_AreNotExposed()
    {
        Assert.DoesNotContain(Classic.NativeNationTypes, n => n.Id is "default" or "camp" or "village" or "city");
        Assert.Throws<KeyNotFoundException>(() => Classic.NativeNation("camp"));
        Assert.Throws<KeyNotFoundException>(() => Classic.NativeNation("model.nationType.martian"));
    }

    [Fact]
    public void Apache_ParsesCampWithInheritedSettlementAndOwnTraits()
    {
        NativeNationType apache = Classic.NativeNation("model.nationType.apache");

        Assert.Equal("model.settlement.camp", apache.SettlementTypeId);
        Assert.Equal("model.settlement.camp.capital", apache.CapitalSettlementTypeId);
        Assert.Equal(SettlementNumber.Average, apache.NumberOfSettlements);
        Assert.Equal(NativeAggression.High, apache.Aggression);
        Assert.Equal("apache", apache.ShortName);
        Assert.Contains(apache.Skills, s => s is { UnitTypeId: "model.unit.masterCottonPlanter", Probability: 20 });
        Assert.Equal(["model.region.center"], apache.Regions);
    }

    [Fact]
    public void Tupi_FoundsManySettlements_LowAggression()
    {
        NativeNationType tupi = Classic.NativeNation("model.nationType.tupi");
        Assert.Equal(SettlementNumber.High, tupi.NumberOfSettlements);
        Assert.Equal(NativeAggression.Low, tupi.Aggression);
    }

    [Fact]
    public void Arawak_IsAVillageNation()
    {
        NativeNationType arawak = Classic.NativeNation("model.nationType.arawak");
        Assert.Equal("model.settlement.village", arawak.SettlementTypeId);
        Assert.Equal(SettlementNumber.Low, arawak.NumberOfSettlements);
        Assert.Equal(NativeAggression.High, arawak.Aggression);
    }

    [Fact]
    public void Inca_InheritsCityNumberAndSkills_PlusOwnSkill()
    {
        NativeNationType inca = Classic.NativeNation("model.nationType.inca");

        Assert.Equal("model.settlement.inca", inca.SettlementTypeId);
        Assert.Equal("model.settlement.inca.capital", inca.CapitalSettlementTypeId);
        // number-of-settlements is declared on the abstract 'city' parent (low) and inherited.
        Assert.Equal(SettlementNumber.Low, inca.NumberOfSettlements);
        Assert.Equal(NativeAggression.Low, inca.Aggression);
        // City skills (silver miner, farmer, fisherman, weaver) PLUS Inca's own ore miner.
        Assert.Contains(inca.Skills, s => s.UnitTypeId == "model.unit.expertSilverMiner");
        Assert.Contains(inca.Skills, s => s.UnitTypeId == "model.unit.expertOreMiner");
        Assert.Equal(["model.region.southWest"], inca.Regions);
    }

    [Fact]
    public void Aztec_IsAggressiveCity()
    {
        NativeNationType aztec = Classic.NativeNation("model.nationType.aztec");
        Assert.Equal("model.settlement.aztec", aztec.SettlementTypeId);
        Assert.Equal(SettlementNumber.Low, aztec.NumberOfSettlements);
        Assert.Equal(NativeAggression.High, aztec.Aggression);
    }

    [Fact]
    public void SettlementTypes_MatchSpecificationValues()
    {
        SettlementType camp = Classic.Settlement("model.settlement.camp");
        Assert.False(camp.Capital);
        Assert.Equal((4, 6), (camp.MinimumSize, camp.MaximumSize));
        Assert.Equal((1, 2), (camp.ClaimableRadius, camp.ExtraClaimableRadius));
        Assert.Equal(1, camp.TradeBonus);
        Assert.Equal(50, camp.DefenceModifier);

        SettlementType campCapital = Classic.Settlement("model.settlement.camp.capital");
        Assert.True(campCapital.Capital);
        Assert.Equal((6, 8), (campCapital.MinimumSize, campCapital.MaximumSize));
        Assert.Equal(2, campCapital.ClaimableRadius);
        Assert.Equal(100, campCapital.DefenceModifier);

        SettlementType village = Classic.Settlement("model.settlement.village");
        Assert.Equal((6, 8), (village.MinimumSize, village.MaximumSize));
        Assert.Equal(50, village.DefenceModifier);

        // Cities are the biggest and best-defended; the capital doubles the defence bonus.
        SettlementType inca = Classic.Settlement("model.settlement.inca");
        Assert.Equal((8, 12), (inca.MinimumSize, inca.MaximumSize));
        Assert.Equal(2, inca.ClaimableRadius);
        Assert.Equal(100, inca.DefenceModifier);

        SettlementType incaCapital = Classic.Settlement("model.settlement.inca.capital");
        Assert.Equal((10, 12), (incaCapital.MinimumSize, incaCapital.MaximumSize));
        Assert.Equal(3, incaCapital.ClaimableRadius);
        Assert.Equal(200, incaCapital.DefenceModifier);
    }

    [Fact]
    public void UnknownSettlementId_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => Classic.Settlement("model.settlement.atlantis"));
    }

    [Fact]
    public void RulesetWithoutNatives_HasNoNativeNations()
    {
        // Minimal valid spec (tile + unit types, no <indian-nation-types>) — test rulesets
        // must keep loading without natives.
        var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, leaveOpen: true))
        {
            writer.Write(
                "<freecol-specification>" +
                "<tile-types><tile-type id=\"model.tile.plains\" basic-move-cost=\"3\" basic-work-turns=\"3\"/></tile-types>" +
                "<unit-types><unit-type id=\"model.unit.freeColonist\"/></unit-types>" +
                "</freecol-specification>");
        }
        ms.Position = 0;

        Ruleset minimal = Ruleset.Load(ms);
        Assert.Empty(minimal.NativeNationTypes);
        Assert.Empty(minimal.SettlementTypes);
    }
}
