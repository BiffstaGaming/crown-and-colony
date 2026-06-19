using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Foreign-power retaliation combat (slice 1c-2): a foreign power at war with the human (war only starts when
/// the human attacks it) sends its armed units after the human's nearest unit, attacking when adjacent. Like
/// the native AI, every combat draw is from the power's OWN RNG stream (ADR-009) — so the human's stream 0
/// stays byte-stable however much the rivals fight. War is the gate; at peace the foreign AI expands as before.
/// </summary>
public class ForeignCombatTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string VeteranSoldier = "model.unit.veteranSoldier";
    private const string SoldierRole = "model.role.soldier";

    private static bool Free(Game g, Position n) =>
        g.Map.InBounds(n) && !g.Map.TerrainAt(n).IsWater
        && g.NativeSettlementAt(n) is null && g.ColonyAt(n) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == n);

    /// <summary>A foreign power with an armed soldier on a free tile beside the human's first on-map unit; optionally already at war.</summary>
    private static (Game game, Player power, Unit prey) Stage(ulong seed, bool atWar)
    {
        Game game = Game.New(Classic, seed);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Unit prey = game.PlayerUnits.First(u => u.IsOnMap);
        Position spot = prey.Position.Neighbours().First(n => Free(game, n));

        Unit soldier = game.SpawnUnit(Classic.Unit(VeteranSoldier), spot);
        soldier.OwnerId = power.PlayerId; // reassign from the human (SpawnUnit only owners natives directly)
        soldier.RoleId = SoldierRole;     // offence 0 (type) + 2 (role) > 0 → armed
        soldier.RoleCount = 1;
        if (atWar)
        {
            game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
        }
        return (game, power, prey);
    }

    [Fact]
    public void AtWar_RetaliatesAgainstAnAdjacentHumanUnit_AndRecordsANotice()
    {
        (Game game, Player power, Unit prey) = Stage(seed: 7, atWar: true);
        Position preyPos = prey.Position;

        game.EndTurn();

        Assert.Contains(game.CombatNotices, n => n.AttackerNationId == power.NationId && n.Position == preyPos);
    }

    [Fact]
    public void WhenNotAtWar_DoesNotAttackTheHuman()
    {
        // The gate is `StanceBetween(power, human) == War`; the default (un-provoked) stance is Uncontacted, so
        // a foreign soldier sitting right next to the human expands instead of attacking.
        (Game game, Player power, _) = Stage(seed: 7, atWar: false);
        Assert.Equal(Stance.Uncontacted, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId));

        game.EndTurn();

        Assert.DoesNotContain(game.CombatNotices, n => n.AttackerNationId == power.NationId);
    }

    // ---- Defend-settlement garrisoning (86d3c9vxj) ----

    [Fact]
    public void AtPeace_AnArmedUnit_MarchesInToGarrisonAnUndefendedOwnColony()
    {
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

        // Clear the power's starting units + purse so only what we stage acts (no other unit can garrison or be bought).
        foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId && u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        power.Gold = 0;

        // An undefended colony for the power on open inland ground.
        Position colonyTile = game.Map.AllPositions().First(p =>
            Free(game, p) && p.Neighbours().All(n => game.Map.InBounds(n) && Free(game, n)));
        Unit founder = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), colonyTile);
        founder.OwnerId = power.PlayerId;
        Colony colony = game.FoundColony(founder);
        colony.OwnerId = power.PlayerId;

        // An armed artillery (a non-founder → always reaches the garrison logic) two tiles from the colony, at peace.
        Position gunTile = colonyTile.Neighbours().SelectMany(n => n.Neighbours())
            .First(p => Free(game, p) && Cheb(p, colonyTile) == 2);
        Unit gun = game.SpawnUnit(Classic.Unit("model.unit.artillery"), gunTile);
        gun.OwnerId = power.PlayerId;
        Assert.Equal(Stance.Uncontacted, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId)); // not at war → defensive

        for (int i = 0; i < 6; i++)
        {
            game.EndTurn();
        }

        Unit moved = game.Units.Single(u => u.Id == gun.Id);
        Assert.Equal(colony.Position, moved.Position); // marched in and stands guard on its undefended colony
    }

    [Fact]
    public void AtPeace_AColonist_SeeksOutAndResolvesAKnownLostCityRumour()
    {
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId && u.IsOnMap).ToList())
        {
            game.Disband(u);
        }

        // A colony so the power is at its colony cap (can't found another → it explores instead).
        Position colonyTile = game.Map.AllPositions().First(p =>
            Free(game, p) && p.Neighbours().All(n => game.Map.InBounds(n) && Free(game, n)));
        Unit founder = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), colonyTile);
        founder.OwnerId = power.PlayerId;
        Colony colony = game.FoundColony(founder);
        colony.OwnerId = power.PlayerId;

        // A colonist far from the colony with a KNOWN Lost City Rumour on the adjacent tile.
        Position scoutTile = game.Map.AllPositions().First(p =>
            Free(game, p) && !game.Map.HasRumour(p) && Cheb(p, colonyTile) > 15
            && p.Neighbours().Any(n => Free(game, n)));
        Position rumour = scoutTile.Neighbours().First(n => Free(game, n));
        game.Map.AddRumour(rumour);
        power.ExploredSet.Add(rumour); // the power has discovered it
        Unit scout = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), scoutTile);
        scout.OwnerId = power.PlayerId;
        Assert.True(game.Map.HasRumour(rumour));

        game.EndTurn();

        Assert.False(game.Map.HasRumour(rumour)); // the colonist marched onto the rumour and resolved it
    }

    [Fact]
    public void AtWar_WithNoHumanUnitOnMap_AttacksNothing()
    {
        // The human-only target contract (NearestHumanUnit) is the sole guard keeping a foreign power from
        // attacking natives or other rivals. Empty the map of human units (found a colony with the start unit):
        // an at-war soldier then attacks nobody — no notice, and no native/rival is lost to combat.
        (Game game, _, Unit prey) = Stage(seed: 7, atWar: true);
        Assert.True(game.CheckFoundColony(prey).Allowed);
        game.FoundColony(prey); // consumes the human's only on-map unit
        Assert.DoesNotContain(game.PlayerUnits, u => u.IsOnMap);

        int nativeCountStart = game.NativeUnits.Count();
        game.EndTurn();

        Assert.Empty(game.CombatNotices);                         // no human on the map → the at-war soldier attacks nobody
        Assert.Equal(nativeCountStart, game.NativeUnits.Count()); // and kills no native/rival either
    }

    [Fact]
    public void ForeignRetaliation_IsReplayStable_ForAFixedSeed()
    {
        (Game a, _, _) = Stage(seed: 31337, atWar: true);
        (Game b, _, _) = Stage(seed: 31337, atWar: true);
        for (int turn = 0; turn < 12; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    [Fact]
    public void ForeignRetaliation_DoesNotTouchTheHumansStream0()
    {
        // Same seed + same staged soldier in both; only one is at war. The war game's soldier raids (its own
        // stream); the peace game's expands — but the human's stream 0 and scoped state stay byte-identical.
        (Game peace, _, _) = Stage(seed: 999, atWar: false);
        (Game war, _, _) = Stage(seed: 999, atWar: true);
        for (int turn = 0; turn < 12; turn++)
        {
            peace.EndTurn();
            war.EndTurn();
        }

        Assert.NotEqual(SaveGame.From(peace).ToJson(), SaveGame.From(war).ToJson()); // the war genuinely diverged…
        Assert.Equal(peace.RandomState, war.RandomState);                            // …yet stream 0 is untouched
        Assert.Equal(peace.HumanPlayer.Gold, war.HumanPlayer.Gold);
        Assert.Equal(peace.HumanPlayer.RecruitDock, war.HumanPlayer.RecruitDock);
    }

    // ---- FP-6a: scored seek-and-destroy target selection ----

    private static int Cheb(Position a, Position b) => System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    private static bool FreeLandTile(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater && g.ColonyAt(p) is null
        && g.NativeSettlementAt(p) is null && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>The nearest free land tile at least <paramref name="minDist"/> Chebyshev tiles from <paramref name="from"/>.</summary>
    private static Position FreeLandAtLeast(Game g, Position from, int minDist) =>
        g.Map.AllPositions().Where(p => Cheb(p, from) >= minDist && FreeLandTile(g, p))
            .OrderBy(p => Cheb(p, from)).ThenBy(p => p.Y).ThenBy(p => p.X).First();

    /// <summary>A war foreign power with an armed soldier on an open central tile, the human's lone map unit cleared into a far colony.</summary>
    private static (Game game, Player power, Unit soldier, Position at) Battlefield(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        Unit start = game.PlayerUnits.First(u => u.IsOnMap);
        Position startPos = start.Position;
        if (game.CheckFoundColony(start).Allowed)
        {
            game.FoundColony(start); // clear the human's only map unit; its colony sits >20 tiles from the battlefield (out of seek range)
        }
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Position at = game.Map.AllPositions()
            .Where(p => FreeLandTile(game, p) && Cheb(p, startPos) >= 20
                        && p.Neighbours().Count(n => FreeLandTile(game, n)) >= 6)
            .OrderBy(p => p.Y).ThenBy(p => p.X).First();
        Unit soldier = game.SpawnUnit(Classic.Unit(VeteranSoldier), at);
        soldier.OwnerId = power.PlayerId;
        soldier.RoleId = SoldierRole;
        soldier.RoleCount = 1;
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
        return (game, power, soldier, at);
    }

    [Fact]
    public void ScoredTarget_PrefersTheNearerOfTwoEqualUnits()
    {
        (Game game, _, Unit soldier, Position at) = Battlefield(7);
        Position near = FreeLandAtLeast(game, at, 2);
        Position far = FreeLandAtLeast(game, at, 7);
        game.SpawnUnit(Classic.Unit(VeteranSoldier), far);  // human-owned (SpawnUnit defaults to the human)
        game.SpawnUnit(Classic.Unit(VeteranSoldier), near);

        Game.ScoredTarget? target = game.PickAttackTarget(soldier);

        Assert.NotNull(target);
        Assert.False(target!.Value.IsColony);
        Assert.Equal(near, target.Value.Position); // the −100·distance term dominates between equal-value units
    }

    [Fact]
    public void ScoredTarget_TreasureTrain_OutweighsACloserPlainUnit()
    {
        (Game game, _, Unit soldier, Position at) = Battlefield(7);
        Position near = FreeLandAtLeast(game, at, 2);
        Position farther = FreeLandAtLeast(game, at, 5);
        game.SpawnUnit(Classic.Unit(VeteranSoldier), near); // closer, plain
        game.SpawnUnit(Classic.Unit("model.unit.treasureTrain"), farther).SetTreasureAmount(600); // +1000 bump

        Game.ScoredTarget? target = game.PickAttackTarget(soldier);

        Assert.NotNull(target);
        Assert.Equal(farther, target!.Value.Position); // the treasure bump beats the small extra distance
    }

    [Fact]
    public void ScoredTarget_EscalatesRange_FindingAFarTargetOnlyWhenNothingIsCloser()
    {
        (Game game, _, Unit soldier, Position at) = Battlefield(7);
        Position far = FreeLandAtLeast(game, at, 9); // beyond the first gate (8), within the second (12)
        Assert.True(Cheb(far, at) is > 8 and <= 12, $"expected the far tile in (8,12], was {Cheb(far, at)}");
        Unit farUnit = game.SpawnUnit(Classic.Unit(VeteranSoldier), far);

        // Alone, the far unit is found only because the ladder widens past range 8.
        Game.ScoredTarget? farOnly = game.PickAttackTarget(soldier);
        Assert.NotNull(farOnly);
        Assert.Equal(far, farOnly!.Value.Position);

        // Add a unit inside the first gate: now the near one wins and the far one is never considered.
        Position near = FreeLandAtLeast(game, at, 3);
        game.SpawnUnit(Classic.Unit(VeteranSoldier), near);
        Assert.Equal(near, game.PickAttackTarget(soldier)!.Value.Position);
    }

    [Fact]
    public void ScoredTarget_Warship_ScoresOnlyShips_NeverLandUnitsOrColonies()
    {
        (Game game, Player power, _, Position at) = Battlefield(7);
        // A war warship near the same battlefield: a land unit nearby must NOT be a candidate (wrong domain).
        Position water = game.Map.AllPositions()
            .Where(p => game.Map.InBounds(p) && game.Map.TerrainAt(p).IsWater && !game.Units.Any(u => u.IsOnMap && u.Position == p))
            .OrderBy(p => Cheb(p, at)).ThenBy(p => p.Y).ThenBy(p => p.X).First();
        Unit frigate = game.SpawnUnit(Classic.Unit("model.unit.frigate"), water);
        frigate.OwnerId = power.PlayerId;
        game.SpawnUnit(Classic.Unit(VeteranSoldier), FreeLandAtLeast(game, water, 2)); // a human land unit nearby

        Assert.Null(game.PickAttackTarget(frigate)); // no human ship in range; the land unit + colonies are not naval candidates
    }

    [Fact]
    public void AtWar_ANonFounderLandUnit_PursuesADistantPrey_InsteadOfIdling()
    {
        // Out-of-seek-range fallback (review fix): a war artillery (a non-founder armed land unit) whose only human
        // target is beyond range 16 must close on it, not idle. Pre-fix it fell through to "non-founder → wait".
        Game game = Game.New(Classic, 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Unit prey = game.PlayerUnits.First(u => u.IsOnMap); // the human's lone colonist; no human colonies founded
        Position at = game.Map.AllPositions()
            .Where(p => FreeLandTile(game, p) && Cheb(p, prey.Position) > 16)
            .OrderBy(p => Cheb(p, prey.Position)).ThenBy(p => p.Y).ThenBy(p => p.X).First();
        Unit gun = game.SpawnUnit(Classic.Unit("model.unit.artillery"), at);
        gun.OwnerId = power.PlayerId;
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
        int before = Cheb(gun.Position, prey.Position);

        game.EndTurn();

        Assert.True(Cheb(gun.Position, prey.Position) < before, "a war land unit should pursue a distant prey, not idle");
    }

    // ---- AI logistics: treasure-train cash-in (86d3c9vq9) ----

    /// <summary>A foreign power whose starting units are cleared, with one undefended colony on open inland ground.</summary>
    private static (Game game, Player power, Colony colony) PowerWithColony(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId && u.IsOnMap).ToList())
        {
            game.Disband(u); // only the staged treasure train should act
        }
        power.Gold = 0;

        Position colonyTile = game.Map.AllPositions().First(p =>
            Free(game, p) && p.Neighbours().All(n => game.Map.InBounds(n) && Free(game, n)));
        Unit founder = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), colonyTile);
        founder.OwnerId = power.PlayerId;
        Colony colony = game.FoundColony(founder);
        colony.OwnerId = power.PlayerId;
        return (game, power, colony);
    }

    [Fact]
    public void APowersTreasureTrain_AtItsOwnColony_IsCashedIn()
    {
        (Game game, Player power, Colony colony) = PowerWithColony(seed: 7);
        Unit train = game.SpawnUnit(Classic.Unit(Game.TreasureTrainUnitTypeId), colony.Position);
        train.OwnerId = power.PlayerId;
        train.SetTreasureAmount(1000);
        int trainId = train.Id;

        game.EndTurn();

        Assert.DoesNotContain(game.Units, u => u.Id == trainId); // cashed in → the train leaves the game
        Assert.True(power.Gold > 0);                             // its net gold banked to the power
    }

    [Fact]
    public void APowersTreasureTrain_AwayFromAColony_StepsTowardItToBankTheGold()
    {
        (Game game, Player power, Colony colony) = PowerWithColony(seed: 7);
        // A free tile exactly two Chebyshev tiles from the colony, reachable by a clear diagonal step.
        Position start = colony.Position.Neighbours().SelectMany(n => n.Neighbours())
            .First(p => Free(game, p) && Cheb(p, colony.Position) == 2);
        Unit train = game.SpawnUnit(Classic.Unit(Game.TreasureTrainUnitTypeId), start);
        train.OwnerId = power.PlayerId;
        train.SetTreasureAmount(1000);

        game.EndTurn();

        Unit moved = game.Units.Single(u => u.Id == train.Id);
        Assert.Equal(1, Cheb(moved.Position, colony.Position)); // stepped 2 → 1, closing on the colony
    }
}
