using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Diplomacy foundation (FP-6a, ADR-019): colonial-player ↔ colonial-player <see cref="Stance"/> + tension,
/// RECORDED only. First contact → Peace; attacking a rival's unit → War + a tension spike; per-turn decay.
/// Natives are excluded (they stay on the per-settlement alarm system). No transition draws RNG, and none
/// gates any move/attack/fog legality — so existing combat/fog/soak behaviour is unchanged.
/// </summary>
public class DiplomacyTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Soldier = "model.role.soldier";

    /// <summary>A fixed RNG forcing an attacker great-win (NextDouble 0 → win band) so the attack resolves cleanly.</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    private static int ForeignPowerId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    private static int NativePlayerId(Game game) =>
        game.Players.First(p => p.PlayerType == PlayerType.Native).PlayerId;

    /// <summary>Disbands the human's starting roster — leaving it military-less, so a power's strength ratio against it stays high and the orthogonal sue-for-peace never fires (these tests exercise the tension→stance decay, not diplomacy).</summary>
    private static void DisbandHumanUnits(Game game)
    {
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
    }

    // ---- Defaults & API ----

    [Fact]
    public void StanceAndTension_DefaultToUncontactedAndZero_EvenWithRivalsPresent()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);

        Assert.Equal(Stance.Uncontacted, game.StanceBetween(0, fid));
        Assert.Equal(Stance.Uncontacted, game.StanceBetween(fid, 0));
        Assert.Equal(0, game.TensionBetween(0, fid));
        Assert.Empty(game.HumanPlayer.Stances);
        Assert.Empty(game.HumanPlayer.Tensions);
    }

    [Fact]
    public void SetStance_IsSymmetricByDefault_AndDirectionalWhenAsked()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);

        game.SetStance(0, fid, Stance.War);
        Assert.Equal(Stance.War, game.StanceBetween(0, fid));
        Assert.Equal(Stance.War, game.StanceBetween(fid, 0)); // symmetric

        game.SetStance(0, fid, Stance.Peace, symmetric: false);
        Assert.Equal(Stance.Peace, game.StanceBetween(0, fid)); // only our view changed
        Assert.Equal(Stance.War, game.StanceBetween(fid, 0));
    }

    [Fact]
    public void ChangeTension_ClampsToZeroAndMax()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);

        game.ChangeTension(0, fid, 5000);
        Assert.Equal(Game.MaxTension, game.TensionBetween(0, fid)); // clamped up to 1100

        game.ChangeTension(0, fid, -5000);
        Assert.Equal(0, game.TensionBetween(0, fid)); // clamped down to 0
    }

    [Fact]
    public void Diplomacy_IgnoresNativePlayers()
    {
        var game = Game.New(Classic, seed: 7);
        int nid = NativePlayerId(game);

        game.SetStance(0, nid, Stance.War);
        game.ChangeTension(0, nid, 500);

        Assert.Equal(Stance.Uncontacted, game.StanceBetween(0, nid)); // natives stay on the alarm system
        Assert.Equal(0, game.TensionBetween(0, nid));
        Assert.Empty(game.HumanPlayer.Stances);
    }

    // ---- Contact → Peace ----

    [Fact]
    public void Contact_WhenHumanSeesARivalColony_RecordsPeaceBothWays()
    {
        var game = Game.New(Classic, seed: 7);
        game.EndTurn(); // a foreign power founds its colony
        int fid = ForeignPowerId(game);
        Colony rivalColony = game.Colonies.First(c => c.OwnerId == fid);
        Assert.Equal(Stance.Uncontacted, game.StanceBetween(0, fid)); // the human hasn't seen it yet

        game.HumanPlayer.ExploredSet.Add(rivalColony.Position); // the human scouts the rival's colony tile
        game.EndTurn();

        Assert.Equal(Stance.Peace, game.StanceBetween(0, fid));
        Assert.Equal(Stance.Peace, game.StanceBetween(fid, 0));
        Assert.Equal(0, game.TensionBetween(0, fid)); // contact carries no tension
    }

    [Fact]
    public void Contact_DoesNotDowngradeAnExistingWar()
    {
        var game = Game.New(Classic, seed: 7);
        game.EndTurn();
        int fid = ForeignPowerId(game);
        Colony rivalColony = game.Colonies.First(c => c.OwnerId == fid);
        // Keep the power's strength ratio against the human high so its own turn never sues for peace (an orthogonal,
        // strength-ratio mechanism) — this isolates the test to its real intent: that *contact* does not downgrade an
        // existing war. Disband the human's starting roster (so it is military-less) and arm one of the power's units.
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        game.Units.First(u => u.OwnerId == fid && u.IsOnMap && !u.Type.IsNaval).RoleId = Soldier;
        game.SetStance(0, fid, Stance.War); // already at war before "meeting"
        game.ChangeTension(0, fid, 1000);   // a hot war (so the tension→stance machine keeps it at War this turn)

        game.HumanPlayer.ExploredSet.Add(rivalColony.Position);
        game.EndTurn();

        Assert.Equal(Stance.War, game.StanceBetween(0, fid)); // contact only promotes Uncontacted → Peace; war holds
    }

    // ---- Attack → War ----

    [Fact]
    public void AttackingARivalColonialUnit_RecordsWar_AndSpikesTension_BothWays()
    {
        var game = Game.New(Classic, seed: 7);
        Unit human = game.PlayerUnits.First(u => u.IsOnMap);
        human.RoleId = Soldier; // arm it so it has offensive strength
        human.RoleCount = 1;
        int fid = ForeignPowerId(game);
        Position rivalTile = AdjacentFreeLand(game, human.Position);
        Unit rival = game.SpawnUnit(Classic.Unit(FreeColonist), rivalTile);
        rival.OwnerId = fid; // a rival colonial power's unit standing next to the human

        Assert.Equal(Stance.Uncontacted, game.StanceBetween(0, fid));
        Assert.True(game.CheckAttack(human, rivalTile).Allowed); // attacking is allowed while Uncontacted — the edge case that declares the war (86d3drn45)

        game.Attack(human, rivalTile, new FixedRandom(0.0));

        Assert.Equal(Stance.War, game.StanceBetween(0, fid));
        Assert.Equal(Stance.War, game.StanceBetween(fid, 0));
        Assert.Equal(Game.TensionWar, game.TensionBetween(0, fid));
        Assert.Equal(Game.TensionWar, game.TensionBetween(fid, 0));
    }

    // ---- Stance gates combat & movement legality (86d3drn45) ----

    /// <summary>An armed human soldier with a foreign-power colonist on an adjacent free tile, the pair set to <paramref name="stance"/>.</summary>
    private static (Game game, Unit human, Unit rival, Position rivalTile, int fid) StageAdjacentColonialPair(Stance stance)
    {
        var game = Game.New(Classic, seed: 7);
        Unit human = game.PlayerUnits.First(u => u.IsOnMap);
        human.RoleId = Soldier; // armed → has offensive strength
        human.RoleCount = 1;
        int fid = ForeignPowerId(game);
        Position rivalTile = AdjacentFreeLand(game, human.Position);
        Unit rival = game.SpawnUnit(Classic.Unit(FreeColonist), rivalTile);
        rival.OwnerId = fid;
        game.SetStance(0, fid, stance);
        return (game, human, rival, rivalTile, fid);
    }

    [Theory]
    [InlineData(Stance.Peace)]
    [InlineData(Stance.CeaseFire)]
    [InlineData(Stance.Alliance)]
    public void CheckAttack_OnAColonialRival_IsRejected_AtPeaceCeaseFireOrAlliance(Stance stance)
    {
        (Game game, Unit human, _, Position rivalTile, _) = StageAdjacentColonialPair(stance);

        // The rival is no longer an enemy at this stance, so there is "no enemy to attack" — you must declare war first.
        Assert.False(game.CheckAttack(human, rivalTile).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.Attack(human, rivalTile, new FixedRandom(0.0)));
    }

    [Fact]
    public void CheckAttack_OnAColonialRival_IsAllowed_AtWar()
    {
        (Game game, Unit human, _, Position rivalTile, int fid) = StageAdjacentColonialPair(Stance.War);

        Assert.True(game.CheckAttack(human, rivalTile).Allowed); // war → hostile, the attack is legal
        Assert.Equal(Stance.War, game.StanceBetween(0, fid));
    }

    [Fact]
    public void CheckMove_OntoAPeaceRivalsTile_IsRejected_NotRoutedToAttack()
    {
        (Game game, Unit human, _, Position rivalTile, _) = StageAdjacentColonialPair(Stance.Peace);

        MoveCheck check = game.CheckMove(human, rivalTile);
        Assert.False(check.Allowed); // can't enter a peaceful neighbour's tile…
        Assert.DoesNotContain("attack", check.Reason!, System.StringComparison.OrdinalIgnoreCase); // …and it is NOT "attack instead"
    }

    [Fact]
    public void CheckMove_OntoAWarRivalsTile_RoutesToAttack()
    {
        (Game game, Unit human, _, Position rivalTile, _) = StageAdjacentColonialPair(Stance.War);

        MoveCheck check = game.CheckMove(human, rivalTile);
        Assert.False(check.Allowed); // still can't step onto the tile…
        Assert.Contains("attack", check.Reason!, System.StringComparison.OrdinalIgnoreCase); // …but now it routes to "attack instead" (enemy)
    }

    [Fact]
    public void CheckAttack_OnANativeBrave_IsUnaffectedByColonialStance()
    {
        // Native combat keys on alarm, not colonial stance: a brave is always an enemy on owner-inequality, so a human
        // soldier may attack an adjacent brave regardless of any colonial Peace the human holds with a foreign power.
        var game = Game.New(Classic, seed: 7);
        Unit human = game.PlayerUnits.First(u => u.IsOnMap);
        human.RoleId = Soldier;
        human.RoleCount = 1;
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.Peace); // at peace with a colonial rival — must not bleed into native legality

        Unit brave = game.NativeUnits.First();
        Position spot = AdjacentFreeLand(game, human.Position);
        brave.Position = spot; // move a brave next to the human soldier

        Assert.True(brave.IsNative);
        Assert.True(game.CheckAttack(human, spot).Allowed); // the brave is still a legal target (native alarm system, not stance)
    }

    // ---- Decay ----

    [Fact]
    public void ColonialTension_DecaysEachTurn_WarPersistsWhileHot()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        DisbandHumanUnits(game); // a military-less human keeps the power's strength ratio high so it never sues for peace (isolates the pure tension→stance decay)
        game.SetStance(0, fid, Stance.War);
        game.ChangeTension(0, fid, 1000);

        game.EndTurn();

        Assert.Equal(986, game.TensionBetween(0, fid)); // 1000 − (1000/100 + 4), same formula as native alarm
        Assert.Equal(Stance.War, game.StanceBetween(0, fid)); // still well above the cease-fire line — war holds
    }

    // ---- Tension → Stance (FP-6b state machine) ----

    [Theory]
    [InlineData(Stance.War, 1000, Stance.War)]              // a hot war holds
    [InlineData(Stance.War, 590, Stance.CeaseFire)]         // cooled to ≤ CONTENT−DELTA (590) → cease-fire
    [InlineData(Stance.War, 591, Stance.War)]               // one above the line → still war
    [InlineData(Stance.CeaseFire, 200, Stance.CeaseFire)]   // a truce holds in the middle band
    [InlineData(Stance.CeaseFire, 90, Stance.Peace)]        // cooled to ≤ HAPPY−DELTA (90) → peace
    [InlineData(Stance.CeaseFire, 91, Stance.CeaseFire)]    // one above the line → still cease-fire
    [InlineData(Stance.CeaseFire, 1011, Stance.War)]        // re-flares above HATEFUL+DELTA (1010)
    [InlineData(Stance.Peace, 1011, Stance.War)]            // peace → war above 1010
    [InlineData(Stance.Peace, 1010, Stance.Peace)]          // at the line → still peace
    [InlineData(Stance.Peace, 0, Stance.Peace)]
    [InlineData(Stance.Uncontacted, 5000, Stance.Uncontacted)] // contact is never derived from tension
    public void StanceFromTension_FollowsFreeColThresholds(Stance current, int tension, Stance expected) =>
        Assert.Equal(expected, Game.StanceFromTension(current, tension));

    [Fact]
    public void War_DeEscalatesToCeaseFire_AsTensionDecaysBelowTheLine()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        DisbandHumanUnits(game); // a military-less human keeps the power's strength ratio high so it never sues for peace (isolates the pure tension→stance decay)
        game.SetStance(0, fid, Stance.War);
        game.ChangeTension(0, fid, 595); // one turn's decay (→586) drops it to ≤590

        game.EndTurn();

        Assert.Equal(Stance.CeaseFire, game.StanceBetween(0, fid));
        Assert.Equal(Stance.CeaseFire, game.StanceBetween(fid, 0)); // symmetric
    }

    [Fact]
    public void CeaseFire_WarmsToPeace_AsTensionDecaysToCalm()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.CeaseFire);
        game.ChangeTension(0, fid, 94); // decays to 90 → peace

        game.EndTurn();

        Assert.Equal(Stance.Peace, game.StanceBetween(0, fid));
    }

    [Fact]
    public void Save_RoundTrips_CeaseFireStance()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.CeaseFire);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(Stance.CeaseFire, loaded.StanceBetween(0, fid));
    }

    // ---- Persistence (additive on save v20) ----

    [Fact]
    public void Save_RoundTrips_ColonialStanceAndTension()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.War);
        game.ChangeTension(0, fid, 500);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(Stance.War, loaded.StanceBetween(0, fid));
        Assert.Equal(Stance.War, loaded.StanceBetween(fid, 0));
        Assert.Equal(500, loaded.TensionBetween(0, fid));
        Assert.Equal(500, loaded.TensionBetween(fid, 0));
    }

    [Fact]
    public void ContactOnlyGame_RoundTrips_StanceWithoutTension()
    {
        // After contact a pair is Peace with tension 0 — the Stances map is written but the Tensions map is
        // omitted (all zero). The mixed save path (one map present, one null) must round-trip cleanly.
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.Peace); // peace, tension stays 0

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(Stance.Peace, loaded.StanceBetween(0, fid));
        Assert.Equal(Stance.Peace, loaded.StanceBetween(fid, 0));
        Assert.Equal(0, loaded.TensionBetween(0, fid));
    }

    [Fact]
    public void NoContactGame_WritesNoDiplomacyBytes_AndRoundTripsByteIdentical()
    {
        var game = Game.New(Classic, seed: 7);
        string json = SaveGame.From(game).ToJson();

        Assert.DoesNotContain("\"Stances\"", json);  // omitted when empty → no churn for older/no-contact saves
        Assert.DoesNotContain("\"Tensions\"", json);
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }

    private static Position AdjacentFreeLand(Game game, Position from) =>
        from.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
}
