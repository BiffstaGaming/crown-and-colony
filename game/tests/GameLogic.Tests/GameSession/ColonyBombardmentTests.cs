using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Colony fort/fortress bombardment (<c>86d3c9tkk</c>, FreeCol <c>ServerPlayer.csBombardEnemyShips</c>): at the
/// start of a colonial player's turn, a coastal colony with a fort/fortress and artillery on its tile fires on
/// adjacent enemy-or-pirate ships — one-sided, no counterattack; the ship is damaged or sunk. Reuses the naval
/// damage/sink path; deterministic on the owner's stream.
/// </summary>
public class ColonyBombardmentTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Fort = "model.building.fort";
    private const string Stockade = "model.building.stockade";
    private const string Artillery = "model.unit.artillery";
    private const string Caravel = "model.unit.caravel";
    private const string Privateer = "model.unit.privateer";

    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    /// <summary>A human coastal colony (a land tile with an adjacent water tile), and that water tile.</summary>
    private static (Game game, Colony colony, Position water) CoastalColony()
    {
        Game game = Game.New(Classic, Seed);
        (Position land, Position water) = (from p in game.Map.AllPositions()
            where game.Map.TerrainAt(p).CanSettle && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
                && !game.Units.Any(u => u.IsOnMap && u.Position == p)
                && !p.Neighbours().Any(n => game.Map.InBounds(n) && game.ColonyAt(n) is not null)
            from w in p.Neighbours()
            where game.Map.InBounds(w) && game.Map.TerrainAt(w).IsWater
            select (p, w)).First();
        Unit founder = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), land);
        Colony colony = game.FoundColony(founder);
        return (game, colony, water);
    }

    private static Player Foreign(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

    private static Unit EnemyShipAtWar(Game game, Position water, string shipType = Caravel)
    {
        Player power = Foreign(game);
        game.SetStance(game.HumanPlayer.PlayerId, power.PlayerId, Stance.War);
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
        return game.SpawnUnit(Classic.Unit(shipType), water, power.PlayerId);
    }

    // ---- Ruleset parse ----

    [Fact]
    public void FortAndFortress_GrantBombardShips_StockadeDoesNot()
    {
        Assert.True(Classic.Building(Fort).BombardsShips);
        Assert.True(Classic.Building("model.building.fortress").BombardsShips); // inherits via extends
        Assert.False(Classic.Building(Stockade).BombardsShips);
    }

    // ---- Bombardment ----

    [Fact]
    public void AFortWithArtillery_SinksAnAdjacentEnemyShip()
    {
        (Game game, Colony colony, Position water) = CoastalColony();
        colony.AddBuilding(Fort);
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position); // human-owned, on the colony tile
        Unit ship = EnemyShipAtWar(game, water);

        game.BombardEnemyShips(game.HumanPlayer, new FixedRandom(0.0)); // great win → sunk

        Assert.DoesNotContain(game.Units, u => u.Id == ship.Id);
    }

    [Fact]
    public void AFortWithArtillery_DamagesAnEnemyShip_OnANonGreatHit()
    {
        (Game game, Colony colony, Position water) = CoastalColony();
        colony.AddBuilding(Fort);
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position);
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position); // power 14 → a high win chance
        Unit ship = EnemyShipAtWar(game, water);

        game.BombardEnemyShips(game.HumanPlayer, new FixedRandom(0.5)); // a win, not great → damaged

        Unit damaged = game.Units.Single(u => u.Id == ship.Id);
        Assert.True(damaged.IsUnderRepair);          // limped off to repair
        Assert.False(damaged.IsOnMap);               // no longer a healthy ship at the bombarded tile (it went to Europe — no foreign drydock)
    }

    [Fact]
    public void AMiss_LeavesTheShipUnharmed_NoCounterattack()
    {
        (Game game, Colony colony, Position water) = CoastalColony();
        colony.AddBuilding(Fort);
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position);
        Unit ship = EnemyShipAtWar(game, water);

        game.BombardEnemyShips(game.HumanPlayer, new FixedRandom(0.99)); // a loss → no effect

        Unit untouched = game.Units.Single(u => u.Id == ship.Id);
        Assert.False(untouched.IsUnderRepair);
        Assert.Equal(water, untouched.Position);
    }

    [Fact]
    public void AFortWithoutArtillery_DoesNotBombard()
    {
        (Game game, Colony colony, Position water) = CoastalColony();
        colony.AddBuilding(Fort); // no artillery on the tile → power 0
        Unit ship = EnemyShipAtWar(game, water);

        game.BombardEnemyShips(game.HumanPlayer, new FixedRandom(0.0));

        Assert.Contains(game.Units, u => u.Id == ship.Id && u.Position == water && !u.IsUnderRepair);
    }

    [Fact]
    public void AColonyWithoutAFort_DoesNotBombard()
    {
        (Game game, Colony colony, Position water) = CoastalColony();
        colony.AddBuilding(Stockade); // a stockade does not grant bombardShips
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position);
        Unit ship = EnemyShipAtWar(game, water);

        game.BombardEnemyShips(game.HumanPlayer, new FixedRandom(0.0));

        Assert.Contains(game.Units, u => u.Id == ship.Id && u.Position == water && !u.IsUnderRepair);
    }

    [Fact]
    public void AShipAtPeace_IsNotBombarded_ButAPrivateerIs()
    {
        (Game game, Colony colony, Position water) = CoastalColony();
        colony.AddBuilding(Fort);
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position);
        Player power = Foreign(game); // default stance is not war (no SetStance)
        Unit trader = game.SpawnUnit(Classic.Unit(Caravel), water, power.PlayerId);

        game.BombardEnemyShips(game.HumanPlayer, new FixedRandom(0.0));
        Assert.Contains(game.Units, u => u.Id == trader.Id); // a peaceful trader is left alone

        Position water2 = colony.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater && n != water);
        Unit pirate = game.SpawnUnit(Classic.Unit(Privateer), water2, power.PlayerId); // a privateer flies no flag
        game.BombardEnemyShips(game.HumanPlayer, new FixedRandom(0.0));
        Assert.DoesNotContain(game.Units, u => u.Id == pirate.Id); // a privateer is fair game at peace
    }

    [Fact]
    public void OwnShips_AreNeverBombarded()
    {
        (Game game, Colony colony, Position water) = CoastalColony();
        colony.AddBuilding(Fort);
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position);
        Unit own = game.SpawnUnit(Classic.Unit(Caravel), water); // human-owned (the colony owner)

        game.BombardEnemyShips(game.HumanPlayer, new FixedRandom(0.0));

        Assert.Contains(game.Units, u => u.Id == own.Id && u.Position == water);
    }
}
