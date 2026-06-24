using System;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
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
/// Missionaries (<c>86d3c9t6e</c>): slice 1 — establish a mission (FreeCol <c>InGameController.establishMission</c>):
/// a missionary-role unit installs a mission if the tribe is Displeased or calmer, or is killed if Angry/Hateful.
/// Slice 2 — per-turn convert accrual (FreeCol <c>ServerIndianSettlement.csStartTurn</c>): a mission gains
/// <c>(skill+6)+2%·alarm</c> progress and, at threshold 100, converts a brave into an Indian Convert at the owner's
/// nearest colony within 10. All RNG-free; mission state persists (save v34).
/// </summary>
public class NativeMissionTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string MissionaryRole = "model.role.missionary";
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Jesuit = "model.unit.jesuitMissionary";

    /// <summary>A fresh game with a <paramref name="unitType"/> in the missionary role adjacent to a land-bordered settlement.</summary>
    private static (Game Game, NativeSettlement Settlement, Unit Missionary) MissionaryAtSettlement(string unitType = FreeColonist)
    {
        Game game = Game.New(Classic, Seed);
        bool HasLandNeighbour(NativeSettlement s) =>
            s.Position.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        NativeSettlement settlement = game.NativeSettlements.First(HasLandNeighbour);
        Position adjacent = settlement.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        Unit missionary = game.SpawnUnit(Classic.Unit(unitType), adjacent);
        missionary.RoleId = MissionaryRole;
        return (game, settlement, missionary);
    }

    // ── CheckEstablishMission gate ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckEstablishMission_AllowsAMissionaryOnAnAdjacentTile()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        Assert.True(game.CheckEstablishMission(missionary, settlement).Allowed); // alarm doesn't gate the command
    }

    [Fact]
    public void CheckEstablishMission_RejectsANonMissionaryUnit()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        missionary.RoleId = "model.role.default"; // strip the missionary role
        Assert.False(game.CheckEstablishMission(missionary, settlement).Allowed);
    }

    [Fact]
    public void CheckEstablishMission_RejectsWhenOutOfMoves()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        missionary.MovementLeft = 0;
        Assert.False(game.CheckEstablishMission(missionary, settlement).Allowed);
    }

    [Fact]
    public void CheckEstablishMission_RejectsWhenNotAdjacent()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        // The missionary still has moves, but a different, distant settlement is neither its tile nor adjacent.
        NativeSettlement distant = game.NativeSettlements.First(s =>
            s.Id != settlement.Id && missionary.Position != s.Position && !missionary.Position.IsAdjacentTo(s.Position));
        Assert.False(game.CheckEstablishMission(missionary, distant).Allowed);
    }

    // ── EstablishMission outcome by alarm ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]    // Happy
    [InlineData(600)]  // Content
    [InlineData(700)]  // Displeased (≤700 establishes)
    public void EstablishMission_InstallsTheMission_WhenDispleasedOrCalmer(int alarm)
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        game.ChangeNativeAlarm(settlement, alarm);

        Assert.True(game.EstablishMission(missionary, settlement));
        Assert.Equal(game.HumanPlayer.PlayerId, settlement.MissionOwnerId);
        Assert.False(settlement.MissionIsExpert);                 // a free colonist, not a jesuit
        Assert.True(settlement.HasMission);
        Assert.DoesNotContain(missionary, game.Units);            // consumed into the settlement
    }

    [Fact]
    public void EstablishMission_RecordsAJesuitAsExpert()
    {
        (Game game, NativeSettlement settlement, Unit jesuit) = MissionaryAtSettlement(Jesuit);
        Assert.True(game.EstablishMission(jesuit, settlement));
        Assert.True(settlement.MissionIsExpert);
    }

    [Fact]
    public void EstablishMission_EasesTheSettlementsAlarm_AsGoodwill()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        game.ChangeNativeAlarm(settlement, 300); // Content
        game.EstablishMission(missionary, settlement);
        Assert.Equal(200, settlement.Alarm); // −100 goodwill (FreeCol ALARM_NEW_MISSIONARY), clamped at 0
    }

    [Theory]
    [InlineData(701)]   // first Angry value — the tight establish/destroy boundary (≤700 installs, >700 destroys)
    [InlineData(750)]   // Angry (701–800)
    [InlineData(1000)]  // Hateful (>800)
    public void EstablishMission_KillsTheMissionary_WhenAngryOrHateful(int alarm)
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        game.ChangeNativeAlarm(settlement, alarm);

        Assert.False(game.EstablishMission(missionary, settlement)); // killed, not installed
        Assert.False(settlement.HasMission);
        Assert.DoesNotContain(missionary, game.Units);              // the missionary is destroyed
    }

    [Fact]
    public void EstablishMission_ThrowsWhenNotAllowed()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        missionary.MovementLeft = 0;
        Assert.Throws<InvalidMoveException>(() => game.EstablishMission(missionary, settlement));
    }

    // ── Determinism + save ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EstablishMission_DrawsNoRandomness()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        var before = game.RandomState;
        game.EstablishMission(missionary, settlement);
        Assert.Equal(before, game.RandomState); // the whole mission mechanic is RNG-free (ADR-009)
    }

    [Fact]
    public void Mission_RoundTripsThroughSave_V33()
    {
        (Game game, NativeSettlement settlement, Unit jesuit) = MissionaryAtSettlement(Jesuit);
        game.EstablishMission(jesuit, settlement);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        NativeSettlement r = restored.NativeSettlements.Single(s => s.Id == settlement.Id);

        Assert.Equal(59, SaveGame.CurrentVersion);
        Assert.Equal(game.HumanPlayer.PlayerId, r.MissionOwnerId); // owner survives
        Assert.True(r.MissionIsExpert);                            // jesuit-ness survives
    }

    [Fact]
    public void AMissionFreeGame_OmitsTheMissionFields()
    {
        Game game = Game.New(Classic, Seed);
        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("MissionOwnerId", json);  // additive: omitted → byte-identical to v32
        Assert.DoesNotContain("MissionIsExpert", json);
        Assert.DoesNotContain("ConvertProgress", json); // v34, also omitted-when-0
    }

    // ── Convert accrual + spawn (slice 2) ────────────────────────────────────────────────────────────────────────

    /// <summary>A mission installed, then a human colony founded on a land tile beside the settlement (within 10). The
    /// tile sits inside the settlement's native claim radius, so founding forces a claim — resolved by stealing it
    /// (86d3e4bj7; free, so the colony lands exactly where intended; the convert tests set alarm/size explicitly).</summary>
    private static (Game Game, NativeSettlement Settlement, Colony Colony) MissionWithNearbyColony()
    {
        (Game game, NativeSettlement settlement, Unit jesuit) = MissionaryAtSettlement(Jesuit);
        game.EstablishMission(jesuit, settlement); // consumes the missionary, freeing its tile
        Position colonyTile = settlement.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.Colonies.All(c => c.Position != n));
        Colony colony = game.FoundColony(game.SpawnUnit(Classic.Unit(FreeColonist), colonyTile), LandClaimChoice.Steal);
        settlement.Size = 5;
        return (game, settlement, colony);
    }

    [Theory]
    [InlineData(FreeColonist, 500, 16)] // (0 + 6) + 500*2/100 = 16
    [InlineData(Jesuit, 500, 19)]       // (3 + 6) + 10 = 19
    [InlineData(FreeColonist, 0, 6)]    // no alarm term
    public void ProcessMissions_AccruesConvertProgress_PerTheFormula(string unitType, int alarm, int expected)
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement(unitType);
        game.EstablishMission(missionary, settlement);
        game.ChangeNativeAlarm(settlement, alarm); // set after establish (establish eased it to 0)

        game.ProcessMissions();
        Assert.Equal(expected, settlement.ConvertProgress); // below threshold → banked, no convert
    }

    [Fact]
    public void ProcessMissions_SpawnsAConvert_AtThreshold()
    {
        (Game game, NativeSettlement settlement, Colony colony) = MissionWithNearbyColony();
        settlement.ConvertProgress = 95; // one jesuit turn (+9 at alarm 0) crosses the 100 threshold

        game.ProcessMissions();

        Assert.Equal(0, settlement.ConvertProgress);  // reset on convert
        Assert.Equal(4, settlement.Size);             // a brave left
        Assert.Contains(game.Units, u => u.Type.Id == IndianConvertTypeId
            && u.OwnerId == game.HumanPlayer.PlayerId && u.Position == colony.Position);
    }

    [Fact]
    public void ProcessMissions_DoesNotConvert_WhenTheSettlementIsTooSmall()
    {
        (Game game, NativeSettlement settlement, Colony _) = MissionWithNearbyColony();
        settlement.Size = 2;             // FreeCol won't convert a settlement of size ≤ 2
        settlement.ConvertProgress = 95;

        game.ProcessMissions();

        Assert.True(settlement.ConvertProgress >= 100);  // banked, not reset
        Assert.DoesNotContain(game.Units, u => u.Type.Id == IndianConvertTypeId);
    }

    [Fact]
    public void ProcessMissions_DoesNotConvert_WithNoColonyInRange()
    {
        (Game game, NativeSettlement settlement, Unit jesuit) = MissionaryAtSettlement(Jesuit);
        game.EstablishMission(jesuit, settlement); // a mission, but no colony anywhere
        settlement.Size = 5;
        settlement.ConvertProgress = 95;

        game.ProcessMissions();

        Assert.True(settlement.ConvertProgress >= 100); // banked (no colony to receive the convert)
        Assert.DoesNotContain(game.Units, u => u.Type.Id == IndianConvertTypeId);
    }

    [Fact]
    public void ProcessMissions_DrawsNoRandomness_EvenWhenConverting()
    {
        (Game game, NativeSettlement settlement, Colony _) = MissionWithNearbyColony();
        settlement.ConvertProgress = 95;
        var before = game.RandomState;
        game.ProcessMissions();
        Assert.Equal(before, game.RandomState); // accrual + spawn are RNG-free (ADR-009)
    }

    [Fact]
    public void ConvertProgress_RoundTripsThroughSave_V34()
    {
        (Game game, NativeSettlement settlement, Unit jesuit) = MissionaryAtSettlement(Jesuit);
        game.EstablishMission(jesuit, settlement);
        settlement.ConvertProgress = 47; // banked (no colony in range)

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        NativeSettlement r = restored.NativeSettlements.Single(s => s.Id == settlement.Id);

        Assert.Equal(59, SaveGame.CurrentVersion);
        Assert.Equal(47, r.ConvertProgress);
    }

    // ── Father Jean de Brébeuf: every missionary converts as an expert jesuit (86d3c7xx9) ────────────────────────

    private const string Brebeuf = "model.foundingFather.fatherJeanDeBrebeuf";

    [Fact]
    public void Spec_DeBrebeuf_GrantsTheExpertMissionaryAbility()
    {
        Assert.Contains(Classic.Father(Brebeuf).Abilities,
            a => a.Id == "model.ability.expertMissionary" && a.Value);
        Assert.DoesNotContain(Classic.Father("model.foundingFather.adamSmith").Abilities,
            a => a.Id == "model.ability.expertMissionary"); // no other father grants it
    }

    [Fact]
    public void ProcessMissions_WithoutBrebeuf_AccruesAnOrdinaryMissionAtTheBaseRate()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement(FreeColonist);
        game.EstablishMission(missionary, settlement); // an ordinary free-colonist mission (not a jesuit)
        Assert.False(settlement.MissionIsExpert);

        game.ProcessMissions();
        Assert.Equal(6, settlement.ConvertProgress); // (0 skill + 6), alarm eased to 0 on establish
    }

    [Fact]
    public void ProcessMissions_WithBrebeuf_AccruesAnOrdinaryMissionAtTheExpertRate()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement(FreeColonist);
        game.EstablishMission(missionary, settlement); // still an ordinary missionary — never a jesuit
        Assert.False(settlement.MissionIsExpert);
        game.HumanPlayer.CongressList.Add(Brebeuf);    // elect Father Jean de Brébeuf

        game.ProcessMissions();
        Assert.Equal(9, settlement.ConvertProgress); // (3 jesuit skill + 6) — Brébeuf makes the ordinary mission expert
    }

    // ── Juan de Sepúlveda: capture-convert on a won settlement assault (86d3c7y0u) ───────────────────────────────
    //
    // FreeCol wires model.modifier.nativeConvertBonus to Unit.getConvertProbability — the chance, on WINNING an
    // assault on a native settlement you hold a mission in, that a brave is captured as a convert (SimpleCombatModel
    // CAPTURE_CONVERT). It is NOT a missionary-accrual modifier. Base chance 30% (model.option.nativeConvertProbability,
    // classic-medium); Juan de Sepúlveda's +20% raises it to 36%, the Spanish conquest nation type's +200% to 90%.

    private const string DeSepulveda = "model.foundingFather.juanDeSepulveda";
    private const string NativeConvertBonus = "model.modifier.nativeConvertBonus";
    private const string ColonialRegular = "model.unit.colonialRegular"; // top of the promotion chain → ApplyWinnerPromotion draws nothing

    /// <summary>Returns scripted doubles in order (for the combat roll then the capture-convert roll); ints take the low end.</summary>
    private sealed class SequenceRandom(params double[] doubles) : IGameRandom
    {
        private int _i;
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => doubles[_i++];
        public RandomState SaveState() => new(0, 0);
    }

    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    /// <summary>A strong human soldier on a free land tile beside the settlement (a colonial regular → no promotion RNG draw).</summary>
    private static Unit SpawnSoldierBeside(Game game, NativeSettlement settlement)
    {
        Position adj = settlement.Position.Neighbours().First(n => game.Map.InBounds(n)
            && !game.Map.TerrainAt(n).IsWater && game.ColonyAt(n) is null && game.NativeSettlementAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit soldier = game.SpawnUnit(Classic.Unit(ColonialRegular), adj); // human-owned
        soldier.RoleId = "model.role.soldier";
        soldier.RoleCount = 1;
        return soldier;
    }

    [Fact]
    public void Spec_DeSepulveda_GrantsTheNativeConvertBonus()
    {
        Assert.Contains(Classic.Father(DeSepulveda).Modifiers, m => m.TargetId == NativeConvertBonus);
        // The Spanish conquest nation type carries the same modifier (its larger +200%).
        Assert.Contains(Classic.EuropeanNations.SelectMany(n => n.NationType.Modifiers),
            m => m.TargetId == NativeConvertBonus);
    }

    [Fact]
    public void AttackSettlement_CapturesAConvert_WhenTheAttackerHoldsTheMission()
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        game.EstablishMission(missionary, settlement); // the human holds this settlement's mission
        Unit soldier = SpawnSoldierBeside(game, settlement);

        game.AttackSettlement(soldier, settlement.Position, new SequenceRandom(0.0, 0.1)); // win, convert roll 0.1 < base 0.30
        Assert.Contains(game.Units, u => u.Type.Id == IndianConvertTypeId
            && u.OwnerId == game.HumanPlayer.PlayerId && u.Position == soldier.Position);
    }

    [Fact]
    public void AttackSettlement_CapturesNoConvert_WithoutAMission()
    {
        (Game game, NativeSettlement settlement, Unit _) = MissionaryAtSettlement(); // a missionary stands by but never establishes
        Unit soldier = SpawnSoldierBeside(game, settlement);

        game.AttackSettlement(soldier, settlement.Position, new FixedRandom(0.0)); // wins; no mission → the convert roll is never reached
        Assert.DoesNotContain(game.Units, u => u.Type.Id == IndianConvertTypeId);
    }

    [Fact]
    public void DeSepulveda_RaisesTheCaptureConvertChance()
    {
        // The combat roll (0.0) always wins; the convert roll 0.33 sits between the 30% base and the 36% de Sepúlveda chance.
        Assert.False(CapturesConvertAt(0.33, withSepulveda: false)); // 0.33 ≥ base 0.30 → no convert
        Assert.True(CapturesConvertAt(0.33, withSepulveda: true));   // 0.33 < 0.36 (base ×1.2) → convert captured
    }

    private static bool CapturesConvertAt(double convertRoll, bool withSepulveda)
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement();
        game.EstablishMission(missionary, settlement);
        if (withSepulveda)
        {
            game.HumanPlayer.CongressList.Add(DeSepulveda); // human has no nation, so only the father's +20% applies
        }
        Unit soldier = SpawnSoldierBeside(game, settlement);
        game.AttackSettlement(soldier, settlement.Position, new SequenceRandom(0.0, convertRoll));
        return game.Units.Any(u => u.Type.Id == IndianConvertTypeId && u.OwnerId == game.HumanPlayer.PlayerId);
    }

    // ── Burn-missions: the high-roll mirror of capture-convert (86d3c9t7z, FreeCol BURN_MISSIONS) ────────────────
    //
    // Off the SAME post-win roll as capture-convert: a low roll converts a brave, a high roll (top burnProbability=2%)
    // makes the natives burn the attacker's missions across the whole assaulted nation (ServerPlayer.csBurnMissions).

    [Fact]
    public void AttackSettlement_BurnsTheAttackersMissionsAcrossTheNation_OnAHighRoll()
    {
        Game game = Game.New(Classic, Seed);
        bool FreeLandNeighbour(NativeSettlement s) => s.Position.Neighbours().Any(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.ColonyAt(n) is null
            && game.NativeSettlementAt(n) is null && !game.Units.Any(u => u.IsOnMap && u.Position == n));

        // An assaultable settlement whose nation has at least one OTHER settlement (to observe the burn spreading).
        NativeSettlement target = game.NativeSettlements.First(s => FreeLandNeighbour(s)
            && game.NativeSettlements.Any(o => o.Id != s.Id && o.NationTypeId == s.NationTypeId));
        NativeSettlement other = game.NativeSettlements.First(o => o.Id != target.Id && o.NationTypeId == target.NationTypeId);
        target.MissionOwnerId = game.HumanPlayer.PlayerId; // the human holds a mission in both
        other.MissionOwnerId = game.HumanPlayer.PlayerId;
        Assert.True(other.HasMission);

        Unit soldier = SpawnSoldierBeside(game, target);
        // win (0.0), then a roll in the top 6% → burn (≥ 0.94), not a convert (≥ 0.30 base).
        game.AttackSettlement(soldier, target.Position, new SequenceRandom(0.0, 0.99));

        Assert.False(other.HasMission); // the natives burned the attacker's mission in the nation's other settlement
        Assert.DoesNotContain(game.Units, u => u.Type.Id == IndianConvertTypeId); // a burn, not a convert
    }

    [Fact]
    public void AttackSettlement_DoesNotBurnMissions_OnAMidRoll()
    {
        Game game = Game.New(Classic, Seed);
        bool FreeLandNeighbour(NativeSettlement s) => s.Position.Neighbours().Any(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.ColonyAt(n) is null
            && game.NativeSettlementAt(n) is null && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        NativeSettlement target = game.NativeSettlements.First(s => FreeLandNeighbour(s)
            && game.NativeSettlements.Any(o => o.Id != s.Id && o.NationTypeId == s.NationTypeId));
        NativeSettlement other = game.NativeSettlements.First(o => o.Id != target.Id && o.NationTypeId == target.NationTypeId);
        target.MissionOwnerId = game.HumanPlayer.PlayerId;
        other.MissionOwnerId = game.HumanPlayer.PlayerId;

        Unit soldier = SpawnSoldierBeside(game, target);
        // win, then a mid roll (≥ 0.30 base → no convert, < 0.94 → no burn).
        game.AttackSettlement(soldier, target.Position, new SequenceRandom(0.0, 0.75));

        Assert.True(other.HasMission); // a mid roll neither converts nor burns
    }

    private const string IndianConvertTypeId = "model.unit.indianConvert";

    // ── Denounce rival missions (86d3c9t7z, FreeCol InGameController.denounceMission) ─────────────────────────────
    //
    // A missionary entering a settlement that already holds a RIVAL player's mission denounces it: the roll is
    // r × rivalImmigration ÷ (myImmigration + 1), +0.2 if the resident is a jesuit, −0.2 if our challenger is.
    // Below 0.5 the rival is ousted and our mission installed (via the shared establish-install tail, so an
    // Angry/Hateful tribe still kills our missionary); otherwise our missionary is consumed for nothing.

    /// <summary>A human missionary adjacent to a Content settlement that already holds <paramref name="rivalIsExpert"/>'s rival mission.</summary>
    private static (Game Game, NativeSettlement Settlement, Unit Missionary, Player Rival) RivalMissionAtSettlement(
        string challengerType = FreeColonist, bool rivalIsExpert = false)
    {
        (Game game, NativeSettlement settlement, Unit missionary) = MissionaryAtSettlement(challengerType);
        Player rival = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        settlement.MissionOwnerId = rival.PlayerId; // the rival already holds the mission here
        settlement.MissionIsExpert = rivalIsExpert;
        return (game, settlement, missionary, rival);
    }

    [Fact]
    public void DenounceMission_OnALowRoll_OustsTheRival_AndInstallsOurs()
    {
        (Game game, NativeSettlement settlement, Unit missionary, Player rival) = RivalMissionAtSettlement();
        game.HumanPlayer.Immigration = rival.Immigration = 10; // ratio 10/11 ≈ 0.91; roll 0.0 × 0.91 = 0 < 0.5 → success

        Assert.True(game.DenounceMission(missionary, settlement, new FixedRandom(0.0)));
        Assert.Equal(game.HumanPlayer.PlayerId, settlement.MissionOwnerId); // ours now
        Assert.DoesNotContain(missionary, game.Units);                      // consumed into the settlement
    }

    [Fact]
    public void DenounceMission_OnAHighRoll_Fails_RivalStands_ChallengerConsumed()
    {
        (Game game, NativeSettlement settlement, Unit missionary, Player rival) = RivalMissionAtSettlement();
        game.HumanPlayer.Immigration = rival.Immigration = 10; // roll 0.99 × 10/11 ≈ 0.90 ≥ 0.5 → failure

        Assert.False(game.DenounceMission(missionary, settlement, new FixedRandom(0.99)));
        Assert.Equal(rival.PlayerId, settlement.MissionOwnerId); // the rival mission stands
        Assert.DoesNotContain(missionary, game.Units);           // our challenger is consumed for nothing
    }

    [Fact]
    public void DenounceMission_AnExpertResident_IsHarderToOust()
    {
        // base = 0.45 × (10/11) ≈ 0.409 < 0.5 would succeed; +0.2 for the jesuit resident → ≈ 0.609 ≥ 0.5 → fails.
        (Game game, NativeSettlement settlement, Unit missionary, Player rival) =
            RivalMissionAtSettlement(rivalIsExpert: true);
        game.HumanPlayer.Immigration = rival.Immigration = 10;

        Assert.False(game.DenounceMission(missionary, settlement, new FixedRandom(0.45)));
        Assert.Equal(rival.PlayerId, settlement.MissionOwnerId); // the expert (+0.2) resists the denounce
    }

    [Fact]
    public void DenounceMission_AnExpertChallenger_DenouncesMoreReadily()
    {
        // base = 0.62 × (10/11) ≈ 0.564 ≥ 0.5 would fail; −0.2 for our jesuit challenger → ≈ 0.364 < 0.5 → succeeds.
        (Game game, NativeSettlement settlement, Unit jesuit, Player rival) = RivalMissionAtSettlement(Jesuit);
        game.HumanPlayer.Immigration = rival.Immigration = 10;

        Assert.True(game.DenounceMission(jesuit, settlement, new FixedRandom(0.62)));
        Assert.Equal(game.HumanPlayer.PlayerId, settlement.MissionOwnerId);
        Assert.True(settlement.MissionIsExpert); // ours is a jesuit
    }

    [Fact]
    public void DenounceMission_ScalesWithTheImmigrationRatio()
    {
        // Our immigration far exceeds the rival's → the roll is divided down well below 0.5, so even a high draw wins.
        (Game game, NativeSettlement settlement, Unit missionary, Player rival) = RivalMissionAtSettlement();
        rival.Immigration = 1;
        game.HumanPlayer.Immigration = 100; // 0.99 × 1/101 ≈ 0.0098 < 0.5 → success despite a near-1 draw

        Assert.True(game.DenounceMission(missionary, settlement, new FixedRandom(0.99)));
        Assert.Equal(game.HumanPlayer.PlayerId, settlement.MissionOwnerId);
    }

    [Fact]
    public void DenounceMission_AtAnAngryTribe_KillsTheChallenger_EvenOnAWinningRoll()
    {
        // A successful denounce routes through the establish-install tail, which kills the missionary at Angry/Hateful.
        (Game game, NativeSettlement settlement, Unit missionary, Player rival) = RivalMissionAtSettlement();
        game.ChangeNativeAlarm(settlement, 1000); // Hateful
        game.HumanPlayer.Immigration = rival.Immigration = 10;

        Assert.False(game.DenounceMission(missionary, settlement, new FixedRandom(0.0))); // roll wins, but the tribe kills it
        Assert.Equal(rival.PlayerId, settlement.MissionOwnerId); // the rival mission is untouched (we never installed)
        Assert.DoesNotContain(missionary, game.Units);           // our missionary is dead
    }

    [Fact]
    public void EstablishMission_OverARivalMission_RoutesThroughDenounce_AndDrawsRng()
    {
        // The plain establish entry point detects the rival mission and routes to the denounce roll (so it now draws
        // randomness on that branch), instead of silently overwriting the rival as the pre-86d3c9t7z code did.
        (Game game, NativeSettlement settlement, Unit missionary, Player rival) = RivalMissionAtSettlement();
        game.HumanPlayer.Immigration = rival.Immigration = 10;
        var before = game.RandomState;

        game.EstablishMission(missionary, settlement); // routes to denounce → draws stream 0
        Assert.NotEqual(before, game.RandomState);      // the rival branch is NOT RNG-free
    }

    [Fact]
    public void EstablishMission_OverOwnMission_IsAPlainRngFreeReinstall()
    {
        // Re-establishing over our OWN mission is not a denounce — it stays the RNG-free install path.
        (Game game, NativeSettlement settlement, Unit first) = MissionaryAtSettlement();
        game.EstablishMission(first, settlement); // our mission

        Unit again = game.SpawnUnit(Classic.Unit(FreeColonist),
            settlement.Position.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
                && game.ColonyAt(n) is null && game.NativeSettlementAt(n) is null
                && !game.Units.Any(u => u.IsOnMap && u.Position == n)));
        again.RoleId = MissionaryRole;
        var before = game.RandomState;
        game.EstablishMission(again, settlement); // re-establish over our own → plain install, no roll

        Assert.Equal(game.HumanPlayer.PlayerId, settlement.MissionOwnerId);
        Assert.Equal(before, game.RandomState); // our-own re-establish draws no RNG
    }

    [Fact]
    public void CheckDenounceMission_RejectsWhenNoRivalMission_OrOwnMission_OrNotADenouncer()
    {
        (Game game, NativeSettlement settlement, Unit missionary, Player rival) = RivalMissionAtSettlement();

        // No mission present → nothing to denounce.
        settlement.MissionOwnerId = null;
        Assert.False(game.CheckDenounceMission(missionary, settlement).Allowed);

        // Our own mission → cannot denounce ourselves.
        settlement.MissionOwnerId = game.HumanPlayer.PlayerId;
        Assert.False(game.CheckDenounceMission(missionary, settlement).Allowed);

        // A rival mission but the unit is not a missionary (no denounceHeresy ability) → rejected.
        settlement.MissionOwnerId = rival.PlayerId;
        missionary.RoleId = "model.role.default";
        Assert.False(game.CheckDenounceMission(missionary, settlement).Allowed);
    }

    [Fact]
    public void DenounceMission_ThrowsWhenNotAllowed()
    {
        (Game game, NativeSettlement settlement, Unit missionary, _) = RivalMissionAtSettlement();
        missionary.MovementLeft = 0;
        Assert.Throws<InvalidMoveException>(() => game.DenounceMission(missionary, settlement));
    }
}
