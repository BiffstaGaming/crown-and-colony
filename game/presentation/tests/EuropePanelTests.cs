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
    private const string Galleon = "model.unit.galleon";
    private const string Colonist = "model.unit.freeColonist";
    private const string TreasureTrain = "model.unit.treasureTrain";
    private const string Sugar = "model.goods.sugar";
    private const string ExpertFarmer = "model.unit.expertFarmer";
    private const string Artillery = "model.unit.artillery";

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
    public async Task MarketBuyButton_BuysGoodsIntoTheHold()
    {
        // A caravel in Europe and a treasury — the goods-market zone trades through the in-port ship.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope)],
            Explored = [], Gold = 1000,
        });
        Unit ship = game.Units[0];
        int goldBefore = game.Gold;

        Button buy = FindButton(controller, "BuyGood_sugar")!; // first tradeable good's market Buy button (×100)
        AssertThat(buy).IsNotNull();
        buy.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.Gold < goldBefore).IsTrue();          // gold was spent
        AssertThat(game.CargoSlotsUsed(ship) > 0).IsTrue();   // goods are now aboard
    }

    [TestCase(Timeout = 60000)]
    public async Task TrainButton_RoutesToTrainUnit_AtTheFlatSpecialistPrice()
    {
        // The train/purchase split (86d3f6…): a specialist routes to TrainUnit (flat price), NOT BuyUnit. An expert
        // farmer's classic price is 1100; training it must debit exactly that with no escalation.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [], Explored = [], Gold = 5000,
        });
        int price = game.EuropeUnitPrice(ExpertFarmer);
        int goldBefore = game.Gold;
        int inEuropeBefore = game.UnitsInEurope.Count();

        // Discriminate the route (finding #6): the OLD assertions passed under BuyUnit too — a specialist is
        // IsPurchasable (Price > 0), and BuyUnit debits the same flat price for a non-artillery type, so neither the
        // gold delta nor the no-escalation check could tell TrainUnit from BuyUnit. The genuine difference is the
        // train/purchase ZONE split (skill > 0 → train, skill ≤ 0 → purchase): the specialist appears ONLY as a
        // Train_ button and NEVER as a Purchase_ one, and the purchase-only artillery never appears as a Train_ button.
        Button train = FindButton(controller, "Train_expertFarmer")!;
        AssertThat(train).IsNotNull();
        AssertThat(FindButton(controller, "Purchase_expertFarmer")).IsNull(); // a specialist is NOT in the purchase zone
        AssertThat(FindButton(controller, "Train_artillery")).IsNull();       // purchase-only artillery is NOT in the train zone

        train.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.UnitsInEurope.Count()).IsEqual(inEuropeBefore + 1);        // a specialist docked
        AssertThat(game.Gold).IsEqual(goldBefore - price);                        // flat specialist price debited
        AssertThat(game.EuropeUnitPrice(ExpertFarmer)).IsEqual(price);            // specialists never escalate
    }

    [TestCase(Timeout = 60000)]
    public async Task TrainButton_IsDisabled_WhenTheTreasuryCannotAffordTheSpecialist()
    {
        // The affordability gate in the FALSE direction (finding #9): with too little gold the should-be-disabled
        // GatedButton must render greyed (Disabled), not vanish — the player still sees the action and its price.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [], Explored = [], Gold = 10, // far below any specialist's price
        });
        await runner.SimulateFrames(1);

        AssertThat(game.CheckTrain(ExpertFarmer).Allowed).IsFalse(); // the engine refuses (can't afford)
        Button train = FindButton(controller, "Train_expertFarmer")!;
        AssertThat(train).IsNotNull();      // still present (greyed, not removed)
        AssertThat(train.Disabled).IsTrue(); // and disabled — the gate is read in the false direction
    }

    [TestCase(Timeout = 60000)]
    public async Task PurchaseButton_RoutesToBuyUnit_AndArtilleryEscalates()
    {
        // Artillery routes to BuyUnit (the escalating path): buying it once docks a unit and ratchets the player's
        // artillery price +100 for the next purchase.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [], Explored = [], Gold = 5000,
        });
        int price = game.EuropeUnitPrice(Artillery);
        int goldBefore = game.Gold;
        int inEuropeBefore = game.UnitsInEurope.Count();

        Button buy = FindButton(controller, "Purchase_artillery")!;
        AssertThat(buy).IsNotNull();
        buy.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.UnitsInEurope.Count()).IsEqual(inEuropeBefore + 1);
        AssertThat(game.Gold).IsEqual(goldBefore - price);
        AssertThat(game.EuropeUnitPrice(Artillery)).IsEqual(price + 100); // escalates +100 for the next buy
    }

    [TestCase(Timeout = 60000)]
    public async Task CashInButton_OnDockTreasureTrain_BanksTheFeeFreeValue_AndConsumesTheTrain()
    {
        // A treasure train carried home and put on the Europe dock (Location InEurope, not aboard): the bug was that
        // such a train fell through every EuropePanel section, so the fee-free cash-in dead-ended. Tax 0 isolates the
        // (waived) King's transport cut, so the full 1000 should bank.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [new SavedUnit(2, TreasureTrain, 0, 0, 0, (int)UnitLocation.InEurope, TreasureAmount: 1000)],
            Explored = [], Tax = 0,
        });
        Unit train = game.Units.First(u => u.Id == 2);
        int goldBefore = game.Gold;

        Button cashIn = FindButton(controller, "CashIn_2")!;
        AssertThat(cashIn).IsNotNull();
        cashIn.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.Gold).IsEqual(goldBefore + 1000);                  // fee-free net banked
        AssertThat(game.Units.Any(u => u.Id == 2)).IsFalse();              // the train left the game
    }

    [TestCase(Timeout = 60000)]
    public async Task CashInButton_TreasureTrainAboardGalleonInEurope_BanksAndFreesTheHold()
    {
        // A treasure train still aboard a galleon docked in Europe (the "carry it home yourself" play) — it can be
        // banked straight from the hold, fee-free, without first putting it on the dock.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units =
            [
                new SavedUnit(1, Galleon, 0, 0, 0, (int)UnitLocation.InEurope),
                // Aboard the galleon (CarrierId set) and so following its Europe location — the classic carried-home play.
                new SavedUnit(2, TreasureTrain, 0, 0, 0, (int)UnitLocation.InEurope, CarrierId: 1, TreasureAmount: 1000),
            ],
            Explored = [], Tax = 0,
        });
        Unit galleon = game.Units.First(u => u.Id == 1);
        Unit train = game.Units.First(u => u.Id == 2);
        AssertThat(train.IsAboard).IsTrue();
        int goldBefore = game.Gold;

        Button cashIn = FindButton(controller, "CashIn_2")!;
        AssertThat(cashIn).IsNotNull();
        cashIn.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.Gold).IsEqual(goldBefore + 1000);                  // fee-free net banked
        AssertThat(game.Units.Any(u => u.Id == 2)).IsFalse();              // the train left the game
        AssertThat(game.Passengers(galleon).Any()).IsFalse();             // and the galleon's hold is freed
    }

    // ── Phase 2: drag-and-drop + ship picker ────────────────────────────────────────────────────────────────────
    // L3 drives the drag CALLBACKS directly (Godot can't synthesise a real mouse-drag headlessly, GdUnit issue): get a
    // source's payload via source._GetDragData(pos), assert the target's target._CanDropData(pos, data), call
    // target._DropData(pos, data), then assert the ENGINE state changed. A position of Vector2.Zero is fine — the
    // EuropePanel sources/targets ignore the position (whole-control hit).

    [TestCase(Timeout = 60000)]
    public async Task DragColonistOntoShip_Boards()
    {
        // A caravel and a colonist, both on the Europe dock — drag the colonist's chip onto the ship card (drop #1).
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 2, MapHeight = 1, Terrain = ["model.tile.plains", "model.tile.ocean"],
            Units =
            [
                new SavedUnit(1, Caravel, 1, 0, 12, (int)UnitLocation.InEurope),
                new SavedUnit(2, Colonist, 0, 0, 3, (int)UnitLocation.InEurope),
            ],
            Explored = [],
        });
        Unit colonist = game.Units.First(u => u.Id == 2);

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "DockColonist_2")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, "ShipDrop_1")!;
        AssertThat(source).IsNotNull();
        AssertThat(target).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsTrue(); // the engine allows boarding
        target._DropData(Vector2.Zero, data);
        await runner.SimulateFrames(1);

        AssertThat(colonist.IsAboard).IsTrue();
        AssertThat(colonist.CarrierId!.Value).IsEqual(1);
    }

    [TestCase(Timeout = 60000)]
    public async Task DragColonistOntoFullShip_IsRefused()
    {
        // The illegal direction: a colonist already filling the caravel's 2 slots, plus a second colonist on the dock —
        // dropping it on the full ship must be refused by _CanDropData (no board, no throw).
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units =
            [
                // A caravel carrying two goods stacks fills both its hold slots (caravel Space = 2) — no room to board.
                new SavedUnit(1, Caravel, 0, 0, 0, (int)UnitLocation.InEurope, 0,
                    new Dictionary<string, int> { [Sugar] = 100, ["model.goods.tobacco"] = 100 }),
                new SavedUnit(2, Colonist, 0, 0, 3, (int)UnitLocation.InEurope),
            ],
            Explored = [],
        });
        Unit colonist = game.Units.First(u => u.Id == 2);
        AssertThat(game.CargoSlotsFree(game.Units.First(u => u.Id == 1))).IsEqual(0); // ship is full

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "DockColonist_2")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, "ShipDrop_1")!;
        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsFalse(); // full ship — refused

        target._DropData(Vector2.Zero, data); // even forced, the re-check no-ops
        await runner.SimulateFrames(1);
        AssertThat(colonist.IsAboard).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    public async Task DragShipOntoSailZone_Sails()
    {
        // Drag a caravel card onto the sail zone (drop #2) → it sails to the New World.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 2, MapHeight = 1, Terrain = ["model.tile.plains", "model.tile.ocean"],
            Units = [new SavedUnit(1, Caravel, 1, 0, 12, (int)UnitLocation.InEurope)],
            Explored = [],
        });
        Unit ship = game.Units.First(u => u.Id == 1);

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "ShipDrag_1")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, "SailDrop")!;
        AssertThat(source).IsNotNull();
        AssertThat(target).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsTrue();
        target._DropData(Vector2.Zero, data);
        await runner.SimulateFrames(1);

        AssertThat(ship.Location).IsEqual(UnitLocation.SailingToNewWorld);
    }

    [TestCase(Timeout = 60000)]
    public async Task DragGoodsOntoShip_Buys()
    {
        // Drag a market goods row onto a ship card (drop #3) → a 100-lot is bought into that ship's hold.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope)],
            Explored = [], Gold = 1000,
        });
        Unit ship = game.Units.First(u => u.Id == 1);
        int goldBefore = game.Gold;

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "MarketGood_sugar")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, "ShipDrop_1")!;
        AssertThat(source).IsNotNull();
        AssertThat(target).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsTrue();
        target._DropData(Vector2.Zero, data);
        await runner.SimulateFrames(1);

        AssertThat(game.Gold < goldBefore).IsTrue();
        AssertThat(ship.CargoOf(Sugar)).IsEqual(100);
    }

    [TestCase(Timeout = 60000)]
    public async Task DragCargoOntoMarket_Sells()
    {
        // Drag a hold cargo stack onto the goods market (drop #4) → the stack is sold, the treasury credited.
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
        Unit ship = game.Units.First(u => u.Id == 1);

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "Cargo_1_sugar")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, "GoodsMarketDrop")!;
        AssertThat(source).IsNotNull();
        AssertThat(target).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsTrue();
        target._DropData(Vector2.Zero, data);
        await runner.SimulateFrames(1);

        AssertThat(game.Gold).IsEqual(200);          // 100 sugar × bid 2, no tax
        AssertThat(ship.CargoOf(Sugar)).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task DragGoodsOntoShip_IsRefused_WhenTheTreasuryCannotAfford()
    {
        // The illegal buy direction: with no gold, dropping a market goods row on a ship must be refused by
        // _CanDropData (CheckBuyEuropeGoods fails on cost) — no buy, no throw.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units = [new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope)],
            Explored = [], Gold = 0, // can't afford a single lot
        });
        Unit ship = game.Units.First(u => u.Id == 1);
        AssertThat(game.CheckBuyEuropeGoods(ship, Sugar, 100).Allowed).IsFalse();

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "MarketGood_sugar")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, "ShipDrop_1")!;
        AssertThat(source).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsFalse(); // unaffordable — refused
        target._DropData(Vector2.Zero, data);                          // forced drop no-ops
        await runner.SimulateFrames(1);
        AssertThat(ship.CargoOf(Sugar)).IsEqual(0);                    // nothing bought
    }

    [TestCase(Timeout = 60000)]
    public async Task DragPassengerOntoDocks_Disembarks()
    {
        // Drag an aboard passenger onto the docks zone (drop #5) → it is put ashore on the dock.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units =
            [
                new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope),
                new SavedUnit(2, Colonist, 0, 0, 3, (int)UnitLocation.InEurope, CarrierId: 1),
            ],
            Explored = [],
        });
        Unit colonist = game.Units.First(u => u.Id == 2);
        AssertThat(colonist.IsAboard).IsTrue();

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "Passenger_2")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, "DocksDrop")!;
        AssertThat(source).IsNotNull();
        AssertThat(target).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsTrue();
        target._DropData(Vector2.Zero, data);
        await runner.SimulateFrames(1);

        AssertThat(colonist.IsAboard).IsFalse();
        AssertThat(colonist.Location).IsEqual(UnitLocation.InEurope);
    }

    [TestCase(Timeout = 60000)]
    public async Task ShipPicker_BuyLandsInTheSelectedShipNotTheFirst()
    {
        // Two ships in port (A=1, B=2). The market defaults to the first (A); selecting B then buying via the market
        // Buy button must load B's hold, not A's.
        (ISceneRunner runner, GameController controller, Game game) = await OpenEurope(new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.highSeas"],
            Units =
            [
                new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope),
                new SavedUnit(2, Galleon, 0, 0, 12, (int)UnitLocation.InEurope),
            ],
            Explored = [], Gold = 5000,
        });
        Unit shipA = game.Units.First(u => u.Id == 1);
        Unit shipB = game.Units.First(u => u.Id == 2);

        // Select ship B via its Select button.
        Button selectB = FindButton(controller, "Select_2")!;
        AssertThat(selectB).IsNotNull();
        selectB.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // Now buy a sugar lot through the market — it must land in B (the selected ship), not A.
        Button buy = FindButton(controller, "BuyGood_sugar")!;
        AssertThat(buy).IsNotNull();
        buy.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(shipB.CargoOf(Sugar)).IsEqual(100); // landed in the SELECTED ship
        AssertThat(shipA.CargoOf(Sugar)).IsEqual(0);   // not the default first ship
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

    /// <summary>Finds the first drag-source / drop-target control of type <typeparamref name="T"/> whose name starts with <paramref name="namePrefix"/> (Phase 2 drag-drop).</summary>
    private static T? FindControl<T>(GameController controller, string namePrefix) where T : Node =>
        controller.GetNode<PanelContainer>("UI/EuropePanel")
            .FindChildren("*", recursive: true, owned: false)
            .OfType<T>()
            .FirstOrDefault(c => c.Name.ToString().StartsWith(namePrefix));
}
