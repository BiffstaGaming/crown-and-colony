using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Colonies;

/// <summary>
/// Drydock-colony ship repair (<c>86d3c9p0q</c> follow-on, FreeCol <c>Unit.getRepairLocation</c>): a ship
/// beaten in naval combat limps to the nearest owned colony with a drydock/shipyard — repairing on the map in
/// its own port — instead of all the way to Europe. With no such colony it still repairs in Europe.
/// </summary>
public class DrydockRepairTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Caravel = "model.unit.caravel";
    private const string Frigate = "model.unit.frigate";
    private const string Docks = "model.building.docks";
    private const string Drydock = "model.building.drydock";

    /// <summary>A fixed RNG (same NextDouble each call) — forces a chosen combat band.</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    // ---- Ruleset parse: which buildings repair ships ----

    [Theory]
    [InlineData("model.building.drydock", true)]
    [InlineData("model.building.shipyard", true)]   // extends drydock → inherits repairUnits
    [InlineData("model.building.docks", false)]
    [InlineData("model.building.warehouse", false)]
    public void RepairsNavalUnits_IsGrantedByDrydockAndShipyardOnly(string buildingId, bool expected)
    {
        Assert.Equal(expected, Classic.Building(buildingId).RepairsNavalUnits);
    }

    // ---- Repair location ----

    [Fact]
    public void DamagedShip_WithAHomeDrydock_RepairsOnTheMapBesideIt()
    {
        (Game game, Unit humanShip, Unit frigate, Colony drydockColony) = StageWithRepairColony(withDrydock: true);

        game.Attack(frigate, humanShip.Position, new FixedRandom(0.5)); // frigate wins → human ship damaged

        Assert.True(humanShip.IsUnderRepair);
        Assert.Equal(UnitLocation.OnMap, humanShip.Location);                      // not shipped off to Europe
        Assert.True(game.Map.TerrainAt(humanShip.Position).IsWater);              // sits in the water by its port
        Assert.Contains(humanShip.Position, drydockColony.Position.Neighbours()); // right beside the drydock colony
        Assert.Equal(5, humanShip.RepairTurnsRemaining);
    }

    [Fact]
    public void DamagedShip_WithoutADrydock_StillRepairsInEurope()
    {
        // A coastal colony that only has docks (no drydock) cannot repair ships.
        (Game game, Unit humanShip, Unit frigate, _) = StageWithRepairColony(withDrydock: false);

        game.Attack(frigate, humanShip.Position, new FixedRandom(0.5));

        Assert.True(humanShip.IsUnderRepair);
        Assert.Equal(UnitLocation.InEurope, humanShip.Location);
    }

    [Fact]
    public void ShipRepairingAtItsDrydock_ReturnsToServiceOnTheMap()
    {
        (Game game, Unit humanShip, Unit frigate, _) = StageWithRepairColony(withDrydock: true);
        game.Attack(frigate, humanShip.Position, new FixedRandom(0.5));
        frigate.Location = UnitLocation.InEurope; // park the attacker off-map so it can't harry the repairing ship

        for (int turn = 1; turn <= 5; turn++)
        {
            game.EndTurn();
        }

        Assert.False(humanShip.IsUnderRepair);
        Assert.Equal(UnitLocation.OnMap, humanShip.Location);
        Assert.True(humanShip.MovementLeft > 0); // healed and free to act on the map (no sailing home needed)
    }

    [Fact]
    public void OnMapRepairState_SurvivesASaveRoundTrip()
    {
        (Game game, Unit humanShip, Unit frigate, _) = StageWithRepairColony(withDrydock: true);
        game.Attack(frigate, humanShip.Position, new FixedRandom(0.5));
        int shipId = humanShip.Id;
        Position berth = humanShip.Position;

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Unit reloaded = restored.Units.First(u => u.Id == shipId);
        Assert.Equal(5, reloaded.RepairTurnsRemaining);
        Assert.Equal(UnitLocation.OnMap, reloaded.Location);
        Assert.Equal(berth, reloaded.Position);
    }

    // ---- Fixture ----

    /// <summary>
    /// A human caravel on water beside a foreign frigate poised to beat it, with a human colony (optionally
    /// holding a drydock) on the adjacent coast. The frigate's win damages the caravel.
    /// </summary>
    private static (Game game, Unit humanShip, Unit frigate, Colony colony) StageWithRepairColony(bool withDrydock)
    {
        Game game = Game.New(Classic, Seed);

        bool Water(Position p) => game.Map.InBounds(p) && game.Map.TerrainAt(p).IsWater;
        bool Free(Position p) =>
            game.Map.InBounds(p) && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p);
        bool SettleableLand(Position p) =>
            Free(p) && !game.Map.TerrainAt(p).IsWater && game.Map.TerrainAt(p).CanSettle
            && p.Neighbours().All(n => game.ColonyAt(n) is null); // founding is barred adjacent to a colony

        // A settleable land tile with a free water neighbour (the caravel's berth) that itself has another free
        // water neighbour (the attacking frigate's tile).
        Position land = game.Map.AllPositions().First(p =>
            SettleableLand(p)
            && p.Neighbours().Any(w => Water(w) && Free(w)
                && w.Neighbours().Any(f => f != p && Water(f) && Free(f))));
        Position ship = land.Neighbours().First(w => Water(w) && Free(w)
            && w.Neighbours().Any(f => f != land && Water(f) && Free(f)));
        Position frigatePos = ship.Neighbours().First(f => f != land && Water(f) && Free(f));

        Unit founder = game.SpawnUnit(Classic.Unit(FreeColonist), land);
        Colony colony = game.FoundColony(founder);
        colony.AddBuilding(Docks);
        if (withDrydock)
        {
            colony.AddBuilding(Drydock);
        }

        Unit humanShip = game.SpawnUnit(Classic.Unit(Caravel), ship); // human (OwnerId 0)
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        Unit frigate = game.SpawnUnit(Classic.Unit(Frigate), frigatePos);
        frigate.OwnerId = foreignId;
        return (game, humanShip, frigate, colony);
    }
}
