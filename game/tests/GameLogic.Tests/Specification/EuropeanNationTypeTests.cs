using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// European nations + nation-types parsed into the ruleset (FP-3a, ADR-019): the four classic colonial
/// powers and their Royal Expeditionary Forces, their advantages and starting units (extends-resolved),
/// and the per-nation classic colony-name lists. Pure data — no players are created and nothing is saved.
/// </summary>
public class EuropeanNationTypeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Dutch = "model.nation.dutch";
    private const string French = "model.nation.french";
    private const string English = "model.nation.english";
    private const string Spanish = "model.nation.spanish";

    [Fact]
    public void TheFourClassicColonialPowers_AreParsed()
    {
        foreach ((string id, string nationType) in new[]
        {
            (Dutch, "model.nationType.trade"),
            (French, "model.nationType.cooperation"),
            (English, "model.nationType.immigration"),
            (Spanish, "model.nationType.conquest"),
        })
        {
            EuropeanNation nation = Classic.EuropeanNation(id);
            Assert.True(nation.Selectable);
            Assert.False(nation.IsRef);
            Assert.Equal(nationType, nation.NationType.Id);
            Assert.False(nation.NationType.IsRef);
            Assert.Equal($"{id}REF", nation.RefNationId); // links to its Royal Expeditionary Force
        }
    }

    [Fact]
    public void RefNations_AreParsed_Inert_AndNotSelectable()
    {
        EuropeanNation dutchRef = Classic.EuropeanNation("model.nation.dutchREF");
        Assert.True(dutchRef.IsRef);
        Assert.True(dutchRef.NationType.IsRef);
        Assert.False(dutchRef.Selectable);
        Assert.Null(dutchRef.RefNationId);   // a REF has no REF of its own
        Assert.Empty(dutchRef.ColonyNames);  // REFs do not found colonies
    }

    [Fact]
    public void NativeNations_AndUnknownEnemy_AreNotEuropeanNations()
    {
        Assert.DoesNotContain(Classic.EuropeanNations, n => n.Id == "model.nation.apache");
        Assert.DoesNotContain(Classic.EuropeanNations, n => n.Id == "model.nation.unknownEnemy");
        Assert.Throws<KeyNotFoundException>(() => Classic.EuropeanNation("model.nation.apache"));
    }

    [Fact]
    public void ColonyNames_AreParsedPerNation_InClassicOrder()
    {
        Assert.Equal("New Amsterdam", Classic.EuropeanNation(Dutch).ColonyNames[0]);
        Assert.Equal("Jamestown", Classic.EuropeanNation(English).ColonyNames[0]);
        Assert.Equal("Quebec", Classic.EuropeanNation(French).ColonyNames[0]);
        Assert.Equal("Isabella", Classic.EuropeanNation(Spanish).ColonyNames[0]);

        // Full classic lists (extracted from FreeCol's message strings).
        Assert.Equal(32, Classic.EuropeanNation(Dutch).ColonyNames.Count);
        Assert.Equal(36, Classic.EuropeanNation(English).ColonyNames.Count);
        Assert.Equal(39, Classic.EuropeanNation(Spanish).ColonyNames.Count);
        Assert.Equal("Plymouth", Classic.EuropeanNation(English).ColonyNames[1]); // index order preserved
    }

    [Fact]
    public void StartingUnits_ResolveTheExtendsChain_And_RegularExcludesExpertVariant()
    {
        // English adds no units of its own → inherits the default force, including the caravel.
        EuropeanNationType immigration = Classic.EuropeanNation(English).NationType;
        Assert.Equal("model.unit.caravel", Ship(immigration));
        // The default soldier slot has a regular (free colonist) and an expert (veteran) variant…
        Assert.Equal(2, immigration.StartingUnits.Count(u => u.Slot == "soldier"));
        // …but only the non-expert units are actually placed.
        Assert.Single(immigration.RegularStartingUnits, u => u.Slot == "soldier");
        Assert.DoesNotContain(immigration.RegularStartingUnits, u => u.Expert);

        // Dutch override the ship slot (merchantman, not caravel); other slots stay inherited.
        Assert.Equal("model.unit.merchantman", Ship(Classic.EuropeanNation(Dutch).NationType));

        // French swap the pioneer for a hardy pioneer.
        EuropeanStartingUnit frenchPioneer = Classic.EuropeanNation(French).NationType
            .RegularStartingUnits.Single(u => u.Slot == "pioneer");
        Assert.Equal("model.unit.hardyPioneer", frenchPioneer.UnitTypeId);

        // Spanish start with a mounted veteran soldier (overriding the default soldiers).
        EuropeanStartingUnit spanishSoldier = Classic.EuropeanNation(Spanish).NationType
            .RegularStartingUnits.Single(u => u.Slot == "soldier");
        Assert.Equal("model.unit.veteranSoldier", spanishSoldier.UnitTypeId);
        Assert.True(spanishSoldier.Mounted);
    }

    [Fact]
    public void NationTypeAdvantages_AreParsed_Inherited_AndOwn()
    {
        EuropeanNationType trade = Classic.EuropeanNation(Dutch).NationType;
        // The Dutch trade advantage: a tradeBonus modifier (own).
        Assert.Contains(trade.Modifiers, m => m.TargetId == "model.modifier.tradeBonus" && m.Value == -50);
        // Abilities inherited from the default nation type.
        Assert.Contains(trade.Abilities, a => a.Id == "model.ability.foundsColonies" && a.Value);
        Assert.Contains(trade.Abilities, a => a.Id == "model.ability.canRecruitUnit" && a.Value);
    }

    private static string Ship(EuropeanNationType type) =>
        type.RegularStartingUnits.Single(u => u.Slot == "ship").UnitTypeId;
}
