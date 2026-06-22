using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// Pins the colony/production scalar constants routed through the ruleset (86d3drpgg) — each parses to its classic
/// FreeCol value, and the routed game paths (food consumption, growth, Sons-of-Liberty divisor, custom-house default
/// export level) are byte-identical to the pre-routing hardcoded behaviour.
/// </summary>
public class ColonyConstantsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Food = Colony.FoodId;

    [Fact]
    public void Classic_ColonyConstants_MatchClassicValues()
    {
        // Each constant parses to the exact FreeCol classic figure: food appetite 2 (the colonist's <consumes food>),
        // growth threshold 200 (Settlement.FOOD_PER_COLONIST / freeColonist required-goods), liberty-per-rebel 200
        // (Colony.LIBERTY_PER_REBEL), default export level 50 (ExportData.EXPORT_LEVEL_DEFAULT), colony sight radius 2
        // (the colony settlement's visible-radius / FreeCol Settlement.getLineOfSight).
        ColonyConstants c = Classic.ColonyConstants;
        Assert.Equal(2, c.FoodPerColonist);
        Assert.Equal(200, c.FoodForGrowth);
        Assert.Equal(200, c.LibertyPerRebel);
        Assert.Equal(50, c.DefaultExportLevel);
        Assert.Equal(2, c.ColonySightRadius); // parsed from model.settlement.colony visible-radius="2"
    }

    [Fact]
    public void ClassicConstants_EqualThePackagedDefaults()
    {
        // The classic spec parses to exactly the ClassicDefaults bundle — so the fallback path and the parsed path are
        // the same bytes (ADR-009).
        Assert.Equal(ColonyConstants.ClassicDefaults, Classic.ColonyConstants);
    }

    [Fact]
    public void FoodConsumptionAndGrowth_UseTheRoutedConstants()
    {
        // A pop-1 plains colony banks net +1 food/turn (centre grain 3 − 1 colonist × 2 eaten). After 199 turns it
        // holds 199; the 200th turn's surplus reaches the 200 growth threshold and a colonist is born (food reset, the
        // 200 consumed). This exercises both FoodPerColonist (consumption) and FoodForGrowth (threshold).
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];

        for (int i = 0; i < 199; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(199, colony.StoreOf(Food));
        Assert.Equal(1, colony.Population);

        game.EndTurn(); // 200th surplus → growth
        Assert.Equal(2, colony.Population);
        Assert.Equal(0, colony.StoreOf(Food)); // 200 consumed on birth; the +1 of this turn left 0 after the −200
    }

    [Fact]
    public void RebelLibertyDivisor_DrivesSonsOfLiberty_AtTheClassic200()
    {
        // SoL% = liberty·100 / (divisor·pop). With the classic divisor 200 and pop 1, banking 200 liberty → 100% SoL.
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];
        Assert.Equal(Classic.ColonyConstants.LibertyPerRebel, colony.RebelLibertyDivisor);

        colony.Liberty = Classic.ColonyConstants.LibertyPerRebel * colony.Population;
        Assert.Equal(100, colony.SonsOfLiberty);
    }

    [Fact]
    public void ExportRetainDefault_IsTheClassic50_ForAnUnconfiguredGood()
    {
        // A founded colony's untouched good reports the classic default retain level (50) and is-not-exported.
        Game game = PlainsColony();
        Colony colony = game.Colonies[0];

        Colony.ExportSetting setting = colony.ExportOf("model.goods.cotton");
        Assert.False(setting.Exported);
        Assert.Equal(50, setting.ExportLevel);
    }

    /// <summary>A pop-1 colony on a 1×1 plains map (plains centre yield: grain 3 + cotton 2).</summary>
    private static Game PlainsColony()
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [0],
            Colonies = [new SavedColony(1, "Testville", 0, 0, 1)],
        };
        return save.Restore(Classic);
    }
}
