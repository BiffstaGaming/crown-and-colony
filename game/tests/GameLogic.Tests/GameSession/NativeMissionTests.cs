using System;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
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

        Assert.Equal(39, SaveGame.CurrentVersion);
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

    /// <summary>A mission installed, then a human colony founded on a free land tile beside the settlement (within 10).</summary>
    private static (Game Game, NativeSettlement Settlement, Colony Colony) MissionWithNearbyColony()
    {
        (Game game, NativeSettlement settlement, Unit jesuit) = MissionaryAtSettlement(Jesuit);
        game.EstablishMission(jesuit, settlement); // consumes the missionary, freeing its tile
        Position colonyTile = settlement.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.Colonies.All(c => c.Position != n));
        Colony colony = game.FoundColony(game.SpawnUnit(Classic.Unit(FreeColonist), colonyTile));
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

        Assert.Equal(39, SaveGame.CurrentVersion);
        Assert.Equal(47, r.ConvertProgress);
    }

    private const string IndianConvertTypeId = "model.unit.indianConvert";
}
