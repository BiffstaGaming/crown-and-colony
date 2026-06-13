using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Unit transport (Phase 4 slice 5): ships carry colonists. Capacity and carry-cost
/// are pinned to FreeCol (<c>UnitType.space</c>/<c>spaceTaken</c>, <c>getCargoCapacity</c>,
/// <c>GoodsContainer.CARGO_SIZE=100</c>); a colonist takes one slot, a caravel holds two.
/// </summary>
public class TransportTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Caravel = "model.unit.caravel";
    private const string Colonist = "model.unit.freeColonist";
    private const string Sugar = "model.goods.sugar";

    private static Game GameOn(string[] terrain, int w, int h, SavedUnit[] units,
        SavedColony[]? colonies = null, int immigration = 0)
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
            Immigration = immigration,
        };
        return save.Restore(Classic);
    }

    // ───────────────────────── spec parsing ─────────────────────────

    [Fact]
    public void UnitType_ReadsCapacity_AndCarryCost()
    {
        UnitType caravel = Classic.Unit(Caravel);
        Assert.Equal(2, caravel.Space);     // caravel holds two
        Assert.True(caravel.IsCarrier);

        UnitType colonist = Classic.Unit(Colonist);
        Assert.Equal(0, colonist.Space);
        Assert.False(colonist.IsCarrier);
        Assert.Equal(1, colonist.CarrySlots); // a colonist takes one slot
    }

    // ───────────────────────── boarding ─────────────────────────

    [Fact]
    public void BoardInEurope_LoadsThePassenger_AndUsesASlot()
    {
        Game game = GameOn(["model.tile.highSeas"], 1, 1,
            [new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope),
             new SavedUnit(2, Colonist, 0, 0, 3, (int)UnitLocation.InEurope)]);
        Unit ship = game.Units[0];
        Unit colonist = game.Units[1];

        game.Board(colonist, ship);

        Assert.Equal(ship.Id, colonist.CarrierId);
        Assert.True(colonist.IsAboard);
        Assert.False(colonist.IsOnMap);
        Assert.Equal(1, game.CargoSlotsUsed(ship));
        Assert.Equal(1, game.CargoSlotsFree(ship)); // caravel holds 2
        Assert.Contains(colonist, game.Passengers(ship));
        Assert.Equal(0, colonist.MovementLeft);     // boarding ends the turn
    }

    [Fact]
    public void BoardOnMap_RequiresAdjacencyToTheShip()
    {
        // plains(0) ocean(1) ocean(2) plains(3): ship on (1); (0) is adjacent, (3) is not.
        Game game = GameOn(
            ["model.tile.plains", "model.tile.ocean", "model.tile.ocean", "model.tile.plains"], 4, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12),
             new SavedUnit(2, Colonist, 0, 0, 3),
             new SavedUnit(3, Colonist, 3, 0, 3)]);
        Unit ship = game.Units[0];

        Assert.True(game.CheckBoard(game.Units[1], ship).Allowed);  // (0,0) adjacent to (1,0)
        Assert.False(game.CheckBoard(game.Units[2], ship).Allowed); // (3,0) not adjacent to (1,0)
    }

    [Fact]
    public void Capacity_IsSharedBetweenGoodsAndPassengers()
    {
        Game game = GameOn(["model.tile.highSeas"], 1, 1,
            [new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope),
             new SavedUnit(2, Colonist, 0, 0, 3, (int)UnitLocation.InEurope),
             new SavedUnit(3, Colonist, 0, 0, 3, (int)UnitLocation.InEurope)]);
        Unit ship = game.Units[0];

        ship.AddCargo(Sugar, 100);                  // one slot of goods
        Assert.Equal(1, game.CargoSlotsUsed(ship));

        game.Board(game.Units[1], ship);            // + one passenger = full (2/2)
        Assert.Equal(0, game.CargoSlotsFree(ship));
        Assert.False(game.CheckBoard(game.Units[2], ship).Allowed); // no room for a second
    }

    [Fact]
    public void Capacity_TwoColonistsFillACaravel()
    {
        Game game = GameOn(["model.tile.highSeas"], 1, 1,
            [new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope),
             new SavedUnit(2, Colonist, 0, 0, 3, (int)UnitLocation.InEurope),
             new SavedUnit(3, Colonist, 0, 0, 3, (int)UnitLocation.InEurope),
             new SavedUnit(4, Colonist, 0, 0, 3, (int)UnitLocation.InEurope)]);
        Unit ship = game.Units[0];

        game.Board(game.Units[1], ship);
        game.Board(game.Units[2], ship);
        Assert.Equal(2, game.Passengers(ship).Count());
        Assert.Throws<InvalidMoveException>(() => game.Board(game.Units[3], ship)); // third over capacity
    }

    [Fact]
    public void CarriedUnit_CannotMove_Sail_OrFound()
    {
        Game game = GameOn(["model.tile.plains", "model.tile.ocean"], 2, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12), new SavedUnit(2, Colonist, 0, 0, 3)]);
        Unit ship = game.Units[0];
        Unit colonist = game.Units[1];
        game.Board(colonist, ship);

        Assert.False(game.CheckMove(colonist, new Position(0, 0)).Allowed);
        Assert.False(game.CheckSailToEurope(colonist).Allowed);
        Assert.False(game.CheckFoundColony(colonist).Allowed);
    }

    // ───────────────────────── disembarking ─────────────────────────

    [Fact]
    public void Disembark_PutsAColonistAshore_OnAdjacentLand()
    {
        Game game = GameOn(["model.tile.plains", "model.tile.ocean"], 2, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12), new SavedUnit(2, Colonist, 0, 0, 3)]);
        Unit ship = game.Units[0];
        Unit colonist = game.Units[1];
        game.Board(colonist, ship);

        game.Disembark(colonist, new Position(0, 0));

        Assert.Null(colonist.CarrierId);
        Assert.True(colonist.IsOnMap);
        Assert.Equal(new Position(0, 0), colonist.Position);
        Assert.Empty(game.Passengers(ship));
    }

    [Fact]
    public void Disembark_Rejects_Water_NonAdjacent_AndShipInEurope()
    {
        Game game = GameOn(["model.tile.plains", "model.tile.ocean", "model.tile.ocean"], 3, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12), new SavedUnit(2, Colonist, 0, 0, 3)]);
        Unit ship = game.Units[0];
        Unit colonist = game.Units[1];
        game.Board(colonist, ship);

        Assert.False(game.CheckDisembark(colonist, new Position(2, 0)).Allowed); // water
        Assert.False(game.CheckDisembark(colonist, new Position(1, 0)).Allowed); // ship's own (water) tile

        // Ship in Europe: cannot put ashore onto the map.
        ship.Location = UnitLocation.InEurope;
        Assert.False(game.CheckDisembark(colonist, new Position(0, 0)).Allowed);
    }

    [Fact]
    public void DisembarkToDock_ReturnsARecruitToTheEuropeDock()
    {
        Game game = GameOn(["model.tile.highSeas"], 1, 1,
            [new SavedUnit(1, Caravel, 0, 0, 12, (int)UnitLocation.InEurope),
             new SavedUnit(2, Colonist, 0, 0, 3, (int)UnitLocation.InEurope)]);
        Unit ship = game.Units[0];
        Unit colonist = game.Units[1];
        game.Board(colonist, ship);

        game.DisembarkToDock(colonist);

        Assert.Null(colonist.CarrierId);
        Assert.Equal(UnitLocation.InEurope, colonist.Location);
        Assert.Empty(game.Passengers(ship));
    }

    // ───────────────────────── interaction with immigration ─────────────────────────

    [Fact]
    public void BoardingAShip_StopsAPersonSuppressingImmigration()
    {
        // A colony makes 1 cross/turn; one colonist idles in Europe with a docked caravel.
        Game game = GameOn(["model.tile.plains", "model.tile.ocean", "model.tile.highSeas"], 3, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12, (int)UnitLocation.InEurope),
             new SavedUnit(2, Colonist, 0, 0, 3, (int)UnitLocation.InEurope)],
            [new SavedColony(1, "Port", 0, 0, 1)]); // null buildings → free base incl. chapel
        Unit ship = game.Units[0];
        Unit colonist = game.Units[1];

        game.EndTurn(); // 1 cross + (1 person in Europe: −4) + 2 → clamped to net 0
        Assert.Equal(0, game.Immigration);

        game.Board(colonist, ship); // the colonist is now aboard, off the dock

        game.EndTurn(); // 1 cross + (0 persons on the dock) + 2 = 3
        Assert.Equal(3, game.Immigration);
    }

    // ───────────────────────── persistence ─────────────────────────

    [Fact]
    public void SaveRoundTrip_PreservesAPassengerAboard()
    {
        Game game = GameOn(["model.tile.plains", "model.tile.ocean"], 2, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12), new SavedUnit(2, Colonist, 0, 0, 3)]);
        game.Board(game.Units[1], game.Units[0]);

        string json = SaveGame.From(game).ToJson();
        Game loaded = SaveGame.FromJson(json).Restore(Classic);

        Unit loadedColonist = loaded.Units.First(u => u.Id == 2);
        Assert.Equal(1, loadedColonist.CarrierId);
        Assert.Single(loaded.Passengers(loaded.Units.First(u => u.Id == 1)));
        Assert.Equal(json, SaveGame.From(loaded).ToJson()); // byte-identical
    }

    [Fact]
    public void PreV13Save_LoadsWithNoPassengers()
    {
        Game game = GameOn(["model.tile.plains", "model.tile.ocean"], 2, 1,
            [new SavedUnit(1, Caravel, 1, 0, 12), new SavedUnit(2, Colonist, 0, 0, 3)]);
        SaveGame v12 = SaveGame.From(game) with
        {
            Version = 12,
            Units = game.Units
                .Select(u => new SavedUnit(u.Id, u.Type.Id, u.Position.X, u.Position.Y,
                    u.MovementLeft, (int)u.Location, u.SailTurnsRemaining, null, null))
                .ToList(),
        };

        Game loaded = SaveGame.FromJson(v12.ToJson()).Restore(Classic);

        Assert.All(loaded.Units, u => Assert.Null(u.CarrierId));
    }
}
