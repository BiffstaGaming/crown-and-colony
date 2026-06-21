using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

public class SailingTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Caravel = "model.unit.caravel";
    private const string Sugar = "model.goods.sugar";
    private const string Tools = "model.goods.tools";

    /// <summary>Builds a game on a hand-crafted map via the save/restore path.</summary>
    private static Game GameOn(string[] terrain, int w, int h, SavedUnit[] units, SavedColony[]? colonies = null)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = w,
            MapHeight = h,
            Terrain = terrain,
            Units = units,
            Explored = [],
            Colonies = colonies ?? [],
        };
        return save.Restore(Classic);
    }

    [Fact]
    public void SailToEurope_TakesThreeTurns()
    {
        // highSeas at (1,0); a caravel parked there.
        Game game = GameOn(["model.tile.ocean", "model.tile.highSeas"], 2, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12)]);
        Unit ship = game.Units[0];

        game.SailToEurope(ship);
        Assert.Equal(UnitLocation.SailingToEurope, ship.Location);
        Assert.Equal(3, ship.SailTurnsRemaining);

        game.EndTurn();
        game.EndTurn();
        Assert.Equal(UnitLocation.SailingToEurope, ship.Location); // still at sea after 2

        game.EndTurn();
        Assert.Equal(UnitLocation.InEurope, ship.Location);        // arrives on the 3rd
    }

    [Fact]
    public void CheckSailToEurope_OnlyShipsOnTheHighSeas()
    {
        Game game = GameOn(["model.tile.plains", "model.tile.ocean", "model.tile.highSeas"], 3, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12), new SavedUnit(2, "model.unit.freeColonist", 0, 0, 3)]);
        Unit ship = game.Units[0];
        Unit colonist = game.Units[1];

        Assert.False(game.CheckSailToEurope(colonist).Allowed);   // not a ship
        Assert.False(game.CheckSailToEurope(ship).Allowed);       // on ocean, not high seas

        game.MoveUnit(ship, new Position(2, 0));                   // to the high seas
        Assert.True(game.CheckSailToEurope(ship).Allowed);
    }

    [Fact]
    public void CheckSailToEurope_RefusesAShipUnderRepair()
    {
        // A ship on the high seas is ready to sail — until it is under forced repair, which blocks acting (consistent
        // with SailToNewWorld/CheckBoard/CheckBuyEuropeGoods; FreeCol isReadyToTrade).
        Game game = GameOn(["model.tile.highSeas"], 1, 1, [new SavedUnit(1, Caravel, 0, 0, 12)]);
        Unit ship = game.Units[0];
        Assert.True(game.CheckSailToEurope(ship).Allowed);

        ship.RepairTurnsRemaining = 3;
        Assert.False(game.CheckSailToEurope(ship).Allowed);
    }

    [Fact]
    public void LoadFromColony_MovesGoodsToTheHold()
    {
        Game game = GameOn(["model.tile.plains", "model.tile.ocean"], 2, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12)],
            [new SavedColony(1, "Port", 0, 0, 1, new Dictionary<string, int> { [Sugar] = 100 })]);
        Unit ship = game.Units[0];
        Colony colony = game.Colonies[0];

        game.LoadFromColony(ship, colony, Sugar, 60);
        Assert.Equal(60, ship.CargoOf(Sugar));
        Assert.Equal(40, colony.StoreOf(Sugar));

        Assert.Throws<InvalidMoveException>(() => game.LoadFromColony(ship, colony, Sugar, 100)); // only 40 left
    }

    // ---- Wagon-train haulage (86d3c9t3g): a land carrier moves goods colony-to-colony ----

    [Fact]
    public void WagonTrain_HaulsGoodsBetweenColoniesOverland()
    {
        // Two adjacent plains colonies; a wagon train (a land carrier, space 2) starts on Alpha's tile.
        Game game = GameOn(["model.tile.plains", "model.tile.plains"], 2, 1,
            [new SavedUnit(1, "model.unit.wagonTrain", 0, 0, 12)],
            [new SavedColony(1, "Alpha", 0, 0, 1, new Dictionary<string, int> { [Sugar] = 100 }),
             new SavedColony(2, "Beta", 1, 0, 1)]);
        Unit wagon = game.Units[0];
        Colony alpha = game.Colonies[0];
        Colony beta = game.Colonies[1];

        game.LoadFromColony(wagon, alpha, Sugar, 60);  // load at Alpha (same tile)
        Assert.Equal(60, wagon.CargoOf(Sugar));
        Assert.Equal(40, alpha.StoreOf(Sugar));

        game.MoveUnit(wagon, new Position(1, 0));       // haul overland to Beta's tile — the cargo travels with it
        Assert.Equal(60, wagon.CargoOf(Sugar));

        game.UnloadToColony(wagon, beta, Sugar, 60);    // deliver to Beta
        Assert.Equal(0, wagon.CargoOf(Sugar));
        Assert.Equal(60, beta.StoreOf(Sugar));
        Assert.Equal(40, alpha.StoreOf(Sugar));
    }

    [Fact]
    public void ANonCarrier_CannotLoadOrUnloadGoods()
    {
        Game game = GameOn(["model.tile.plains"], 1, 1,
            [new SavedUnit(1, "model.unit.freeColonist", 0, 0, 3)],
            [new SavedColony(1, "Port", 0, 0, 1, new Dictionary<string, int> { [Sugar] = 100 })]);
        Unit colonist = game.Units[0]; // space 0 → not a carrier
        Colony colony = game.Colonies[0];

        Assert.Throws<InvalidMoveException>(() => game.LoadFromColony(colonist, colony, Sugar, 10));
        Assert.Throws<InvalidMoveException>(() => game.UnloadToColony(colonist, colony, Sugar, 10));
    }

    [Fact]
    public void SellShipCargo_InEurope_CreditsTreasury_AndMovesMarket()
    {
        Game game = GameOn(["model.tile.highSeas"], 1, 1, [new SavedUnit(1, Caravel, 0, 0, 12)]);
        Unit ship = game.Units[0];
        ship.Location = UnitLocation.InEurope;
        ship.AddCargo(Sugar, 100);
        int amountBefore = game.Market.AmountInMarket(Sugar);

        int credited = game.SellShipCargo(ship, Sugar, 100);

        Assert.Equal(100 * 2, credited);                // 100 sugar × bid 2, no tax
        Assert.Equal(credited, game.Gold);
        Assert.Equal(0, ship.CargoOf(Sugar));
        Assert.True(game.Market.AmountInMarket(Sugar) > amountBefore);

        // A ship still at sea cannot trade.
        Unit atSea = game.Units[0];
        atSea.Location = UnitLocation.SailingToEurope;
        Assert.Throws<InvalidMoveException>(() => game.SellShipCargo(atSea, Sugar, 1));
    }

    [Fact]
    public void BuyEuropeGoods_DebitsGoldAtTheAskPrice_AndDrainsTheMarket()
    {
        // A docked ship and a treasury of 1000 gold (player state lives in Players[] from save v20).
        SaveGame seed = SaveGame.From(
            GameOn(["model.tile.highSeas"], 1, 1, [new SavedUnit(1, Caravel, 0, 0, 12)]));
        seed = seed with { Players = seed.Players!.Select(p => p with { Gold = 1000 }).ToList() };
        Game game = seed.Restore(Classic);
        Unit ship = game.Units[0];
        ship.Location = UnitLocation.InEurope;

        int ask = game.Market.AskPrice(Tools);                  // tools ask = 3
        int amountBefore = game.Market.AmountInMarket(Tools);

        int spent = game.BuyEuropeGoods(ship, Tools, 10);       // one sub-chunk → priced at the current ask

        Assert.Equal(ask * 10, spent);
        Assert.Equal(1000 - spent, game.Gold);
        Assert.Equal(10, ship.CargoOf(Tools));
        Assert.True(game.Market.AmountInMarket(Tools) < amountBefore); // buying removes supply (the price-rise detail is in MarketTests)
        Assert.True(game.Market.AskPrice(Tools) >= ask);              // buying never lowers the ask

        Assert.False(game.CheckBuyEuropeGoods(ship, Tools, 10_000).Allowed); // not enough gold (chunked cost climbs past 1000)
    }

    [Fact]
    public void SailToNewWorld_ReturnsTheShipToItsDepartureTile()
    {
        Game game = GameOn(["model.tile.ocean", "model.tile.highSeas"], 2, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12)]);
        Unit ship = game.Units[0];
        game.SailToEurope(ship);
        for (int i = 0; i < 3; i++) game.EndTurn();
        Assert.Equal(UnitLocation.InEurope, ship.Location);

        game.SailToNewWorld(ship);
        Assert.Equal(UnitLocation.SailingToNewWorld, ship.Location);
        for (int i = 0; i < 3; i++) game.EndTurn();

        Assert.True(ship.IsOnMap);
        Assert.Equal(new Position(1, 0), ship.Position); // re-enters where it left
    }

    [Fact]
    public void OffMapUnits_CannotBeMoved()
    {
        Game game = GameOn(["model.tile.highSeas", "model.tile.ocean"], 2, 1,
            [new SavedUnit(1, Caravel, 0, 0, 12)]);
        Unit ship = game.Units[0];
        ship.Location = UnitLocation.InEurope;

        Assert.False(game.CheckMove(ship, new Position(1, 0)).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.MoveUnit(ship, new Position(1, 0)));
    }

    [Fact]
    public void SaveRoundTrip_MidVoyage_PreservesSailStateAndCargo()
    {
        Game game = GameOn(["model.tile.ocean", "model.tile.highSeas"], 2, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12)]);
        Unit ship = game.Units[0];
        ship.AddCargo(Sugar, 50);
        game.SailToEurope(ship);
        game.EndTurn(); // 2 turns remaining, cargo aboard

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Unit r = loaded.Units[0];
        Assert.Equal(UnitLocation.SailingToEurope, r.Location);
        Assert.Equal(ship.SailTurnsRemaining, r.SailTurnsRemaining);
        Assert.Equal(50, r.CargoOf(Sugar));

        // Continuing the voyage arrives identically.
        loaded.EndTurn();
        loaded.EndTurn();
        Assert.Equal(UnitLocation.InEurope, loaded.Units[0].Location);
    }

    [Fact]
    public void FullTradeVoyage_LoadSailSellReturn()
    {
        // The whole loop on a coast → ocean → high-seas strip.
        Game game = GameOn(
            ["model.tile.plains", "model.tile.ocean", "model.tile.ocean", "model.tile.ocean", "model.tile.highSeas"],
            5, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12)],
            [new SavedColony(1, "Port", 0, 0, 1, new Dictionary<string, int> { [Sugar] = 100 })]);
        Unit ship = game.Units[0];
        Colony colony = game.Colonies[0];

        // Load at the colony, sail to the high-seas edge.
        game.LoadFromColony(ship, colony, Sugar, 100);
        game.MoveUnit(ship, new Position(2, 0));
        game.MoveUnit(ship, new Position(3, 0));
        game.MoveUnit(ship, new Position(4, 0));
        Assert.True(game.CheckSailToEurope(ship).Allowed);

        game.SailToEurope(ship);
        for (int i = 0; i < 3; i++) game.EndTurn();
        Assert.Equal(UnitLocation.InEurope, ship.Location);

        // Sell the cargo for gold, then sail home.
        int credited = game.SellShipCargo(ship, Sugar, 100);
        Assert.Equal(200, credited);            // 100 × bid 2
        Assert.Equal(200, game.Gold);
        Assert.Equal(0, ship.CargoOf(Sugar));

        game.SailToNewWorld(ship);
        for (int i = 0; i < 3; i++) game.EndTurn();
        Assert.True(ship.IsOnMap);
        Assert.Equal(new Position(4, 0), ship.Position);
    }
}
