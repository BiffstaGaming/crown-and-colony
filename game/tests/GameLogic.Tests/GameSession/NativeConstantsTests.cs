using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Verification (drift guard) for the hardcoded native-interaction tuning constants in
/// <see cref="Game"/> and <see cref="NativeSettlement"/>. Each test pins a code constant to its
/// source of truth — either the classic-medium difficulty group in the embedded
/// <c>specification.xml</c> (the <c>model.option.*</c> options FreeCol reads with
/// <c>spec.getInteger(...)</c>) or a FreeCol-source constant (<c>Tension.java</c>,
/// <c>IndianSettlement.java</c>, <c>ServerIndianSettlement.java</c>). If a future edit drifts a
/// constant off the spec/FreeCol value, the matching assertion fails.
///
/// <para>This is a <b>verification</b> task (86d3drphp): it does not migrate the constants into the
/// ruleset — that is a separate task (86d3bb1x3). The convert/burn probabilities are still hardcoded
/// here and read straight from the spec; once migrated, these spec-option tests move to assert the
/// parsed <see cref="DifficultyOptions"/> value instead.</para>
///
/// <para>Two genuine mismatches were found and corrected in place by this task:
/// <c>NativeConvertProbabilityPercent</c> (was 50 → spec 30) and <c>NativeBurnProbabilityPercent</c>
/// (was 2 → spec 6). See <c>docs/systems/natives.md</c> findings table.</para>
/// </summary>
public class NativeConstantsTests
{
    /// <summary>The classic-medium difficulty option group, parsed from the same embedded spec the game loads.</summary>
    private static readonly XElement MediumLevel = LoadMediumDifficultyLevel();

    private static XElement LoadMediumDifficultyLevel()
    {
        // The classic spec is embedded in the GameLogic assembly (see GameLogic.csproj LogicalName).
        Assembly gameLogic = typeof(Ruleset).Assembly;
        const string resource = "CrownAndColony.GameLogic.Specification.classic.specification.xml";
        using System.IO.Stream stream = gameLogic.GetManifestResourceStream(resource)
            ?? throw new System.InvalidOperationException($"Embedded spec '{resource}' not found.");
        XDocument doc = XDocument.Load(stream);
        return doc.Descendants("optionGroup")
            .First(g => (string?)g.Attribute("id") == DifficultyLevels.DefaultId); // model.difficulty.medium
    }

    /// <summary>Reads an <c>&lt;integerOption&gt;</c> value from the classic-medium difficulty group.</summary>
    private static int MediumIntOption(string id) =>
        (int?)MediumLevel.Descendants("integerOption")
            .First(o => (string?)o.Attribute("id") == id)
            .Attribute("value")
        ?? throw new System.InvalidOperationException($"option {id} has no value");

    // ---- Spec-option constants (classic-medium model.difficulty.natives) ----

    /// <summary>
    /// The capture-convert base chance the assault path uses must equal the classic-medium
    /// <c>nativeConvertProbability</c> (30) — FreeCol <c>Unit.getConvertProbability</c> = 0.01 × the option.
    /// (Corrected from 50 by this task.)
    /// </summary>
    [Fact]
    public void NativeConvertProbability_MatchesClassicMediumSpec()
    {
        Assert.Equal(30, MediumIntOption("model.option.nativeConvertProbability"));
        Assert.Equal(30, Game.NativeConvertProbabilityPercent);
    }

    /// <summary>
    /// The burn-missions chance must equal the classic-medium <c>burnProbability</c> (6) — FreeCol
    /// <c>Unit.getBurnProbability</c> = 0.01 × the option, unscaled. (Corrected from 2 by this task.)
    /// </summary>
    [Fact]
    public void NativeBurnProbability_MatchesClassicMediumSpec()
    {
        Assert.Equal(6, MediumIntOption("model.option.burnProbability"));
        Assert.Equal(6, Game.NativeBurnProbabilityPercent);
    }

    /// <summary>The tribute-demand <c>dx</c> derives from the medium <c>nativeDemands</c> option (2 → dx 3).</summary>
    [Fact]
    public void NativeDemands_MatchesClassicMediumSpec()
    {
        int spec = MediumIntOption("model.option.nativeDemands");
        Assert.Equal(2, spec);
        Assert.Equal(spec, Ruleset.LoadClassic().Difficulty.NativeDemands);
    }

    /// <summary>The land-price multiplier comes from the medium <c>landPriceFactor</c> option (60).</summary>
    [Fact]
    public void LandPriceFactor_MatchesClassicMediumSpec()
    {
        int spec = MediumIntOption("model.option.landPriceFactor");
        Assert.Equal(60, spec);
        Assert.Equal(spec, Ruleset.LoadClassic().Difficulty.LandPriceFactor);
    }

