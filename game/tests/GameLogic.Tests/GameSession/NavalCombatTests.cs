using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Naval combat: a ship attacks an adjacent enemy ship on water; the defender may evade, and laden ships fight
/// worse. Reuses the land-combat spine (Attack/CheckAttack/CombatModel) with naval modifiers; outcomes forced
/// via a fixed RNG. A beaten ship is <b>damaged</b> — it loses its cargo + everyone aboard and limps to Europe
/// to repair over five turns (1c-3b) — UNLESS the defeat is decisive (a great loss), when it <b>sinks</b>.
/// Foreign warships at war hunt the human's ships on the power's own stream (1c-3a′); the human's stream 0 stays
/// byte-stable. Loot, privateers/Drake, and colony capture are later sub-slices.
/// </summary>
public class NavalCombatTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Frigate = "model.unit.frigate";   // offence 16
    private const string Caravel = "model.unit.caravel";   // offence 0 (transport)
    private const string Privateer = "model.unit.privateer"; // 8/8

    /// <summary>A fixed RNG (same NextDouble each call) — forces a chosen combat band.</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    private static bool Water(Game g, Position p) =>
        g.Map.InBounds(p) && g.Map.TerrainAt(p).IsWater && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>A human ship of <paramref name="attackerType"/> adjacent (on water) to a foreign-owned ship of <paramref name="defenderType"/>.</summary>
    private static (Game game, Unit attacker, Unit defender, int foreignId) TwoShips(string attackerType, string defenderType)
    {
        Game game = Game.New(Classic, Seed);
        Position a = game.Map.AllPositions().First(p => Water(game, p) && p.Neighbours().Any(n => Water(game, n)));
        Position b = a.Neighbours().First(n => Water(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(attackerType), a); // human (OwnerId 0)
        Unit defender = game.SpawnUnit(Classic.Unit(defenderType), b);
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        defender.OwnerId = foreignId; // an enemy ship
        return (game, attacker, defender, foreignId);
    }

    // Frigate(16) vs Caravel(defence 2): win ≈ 0.923, so the naval bands are GreatWin r<0.092, Win <0.923,
    // Evade <0.938, Loss <0.992, GreatLoss otherwise. The fixed rolls below pick a specific band.

    [Fact]
    public void GreatWin_SinksTheAdjacentEnemyShip()
    {
        (Game game, Unit attacker, Unit defender, _) = TwoShips(Frigate, Caravel);
        int defenderId = defender.Id;

        CombatResult result = game.Attack(attacker, defender.Position, new FixedRandom(0.0));

        Assert.Equal(CombatResult.GreatWin, result);
        Assert.DoesNotContain(game.Units, u => u.Id == defenderId); // a decisive loss sinks the ship
        Assert.Contains(attacker, game.Units);                       // the victor sails on
    }

    [Fact]
    public void Win_DamagesTheDefender_AndSendsItToEuropeToRepair()
    {
        (Game game, Unit attacker, Unit defender, _) = TwoShips(Frigate, Caravel);

        CombatResult result = game.Attack(attacker, defender.Position, new FixedRandom(0.5));

        Assert.Equal(CombatResult.Win, result);
        Assert.Contains(defender, game.Units);                                  // not sunk — damaged
        Assert.True(defender.IsUnderRepair);
        Assert.Equal(UnitLocation.InEurope, defender.Location);                 // limped off the map to repair
        Assert.Equal(5, defender.RepairTurnsRemaining);                         // hit-points 6 → 5 turns
        Assert.Equal(0, defender.MovementLeft);
        Assert.Contains(attacker, game.Units);
    }

    [Fact]
    public void Loss_DamagesTheAttacker_NotSunk()
    {
        (Game game, Unit attacker, Unit defender, _) = TwoShips(Frigate, Caravel);

        CombatResult result = game.Attack(attacker, defender.Position, new FixedRandom(0.99));

        Assert.Equal(CombatResult.Loss, result);
        Assert.Contains(attacker, game.Units);                                  // the attacker limps home, not sunk
        Assert.True(attacker.IsUnderRepair);
        Assert.Equal(UnitLocation.InEurope, attacker.Location);
        Assert.Contains(defender, game.Units);
    }

    [Fact]
    public void GreatLoss_SinksTheAttacker()
    {
        (Game game, Unit attacker, Unit defender, _) = TwoShips(Frigate, Caravel);
        int attackerId = attacker.Id;

        CombatResult result = game.Attack(attacker, defender.Position, new FixedRandom(0.999));

        Assert.Equal(CombatResult.GreatLoss, result);
        Assert.DoesNotContain(game.Units, u => u.Id == attackerId); // a decisive loss sinks the attacker
        Assert.Contains(defender, game.Units);
    }

    [Fact]
    public void DamagedShip_LosesItsCargoAndPassengers_LikeSinking()
    {
        (Game game, Unit attacker, Unit defender, _) = TwoShips(Frigate, Caravel);
        defender.AddCargo("model.goods.sugar", 50);
        Unit passenger = game.Units.First(u => !u.Type.IsNaval);
        passenger.CarrierId = defender.Id;
        int passengerId = passenger.Id;

        game.Attack(attacker, defender.Position, new FixedRandom(0.5)); // normal win → damaged (not sunk)

        Assert.Contains(defender, game.Units);                          // the hull survives…
        Assert.Empty(defender.Cargo);                                   // …but the cargo is gone
        Assert.DoesNotContain(game.Units, u => u.Id == passengerId);    // …and the passenger is lost
    }

    [Fact]
    public void Evade_LeavesBothShips_ButSpendsTheAttackersTurn()
    {
        // Equal ships → win ≈ 0.6; the evade band is [win, 0.8·win+0.2) = [0.6, 0.68); 0.65 lands in it.
        (Game game, Unit attacker, Unit defender, _) = TwoShips(Privateer, Privateer);

        CombatResult result = game.Attack(attacker, defender.Position, new FixedRandom(0.65));

        Assert.Equal(CombatResult.Evade, result);
        Assert.Contains(attacker, game.Units);
        Assert.Contains(defender, game.Units);
        Assert.Equal(0, attacker.MovementLeft); // the turn is still spent
    }

    [Fact]
    public void SunkShip_TakesItsPassengersDown_NoOrphans()
    {
        (Game game, Unit attacker, Unit defender, _) = TwoShips(Frigate, Caravel);
        Unit passenger = game.Units.First(u => !u.Type.IsNaval); // a land unit, put aboard the doomed ship
        passenger.CarrierId = defender.Id;
        int passengerId = passenger.Id;

        game.Attack(attacker, defender.Position, new FixedRandom(0.0)); // great win → sink

        Assert.DoesNotContain(game.Units, u => u.Id == passengerId); // drowned, not left orphaned with a dangling CarrierId
    }

    [Fact]
    public void CargoPenalty_CountsGoodsOnly_NotPassengers()
    {
        // FreeCol's combat cargo penalty is goods-space only; passengers aboard don't weaken a ship in battle.
        (Game game, _, Unit ship, _) = TwoShips(Frigate, Caravel);
        Unit passenger = game.Units.First(u => !u.Type.IsNaval);
        passenger.CarrierId = ship.Id; // a passenger aboard, but no goods in the hold

        Assert.True(game.CargoSlotsUsed(ship) > 0); // the passenger occupies a slot…
        Assert.Equal(0, game.GoodsSlotsUsed(ship));  // …but the combat penalty (goods-space) is zero
    }

    [Fact]
    public void UnarmedShip_CannotInitiateAnAttack()
    {
        (Game game, Unit attacker, Unit defender, _) = TwoShips(Caravel, Frigate); // caravel offence 0
        Assert.False(game.CheckAttack(attacker, defender.Position).Allowed);
    }

    [Fact]
    public void ShipsAndLandUnits_CannotAttackEachOtherDirectly()
    {
        // FreeCol Unit.canAttack forbids cross-domain melee (UI-reachable on the human path otherwise).
        Game game = Game.New(Classic, Seed);
        Position land = game.Map.AllPositions().First(p => game.Map.InBounds(p) && !game.Map.TerrainAt(p).IsWater
            && p.Neighbours().Any(n => Water(game, n))
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        Position water = land.Neighbours().First(n => Water(game, n));
        Unit soldier = game.SpawnUnit(Classic.Unit("model.unit.artillery"), land); // human land unit, offence 7
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        Unit ship = game.SpawnUnit(Classic.Unit(Frigate), water);
        ship.OwnerId = foreignId;

        Assert.False(game.CheckAttack(soldier, water).Allowed); // land unit can't attack a ship
        Assert.False(game.CheckAttack(ship, land).Allowed);     // ship can't attack a land unit
    }

    [Fact]
    public void LadenShip_HasLowerDefencePower_ThanEmpty()
    {
        double empty = CombatModel.DefencePower(8, new DefenceContext());
        double laden = CombatModel.DefencePower(8, new DefenceContext(GoodsCarried: 2));
        Assert.True(laden < empty);
        Assert.Equal(8 * (1 - (0.125 * 2)), laden, 5); // −12.5% per slot → 8 × 0.75 = 6
    }

    [Fact]
    public void HumanNavalAttack_IsReplayStable_ForAFixedSeed()
    {
        (Game a, Unit aAtt, Unit aDef, _) = TwoShips(Frigate, Caravel);
        (Game b, Unit bAtt, Unit bDef, _) = TwoShips(Frigate, Caravel);

        a.Attack(aAtt, aDef.Position); // public overload → the game's main stream 0
        b.Attack(bAtt, bDef.Position);

        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    // ── 1c-3b — damage + repair ──

    /// <summary>A human caravel on water adjacent to a foreign frigate that will attack (and beat) it.</summary>
    private static (Game game, Unit humanShip, Unit foreignFrigate) StageHumanShipUnderAttack()
    {
        Game game = Game.New(Classic, Seed);
        Position a = game.Map.AllPositions().First(p => Water(game, p) && p.Neighbours().Any(n => Water(game, n)));
        Position b = a.Neighbours().First(n => Water(game, n));
        Unit humanShip = game.SpawnUnit(Classic.Unit(Caravel), a); // human (OwnerId 0)
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        Unit frigate = game.SpawnUnit(Classic.Unit(Frigate), b);
        frigate.OwnerId = foreignId;
        return (game, humanShip, frigate);
    }

    [Fact]
    public void DamagedShip_RepairsOneTurnAtATime_ThenReturnsToService()
    {
        (Game game, Unit humanShip, Unit frigate) = StageHumanShipUnderAttack();
        game.Attack(frigate, humanShip.Position, new FixedRandom(0.5)); // foreign frigate wins → human ship damaged
        Assert.Equal(5, humanShip.RepairTurnsRemaining);

        for (int turn = 1; turn <= 4; turn++)
        {
            game.EndTurn();
            Assert.Equal(5 - turn, humanShip.RepairTurnsRemaining);
            Assert.True(humanShip.IsUnderRepair); // still in dock
        }

        game.EndTurn(); // the fifth turn completes the repair
        Assert.Equal(0, humanShip.RepairTurnsRemaining);
        Assert.False(humanShip.IsUnderRepair);
        game.SailToNewWorld(humanShip); // back in service — sails without throwing
        Assert.Equal(UnitLocation.SailingToNewWorld, humanShip.Location);
    }

    [Fact]
    public void ShipUnderRepair_CannotSailToNewWorld()
    {
        (Game game, Unit humanShip, Unit frigate) = StageHumanShipUnderAttack();
        game.Attack(frigate, humanShip.Position, new FixedRandom(0.5)); // damaged → in Europe under repair

        Assert.True(humanShip.IsUnderRepair);
        Assert.Throws<InvalidMoveException>(() => game.SailToNewWorld(humanShip));
    }

    [Fact]
    public void DamagedShip_RepairState_SurvivesSaveRoundTrip()
    {
        (Game game, Unit humanShip, Unit frigate) = StageHumanShipUnderAttack();
        game.Attack(frigate, humanShip.Position, new FixedRandom(0.5));
        int shipId = humanShip.Id;

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Unit reloaded = restored.Units.First(u => u.Id == shipId);
        Assert.Equal(5, reloaded.RepairTurnsRemaining);
        Assert.Equal(UnitLocation.InEurope, reloaded.Location);
    }

    [Fact]
    public void ShipUnderRepair_CannotBeBoardedOrLoaded()
    {
        // FreeCol: a ship under forced repair is "not ready to trade" — no boarding, no taking on cargo.
        (Game game, Unit humanShip, Unit frigate) = StageHumanShipUnderAttack();
        game.Attack(frigate, humanShip.Position, new FixedRandom(0.5)); // damaged → in Europe under repair
        Unit colonist = game.Units.First(u => !u.Type.IsNaval && u.OwnerId == 0);
        colonist.Location = UnitLocation.InEurope; // a colonist on the dock beside the repairing ship

        Assert.False(game.CheckBoard(colonist, humanShip).Allowed);
        Assert.False(game.CheckBuyEuropeGoods(humanShip, "model.goods.sugar", 100).Allowed);
    }

    [Fact]
    public void HealthyFleet_SerializesByteIdenticallyToTheNoRepairCase()
    {
        // The repair field is omitted when 0, so an undamaged game is byte-identical to a pre-1c-3b save.
        Game game = Game.New(Classic, Seed);
        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("RepairTurns", json);
    }

    // ── 1c-3a′ — foreign-power naval AI ──

    /// <summary>A human ship adjacent (on water) to a foreign warship, optionally already at war.</summary>
    private static (Game game, Unit humanShip, int foreignId) StageNavalWar(bool atWar)
    {
        Game game = Game.New(Classic, Seed);
        Position a = game.Map.AllPositions().First(p => Water(game, p) && p.Neighbours().Any(n => Water(game, n)));
        Position b = a.Neighbours().First(n => Water(game, n));
        Unit humanShip = game.SpawnUnit(Classic.Unit(Caravel), a); // human transport
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        Unit warship = game.SpawnUnit(Classic.Unit(Frigate), b);
        warship.OwnerId = foreignId;
        if (atWar)
        {
            game.SetStance(foreignId, game.HumanPlayer.PlayerId, Stance.War);
        }
        return (game, humanShip, foreignId);
    }

    [Fact]
    public void ForeignWarshipAtWar_AttacksTheHumanShip_AndRecordsANotice()
    {
        (Game game, Unit humanShip, int foreignId) = StageNavalWar(atWar: true);
        string nation = game.Players.First(p => p.PlayerId == foreignId).NationId!;
        Position shipPos = humanShip.Position;

        game.EndTurn(); // the at-war foreign frigate hunts + attacks the human ship on its own stream

        Assert.Contains(game.CombatNotices, n => n.AttackerNationId == nation && n.Position == shipPos);
    }

    [Fact]
    public void ForeignNavalCombat_DoesNotTouchTheHumansStream0()
    {
        (Game peace, _, _) = StageNavalWar(atWar: false);
        (Game war, _, _) = StageNavalWar(atWar: true);
        for (int turn = 0; turn < 10; turn++)
        {
            peace.EndTurn();
            war.EndTurn();
        }

        Assert.NotEqual(SaveGame.From(peace).ToJson(), SaveGame.From(war).ToJson()); // the war diverged…
        Assert.Equal(peace.RandomState, war.RandomState);                            // …but stream 0 is untouched
    }
}
