using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
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

    /// <summary>Founds <see cref="Game.MaxAiColonies"/> well-separated colonies for <paramref name="power"/> so it sits
    /// at its colony cap (its remaining colonist then explores/visits rather than founding). Returns the last tile used.</summary>
    private static Position FoundColoniesToCap(Game game, Player power)
    {
        Position last = default;
        var taken = new System.Collections.Generic.List<Position>();
        for (int i = 0; i < Game.MaxAiColonies; i++)
        {
            Position tile = game.Map.AllPositions().First(p =>
                Free(game, p) && game.Map.TerrainAt(p).CanSettle
                && p.Neighbours().All(n => game.Map.InBounds(n) && Free(game, n))
                && taken.All(t => Cheb(t, p) > 3)); // spaced so footprints never touch (founding's min-spacing rule). AI founder → auto-resolves any native claim (86d3e4bj7)
            Unit founder = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), tile);
            founder.OwnerId = power.PlayerId;
            Colony colony = game.FoundColony(founder);
            colony.OwnerId = power.PlayerId;
            taken.Add(tile);
            last = tile;
        }
        return last;
    }

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

    [Fact]
    public void AtPeace_DoesNotAttackTheHuman_AndCheckAttackRefuses()
    {
        // 86d3drn45: once met and at PEACE, a foreign power is not an enemy — its armed unit beside the human does not
        // raid (the offensive is war-gated), and the legality oracle itself refuses an attack on the human's unit.
        (Game game, Player power, Unit prey) = Stage(seed: 7, atWar: false);
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.Peace); // they've met; peace, not war
        // The staged armed soldier stands on a tile adjacent to the prey (Stage spawns it on prey.Neighbours().First).
        Unit soldier = game.Units.First(u =>
            u.OwnerId == power.PlayerId && u.RoleId == SoldierRole && u.Position.IsAdjacentTo(prey.Position));

        Assert.False(game.CheckAttack(soldier, prey.Position).Allowed); // peace → no enemy to attack, though it's adjacent

        game.EndTurn();

        Assert.DoesNotContain(game.CombatNotices, n => n.AttackerNationId == power.NationId); // and it never raids at peace
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
            Free(game, p) && game.Map.TerrainAt(p).CanSettle && p.Neighbours().All(n => game.Map.InBounds(n) && Free(game, n)));
        Unit founder = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), colonyTile);
        founder.OwnerId = power.PlayerId;
        Colony colony = game.FoundColony(founder); // AI founder → auto-resolves any native claim (86d3e4bj7)
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

        // Found colonies up to the power's cap so its remaining colonist explores (can't found another).
        Position colonyTile = FoundColoniesToCap(game, power);

        // A colonist far from the colonies with a KNOWN Lost City Rumour on the adjacent tile.
        Position scoutTile = game.Map.AllPositions().First(p =>
            Free(game, p) && !game.Map.HasRumour(p) && Cheb(p, colonyTile) > 15
            && p.Neighbours().Any(n => Free(game, n))
            && game.ColonyAt(p) is null && p.Neighbours().All(n => !game.Map.InBounds(n) || game.ColonyAt(n) is null));
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
    public void AtPeace_AColonist_HeadsForTheNearestKnownUnvisitedNativeSettlement()
    {
        // The scout facet (86d3c9vta): with no rumour known and the power at its colony cap, a colonist makes for the
        // nearest native settlement it has discovered but not yet spoken with — closing the Chebyshev distance — rather
        // than wandering toward unexplored fog.
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId && u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        Position colonyTile = FoundColoniesToCap(game, power);

        // Mark every settlement as already visited by the power EXCEPT one chosen target → the target is the only
        // unvisited settlement, so the AI must head for it specifically.
        NativeSettlement target = game.NativeSettlements.First();
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            if (s.Id != target.Id) { s.MarkVisitedBy(power.PlayerId); }
        }
        power.ExploredSet.Add(target.Position); // the power has discovered the target settlement

        // Clear any rumour already in the power's fog (founding colonies reveals tiles) so the rumour branch is skipped
        // and the unvisited-settlement branch is the one under test.
        foreach (Position r in game.Map.Rumours.Where(power.Explored.Contains).ToList())
        {
            game.Map.RemoveRumour(r);
        }

        // A colonist on a free tile, away from the target.
        Position scoutTile = game.Map.AllPositions().First(p =>
            Free(game, p) && Cheb(p, target.Position) is > 2 and < 12
            && game.ColonyAt(p) is null && p.Neighbours().All(n => !game.Map.InBounds(n) || game.ColonyAt(n) is null));
        Unit scout = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), scoutTile);
        scout.OwnerId = power.PlayerId;
        Assert.DoesNotContain(game.Map.Rumours, r => power.Explored.Contains(r)); // no known rumour to distract it
        int before = Cheb(scoutTile, target.Position);

        game.EndTurn();

        Unit moved = game.Units.Single(u => u.Id == scout.Id);
        Assert.True(Cheb(moved.Position, target.Position) < before, // it closed on the settlement
            $"scout at {moved.Position} did not approach the settlement at {target.Position} (was {before})");
    }

    [Fact]
    public void AColonist_PrefersAKnownRumour_OverAnUnvisitedSettlement()
    {
        // Branch ordering: a known Lost City Rumour outranks heading for an unvisited settlement (the rumour is the
        // higher-value, resolve-on-arrival target). A scout with a rumour one way and a settlement the other goes for
        // the rumour.
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId && u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        FoundColoniesToCap(game, power);

        NativeSettlement target = game.NativeSettlements.First();
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            if (s.Id != target.Id) { s.MarkVisitedBy(power.PlayerId); }
        }
        power.ExploredSet.Add(target.Position);

        Position scoutTile = game.Map.AllPositions().First(p =>
            Free(game, p) && !game.Map.HasRumour(p) && Cheb(p, target.Position) is > 4 and < 12
            && p.Neighbours().Any(n => Free(game, n) && !game.Map.HasRumour(n))
            && game.ColonyAt(p) is null && p.Neighbours().All(n => !game.Map.InBounds(n) || game.ColonyAt(n) is null));
        // A rumour right beside the scout, on the OPPOSITE side from the settlement (so the two pull apart).
        Position rumour = scoutTile.Neighbours()
            .Where(n => Free(game, n))
            .OrderByDescending(n => Cheb(n, target.Position)) // farthest from the settlement → opposite pull
            .First();
        game.Map.AddRumour(rumour);
        power.ExploredSet.Add(rumour);
        Unit scout = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), scoutTile);
        scout.OwnerId = power.PlayerId;

        game.EndTurn();

        Assert.False(game.Map.HasRumour(rumour)); // it went for the rumour (resolved on arrival), not the settlement
    }

    // ── AI missionary (86d3c9vta missionary half — establish a mission) ───────────────────────────────────────────────

    private const string MissionaryRole = "model.role.missionary";

    /// <summary>Stages a foreign power at its colony cap with a missionary-role colonist on a free tile adjacent to a calm,
    /// unmissioned native settlement; returns the game, power, missionary and target settlement.</summary>
    private static (Game game, Player power, Unit missionary, NativeSettlement target) StageMissionary(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId && u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        FoundColoniesToCap(game, power); // at the cap → it won't found instead of establishing the mission

        // A settlement with a free adjacent land tile to stand the missionary on; clear any known rumour so the mission
        // branch (which precedes the rumour seek only via the adjacency test) is the one exercised.
        NativeSettlement target = game.NativeSettlements
            .First(s => s.Position.Neighbours().Any(n => Free(game, n)));
        Position spot = target.Position.Neighbours().First(n => Free(game, n));
        Unit missionary = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), spot);
        missionary.OwnerId = power.PlayerId;
        missionary.RoleId = MissionaryRole;
        return (game, power, missionary, target);
    }

    [Fact]
    public void AMissionary_EstablishesAMission_AtAnAdjacentUnmissionedSettlement()
    {
        (Game game, Player power, Unit missionary, NativeSettlement target) = StageMissionary(seed: 7);
        Assert.False(target.HasMission);                 // premise: no mission there yet
        int missionaryId = missionary.Id;

        game.EndTurn();

        Assert.True(target.HasMission);                  // the AI founded a mission
        Assert.Equal(power.PlayerId, target.MissionOwnerId);
        Assert.DoesNotContain(game.Units, u => u.Id == missionaryId); // the missionary was installed (left the map)
    }

    [Fact]
    public void AMissionary_DoesNotReFoundItsOwnExistingMission()
    {
        (Game game, Player power, Unit missionary, NativeSettlement target) = StageMissionary(seed: 7);
        target.MissionOwnerId = power.PlayerId;          // the power already holds the mission here
        int missionaryId = missionary.Id;

        game.EndTurn();

        // The mission branch is skipped (already ours), so the missionary stays on the map (it explores/visits instead).
        Assert.Contains(game.Units, u => u.Id == missionaryId);
    }

    [Fact]
    public void AMissionaryMission_IsReplayStable_AndLeavesTheHumanStream0ByteIdentical()
    {
        // The whole mission mechanic is RNG-free; staging an AI missionary must leave the human's stream 0 byte-identical
        // and play identically across two same-seed games.
        (Game a, _, _, _) = StageMissionary(seed: 4242);
        (Game b, _, _, _) = StageMissionary(seed: 4242);
        for (int i = 0; i < 3; i++) { a.EndTurn(); b.EndTurn(); }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());

        (Game withMission, _, _, _) = StageMissionary(seed: 4242);
        Game noMission = Game.New(Classic, seed: 4242);
        for (int i = 0; i < 3; i++) { withMission.EndTurn(); noMission.EndTurn(); }
        Assert.Equal(noMission.RandomState, withMission.RandomState); // stream 0 untouched by the missionary's activity
    }

    [Fact]
    public void ScoutTargeting_IsReplayStable_AndLeavesTheHumanStream0ByteIdentical()
    {
        // ADR-009: the scout-targeting branch draws only the power's own stream — two same-seed games play identically,
        // and a game where a power scouts a settlement keeps the human's stream 0 byte-stable.
        static Game Staged()
        {
            Game game = Game.New(Classic, seed: 4242);
            Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
            foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId && u.IsOnMap).ToList())
            {
                game.Disband(u);
            }
            FoundColoniesToCap(game, power);
            NativeSettlement target = game.NativeSettlements.First();
            foreach (NativeSettlement s in game.NativeSettlements)
            {
                if (s.Id != target.Id) { s.MarkVisitedBy(power.PlayerId); }
            }
            power.ExploredSet.Add(target.Position);
            Position scoutTile = game.Map.AllPositions().First(p =>
                Free(game, p) && Cheb(p, target.Position) is > 2 and < 12 && game.ColonyAt(p) is null
                && p.Neighbours().All(n => !game.Map.InBounds(n) || game.ColonyAt(n) is null));
            Unit scout = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), scoutTile);
            scout.OwnerId = power.PlayerId;
            return game;
        }

        Game a = Staged();
        Game b = Staged();
        for (int i = 0; i < 5; i++) { a.EndTurn(); b.EndTurn(); }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson()); // same seed + setup → identical play

        // The human's stream 0 is byte-identical to a same-seed game with NO staged scout (the scout draws its own stream).
        Game withScout = Staged();
        Game noScout = Game.New(Classic, seed: 4242);
        for (int i = 0; i < 5; i++) { withScout.EndTurn(); noScout.EndTurn(); }
        Assert.Equal(noScout.RandomState, withScout.RandomState); // stream 0 untouched by the scout's activity
    }

    [Fact]
    public void AtWar_WithNoHumanUnitOnMap_AttacksNothing()
    {
        // The human-only target contract (NearestHumanUnit) is the sole guard keeping a foreign power from
        // attacking natives or other rivals. Empty the map of human units (found a colony with the start unit):
        // an at-war soldier then attacks nobody — no notice, and no native/rival is lost to combat.
        (Game game, _, Unit prey) = Stage(seed: 7, atWar: true);
        Assert.True(game.CheckFoundColony(prey).Allowed);
        game.FoundColony(prey); // a colony from one starting unit
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u); // disband the rest of the roster so the map holds no human unit at all
        }
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
            Free(game, p) && game.Map.TerrainAt(p).CanSettle && p.Neighbours().All(n => game.Map.InBounds(n) && Free(game, n)));
        Unit founder = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), colonyTile);
        founder.OwnerId = power.PlayerId;
        Colony colony = game.FoundColony(founder); // AI founder → auto-resolves any native claim (86d3e4bj7)
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

    // ---- AI breaks the peace from accumulated territorial tension (86d3c9udb) ----

    /// <summary>
    /// A foreign power with one undefended colony and a human colony planted right beside it so the power accrues
    /// territorial tension each turn. Both powers' starting units are cleared so nothing else acts. When
    /// <paramref name="atPeace"/> the pair is set to Peace (so the accrual can escalate it to War); otherwise it stays
    /// Uncontacted (the accrual early-returns) — a valid stream-0 control that shares the identical human colony.
    /// </summary>
    private static (Game game, Player power) PowerCrowdedByHuman(ulong seed, bool atPeace = true)
    {
        (Game game, Player power, Colony powerColony) = PowerWithColony(seed);
        // A human colony two tiles from the power's colony (inside ColonialTensionRadius = 3, so it counts as encroachment).
        Position humanTile = powerColony.Position.Neighbours().SelectMany(n => n.Neighbours())
            .First(p => Free(game, p) && game.Map.TerrainAt(p).CanSettle && Cheb(p, powerColony.Position) == 2);
        Unit settler = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), humanTile);
        // The tile may sit on native land here; the human founds it by stealing (86d3e4bj7 — free + RNG-free, so the
        // power-tension/stream-0 assertions are unaffected). This test is about colonial encroachment, not the claim.
        Colony humanColony = game.FoundColony(settler, LandClaimChoice.Steal); // human-owned (OwnerId 0)
        Assert.True(game.ColonyAt(humanColony.Position) is { OwnerId: 0 });
        if (atPeace)
        {
            game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.Peace); // they've met and are at peace
        }
        return (game, power);
    }

    [Fact]
    public void APeacefulPower_CrowdedByHumanColonies_AccruesTensionAndEventuallyDeclaresWar()
    {
        (Game game, Player power) = PowerCrowdedByHuman(seed: 7);
        Assert.Equal(Stance.Peace, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId));
        int startTension = game.TensionBetween(power.PlayerId, game.HumanPlayer.PlayerId);

        bool wentToWar = false;
        for (int turn = 0; turn < 30 && !wentToWar; turn++)
        {
            game.EndTurn();
            wentToWar = game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId) == Stance.War;
        }

        Assert.True(wentToWar, "a power steadily crowded by a human colony should break the peace once tension crosses the war line");
        Assert.True(game.TensionBetween(power.PlayerId, game.HumanPlayer.PlayerId) > startTension); // tension rose to get there
        // War is mutual once declared (SetStance is symmetric), so the human now reads as at war with the power too.
        Assert.Equal(Stance.War, game.StanceBetween(game.HumanPlayer.PlayerId, power.PlayerId));
    }

    [Fact]
    public void APeacefulPower_WithNoHumanColonyNearby_StaysAtPeace()
    {
        // The trigger is territorial encroachment: with no human colony inside the radius, tension never rises, so the
        // Peace→War branch stays unreachable — the power keeps the peace indefinitely.
        (Game game, Player power, _) = PowerWithColony(seed: 7); // no human colony founded at all
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.Peace);

        for (int turn = 0; turn < 30; turn++)
        {
            game.EndTurn();
            Assert.Equal(0, game.TensionBetween(power.PlayerId, game.HumanPlayer.PlayerId)); // nothing to resent
            Assert.Equal(Stance.Peace, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId));
        }
    }

    [Fact]
    public void AccruingTerritorialTension_DrawsNoRng_HumanStream0ByteStable()
    {
        // The decisive ADR-009 guard for the escalation itself: the territorial-tension accrual (and, when it tips the
        // pair into war, the declaration spike) is RNG-free. Called directly and repeatedly until war is reached, the
        // human's RNG stream 0 never advances — only the diplomacy bookkeeping changes.
        (Game game, Player power) = PowerCrowdedByHuman(seed: 4242, atPeace: true);
        RandomState before = game.RandomState;

        for (int i = 0; i < 20 && game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId) != Stance.War; i++)
        {
            game.AccrueTerritorialTension(power); // accrue; then mirror the turn-loop's flip so we actually reach war
            if (power.Stances.GetValueOrDefault(game.HumanPlayer.PlayerId) is Stance.Peace or Stance.CeaseFire
                && Game.StanceFromTension(power.Stances[game.HumanPlayer.PlayerId],
                                          power.Tensions.GetValueOrDefault(game.HumanPlayer.PlayerId)) == Stance.War)
            {
                game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
                game.ChangeTension(power.PlayerId, game.HumanPlayer.PlayerId, Game.TensionWar);
            }
        }

        Assert.Equal(Stance.War, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId)); // it escalated to war…
        Assert.Equal(before, game.RandomState);                                                 // …drawing nothing from stream 0
    }

    [Fact]
    public void ACrowdedGame_ThatBreaksThePeace_ReplaysByteIdentically()
    {
        // Determinism (ADR-009): two same-seed games, both with a power crowded into breaking the peace, play out
        // byte-identically over many turns — the accrual/declaration and the ensuing war all draw deterministically.
        (Game a, Player _) = PowerCrowdedByHuman(seed: 13);
        (Game b, Player _) = PowerCrowdedByHuman(seed: 13);
        for (int turn = 0; turn < 25; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    // ---- Benjamin Franklin's peaceTreaty +50% holds the peace (FreeCol EuropeanAIPlayer.peaceHolds) ----

    private const string BenjaminFranklin = "model.foundingFather.benjaminFranklin";

    /// <summary>The first turn (1..max) on which the crowded power breaks the peace and declares war, or <c>max+1</c> if it
    /// never did within the window. Optionally elects Benjamin Franklin to the human first (his peaceTreaty +50% gives the
    /// power a per-turn reprieve from re-declaring war).</summary>
    private static int TurnsUntilWar(ulong seed, bool humanHasFranklin, int max = 60)
    {
        (Game game, Player power) = PowerCrowdedByHuman(seed);
        if (humanHasFranklin)
        {
            game.HumanPlayer.CongressList.Add(BenjaminFranklin);
        }
        for (int turn = 1; turn <= max; turn++)
        {
            game.EndTurn();
            if (game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId) == Stance.War)
            {
                return turn;
            }
        }
        return max + 1;
    }

    [Fact]
    public void Franklin_HoldsThePeace_SoWarComesLater_AcrossManySeeds()
    {
        // Faithful to FreeCol's peaceHolds: with Franklin's peaceTreaty +50% the human's treaty holds each turn the power
        // WOULD break it (a 50% reprieve roll on the power's own stream), so across a spread of seeds war arrives strictly
        // LATER on aggregate than without Franklin (some games it is averted within the whole window). Aggregating over
        // seeds keeps the test robust to a single unlucky run while still pinning the effect's direction. (A handful of
        // seeds place the staged AI colony on un-settleable terrain in PowerWithColony — those throw on setup and are
        // skipped; we just need a healthy sample.)
        int withoutTotal = 0, withTotal = 0, used = 0;
        for (ulong seed = 1; seed <= 24 && used < 12; seed++)
        {
            int without, with;
            try
            {
                without = TurnsUntilWar(seed, humanHasFranklin: false);
                with = TurnsUntilWar(seed, humanHasFranklin: true);
            }
            catch (InvalidMoveException)
            {
                continue; // this seed's staged colony site is un-settleable — skip it
            }
            withoutTotal += without;
            withTotal += with;
            used++;
        }

        Assert.True(used >= 8, $"expected a healthy sample of usable seeds, got {used}");
        Assert.True(withTotal > withoutTotal,
            $"Franklin's peaceTreaty should delay (or avert) war: with={withTotal} should exceed without={withoutTotal} (over {used} seeds)");
    }

    [Fact]
    public void WithoutFranklin_TheWarTiming_IsByteIdenticalToBefore_NoRollDrawn()
    {
        // The peace-hold roll is short-circuited (no draw) when the human holds no Franklin: the probability is 0, so
        // PeaceTreatyHolds returns false without touching any stream. Two same-seed no-Franklin games therefore reach war
        // on the exact same turn and replay byte-identically — the new gate is inert in the default game (ADR-009).
        Assert.Equal(TurnsUntilWar(7, humanHasFranklin: false), TurnsUntilWar(7, humanHasFranklin: false));

        (Game a, Player _) = PowerCrowdedByHuman(seed: 21);
        (Game b, Player _) = PowerCrowdedByHuman(seed: 21);
        for (int turn = 0; turn < 25; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    [Fact]
    public void Franklin_PeaceHold_DrawsOnlyThePowersStream_HumanStream0ByteStable()
    {
        // The reprieve roll is drawn from the POWER'S own RNG stream, never the human's stream 0 — so even with Franklin
        // elected and the power repeatedly tempted to break the peace, the human's stream 0 is untouched until something
        // the human does draws from it. We assert stream 0 is byte-stable across the crowded turns up to (and not past)
        // the war declaration, which is the diplomacy bookkeeping the gate governs.
        (Game game, Player power) = PowerCrowdedByHuman(seed: 99);
        game.HumanPlayer.CongressList.Add(BenjaminFranklin);
        RandomState before = game.RandomState;

        for (int turn = 0; turn < 40
            && game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId) != Stance.War; turn++)
        {
            game.AccrueTerritorialTension(power);
            if (power.Stances.GetValueOrDefault(game.HumanPlayer.PlayerId) is Stance.Peace or Stance.CeaseFire
                && Game.StanceFromTension(power.Stances[game.HumanPlayer.PlayerId],
                                          power.Tensions.GetValueOrDefault(game.HumanPlayer.PlayerId)) == Stance.War
                && !game.PeaceTreatyHolds(power, game.HumanPlayer))
            {
                game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
                game.ChangeTension(power.PlayerId, game.HumanPlayer.PlayerId, Game.TensionWar);
            }
        }

        Assert.Equal(before, game.RandomState); // the peace-hold roll never advanced the human's stream 0
    }

    // ── AI answers / proposes treaties (86d3c9uar turn-wiring) ────────────────────────────────────────────────────────

    /// <summary>A foreign power at war with the human, stripped of its land units so it is militarily weak (a low strength
    /// ratio → its own peace evaluation accepts). Returns the game + power.</summary>
    private static (Game game, Player power) WeakPowerAtWar(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        foreach (Unit u in game.Units.Where(u => u.OwnerId == power.PlayerId && !u.Type.IsNaval).ToList())
        {
            game.Disband(u); // no land power → StrengthRatio ≈ 0 → peace is strongly positive
        }
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
        game.ChangeTension(power.PlayerId, game.HumanPlayer.PlayerId, Game.TensionWar); // a genuine war (won't decay off this turn)
        return (game, power);
    }

    [Fact]
    public void AWeakPowerAtWar_SuesForPeace_OnItsTurn()
    {
        // Turn-wiring: a power at war that is militarily weak weighs a peace treaty through EvaluateTrade, accepts, and
        // settles it on its own turn — so the war ends without any human action.
        (Game game, Player power) = WeakPowerAtWar(seed: 7);
        Assert.Equal(Stance.War, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId));

        game.EndTurn();

        Assert.Equal(Stance.Peace, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId)); // it sued for peace
    }

    [Fact]
    public void AStrongPowerAtWar_FightsOn_RatherThanSuingForPeace()
    {
        // A power that is militarily strong (a high strength ratio) has its peace evaluation REJECT the truce — so it
        // keeps the war rather than offering peace. Built magic-number-free: it simply has overwhelming land force.
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        // Strip the human's land units and give the power a big army → its strength ratio is ≈ 1 (peace eval invalid).
        foreach (Unit u in game.Units.Where(u => u.OwnerId == game.HumanPlayer.PlayerId && !u.Type.IsNaval).ToList())
        {
            game.Disband(u);
        }
        for (int i = 0; i < 6; i++)
        {
            Position spot = game.Map.AllPositions().First(p => Free(game, p));
            Unit gun = game.SpawnUnit(Classic.Unit("model.unit.artillery"), spot);
            gun.OwnerId = power.PlayerId;
        }
        game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
        game.ChangeTension(power.PlayerId, game.HumanPlayer.PlayerId, Game.TensionWar); // a genuine war so it can't decay off this turn

        game.EndTurn();

        Assert.Equal(Stance.War, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId)); // strong → fights on
    }

    [Fact]
    public void AtPeace_APower_DoesNotProposeAnything()
    {
        // The diplomacy step only acts on a war; an un-provoked power at the default Uncontacted stance offers nothing.
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Assert.Equal(Stance.Uncontacted, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId));

        game.EndTurn();

        Assert.Equal(Stance.Uncontacted, game.StanceBetween(power.PlayerId, game.HumanPlayer.PlayerId)); // unchanged
    }

    [Fact]
    public void RespondToTrade_SettlesAnAcceptableOffer_AndDeclinesAnUnacceptableOne()
    {
        // The answer seam: a power settles a treaty it accepts (free gold) and leaves an unacceptable one (pure loss) be.
        Game game = Game.New(Classic, seed: 7);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Player human = game.HumanPlayer;
        human.Gold = 1000;
        int powerBefore = power.Gold;

        // Offer the power 300 gold from the human (the power is the destination → positive value → accepted + settled).
        var gift = new DiplomaticTrade(human.PlayerId, power.PlayerId)
            .Add(new GoldTradeItem(human.PlayerId, power.PlayerId, 300));
        Assert.True(game.RespondToTrade(power, gift));
        Assert.Equal(powerBefore + 300, power.Gold); // it accepted and the gold moved

        // Offer that the power pay 300 gold for nothing (pure cost → negative value → declined, nothing moves).
        int powerNow = power.Gold;
        var demand = new DiplomaticTrade(human.PlayerId, power.PlayerId)
            .Add(new GoldTradeItem(power.PlayerId, human.PlayerId, 300));
        Assert.False(game.RespondToTrade(power, demand));
        Assert.Equal(powerNow, power.Gold); // declined → unchanged
    }

    [Fact]
    public void SuingForPeace_IsReplayStable_AndLeavesTheHumanStream0ByteIdentical()
    {
        // ADR-009: the diplomacy step is deterministic and RNG-free — two same-seed weak-power-at-war games play
        // identically, and the human's stream 0 is byte-identical to a same-seed game with no staged war.
        (Game a, _) = WeakPowerAtWar(seed: 4242);
        (Game b, _) = WeakPowerAtWar(seed: 4242);
        for (int i = 0; i < 3; i++) { a.EndTurn(); b.EndTurn(); }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());

        (Game withWar, _) = WeakPowerAtWar(seed: 4242);
        Game noWar = Game.New(Classic, seed: 4242);
        for (int i = 0; i < 3; i++) { withWar.EndTurn(); noWar.EndTurn(); }
        Assert.Equal(noWar.RandomState, withWar.RandomState); // stream 0 untouched by the diplomacy step
    }

    // ── AI counter-offers (86d3c9uar) ─────────────────────────────────────────────────────────────────────────────────

    private static int ForeignPowerId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    private static int SecondForeignPowerId(Game game) =>
        game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).Skip(1).First().PlayerId;

    [Fact]
    public void NegotiateTrade_TheRecipientPrunesABadClause_AndTheCounterSettles()
    {
        // AI-to-AI converging haggle: two evenly-matched powers at WAR both want peace (a balanced strength ratio scores
        // peace positively for each). A offers peace bundled with a unit demand B can't stomach (B's roster is thin →
        // invalid). B prunes the unit demand and counters BARE peace; A — which also wants peace — accepts and the war
        // ends. The pruned clause is what made the raw offer unacceptable; once gone, both sides agree.
        Game game = Game.New(Classic, seed: 7);
        Player a = game.Players.First(p => p.PlayerId == ForeignPowerId(game));
        Player b = game.Players.First(p => p.PlayerId == SecondForeignPowerId(game));

        // Even strength so peace is positive for BOTH (ratio ≈ 0.5 → not refused either way).
        GiveSoldiers(game, a.PlayerId, 3);
        GiveSoldiers(game, b.PlayerId, 3);
        game.SetStance(a.PlayerId, b.PlayerId, Stance.War);
        game.ChangeTension(a.PlayerId, b.PlayerId, Game.TensionWar);
        Assert.Equal(Stance.War, game.StanceBetween(a.PlayerId, b.PlayerId));

        Unit bsOnly = game.Units.First(u => u.OwnerId == b.PlayerId && u.OwnerNationId is null);
        var offer = new DiplomaticTrade(a.PlayerId, b.PlayerId)
            .Add(new StanceTradeItem(a.PlayerId, b.PlayerId, Stance.Peace))                  // both want this
            .Add(new UnitTradeItem(source: b.PlayerId, destination: a.PlayerId, unitId: bsOnly.Id)); // B won't give (thin roster)

        Assert.False(game.EvaluateTrade(b.PlayerId, offer).Accept); // the unit demand vetoes the raw offer for B

        bool agreed = game.NegotiateTrade(a, b, offer);

        Assert.True(agreed); // B pruned the unit clause, countered bare peace, A accepted
        Assert.Equal(Stance.Peace, game.StanceBetween(a.PlayerId, b.PlayerId)); // the war ended via the counter
        Assert.Equal(b.PlayerId, bsOnly.OwnerId); // the pruned unit never changed hands
    }

    private static void GiveSoldiers(Game g, int ownerId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Position spot = g.Map.AllPositions().First(p => Free(g, p));
            Unit soldier = g.SpawnUnit(Classic.Unit(VeteranSoldier), spot);
            soldier.RoleId = SoldierRole;
            soldier.OwnerId = ownerId;
        }
    }

    [Fact]
    public void NegotiateTrade_CollapsesWhenTheOfferIsUnsalvageable()
    {
        // An all-loss offer (B gives away its only colony AND its only unit, both invalid) → no counter is possible, so
        // the negotiation collapses with nothing settled.
        Game game = Game.New(Classic, seed: 7);
        Player a = game.Players.First(p => p.PlayerId == ForeignPowerId(game));
        Player b = game.Players.First(p => p.PlayerId == SecondForeignPowerId(game));

        Position site = game.Map.AllPositions().First(p => Free(game, p) && game.Map.TerrainAt(p).CanSettle
            && p.Neighbours().All(n => !game.Map.InBounds(n) || game.ColonyAt(n) is null));
        Unit founder = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), site);
        founder.OwnerId = b.PlayerId;
        Colony only = game.FoundColony(founder);
        only.OwnerId = b.PlayerId;

        var offer = new DiplomaticTrade(a.PlayerId, b.PlayerId)
            .Add(new ColonyTradeItem(source: b.PlayerId, destination: a.PlayerId, colonyId: only.Id)); // B's only colony

        Assert.False(game.NegotiateTrade(a, b, offer)); // unsalvageable → collapses
        Assert.Equal(b.PlayerId, only.OwnerId);          // and nothing changed hands
    }

    // ── AI-to-AI NegotiateTrade wired into the foreign turn (86d3drn4h) ──────────────────────────────────────────────────

    /// <summary>Two evenly-matched foreign powers set at war with EACH OTHER (no human colonies near them, so neither
    /// accrues tension toward the human and the foreign turn's only diplomacy is the foreign-foreign haggle).</summary>
    private static (Game game, Player a, Player b) TwoForeignPowersAtWar(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        Player a = game.Players.First(p => p.PlayerId == ForeignPowerId(game));
        Player b = game.Players.First(p => p.PlayerId == SecondForeignPowerId(game));
        // Even land strength so a mutual peace scores positive for BOTH (ratio ≈ 0.5 → not refused either way).
        GiveSoldiers(game, a.PlayerId, 3);
        GiveSoldiers(game, b.PlayerId, 3);
        game.SetStance(a.PlayerId, b.PlayerId, Stance.War);
        game.ChangeTension(a.PlayerId, b.PlayerId, Game.TensionWar); // a genuine war (won't decay off in one turn)
        return (game, a, b);
    }

    [Fact]
    public void OnTheForeignTurn_TwoPowersAtWarWithEachOther_HaggleToPeaceViaNegotiateTrade()
    {
        // Turn-wiring (86d3drn4h): RunForeignPowerDiplomacy now drives the multi-round NegotiateTrade over a power's
        // foreign-foreign wars. Two evenly-matched rivals both want peace → on the lower-id power's turn the haggle fires
        // and settles a truce, ending the war without any human action.
        (Game game, Player a, Player b) = TwoForeignPowersAtWar(seed: 7);
        Assert.Equal(Stance.War, game.StanceBetween(a.PlayerId, b.PlayerId));

        game.EndTurn();

        Assert.Equal(Stance.Peace, game.StanceBetween(a.PlayerId, b.PlayerId)); // the AI-to-AI haggle ended the war
        Assert.Equal(Stance.Peace, game.StanceBetween(b.PlayerId, a.PlayerId)); // both ways (the truce is symmetric)
    }

    [Fact]
    public void TheForeignForeignHaggle_IsReplayStable_AndLeavesTheHumanStream0ByteIdentical()
    {
        // ADR-009: the foreign-foreign NegotiateTrade draws only the AI powers' own streams — two same-seed games play
        // identically, and the human's stream 0 is byte-identical to a same-seed game with no staged foreign war.
        (Game x, _, _) = TwoForeignPowersAtWar(seed: 4242);
        (Game y, _, _) = TwoForeignPowersAtWar(seed: 4242);
        for (int i = 0; i < 3; i++) { x.EndTurn(); y.EndTurn(); }
        Assert.Equal(SaveGame.From(x).ToJson(), SaveGame.From(y).ToJson());

        (Game withWar, _, _) = TwoForeignPowersAtWar(seed: 4242);
        Game noWar = Game.New(Classic, seed: 4242);
        for (int i = 0; i < 3; i++) { withWar.EndTurn(); noWar.EndTurn(); }
        Assert.Equal(noWar.RandomState, withWar.RandomState); // stream 0 untouched by the foreign-foreign haggle
    }

    [Fact]
    public void NegotiateTrade_InvolvingTheHuman_NeverAutoCounters_LeavingStream0ByteIdentical()
    {
        // ADR-009: a negotiation involving the human is the (future) UI's job — NegotiateTrade refuses it AI-side, so
        // the human's stream 0 is never drawn by the counter loop.
        Game game = Game.New(Classic, seed: 7);
        Player human = game.HumanPlayer;
        Player power = game.Players.First(p => p.PlayerId == ForeignPowerId(game));
        human.Gold = 1000;

        var offer = new DiplomaticTrade(human.PlayerId, power.PlayerId)
            .Add(new GoldTradeItem(source: human.PlayerId, destination: power.PlayerId, amount: 100))
            .Add(new GoldTradeItem(source: power.PlayerId, destination: human.PlayerId, amount: 500)); // net −400 to power

        var before = game.RandomState;
        Assert.False(game.NegotiateTrade(human, power, offer)); // refused: a human party never auto-counters
        Assert.Equal(before, game.RandomState);                  // stream 0 byte-identical
    }

    // ── AI proactive alliance + cease-fire proposals (86d3drn4f) ───────────────────────────────────────────────────────

    private static int ThirdForeignPowerId(Game game) =>
        game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).Skip(2).First().PlayerId;

    [Fact]
    public void AStrongFriendlyPower_SharingAnEnemy_ProactivelyOffersAlliance()
    {
        // Proactive (86d3drn4f): A is strong, at peace with B, and both A and B are at war with C (a shared enemy). On
        // A's proactive-diplomacy step it offers B an alliance, which B — also wanting a partner against C — accepts via
        // the existing NegotiateTrade backend, so the two ally.
        Game game = Game.New(Classic, seed: 7);
        Player a = game.Players.First(p => p.PlayerId == ForeignPowerId(game));
        Player b = game.Players.First(p => p.PlayerId == SecondForeignPowerId(game));
        Player c = game.Players.First(p => p.PlayerId == ThirdForeignPowerId(game));

        // A and B are even (so allying scores positive for both); C is the shared enemy.
        GiveSoldiers(game, a.PlayerId, 3);
        GiveSoldiers(game, b.PlayerId, 3);
        game.SetStance(a.PlayerId, b.PlayerId, Stance.Peace);     // friendly
        game.SetStance(a.PlayerId, c.PlayerId, Stance.War);       // A's enemy
        game.SetStance(b.PlayerId, c.PlayerId, Stance.War);       // …who is also B's enemy
        Assert.Equal(Stance.Peace, game.StanceBetween(a.PlayerId, b.PlayerId));

        game.ProposeProactiveTreaties(a);

        Assert.Equal(Stance.Alliance, game.StanceBetween(a.PlayerId, b.PlayerId)); // A proactively allied with B
    }

    [Fact]
    public void AStrongPower_DoesNotAlly_WithAFriendThatSharesNoEnemy()
    {
        // The "enemy of my enemy" trigger is required: a strong power at peace with a friend who shares NO enemy makes
        // no alliance offer (so an un-provoked friendly relationship is left untouched).
        Game game = Game.New(Classic, seed: 7);
        Player a = game.Players.First(p => p.PlayerId == ForeignPowerId(game));
        Player b = game.Players.First(p => p.PlayerId == SecondForeignPowerId(game));
        GiveSoldiers(game, a.PlayerId, 5);
        GiveSoldiers(game, b.PlayerId, 1);
        game.SetStance(a.PlayerId, b.PlayerId, Stance.Peace); // friendly but no shared enemy

        game.ProposeProactiveTreaties(a);

        Assert.Equal(Stance.Peace, game.StanceBetween(a.PlayerId, b.PlayerId)); // unchanged — no alliance without a shared foe
    }

    [Fact]
    public void AStalematedWar_DrawsACeaseFireOffer_ThatBothSidesAccept()
    {
        // Proactive cease-fire: two evenly-matched powers at war (a stalemate — neither can win) → the lower-id power
        // offers a cease-fire on its turn, the other accepts, and the war cools to a truce.
        Game game = Game.New(Classic, seed: 7);
        Player a = game.Players.First(p => p.PlayerId == ForeignPowerId(game));
        Player b = game.Players.First(p => p.PlayerId == SecondForeignPowerId(game));
        GiveSoldiers(game, a.PlayerId, 3);
        GiveSoldiers(game, b.PlayerId, 3); // ratio ≈ 0.5 → stalemate
        game.SetStance(a.PlayerId, b.PlayerId, Stance.War);
        game.ChangeTension(a.PlayerId, b.PlayerId, Game.TensionWar);

        game.ProposeProactiveTreaties(a);

        Assert.Equal(Stance.CeaseFire, game.StanceBetween(a.PlayerId, b.PlayerId)); // the stalemate drew a truce
    }

    [Fact]
    public void AnAllianceOfferToTheHuman_IsQueuedForTheUI_AndNeverAutoApplied()
    {
        // An offer to the HUMAN is surfaced via the pending-proposal seam (for the backlogged negotiation UI) — it is NOT
        // applied: the human decides at the table, and the AI never auto-allies the human.
        Game game = Game.New(Classic, seed: 7);
        Player a = game.Players.First(p => p.PlayerId == ForeignPowerId(game));
        Player human = game.HumanPlayer;
        Player c = game.Players.First(p => p.PlayerId == SecondForeignPowerId(game));
        GiveSoldiers(game, a.PlayerId, 3);
        game.SetStance(a.PlayerId, human.PlayerId, Stance.Peace);   // A is friendly with the human
        game.SetStance(a.PlayerId, c.PlayerId, Stance.War);         // shared enemy C
        game.SetStance(human.PlayerId, c.PlayerId, Stance.War);

        var before = game.RandomState;
        game.ProposeProactiveTreaties(a);

        Assert.Equal(Stance.Peace, game.StanceBetween(a.PlayerId, human.PlayerId)); // NOT auto-allied — still peace
        Assert.Single(game.PendingHumanProposals);                                  // the offer is queued for the UI
        DiplomaticTrade queued = game.PendingHumanProposals[0];
        Assert.Equal(a.PlayerId, queued.ProposerId);
        Assert.Equal(human.PlayerId, queued.RecipientId);
        Assert.Equal(Stance.Alliance, Assert.IsType<StanceTradeItem>(Assert.Single(queued.Items)).Stance);
        Assert.Equal(before, game.RandomState);                                     // queuing draws nothing from stream 0

        // The seam drains once.
        Assert.Single(game.TakePendingHumanProposals());
        Assert.Empty(game.PendingHumanProposals);
    }

    [Fact]
    public void TheProactiveHaggle_IsDeterministic_AndLeavesTheHumanStream0ByteIdentical()
    {
        // ADR-009: the proactive AI-to-AI proposals draw only the proposer's own stream — two same-seed runs settle
        // identically, and the human's stream 0 is never advanced by queuing a human-bound offer.
        Game Build(ulong seed)
        {
            Game g = Game.New(Classic, seed);
            Player a = g.Players.First(p => p.PlayerId == ForeignPowerId(g));
            Player b = g.Players.First(p => p.PlayerId == SecondForeignPowerId(g));
            GiveSoldiers(g, a.PlayerId, 3);
            GiveSoldiers(g, b.PlayerId, 3);
            g.SetStance(a.PlayerId, b.PlayerId, Stance.War);
            g.ChangeTension(a.PlayerId, b.PlayerId, Game.TensionWar);
            return g;
        }

        Game x = Build(4242);
        Game y = Build(4242);
        game_ProposeFor(x);
        game_ProposeFor(y);
        Assert.Equal(SaveGame.From(x).ToJson(), SaveGame.From(y).ToJson()); // identical outcome per seed

        // Queuing a human-bound proposal advances NO stream-0 state.
        Game z = Game.New(Classic, seed: 4242);
        Player za = z.Players.First(p => p.PlayerId == ForeignPowerId(z));
        Player zc = z.Players.First(p => p.PlayerId == SecondForeignPowerId(z));
        GiveSoldiers(z, za.PlayerId, 3);
        z.SetStance(za.PlayerId, z.HumanPlayer.PlayerId, Stance.Peace);
        z.SetStance(za.PlayerId, zc.PlayerId, Stance.War);
        z.SetStance(z.HumanPlayer.PlayerId, zc.PlayerId, Stance.War);
        var before = z.RandomState;
        z.ProposeProactiveTreaties(za);
        Assert.Equal(before, z.RandomState); // stream 0 untouched by queuing a human offer

        static void game_ProposeFor(Game g) =>
            g.ProposeProactiveTreaties(g.Players.First(p => p.PlayerId == ForeignPowerId(g)));
    }
}
