using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 tests for the on-map native-trade UI (86d3f62qh): a human carrier adjacent to a coastal settlement opens the
/// <see cref="NativeSettlementPanel"/> and drives a <b>sell</b> and a <b>buy</b> through the real panel controls —
/// pick a good, accept the natives' price — asserting gold, ship cargo and the settlement's goods stock all change.
/// Presentation-only (ADR-006): every action forwards to a shipped <see cref="Game"/> trade oracle. State is staged
/// through the save layer (the presentation project can't load cargo / set gold directly), the established pattern.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NativeSettlementPanelTests
{
    private const ulong Seed = 424242;
    private const string Sugar = "model.goods.sugar";

    [TestCase(Timeout = 60000)]
    public async Task SellButton_SellsCargoToTheSettlement_ForGold_DrainingTheHold_GrowingTheStore()
    {
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit ship) =
            await OpenTradePanel(shipCargo: 100, settlementSugar: 0);

        // The ship holds 100 sugar; selling it grows the settlement's store and empties the hold.
        AssertThat(ship.CargoOf(Sugar)).IsEqual(100);
        int goldBefore = game.Gold;
        int stockBefore = settlement.GeneralStockOf(Sugar); // the store may hold a generator-seeded amount already

        // Open the Sell sub-flow, pick the sugar lot, accept the natives' offer.
        await Press(runner, controller, "Sell");
        await Press(runner, controller, "Trade_sugar");
        await Press(runner, controller, "Accept");

        AssertThat(game.Gold).IsGreater(goldBefore);                              // paid for the cargo
        AssertThat(ship.CargoOf(Sugar)).IsEqual(0);                              // the hold is empty
        AssertThat(settlement.GeneralStockOf(Sugar)).IsEqual(stockBefore + 100); // the goods joined the store
    }

    [TestCase(Timeout = 60000)]
    public async Task BuyButton_BuysFromTheSettlementStore_ForGold_LoadingTheHold_DrainingTheStore()
    {
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit ship) =
            await OpenTradePanel(shipCargo: 0, settlementSugar: 80, humanGold: 10000);

        // The settlement holds sugar to sell; the ship is empty and the human can afford it.
        AssertThat(ship.CargoOf(Sugar)).IsEqual(0);
        int goldBefore = game.Gold;
        int stockBefore = settlement.GeneralStockOf(Sugar);
        int lot = System.Math.Min(stockBefore, 100); // GoodsToSell offers the store capped at one hold (100)

        // Open the Buy sub-flow, pick the sugar lot, accept (pay) the natives' asking price.
        await Press(runner, controller, "Buy");
        await Press(runner, controller, "Trade_sugar");
        await Press(runner, controller, "Accept");

        AssertThat(game.Gold).IsLess(goldBefore);                                // gold was spent
        AssertThat(ship.CargoOf(Sugar)).IsEqual(lot);                            // the hold loaded the lot
        AssertThat(settlement.GeneralStockOf(Sugar)).IsEqual(stockBefore - lot); // the store drained by the lot
    }

    /// <summary>Presses the named button inside the native panel and lets the rebuild settle.</summary>
    private static async Task Press(ISceneRunner runner, GameController controller, string name)
    {
        Button? button = FindButton(controller, name);
        AssertThat(button).IsNotNull();
        button!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
    }

    /// <summary>
    /// Starts the main scene, then stages (through the save layer) a human caravel — carrying <paramref name="shipCargo"/>
    /// sugar — on coastal water beside a discovered native settlement holding <paramref name="settlementSugar"/> sugar,
    /// with the human given <paramref name="humanGold"/>. Sets the settlement Content (so trade is allowed) and opens the
    /// native-settlement panel acting with that ship.
    /// </summary>
    private static async Task<(ISceneRunner, GameController, Game, NativeSettlement, Unit)> OpenTradePanel(
        int shipCargo, int settlementSugar, int humanGold = 0)
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        // A native settlement with an adjacent free water tile (a coastal settlement reachable by ship).
        NativeSettlement seed = game.NativeSettlements.First(s =>
            s.Position.Neighbours().Any(n => FreeWater(game, n)));
        Position water = seed.Position.Neighbours().First(n => FreeWater(game, n));

        // Stage the caravel-with-cargo and the human's gold through the save layer; the settlement's sugar stock and
        // its alarm round-trip too, so the restored game is fully set up for trade.
        SaveGame save = SaveGame.From(game);
        int shipId = game.Units.Max(u => u.Id) + 1;
        int caravelMove = game.Ruleset.Unit("model.unit.caravel").Movement;
        var cargo = shipCargo > 0 ? new Dictionary<string, int> { [Sugar] = shipCargo } : null;
        var staged = new SavedUnit(shipId, "model.unit.caravel", water.X, water.Y, caravelMove,
            Cargo: cargo, OwnerId: humanId);
        List<SavedPlayer> players = save.Players!
            .Select(p => p.PlayerId == humanId ? p with { Gold = humanGold } : p).ToList();
        Game injected = (save with { Units = save.Units.Concat([staged]).ToList(), Players = players })
            .Restore(game.Ruleset);
        SetGame(controller, injected);

        NativeSettlement settlement = injected.NativeSettlements.First(s => s.Id == seed.Id);
        if (settlementSugar > 0)
        {
            settlement.AddGoods(Sugar, settlementSugar); // public goods-store mutator
        }
        injected.ChangeNativeAlarm(settlement, 300); // Content — trade is allowed (public alarm mutator)
        Unit ship = injected.Units.First(u => u.Id == shipId);

        controller.OpenNativeSettlementPanel(settlement, ship);
        await runner.SimulateFrames(1);
        var panel = controller.GetNode<PanelContainer>("UI/NativeSettlementPanel");
        AssertThat(panel.Visible).IsTrue();
        return (runner, controller, injected, settlement, ship);
    }

    /// <summary>A water tile that is free for a ship — in bounds, water, and clear of settlements/colonies/units.</summary>
    private static bool FreeWater(Game game, Position n) =>
        game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater
        && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
        && !game.Units.Any(u => u.IsOnMap && u.Position == n);

    private static Button? FindButton(GameController controller, string name) =>
        controller.GetNode<PanelContainer>("UI/NativeSettlementPanel")
            .FindChild(name, recursive: true, owned: false) as Button;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, game);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