    /// <summary>The ship-trade price penalty comes from the medium <c>shipTradePenalty</c> option (−30).</summary>
    [Fact]
    public void ShipTradePenalty_MatchesClassicMediumSpec()
    {
        int spec = MediumIntOption("model.option.shipTradePenalty");
        Assert.Equal(-30, spec);
        Assert.Equal(spec, Ruleset.LoadClassic().Difficulty.ShipTradePenalty);
    }

    // ---- FreeCol-source constants (Tension.java) ----

    /// <summary>Tension band upper limits must match FreeCol <c>Tension.Level</c> (Happy 100, Content 600, Displeased 700, Angry 800, Hateful 1000).</summary>
    [Fact]
    public void AlarmBandLimits_MatchFreeColTensionLevels()
    {
        Assert.Equal(100, NativeSettlement.AlarmHappyMax);
        Assert.Equal(600, NativeSettlement.AlarmContentMax);
        Assert.Equal(700, NativeSettlement.AlarmDispleasedMax);
        Assert.Equal(800, NativeSettlement.AlarmAngryMax);
        Assert.Equal(1000, NativeSettlement.MaxAlarm); // Hateful.limit
    }

    /// <summary>Hostile-act tension deltas must match FreeCol <c>Tension.TENSION_ADD_*</c>.</summary>
    [Fact]
    public void TensionDeltas_MatchFreeColConstants()
    {
        Assert.Equal(100, NativeSettlement.TensionAddMinor);              // TENSION_ADD_MINOR
        Assert.Equal(200, NativeSettlement.TensionAddNormal);             // TENSION_ADD_NORMAL
        Assert.Equal(300, NativeSettlement.TensionAddMajor);              // TENSION_ADD_MAJOR
        Assert.Equal(400, NativeSettlement.TensionAddUnitDestroyed);      // TENSION_ADD_UNIT_DESTROYED
        Assert.Equal(500, NativeSettlement.TensionAddSettlementAttacked); // TENSION_ADD_SETTLEMENT_ATTACKED
        Assert.Equal(600, NativeSettlement.TensionAddCapitalAttacked);    // TENSION_ADD_CAPITAL_ATTACKED
    }

    /// <summary>Land-taken alarm must match FreeCol <c>Tension.TENSION_ADD_LAND_TAKEN</c> (200).</summary>
    [Fact]
    public void LandTakenAlarm_MatchesFreeColConstant() =>
        Assert.Equal(200, Game.LandTakenAlarm);

    /// <summary>The surrendered-nation alarm must match FreeCol <c>Tension.SURRENDERED</c> = (Content + Happy)/2 = 350.</summary>
    [Fact]
    public void SurrenderedAlarm_MatchesFreeColFormula() =>
        Assert.Equal((600 + 100) / 2, NativeSettlement.SurrenderedAlarm);

    // ---- FreeCol-source constants (IndianSettlement / ServerIndianSettlement / IndianDemandMission) ----

    /// <summary>First-contact gift bounds must match FreeCol <c>IndianSettlement.GIFT_MINIMUM/MAXIMUM</c> (10/80).</summary>
    [Fact]
    public void GiftBounds_MatchFreeColConstants()
    {
        Assert.Equal(10, Game.GiftMinimum);
        Assert.Equal(80, Game.GiftMaximum);
    }

    /// <summary>New-mission goodwill must match FreeCol <c>ServerIndianSettlement.ALARM_NEW_MISSIONARY</c> (−100; stored as the +100 we subtract).</summary>
    [Fact]
    public void AlarmNewMissionary_MatchesFreeColConstant() =>
        Assert.Equal(100, Game.AlarmNewMissionary);

    /// <summary>Convert-distance cap must match FreeCol <c>ServerIndianSettlement.MAX_CONVERT_DISTANCE</c> (10).</summary>
    [Fact]
    public void MaxConvertDistance_MatchesFreeColConstant() =>
        Assert.Equal(10, Game.MaxConvertDistance);

    /// <summary>Convert-accrual terms must match FreeCol <c>conversionSkill</c> (+6), the jesuit skill (3) and <c>conversionAlarmRate</c> (+2%).</summary>
    [Fact]
    public void ConvertAccrualTerms_MatchFreeColModifiers()
    {
        Assert.Equal(6, Game.ConversionSkillBonus);
        Assert.Equal(3, Game.JesuitConversionSkill);
        Assert.Equal(2, Game.ConversionAlarmRatePercent);
    }

    /// <summary>Tribute-demand bounds must match FreeCol <c>IndianDemandMission.GOODS_DEMAND_MIN</c> (30) and <c>GoodsContainer.CARGO_SIZE</c> (100).</summary>
    [Fact]
    public void DemandBounds_MatchFreeColConstants()
    {
        Assert.Equal(30, Game.NativeDemandMin);
        Assert.Equal(100, Game.NativeDemandMax);
    }
}
