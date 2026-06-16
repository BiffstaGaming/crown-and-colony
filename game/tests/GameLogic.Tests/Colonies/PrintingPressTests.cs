using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Printing press + newspaper bell multiplier (<c>86d3c9p33</c>, FreeCol <c>model.goods.bells</c> building
/// modifier): a printing press boosts a colony's bell output +50%, a newspaper +100% — accelerating the colony's
/// Sons-of-Liberty liberty (and the player's founding-father pool).
/// </summary>
public class PrintingPressTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Bells = "model.goods.bells";
    private const string PrintingPress = "model.building.printingPress";
    private const string Newspaper = "model.building.newspaper";

    /// <summary>A pop-1 colony on a 1×1 plains map (town hall present → 1 bell/turn unattended; no bell upkeep at pop 1).</summary>
    private static Game PressColony(out Colony colony, params string[] extraBuildings)
    {
        var game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [0],
            Colonies = [new SavedColony(1, "Pressville", 0, 0, 1)],
        }.Restore(Classic);
        colony = game.Colonies[0];
        foreach (string b in extraBuildings)
        {
            colony.AddBuilding(b);
        }
        return game;
    }

    [Fact]
    public void BellBonus_ParsedFromTheBuildingModifier()
    {
        Assert.Equal(50, Classic.Building(PrintingPress).BellBonus);
        Assert.Equal(100, Classic.Building(Newspaper).BellBonus); // deletes the inherited +50, redefines +100
        Assert.Equal(0, Classic.Building("model.building.townHall").BellBonus);
    }

    [Fact]
    public void BellProductionBonus_FollowsTheBuiltTier()
    {
        Game game = PressColony(out Colony plain);
        Assert.Equal(0, game.BellProductionBonus(plain));

        Game pressGame = PressColony(out Colony press, PrintingPress);
        Assert.Equal(50, pressGame.BellProductionBonus(press));

        Game paperGame = PressColony(out Colony paper, Newspaper);
        Assert.Equal(100, paperGame.BellProductionBonus(paper));
    }

    [Fact]
    public void PrintingPress_AddsHalfAgainToLiberty()
    {
        // Seed 100 bells; the town hall adds 1 (unattended) → 101 banked. No upkeep at pop 1, no fathers.
        Game plainGame = PressColony(out Colony plain);
        plain.AddGoods(Bells, 100);
        plainGame.EndTurn();
        Assert.Equal(101, plain.Liberty);

        Game pressGame = PressColony(out Colony press, PrintingPress);
        press.AddGoods(Bells, 100);
        pressGame.EndTurn();
        Assert.Equal(151, press.Liberty); // 101 + 50% = 151
    }

    [Fact]
    public void Newspaper_DoublesTheBellContribution()
    {
        // Seed 50 bells; +1 town hall → 51 banked; +100% → 102 (still below the 200·pop = 200 cap at pop 1).
        Game game = PressColony(out Colony paper, Newspaper);
        paper.AddGoods(Bells, 50);
        game.EndTurn();
        Assert.Equal(102, paper.Liberty);
    }
}
