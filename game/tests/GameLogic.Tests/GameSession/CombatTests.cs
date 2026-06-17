using CrownAndColony.GameLogic.Colonies;
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
/// The stateful attack action (Phase 5 slice 5b): equipping roles, attacking native braves, the
/// loser/winner outcome precedence (slaughter / disarm / demote / promote), native alarm on attack,
/// and the military founding fathers (Washington auto-promote, Revere auto-arm). Combat resolution
/// draws from an injected fixed RNG so outcomes are forced; the underlying odds are tested in
/// <see cref="CrownAndColony.GameLogic.Tests.Combat.CombatModelTests"/>.
/// </summary>
public class CombatTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    private const string FreeColonist = "model.unit.freeColonist";
    private const string Soldier = "model.role.soldier";
    private const string Dragoon = "model.role.dragoon";
    private const string Washington = "model.foundingFather.georgeWashington";
    private const string Revere = "model.foundingFather.paulRevere";
    private const string Pocahontas = "model.foundingFather.pocahontas";

    /// <summary>A fixed RNG returning the same NextDouble — forces a chosen combat band (0 → great win, 0.99 → great loss).</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    /// <summary>
    /// A new game (optionally with elected fathers), a native brave that has free adjacent land, and a
    /// player attacker of the given type/role standing on that adjacent tile, ready to attack the brave.
    /// </summary>
    private static (Game game, Unit attacker, Unit brave, NativeSettlement home) SetupAttack(
        string attackerType = FreeColonist, string? attackerRole = null, params string[] congress)
    {
        Game game = Game.New(Classic, Seed);
        if (congress.Length > 0)
        {
            game = Elect(game, congress);
        }

        bool Free(Game g, Position n) =>
            g.Map.InBounds(n) && !g.Map.TerrainAt(n).IsWater
            && g.NativeSettlementAt(n) is null && g.ColonyAt(n) is null
            && !g.Units.Any(u => u.IsOnMap && u.Position == n);

        Unit brave = game.NativeUnits.First(b => b.Position.Neighbours().Any(n => Free(game, n)));
        Position spot = brave.Position.Neighbours().First(n => Free(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(attackerType), spot);
        if (attackerRole is not null)
        {
            attacker.RoleId = attackerRole;
            attacker.RoleCount = 1;
        }
        NativeSettlement home = game.NativeSettlements
            .Where(s => s.NationTypeId == brave.OwnerNationId)
            .OrderBy(s => Chebyshev(s.Position, brave.Position))
            .First();
        return (game, attacker, brave, home);
    }

    private static int Chebyshev(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// A copy of <paramref name="game"/> with the given fathers elected to the human player (via a save
    /// round-trip). Player state lives in <c>Players[]</c> from save v20, so the injection targets it.
    /// </summary>
    private static Game Elect(Game game, params string[] congress)
    {
        SaveGame save = SaveGame.From(game);
        return (save with
        {
            Players = save.Players!.Select(p => p with { Congress = congress }).ToList(),
        }).Restore(Classic);
    }

    // ---- Brave garrison ----

    [Fact]
    public void NewGame_GarrisonsSettlementsWithNativeBraves()
    {
        Game game = Game.New(Classic, Seed);
        Assert.NotEmpty(game.NativeUnits);
        Assert.True(game.NativeUnits.Count() <= game.NativeSettlements.Count); // at most one per settlement

        foreach (Unit brave in game.NativeUnits)
        {
            Assert.True(brave.IsNative);
            Assert.Equal(Game.BraveUnitTypeId, brave.Type.Id);
            Assert.Equal(RoleType.DefaultRoleId, brave.RoleId);
            Assert.Contains(game.NativeSettlements, s =>
                s.NationTypeId == brave.OwnerNationId && s.Position.IsAdjacentTo(brave.Position));
        }
    }

    [Fact]
    public void Braves_DoNotLiftThePlayersFog()
    {
        Game game = Game.New(Classic, Seed);
        // Every currently-visible tile is within a player unit's or colony's sight, never a brave's.
        foreach (Position p in game.CurrentlyVisible)
        {
            Assert.Contains(game.PlayerUnits.Where(u => u.IsOnMap),
                u => Chebyshev(u.Position, p) <= u.Type.LineOfSight);
        }
    }

    [Fact]
    public void MovingABrave_DoesNotEnlargeThePlayersExploredArea()
    {
        Game game = Game.New(Classic, Seed);
        bool Open(Position n) =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n);
        Unit brave = game.NativeUnits.First(b => b.Position.Neighbours().Any(Open));
        Position dest = brave.Position.Neighbours().First(Open);

        int exploredBefore = game.Explored.Count;
        game.MoveUnit(brave, dest);

        Assert.Equal(exploredBefore, game.Explored.Count); // a native unit never reveals tiles for the player
    }

    // ---- Combat power fold (role at index 30, before the type percentage) ----

    [Fact]
    public void VeteranSoldier_RolePowerFoldsBeforeTheTypePercentage()
    {
        // A veteran soldier's own +50% must apply to base AND role: offence (0+2)×1.5 = 3, defence (1+1)×1.5 = 3.
        Game game = Game.New(Classic, Seed);
        Unit vet = game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), game.PlayerUnits.First().Position);
        vet.RoleId = Soldier;
        vet.RoleCount = 1;

        Assert.Equal(3.0, game.OffenceBase(vet), 5);
        Assert.Equal(3.0, game.DefenceBase(vet), 5);
        Assert.Equal(4.5, CombatModel.AttackPower(game.OffenceBase(vet), new AttackContext()), 5); // 3 × 1.5 attack bonus
    }

    // ---- CheckAttack ----

    [Fact]
    public void CheckAttack_RejectsTheUnwinnableAndTheUnreachable()
    {
        (Game game, Unit attacker, Unit brave, _) = SetupAttack(); // unarmed colonist (offence 0)
        Assert.False(game.CheckAttack(attacker, brave.Position).Allowed); // no offensive strength

        attacker.RoleId = Soldier; // now armed
        Assert.True(game.CheckAttack(attacker, brave.Position).Allowed);

        attacker.MovementLeft = 0;
        Assert.False(game.CheckAttack(attacker, brave.Position).Allowed); // no movement
    }

    [Fact]
    public void CheckMove_OntoABrave_IsRejectedInFavourOfAttacking()
    {
        (Game game, Unit attacker, Unit brave, _) = SetupAttack(FreeColonist, Soldier);
        MoveCheck check = game.CheckMove(attacker, brave.Position);
        Assert.False(check.Allowed);
        Assert.Contains("attack", check.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Brave_CanAttackAnAdjacentHumanUnit_ButNotAssaultSettlements()
    {
        // Slice 1b removed the "natives do not attack" gate on CheckAttack: a brave adjacent to a human unit
        // may raid it. The CheckAttackSettlement gate stays closed — braves don't assault settlements yet.
        (Game game, Unit human, Unit brave, NativeSettlement home) = SetupAttack();
        Assert.True(game.CheckAttack(brave, human.Position).Allowed);
        Assert.False(game.CheckAttackSettlement(brave, home.Position).Allowed);

        brave.MovementLeft = 0;
        Assert.False(game.CheckAttack(brave, human.Position).Allowed); // ...but not when out of movement
    }

    [Fact]
    public void Brave_BeatsUnarmedHumanColonist_DestroysIt_NeverReassignsItToTheHuman()
    {
        // Braves cannot capture units in the classic ruleset (captureUnits=false), so a beaten unarmed colonist
        // is slaughtered — never captured and (via the brave's OwnerId 0 == the human) handed back to the human.
        // Pins the observable outcome; the OwnerId-hardening line in CaptureUnit is a dormant safety net behind it.
        (Game game, Unit colonist, Unit brave, _) = SetupAttack(); // unarmed human free colonist as the prey
        int colonistId = colonist.Id;
        int humanUnitsBefore = game.PlayerUnits.Count();

        game.Attack(brave, colonist.Position, new FixedRandom(0.0)); // force a brave great-win

        Assert.DoesNotContain(colonist, game.Units);                  // slaughtered (no capture path for braves)
        Assert.DoesNotContain(game.Units, u => u.Id == colonistId);   // and not lingering, re-owned to anyone
        Assert.Equal(humanUnitsBefore - 1, game.PlayerUnits.Count()); // the human lost it; it was not handed back
        Assert.Contains(brave, game.Units);                           // the raider survives, still native
        Assert.True(brave.IsNative);
    }

    [Fact]
    public void NativeRaid_OnTheHuman_DoesNotChangeTheRaidersOwnAlarm()
    {
        // Native combat tension keys on a NATIVE defender (a European attacking braves). A brave raiding the
        // human takes the defenderNation-is-null path, so the raider's own settlement alarm is untouched.
        (Game game, Unit colonist, Unit brave, NativeSettlement home) = SetupAttack();
        game.ChangeNativeAlarm(home, 650); // Displeased
        int alarmBefore = home.Alarm;

        game.Attack(brave, colonist.Position, new FixedRandom(0.0)); // brave wins

        Assert.Equal(alarmBefore, home.Alarm);
    }

    // ---- Outcomes ----

    [Fact]
    public void GreatWin_SlaughtersTheBrave_AndRaisesAlarm()
    {
        (Game game, Unit attacker, Unit brave, NativeSettlement home) = SetupAttack("model.unit.artillery");

        CombatResult result = game.Attack(attacker, brave.Position, new FixedRandom(0.0));

        Assert.Equal(CombatResult.GreatWin, result);
        Assert.DoesNotContain(brave, game.Units);                 // the brave is destroyed
        // Killing the brave in the open: UNIT_DESTROYED (+400) + a minor insult (+100) = +500.
        Assert.Equal(
            NativeSettlement.TensionAddUnitDestroyed + NativeSettlement.TensionAddMinor,
            home.Alarm);
    }

    [Fact]
    public void Pocahontas_DoesNotDampCombatAlarm()
    {
        // Pocahontas's −50% nativeAlarmModifier damps the per-turn AMBIENT alarm (see AmbientAlarmTests), NOT
        // combat: a combat win raises a settlement's alarm by the full +500 even with Pocahontas elected (FreeCol
        // applies combat tension raw). The ambient damping is verified in AmbientAlarmTests.
        (Game game, Unit attacker, Unit brave, NativeSettlement home) =
            SetupAttack("model.unit.artillery", null, Pocahontas);

        game.Attack(attacker, brave.Position, new FixedRandom(0.0)); // great win, kills the brave

        Assert.Equal(NativeSettlement.TensionAddUnitDestroyed + NativeSettlement.TensionAddMinor, home.Alarm); // full +500
    }

    [Fact]
    public void SoldierLoss_DisarmsTheSoldier_AndBraveCapturesTheMuskets()
    {
        (Game game, Unit attacker, Unit brave, NativeSettlement home) = SetupAttack(FreeColonist, Soldier);
        int id = attacker.Id;
        game.ChangeNativeAlarm(home, 500); // start hostile so the post-loss drop is observable

        CombatResult result = game.Attack(attacker, brave.Position, new FixedRandom(0.99));

        Assert.Equal(CombatResult.GreatLoss, result);
        Unit disarmed = game.Units.First(u => u.Id == id);
        Assert.Equal(RoleType.DefaultRoleId, disarmed.RoleId);    // the soldier loses his muskets
        Assert.Equal(FreeColonist, disarmed.Type.Id);            // but survives as a colonist
        Assert.Equal("model.role.armedBrave", brave.RoleId);     // the brave arms itself with them
        Assert.Equal(500 - NativeSettlement.TensionAddMinor, home.Alarm); // a repelled attack calms them (−100)
    }

    [Fact]
    public void DragoonLoss_DowngradesToSoldier_AndBraveTakesTheHorses()
    {
        (Game game, Unit attacker, Unit brave, _) = SetupAttack(FreeColonist, Dragoon);
        int id = attacker.Id;

        game.Attack(attacker, brave.Position, new FixedRandom(0.99));

        Unit downgraded = game.Units.First(u => u.Id == id);
        Assert.Equal(Soldier, downgraded.RoleId);                // dragoon → soldier (keeps the muskets)
        Assert.Equal("model.role.mountedBrave", brave.RoleId);   // the brave captures the horses
    }

    [Fact]
    public void ArtilleryLoss_DemotesToDamagedArtillery()
    {
        (Game game, Unit attacker, _, _) = SetupAttack("model.unit.artillery");
        int id = attacker.Id;

        game.Attack(attacker, game.NativeUnits.First(u => u.Position.IsAdjacentTo(attacker.Position)).Position,
            new FixedRandom(0.99));

        Assert.Equal("model.unit.damagedArtillery", game.Units.First(u => u.Id == id).Type.Id);
    }

    // ---- Promotion / founding fathers ----

    [Fact]
    public void GreatWin_PromotesTheWinner()
    {
        (Game game, Unit attacker, Unit brave, _) = SetupAttack(FreeColonist, Soldier);
        int id = attacker.Id;

        game.Attack(attacker, brave.Position, new FixedRandom(0.0)); // great win → promotion roll succeeds

        Unit winner = game.Units.First(u => u.Id == id);
        Assert.Equal("model.unit.veteranSoldier", winner.Type.Id);
        Assert.Equal(Soldier, winner.RoleId); // promotion keeps the role
    }

    [Fact]
    public void Washington_PromotesEvenANonGreatWin()
    {
        (Game with, Unit a1, Unit b1, _) = SetupAttack(FreeColonist, Soldier, Washington);
        int id1 = a1.Id;
        with.Attack(a1, b1.Position, new FixedRandom(0.5)); // ordinary (non-great) win
        Assert.Equal("model.unit.veteranSoldier", with.Units.First(u => u.Id == id1).Type.Id);

        (Game without, Unit a2, Unit b2, _) = SetupAttack(FreeColonist, Soldier);
        int id2 = a2.Id;
        without.Attack(a2, b2.Position, new FixedRandom(0.5));
        Assert.Equal(FreeColonist, without.Units.First(u => u.Id == id2).Type.Id); // no promotion without Washington
    }

    [Fact]
    public void Revere_AutoArmsAnUnarmedDefenderInAStockedColony()
    {
        Game baseGame = Game.New(Classic, Seed);
        Colony colony = baseGame.FoundColony(baseGame.PlayerUnits.First());
        Game game = Elect(baseGame, Revere);
        colony = game.Colonies.First();

        Unit defender = game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position);
        Assert.Equal(RoleType.DefaultRoleId, game.EffectiveCombatRole(defender, defending: true)); // no muskets yet

        colony.AddGoods("model.goods.muskets", 50);
        Assert.Equal(Soldier, game.EffectiveCombatRole(defender, defending: true)); // Revere arms the defender
        Assert.Equal(RoleType.DefaultRoleId, game.EffectiveCombatRole(defender, defending: false)); // attacking is unaffected
    }

    [Fact]
    public void WithoutRevere_AnUnarmedDefenderStaysUnarmed()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First());
        colony.AddGoods("model.goods.muskets", 100);
        Unit defender = game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position);
        Assert.Equal(RoleType.DefaultRoleId, game.EffectiveCombatRole(defender, defending: true));
    }

    // ---- Equipping ----

    [Fact]
    public void EquipRole_ArmsAColonistFromTheColonyStock()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First());
        colony.AddGoods("model.goods.muskets", 60);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position);

        game.EquipRole(colonist, colony, Soldier);

        Assert.Equal(Soldier, colonist.RoleId);
        Assert.Equal(1, colonist.RoleCount);
        Assert.Equal(10, colony.StoreOf("model.goods.muskets")); // 60 − 50
    }

    [Fact]
    public void CheckEquipRole_RejectsWhenTheColonyLacksTheGoods()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First());
        colony.AddGoods("model.goods.muskets", 50); // enough for a soldier, but not a dragoon (needs horses too)
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position);

        Assert.True(game.CheckEquipRole(colonist, colony, Soldier).Allowed);
        Assert.False(game.CheckEquipRole(colonist, colony, Dragoon).Allowed); // no horses
    }

    // ---- Settlement assault (5c) ----

    private const string Artillery = "model.unit.artillery";
    private const string Cortes = "model.foundingFather.hernanCortes";

    /// <summary>A scripted RNG: a fixed combat roll (NextDouble) plus a queue of Next(int) values for plunder.</summary>
    private sealed class ScriptedRandom(double roll, params int[] ints) : IGameRandom
    {
        private int _i;
        public int Next(int maxExclusive) => _i < ints.Length ? ints[_i++] : 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => roll;
        public RandomState SaveState() => new(0, 0);
    }

    /// <summary>A new game (optionally with fathers), a native settlement (capital filter optional), and a player attacker on a free adjacent tile.</summary>
    private static (Game game, Unit attacker, NativeSettlement settlement) SetupSettlementAttack(
        string attackerType = Artillery, bool? capital = null, params string[] congress)
    {
        Game game = Game.New(Classic, Seed);
        if (congress.Length > 0)
        {
            game = Elect(game, congress);
        }
        bool Free(Position n) =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n);
        NativeSettlement settlement = game.NativeSettlements.First(s =>
            (capital is null || s.IsCapital == capital) && s.Position.Neighbours().Any(Free));
        Position spot = settlement.Position.Neighbours().First(Free);
        Unit attacker = game.SpawnUnit(Classic.Unit(attackerType), spot);
        return (game, attacker, settlement);
    }

    [Theory]
    [InlineData("model.settlement.camp", 20, 2, 3, 100, 100, 3, 6, 100)]
    [InlineData("model.settlement.village", 50, 3, 8, 100, 100, 4, 12, 100)]
    [InlineData("model.settlement.camp.capital", 100, 2, 4, 200, 100, 2, 4, 300)]
    public void SettlementPlunder_MatchesSpec(
        string id, int bp, int bmin, int bmax, int bf, int xp, int xmin, int xmax, int xf)
    {
        SettlementType type = Classic.Settlement(id);
        SettlementPlunder baseRange = type.PlunderRange(hasPlunderAbility: false)!;
        SettlementPlunder extraRange = type.PlunderRange(hasPlunderAbility: true)!;
        Assert.Equal((bp, bmin, bmax, bf), (baseRange.Probability, baseRange.Minimum, baseRange.Maximum, baseRange.Factor));
        Assert.Equal((xp, xmin, xmax, xf), (extraRange.Probability, extraRange.Minimum, extraRange.Maximum, extraRange.Factor));
    }

    private const string TreasureTrain = "model.unit.treasureTrain";

    [Fact]
    public void AttackSettlement_GreatWin_DestroysAndSpawnsATreasureTrain()
    {
        (Game game, Unit attacker, NativeSettlement settlement) = SetupSettlementAttack();
        Position pos = settlement.Position;
        int goldBefore = game.Gold;

        SettlementPlunder baseRange = Classic.Settlement(settlement.SettlementTypeId).PlunderRange(false)!;
        CombatResult result = game.AttackSettlement(attacker, pos, new FixedRandom(0.0)); // great win; all plunder rolls 0

        Assert.Equal(CombatResult.GreatWin, result);
        Assert.Null(game.NativeSettlementAt(pos));                  // settlement destroyed
        Assert.DoesNotContain(settlement, game.NativeSettlements);
        // The plunder is NOW a treasure train on the razed tile, not instant gold (FreeCol csDestroySettlement).
        Assert.Equal(goldBefore, game.Gold);
        Unit train = game.Units.Single(u => u.Type.Id == TreasureTrain);
        Assert.Equal(pos, train.Position);
        Assert.Equal(0, train.OwnerId);                            // owned by the human attacker
        // base range, all rolls 0: probability gate passes, range roll 0 → (0 + min) × factor.
        Assert.Equal(baseRange.Minimum * baseRange.Factor, train.TreasureAmount);
    }

    [Fact]
    public void AttackSettlement_PlunderProbabilityFails_SpawnsNoTreasureTrain()
    {
        (Game game, Unit attacker, NativeSettlement settlement) =
            SetupSettlementAttack(capital: false);
        // Only test a settlement whose base plunder is a sub-100 probability (camp 20 / village 50).
        int prob = Classic.Settlement(settlement.SettlementTypeId).PlunderRange(false)!.Probability;
        int goldBefore = game.Gold;
        var rng = new ScriptedRandom(0.0, prob); // probability roll == prob → NOT < prob → no plunder

        game.AttackSettlement(attacker, settlement.Position, rng);

        Assert.Null(game.NativeSettlementAt(settlement.Position)); // still destroyed
        Assert.Equal(goldBefore, game.Gold);                       // no plunder, no gold
        Assert.DoesNotContain(game.Units, u => u.Type.Id == TreasureTrain); // and no treasure train
    }

    [Fact]
    public void AttackSettlement_WithCortes_TreasureTrainUsesTheRicherExtraRange()
    {
        (Game game, Unit attacker, NativeSettlement settlement) =
            SetupSettlementAttack(Artillery, capital: false, Cortes);
        SettlementPlunder extra = Classic.Settlement(settlement.SettlementTypeId).PlunderRange(true)!;
        // extra range is probability 100 (no probability draw); range roll 1 → (1 + min) × factor.
        int expected = (1 + extra.Minimum) * extra.Factor;
        int goldBefore = game.Gold;
        var rng = new ScriptedRandom(0.0, 1);

        game.AttackSettlement(attacker, settlement.Position, rng);

        Assert.Equal(goldBefore, game.Gold); // still no instant gold — the richer plunder rides the treasure train
        Unit train = game.Units.Single(u => u.Type.Id == TreasureTrain);
        Assert.Equal(expected, train.TreasureAmount); // the .extra-range value (Cortés grants model.ability.plunderNatives)
    }

    [Fact]
    public void AttackSettlement_Loss_LowersAlarmAndDemotesArtillery()
    {
        (Game game, Unit attacker, NativeSettlement settlement) = SetupSettlementAttack(Artillery);
        int id = attacker.Id;
        game.ChangeNativeAlarm(settlement, 500); // start hostile so the post-loss drop is observable

        CombatResult result = game.AttackSettlement(attacker, settlement.Position, new FixedRandom(0.99)); // great loss

        Assert.Equal(CombatResult.GreatLoss, result);
        Assert.Same(settlement, game.NativeSettlementAt(settlement.Position));          // survives
        Assert.Equal(500 - NativeSettlement.TensionAddMinor, settlement.Alarm);         // a repelled assault calms them (−100)
        Assert.Equal("model.unit.damagedArtillery", game.Units.First(u => u.Id == id).Type.Id); // attacker demoted
    }

    [Fact]
    public void AttackSettlement_Loss_AttackerSlain_LowersAlarmBy300()
    {
        // A damaged-artillery attacker is destroyed on loss (no demotion left, the garrison can't capture it)
        // → minor (−100) + normal (−200) = −300.
        (Game game, Unit attacker, NativeSettlement settlement) = SetupSettlementAttack("model.unit.damagedArtillery");
        int id = attacker.Id;
        game.ChangeNativeAlarm(settlement, 500);

        game.AttackSettlement(attacker, settlement.Position, new FixedRandom(0.99)); // great loss

        Assert.DoesNotContain(game.Units, u => u.Id == id); // attacker slain
        Assert.Equal(
            500 - (NativeSettlement.TensionAddMinor + NativeSettlement.TensionAddNormal),
            settlement.Alarm);
    }

    /// <summary>Finds a multi-settlement tribe whose member matching <paramref name="want"/> has a free adjacent tile to attack from.</summary>
    private static (NativeSettlement target, NativeSettlement sibling, Position spawn) MultiSettlementTribe(
        Game game, Func<NativeSettlement, bool> want)
    {
        bool Free(Position n) =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n);
        var nation = game.NativeSettlements
            .GroupBy(s => s.NationTypeId)
            .First(g => g.Count() >= 2 && g.Any(s => want(s) && s.Position.Neighbours().Any(Free)));
        NativeSettlement target = nation.First(s => want(s) && s.Position.Neighbours().Any(Free));
        return (target, nation.First(s => s != target), target.Position.Neighbours().First(Free));
    }

    [Fact]
    public void AttackSettlement_Win_PropagatesAlarmToTheNationsOtherSettlements()
    {
        Game game = Game.New(Classic, Seed);
        (NativeSettlement target, NativeSettlement sibling, Position spawn) = MultiSettlementTribe(game, s => !s.IsCapital);
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), spawn);

        game.AttackSettlement(attacker, target.Position, new FixedRandom(0.0)); // great win → destroy

        Assert.Null(game.NativeSettlementAt(target.Position)); // target destroyed
        // slaughter (+500) + destroy major (+300) + minor (+100) = +900, propagated to the surviving sibling.
        Assert.Equal(
            NativeSettlement.TensionAddSettlementAttacked + NativeSettlement.TensionAddMajor + NativeSettlement.TensionAddMinor,
            sibling.Alarm);
    }

    [Fact]
    public void AttackSettlement_BurningTheCapital_SurrendersTheNation()
    {
        Game game = Game.New(Classic, Seed);
        (NativeSettlement capital, NativeSettlement sibling, Position spawn) = MultiSettlementTribe(game, s => s.IsCapital);
        game.ChangeNativeAlarm(sibling, 900); // start hateful to prove surrender SETS the alarm (not adds)
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), spawn);

        game.AttackSettlement(attacker, capital.Position, new FixedRandom(0.0)); // great win → burn the capital

        Assert.Null(game.NativeSettlementAt(capital.Position));         // capital destroyed
        Assert.Equal(NativeSettlement.SurrenderedAlarm, sibling.Alarm); // the nation surrenders to peace (set, not added)
    }

    [Fact]
    public void CheckAttackSettlement_RejectsNonSettlementsAndBadAttackers()
    {
        (Game game, Unit attacker, NativeSettlement settlement) = SetupSettlementAttack();
        Assert.True(game.CheckAttackSettlement(attacker, settlement.Position).Allowed);

        // An adjacent tile with no settlement on it is rejected.
        Position empty = attacker.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && game.NativeSettlementAt(n) is null);
        Assert.False(game.CheckAttackSettlement(attacker, empty).Allowed);

        attacker.MovementLeft = 0;
        Assert.False(game.CheckAttackSettlement(attacker, settlement.Position).Allowed); // no movement
    }

    [Fact]
    public void CheckMove_OntoANativeSettlement_IsRejected()
    {
        (Game game, Unit attacker, NativeSettlement settlement) = SetupSettlementAttack();
        MoveCheck check = game.CheckMove(attacker, settlement.Position);
        Assert.False(check.Allowed);
        Assert.Contains("settlement", check.Reason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AttackSettlement_DestroyedState_SurvivesASaveRoundTrip()
    {
        (Game game, Unit attacker, NativeSettlement settlement) = SetupSettlementAttack();
        int before = game.NativeSettlements.Count;
        game.AttackSettlement(attacker, settlement.Position, new FixedRandom(0.0)); // forced great win → destroy + plunder

        SaveGame snapshot = SaveGame.From(game);
        Assert.Equal(SaveGame.CurrentVersion, snapshot.Version);
        Game loaded = SaveGame.FromJson(snapshot.ToJson()).Restore(Classic);

        Assert.Equal(before - 1, game.NativeSettlements.Count);                     // exactly one destroyed
        Assert.Equal(game.NativeSettlements.Count, loaded.NativeSettlements.Count); // stays gone after reload
        Assert.Equal(game.Gold, loaded.Gold);                                      // plunder gold persisted
        Assert.Equal(game.RandomState, loaded.RandomState);                        // resume-deterministic
    }

    [Fact]
    public void AttackSettlement_ProductionPath_DrawsFromTheSavedRngAndRoundTrips()
    {
        (Game game, Unit attacker, NativeSettlement settlement) = SetupSettlementAttack();
        RandomState before = game.RandomState;

        game.AttackSettlement(attacker, settlement.Position); // public path → the game's main saved RNG

        Assert.NotEqual(before, game.RandomState); // the saved stream advanced (≥ the combat draw)
        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(game.RandomState, loaded.RandomState); // resume-deterministic
        Assert.Equal(game.Gold, loaded.Gold);
        Assert.Equal(
            game.NativeSettlements.Select(s => s.Id).OrderBy(i => i),
            loaded.NativeSettlements.Select(s => s.Id).OrderBy(i => i));
    }

    // ---- Save round-trip (v18) ----

    [Fact]
    public void Owner_Role_And_Braves_SurviveASaveRoundTrip()
    {
        (Game game, Unit attacker, Unit brave, _) = SetupAttack(FreeColonist, Soldier);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Unit soldier = loaded.Units.First(u => u.Id == attacker.Id);
        Assert.Equal(Soldier, soldier.RoleId);
        Assert.Equal(1, soldier.RoleCount);
        Assert.Null(soldier.OwnerNationId);

        Unit loadedBrave = loaded.Units.First(u => u.Id == brave.Id);
        Assert.True(loadedBrave.IsNative);
        Assert.Equal(brave.OwnerNationId, loadedBrave.OwnerNationId);
    }

    [Fact]
    public void DefenderAt_RanksByComputedPower_NotBaseDefence()
    {
        // Two enemy units stacked on one tile: a damaged artillery (base defence 3, but −75% caught in the open →
        // 0.75) and a fortified free colonist (base defence 1, +50% → 1.5). FreeCol betterDefender picks the higher
        // computed power, so the colonist defends even though the gun has the higher BASE defence.
        Game game = Game.New(Classic, Seed);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        game.SetStance(game.HumanPlayer.PlayerId, power.PlayerId, Stance.War);
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
        Position tile = game.Map.AllPositions().First(p => !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));

        Unit gun = game.SpawnUnit(Classic.Unit("model.unit.damagedArtillery"), tile, power.PlayerId);
        Unit fortified = game.SpawnUnit(Classic.Unit(FreeColonist), tile, power.PlayerId);
        fortified.Orders = UnitOrders.Fortified;
        Unit attacker = game.PlayerUnits.First(u => u.IsOnMap); // any human enemy (DefenderAt ignores offence/adjacency)

        Assert.True(game.DefenceBase(gun) > game.DefenceBase(fortified)); // the gun has the higher BASE defence…
        Assert.Equal(fortified.Id, game.DefenderAt(attacker, tile)!.Id);  // …but the fortified colonist defends (higher power)
    }
}
