using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Units;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 tests for the Europe screen: recruiting from the dock, boarding a colonist
/// to sail home, and selling/buying goods — all via the real UI controls. The
/// Europe state is injected (a constructed <see cref="Game"/>) so the seam is
/// exercised deterministically without playing turns to reach it.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class EuropePanelTests
{
    private const string Caravel = "model.unit.caravel";
    private const string Colonist = "model.unit.freeColonist";
    private const string Sugar = "model.goods.sugar";

    [TestCase(Timeout = 60000)]
    public async Task RecruitButton_BuysAColonistIntoEurope()
    {
        // A treasury but no units: just the three-slot dock.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [], Explored = [], Gold = 1000,
        });

        int goldBefore = game.Gold;
        int inEuropeBefore = game.UnitsInEurope.Count();

        Button recruit = FindButton(controller, "Recruit_0")!;
        AssertThat(recruit).IsNotNull();
        recruit.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.UnitsInEurope.Count()).IsEqual(inEuropeBefore + 1); // a recruit appeared
        AssertThat(game.Gold).IsEqual(goldBefore - 200);                    // paid the base price
    }

    [TestCase(Timeout = 60000)]
    public async Task BoardThenSail_SendsAColonistHome()
    {
        // A caravel and a colonist, both in Europe.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 2, MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean"],
            Units =
            [
                new SavedUnit(1, Caravel, 1, 0, 12, (int)UnitLocation.InEurope),
                new SavedUnit(2, Colonist, 0, 0, 3, (int)UnitLocation.InEurope),
            ],
            Explored = [],
        });

        Unit ship = game.Units.First(u => u.Id == 1);
        Unit colonist = game.Units.First(u => u.Id == 2);

        Button board = FindButton(controller, "Board_2_1")!;
        AssertThat(board).IsNotNull();
        board.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colonist.IsAboard).IsTrue();
        AssertThat(colonist.CarrierId!.Value).IsEqual(ship.Id);

        Button sail = FindButton(controller, "Sail_1")!;
        AssertThat(sail).IsNotNull();
        sail.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(ship.Location).IsEqual(UnitLocation.SailingToNewWorld);
    }

    [TestCase(Timeout = 60000)]
    public async Task ShipUnderRepair_ShowsNoSailButton()
    {
        // A caravel damaged in combat is in port repairing (5 turns left) — it must not offer a sail button.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [new SavedUnit(1, Caravel, 0, 0, 0, (int)UnitLocation.InEurope, RepairTurns: 5)],
            Explored = [],
        });
        await runner.SimulateFrames(1);

        AssertThat(game.Units[0].IsUnderRepair).IsTrue();
        AssertThat(FindButton(controller, "Sail_1")).IsNull(); // cannot be sailed while repairing
    }

    [TestCase(Timeout = 60000)]
    public async Task SellButton_SellsCargo_AndCreditsTreasury()
    {
        // A caravel in Europe carrying 100 sugar.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units =
            [
                new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope, 0,
                    new Dictionary<string, int> { [Sugar] = 100 }),
            ],
            Explored = [],
        });
        Unit ship = game.Units[0];

        Button sell = FindButton(controller, "Sell_1_sugar")!;
        AssertThat(sell).IsNotNull();
        sell.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.Gold).IsEqual(200);            // 100 sugar × bid 2, no tax
        AssertThat(ship.CargoOf(Sugar)).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task BuyDropdown_BuysGoodsIntoTheHold()
    {
        // A caravel in Europe and a treasury.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope)],
            Explored = [], Gold = 1000,
        });
        Unit ship = game.Units[0];
        int goldBefore = game.Gold;

        var buy = controller.GetNode<PanelContainer>("UI/EuropePanel")
            .FindChild("Buy_1", recursive: true, owned: false) as OptionButton;
        AssertThat(buy).IsNotNull();
        buy!.EmitSignal(OptionButton.SignalName.ItemSelected, 1L); // the first tradeable good, ×100
        await runner.SimulateFrames(1);

        AssertThat(game.Gold < goldBefore).IsTrue();          // gold was spent
        AssertThat(game.CargoSlotsUsed(ship) > 0).IsTrue();   // goods are now aboard
    }

    [TestCase(Timeout = 60000)]
    public async Task BuyUnitDropdown_PurchasesAUnitIntoEurope()
    {
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [], Explored = [], Gold = 5000,
        });
        int goldBefore = game.Gold;
        int inEuropeBefore = game.UnitsInEurope.Count();

        var buy = controller.GetNode<PanelContainer>("UI/EuropePanel")
            .FindChild("BuyUnit", recursive: true, owned: false) as OptionButton;
        AssertThat(buy).IsNotNull();
        buy!.EmitSignal(OptionButton.SignalName.ItemSelected, 1L); // the first purchasable unit type
        await runner.SimulateFrames(1);

        AssertThat(game.Gold < goldBefore).IsTrue();
        AssertThat(game.UnitsInEurope.Count()).IsEqual(inEuropeBefore + 1);
    }

    private static async Task<(ISceneRunner, GameController, Game)> OpenEurope(SaveGame state)
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);

        Game game = state.Restore(GameLogic.Specification.Ruleset.LoadClassic());
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, game);
        controller.OpenEuropePanel();
        await runner.SimulateFrames(1);
        return (runner, controller, game);
    }

    private static Button? FindButton(GameController controller, string namePrefix) =>
        controller.GetNode<PanelContainer>("UI/EuropePanel")
            .FindChildren("*", recursive: true, owned: false)
            .OfType<Button>()
            .FirstOrDefault(b => b.Name.ToString().StartsWith(namePrefix));
}
