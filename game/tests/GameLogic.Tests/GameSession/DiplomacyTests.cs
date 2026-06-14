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
        game.SetStance(0, fid, Stance.War); // already at war before "meeting"

        game.HumanPlayer.ExploredSet.Add(rivalColony.Position);
        game.EndTurn();

        Assert.Equal(Stance.War, game.StanceBetween(0, fid)); // contact only promotes Uncontacted → Peace
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
        Assert.True(game.CheckAttack(human, rivalTile).Allowed); // attacking is allowed even while Uncontacted — no gate (FP-6a)

        game.Attack(human, rivalTile, new FixedRandom(0.0));

        Assert.Equal(Stance.War, game.StanceBetween(0, fid));
        Assert.Equal(Stance.War, game.StanceBetween(fid, 0));
        Assert.Equal(Game.TensionWar, game.TensionBetween(0, fid));
        Assert.Equal(Game.TensionWar, game.TensionBetween(fid, 0));
    }

    // ---- Decay ----

    [Fact]
    public void ColonialTension_DecaysEachTurn_AndNeverChangesStance()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.War);
        game.ChangeTension(0, fid, 500);

        game.EndTurn();

        Assert.Equal(491, game.TensionBetween(0, fid)); // 500 − (500/100 + 4), same formula as native alarm
        Assert.Equal(Stance.War, game.StanceBetween(0, fid)); // decay cools the number but never changes stance
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
