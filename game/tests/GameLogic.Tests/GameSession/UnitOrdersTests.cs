using System.Linq;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Standing unit orders (<c>86d3c9pfh</c>): fortify (dig in for +50% defence — FreeCol FORTIFYING → FORTIFIED),
/// sentry (rest), clear-orders, and disband (permanent removal). The order state persists across save/load
/// (v23) and the fortify bonus flows into the real combat path.
/// </summary>
public class UnitOrdersTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Caravel = "model.unit.caravel";

    /// <summary>A fixed RNG returning the same NextDouble — forces a chosen combat band.</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    // ---- Fortify ----

    [Fact]
    public void Fortify_SetsFortifying_AndSpendsTheTurn()
    {
        var game = Game.New(Classic, Seed);
        Unit unit = game.PlayerUnits.First();

        Assert.True(game.CheckFortify(unit).Allowed);
        game.Fortify(unit);

        Assert.Equal(UnitOrders.Fortifying, unit.Orders);
        Assert.Equal(0, unit.MovementLeft); // digging in consumes the turn
        Assert.False(unit.IsFortified);      // not yet — it completes next turn
    }

    [Fact]
    public void Fortifying_BecomesFortified_OnTheNextTurn()
    {
        var game = Game.New(Classic, Seed);
        Unit unit = game.PlayerUnits.First();
        game.Fortify(unit);

        game.EndTurn();

        Assert.Equal(UnitOrders.Fortified, unit.Orders);
        Assert.True(unit.IsFortified);
        Assert.True(unit.MovementLeft > 0); // it gets a fresh turn — free to stay dug in or move (which wakes it)
    }

    [Fact]
    public void Moving_WakesAFortifiedUnit()
    {
        var game = Game.New(Classic, Seed);
        Unit unit = game.PlayerUnits.First();
        game.Fortify(unit);
        game.EndTurn(); // → Fortified, full moves
        Position step = unit.Position.Neighbours().First(n => game.CheckMove(unit, n).Allowed);

        game.MoveUnit(unit, step);

        Assert.Equal(UnitOrders.Active, unit.Orders); // a move clears the dig-in
        Assert.False(unit.IsFortified);
    }

    [Fact]
    public void Fortify_RejectedForAShip()
    {
        var game = Game.New(Classic, Seed);
        Position water = game.Map.AllPositions().First(p => game.Map.TerrainAt(p).IsWater);
        Unit ship = game.SpawnUnit(Classic.Unit(Caravel), water);

        MoveCheck check = game.CheckFortify(ship);

        Assert.False(check.Allowed); // ships sentry, they don't fortify
        Assert.Throws<InvalidMoveException>(() => game.Fortify(ship));
    }

    [Fact]
    public void AFortifiedDefender_RepelsAnAttackAnActiveOneWouldLose()
    {
        // A player attacker beside a native brave; the only difference between the two runs is the brave's
        // fortify state. The +50% defence lowers the attacker's odds, so a draw chosen between the two win
        // probabilities is a win against an active defender but a loss against the same defender dug in.
        var probe = SetupAttack(out _, out Unit attacker0, out Unit brave0);
        double terrain = probe.Map.TerrainAt(brave0.Position).DefenceBonus;
        double atk = CombatModel.AttackPower(probe.OffenceBase(attacker0), new AttackContext());
        double pActive = CombatModel.WinProbability(
            atk, CombatModel.DefencePower(probe.DefenceBase(brave0), new DefenceContext(TerrainDefenceBonus: terrain)));
        double pFortified = CombatModel.WinProbability(
            atk, CombatModel.DefencePower(probe.DefenceBase(brave0), new DefenceContext(TerrainDefenceBonus: terrain, Fortified: true)));
        Assert.True(pFortified < pActive); // sanity: fortifying helps the defender
        double draw = (pActive + pFortified) / 2; // strictly between → flips the outcome

        Game active = SetupAttack(out _, out Unit a1, out Unit b1);
        Assert.Equal(UnitOrders.Active, b1.Orders);
        CombatResult vsActive = active.Attack(a1, b1.Position, new FixedRandom(draw));

        Game fortified = SetupAttack(out _, out Unit a2, out Unit b2);
        b2.Orders = UnitOrders.Fortified;
        CombatResult vsFortified = fortified.Attack(a2, b2.Position, new FixedRandom(draw));

        Assert.True(vsActive is CombatResult.Win or CombatResult.GreatWin);      // beats an exposed defender
        Assert.True(vsFortified is CombatResult.Loss or CombatResult.GreatLoss); // the dug-in defender holds
    }

    /// <summary>A fresh game (Seed) with a player free-colonist standing next to a native brave, ready to attack it.</summary>
    private static Game SetupAttack(out NativeSettlement home, out Unit attacker, out Unit brave)
    {
        Game game = Game.New(Classic, Seed);
        bool Free(Position n) =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n);
        brave = game.NativeUnits.First(b => b.Position.Neighbours().Any(Free));
        Position spot = brave.Position.Neighbours().First(Free);
        attacker = game.SpawnUnit(Classic.Unit(FreeColonist), spot);
        attacker.RoleId = "model.role.soldier"; // armed so it has offence (a bare colonist can't attack)
        attacker.RoleCount = 1;
        string nation = brave.OwnerNationId!;
        home = game.NativeSettlements.First(s => s.NationTypeId == nation);
        return game;
    }

    // ---- Sentry / clear-orders ----

    [Fact]
    public void Sentry_RestsTheUnit_AndClearOrdersWakesIt()
    {
        var game = Game.New(Classic, Seed);
        Unit unit = game.PlayerUnits.First();

        game.Sentry(unit);
        Assert.Equal(UnitOrders.Sentry, unit.Orders);
        Assert.Equal(0, unit.MovementLeft);

        game.ClearOrders(unit);
        Assert.Equal(UnitOrders.Active, unit.Orders);
    }

    [Fact]
    public void ClearOrders_DropsAFortifiedUnitBackToActive()
    {
        var game = Game.New(Classic, Seed);
        Unit unit = game.PlayerUnits.First();
        game.Fortify(unit);
        game.EndTurn(); // Fortified

        game.ClearOrders(unit);

        Assert.Equal(UnitOrders.Active, unit.Orders);
        Assert.False(unit.IsFortified);
    }

    // ---- Disband ----

    [Fact]
    public void Disband_RemovesTheUnitForGood()
    {
        var game = Game.New(Classic, Seed);
        Unit unit = game.PlayerUnits.First();
        int before = game.PlayerUnits.Count();

        Assert.True(game.CheckDisband(unit).Allowed);
        game.Disband(unit);

        Assert.Equal(before - 1, game.PlayerUnits.Count());
        Assert.DoesNotContain(unit, game.Units);
    }

    [Fact]
    public void Disband_RejectedForAShipStillCarryingPassengers()
    {
        var game = Game.New(Classic, Seed);
        Position water = game.Map.AllPositions().First(p => game.Map.TerrainAt(p).IsWater);
        Position land = game.Map.AllPositions().First(p => !game.Map.TerrainAt(p).IsWater);
        Unit ship = game.SpawnUnit(Classic.Unit(Caravel), water);
        Unit passenger = game.SpawnUnit(Classic.Unit(FreeColonist), land);
        passenger.CarrierId = ship.Id; // aboard (position is immaterial to the disband guard)

        MoveCheck check = game.CheckDisband(ship);

        Assert.False(check.Allowed); // would orphan the passenger
        Assert.Throws<InvalidMoveException>(() => game.Disband(ship));
        Assert.True(game.CheckDisband(passenger).Allowed); // the passenger itself can still be disbanded
    }

    // ---- Persistence ----

    [Fact]
    public void OrderState_RoundTripsThroughSaveLoad()
    {
        var game = Game.New(Classic, Seed);
        Unit fortifier = game.PlayerUnits.First();
        game.Fortify(fortifier);
        game.EndTurn(); // → Fortified
        int id = fortifier.Id;

        string json = SaveGame.From(game).ToJson();
        Game restored = SaveGame.FromJson(json).Restore(Classic);

        Assert.True(restored.Units.First(u => u.Id == id).IsFortified);
        Assert.Equal(json, SaveGame.From(restored).ToJson()); // acid test: byte-identical round-trip
        Assert.Equal(34, SaveGame.CurrentVersion);
    }

    [Fact]
    public void AnAllActiveGame_SerializesWithoutAnyOrderToken()
    {
        // The order field is omitted for an active unit, so a no-orders game stays byte-identical to v22.
        string json = SaveGame.From(Game.New(Classic, Seed)).ToJson();
        Assert.DoesNotContain("\"Orders\"", json);
    }

    [Fact]
    public void APreV23Save_WithNoOrders_LoadsEveryUnitActive()
    {
        SaveGame save = SaveGame.From(Game.New(Classic, Seed)) with { Version = 22 };
        Game restored = save.Restore(Classic);
        Assert.All(restored.Units, u => Assert.Equal(UnitOrders.Active, u.Orders));
    }
}
