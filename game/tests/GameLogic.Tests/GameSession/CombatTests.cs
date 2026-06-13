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
            game = (SaveGame.From(game) with { Congress = congress }).Restore(Classic);
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
    public void Natives_CannotAttack()
    {
        (Game game, _, Unit brave, _) = SetupAttack();
        Assert.False(game.CheckAttack(brave, brave.Position.Neighbours().First()).Allowed);
    }

    // ---- Outcomes ----

    [Fact]
    public void GreatWin_SlaughtersTheBrave_AndRaisesAlarm()
    {
        (Game game, Unit attacker, Unit brave, NativeSettlement home) = SetupAttack("model.unit.artillery");

        CombatResult result = game.Attack(attacker, brave.Position, new FixedRandom(0.0));

        Assert.Equal(CombatResult.GreatWin, result);
        Assert.DoesNotContain(brave, game.Units);                 // the brave is destroyed
        Assert.Equal(
            NativeSettlement.TensionAddNormal + NativeSettlement.TensionAddUnitDestroyed,
            home.Alarm);                                          // +200 attack, +400 kill
    }

    [Fact]
    public void SoldierLoss_DisarmsTheSoldier_AndBraveCapturesTheMuskets()
    {
        (Game game, Unit attacker, Unit brave, NativeSettlement home) = SetupAttack(FreeColonist, Soldier);
        int id = attacker.Id;

        CombatResult result = game.Attack(attacker, brave.Position, new FixedRandom(0.99));

        Assert.Equal(CombatResult.GreatLoss, result);
        Unit disarmed = game.Units.First(u => u.Id == id);
        Assert.Equal(RoleType.DefaultRoleId, disarmed.RoleId);    // the soldier loses his muskets
        Assert.Equal(FreeColonist, disarmed.Type.Id);            // but survives as a colonist
        Assert.Equal("model.role.armedBrave", brave.RoleId);     // the brave arms itself with them
        Assert.Equal(NativeSettlement.TensionAddNormal, home.Alarm); // +200 attack only (nobody died)
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
        Game game = (SaveGame.From(baseGame) with { Congress = [Revere] }).Restore(Classic);
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
}
