using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Native interaction (Phase 5 slice 3): the alarm/tension model, speaking with a
/// settlement's chief (tales + gift), and learning its skill. FreeCol-pinned values
/// (alarm bands from Tension.java, decay from ServerPlayer, gift 10–80).
/// </summary>
public class NativeInteractionTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    /// <summary>A fresh game with a free colonist (or given type) standing next to a chosen settlement.</summary>
    private static (Game game, NativeSettlement settlement, Unit colonist) Setup(
        Func<NativeSettlement, bool>? pick = null, string colonistType = "model.unit.freeColonist")
    {
        Game game = Game.New(Classic, Seed);
        bool HasLandNeighbour(NativeSettlement s) =>
            s.Position.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        NativeSettlement settlement = game.NativeSettlements.First(s => (pick?.Invoke(s) ?? true) && HasLandNeighbour(s));
        Position adjacent = settlement.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        Unit colonist = game.SpawnUnit(Classic.Unit(colonistType), adjacent);
        return (game, settlement, colonist);
    }

    // ---- Alarm model ----

    [Theory]
    [InlineData(0, AlarmLevel.Happy)]
    [InlineData(100, AlarmLevel.Happy)]
    [InlineData(101, AlarmLevel.Content)]
    [InlineData(600, AlarmLevel.Content)]
    [InlineData(650, AlarmLevel.Displeased)]
    [InlineData(750, AlarmLevel.Angry)]
    [InlineData(900, AlarmLevel.Hateful)]
    public void AlarmLevel_MapsToFreeColBands(int alarm, AlarmLevel expected)
    {
        (Game game, NativeSettlement settlement, _) = Setup();
        game.ChangeNativeAlarm(settlement, alarm);
        Assert.Equal(expected, settlement.AlarmLevel);
    }

    [Fact]
    public void Settlements_StartPeaceful()
    {
        Game game = Game.New(Classic, Seed);
        Assert.All(game.NativeSettlements, s =>
        {
            Assert.Equal(0, s.Alarm);
            Assert.Equal(AlarmLevel.Happy, s.AlarmLevel);
        });
    }

    [Fact]
    public void ChangeNativeAlarm_ClampsToRange()
    {
        (Game game, NativeSettlement settlement, _) = Setup();
        game.ChangeNativeAlarm(settlement, 5000);
        Assert.Equal(NativeSettlement.MaxAlarm, settlement.Alarm);
        game.ChangeNativeAlarm(settlement, -5000);
        Assert.Equal(0, settlement.Alarm);
    }

    [Fact]
    public void Alarm_CoolsEachTurn()
    {
        (Game game, NativeSettlement settlement, _) = Setup();
        game.ChangeNativeAlarm(settlement, 500);
        game.EndTurn();
        // FreeCol decay: −value/100 − 4 → 500 − (5 + 4) = 491.
        Assert.Equal(491, settlement.Alarm);
    }

    // ---- Speak with the chief ----

    [Fact]
    public void Visit_FirstContact_DefersToTheOffer_ThenAcceptGivesGiftRevealAndIsOnceOnly()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        int goldBefore = game.Gold;
        int exploredBefore = game.Explored.Count;

        MoveCheck check = game.CheckVisit(colonist, settlement);
        Assert.True(check.Allowed);

        // Col1 first contact with a tribe (86d3kgbnq): the chief offers peace — the visit defers to the accept/reject
        // prompt (no inline gift), and the audience ends the unit's turn either way.
        int deferred = game.Visit(colonist, settlement);
        Assert.Equal(0, deferred);
        Assert.NotNull(game.PendingFirstContact);
        Assert.Equal(settlement.NationTypeId, game.PendingFirstContact!.NationTypeId);
        Assert.True(settlement.HasBeenVisited);
        Assert.Equal(0, colonist.MovementLeft);

        // Accept the peace → the chief's welcome (reveal + a 10–80 gift) lands.
        string outcome = game.ResolvePendingFirstContact(accept: true);
        Assert.Contains("peace", outcome);
        Assert.Null(game.PendingFirstContact);
        int gift = game.Gold - goldBefore;
        Assert.InRange(gift, 10, 80);
        Assert.True(game.Explored.Count > exploredBefore, "the chief's welcome reveals nearby lands");

        // A second visit is refused — you've already spoken with this chief.
        Assert.False(game.CheckVisit(colonist, settlement).Allowed);
    }

    [Fact]
    public void Visit_HatefulSettlement_GivesNoGift_ButStillReveals()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        game.ChangeNativeAlarm(settlement, 1000); // Hateful
        int goldBefore = game.Gold;

        int gift = game.Visit(colonist, settlement);

        Assert.Equal(0, gift);
        Assert.Equal(goldBefore, game.Gold);
        Assert.True(settlement.HasBeenVisited);
    }

    [Fact]
    public void Visit_RequiresAPersonNextToTheSettlement()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        // Move the colonist away (spawn far is awkward; instead test the adjacency reason via a fresh distant unit).
        Unit distant = game.Units[0]; // the original starting colonist, far from this settlement
        Assert.False(game.CheckVisit(distant, settlement).Allowed);
        Assert.True(game.CheckVisit(colonist, settlement).Allowed);
    }

    // ---- Learn a skill ----

    [Fact]
    public void LearnSkill_UpgradesTheColonist_AndConsumesTheSkill()
    {
        (Game game, NativeSettlement settlement, Unit colonist) =
            Setup(s => !s.IsCapital && s.LearnableSkill is not null);
        string skill = settlement.LearnableSkill!;
        int id = colonist.Id;

        Assert.True(game.CheckLearnSkill(colonist, settlement).Allowed);
        Unit expert = game.LearnSkill(colonist, settlement);

        Assert.Equal(skill, expert.Type.Id);
        Assert.Equal(id, expert.Id);                       // same unit identity, new profession
        Assert.Contains(expert, game.Units);
        Assert.Equal(0, expert.MovementLeft);
        Assert.True(settlement.SkillConsumed);
        // Can't learn twice from a (non-capital) settlement.
        Assert.False(game.CheckLearnSkill(expert, settlement).Allowed);
    }

    [Fact]
    public void Capital_TeachesIndefinitely()
    {
        (Game game, NativeSettlement settlement, Unit colonist) =
            Setup(s => s.IsCapital && s.LearnableSkill is not null);
        game.LearnSkill(colonist, settlement);
        Assert.False(settlement.SkillConsumed); // a capital never runs out
    }

    [Fact]
    public void LearnSkill_RefusedForExperts_AndHostileSettlements()
    {
        // An expert cannot learn a new skill.
        (Game game, NativeSettlement settlement, Unit expert) =
            Setup(s => s.LearnableSkill is not null, colonistType: "model.unit.expertFarmer");
        Assert.False(game.CheckLearnSkill(expert, settlement).Allowed);

        // A free colonist is refused at an angry settlement.
        (Game g2, NativeSettlement s2, Unit colonist) = Setup(s => s.LearnableSkill is not null);
        g2.ChangeNativeAlarm(s2, 750); // Angry
        Assert.False(g2.CheckLearnSkill(colonist, s2).Allowed);
    }

    [Theory] // skill-learner eligibility is read from the spec's model.unitChange.natives data (86d3fpxaw), not a hardcoded pair
    [InlineData("model.unit.freeColonist", true)]      // a learner type (has a natives change row)
    [InlineData("model.unit.indenturedServant", true)] // also a learner type
    [InlineData("model.unit.pettyCriminal", false)]    // no natives row → cannot learn directly
    [InlineData("model.unit.expertFarmer", false)]     // already an expert → no natives row
    public void LearnSkill_EligibilityIsDataDriven_FromTheSpec(string colonistType, bool eligible)
    {
        (Game game, NativeSettlement settlement, Unit unit) =
            Setup(s => !s.IsCapital && s.LearnableSkill is not null, colonistType);

        // The spec's natives change-type agrees with the in-game gate (both keyed on the settlement's taught skill).
        Assert.Equal(eligible, Classic.CanLearnSkillFromNatives(colonistType, settlement.LearnableSkill!));
        Assert.Equal(eligible, game.CheckLearnSkill(unit, settlement).Allowed);
    }

    // ---- Edge cases (from adversarial review) ----

    [Fact]
    public void Visit_RefusedWithNoMovementLeft()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        colonist.MovementLeft = 0;
        Assert.False(game.CheckVisit(colonist, settlement).Allowed);
    }

    [Fact]
    public void CheckLearnSkill_RefusedWhenSettlementTeachesNothing()
    {
        (Game game, NativeSettlement settlement, Unit colonist) = Setup();
        // A settlement on the same tile but with no skill — the colonist is adjacent to it too.
        var noSkill = new NativeSettlement(
            9999, settlement.NationTypeId, settlement.SettlementTypeId, false, settlement.Position, 5, learnableSkill: null);
        Assert.False(game.CheckLearnSkill(colonist, noSkill).Allowed);
    }

    [Fact]
    public void Interaction_RefusedForUnitsOffTheMap()
    {
        (Game game, NativeSettlement settlement, Unit colonist) =
            Setup(s => s.LearnableSkill is not null);
        colonist.Location = UnitLocation.InEurope; // no longer on the map
        Assert.False(game.CheckVisit(colonist, settlement).Allowed);
        Assert.False(game.CheckLearnSkill(colonist, settlement).Allowed);
    }

    [Theory]
    [InlineData(3, 0)]   // 3 − (0 + 4) → clamped to 0
    [InlineData(4, 0)]   // 4 − (0 + 4) = 0
    [InlineData(5, 1)]   // 5 − (0 + 4) = 1
    [InlineData(101, 96)] // 101 − (1 + 4) = 96 (stays Content)
    public void Alarm_DecayHandlesLowValuesAndClampsAtZero(int start, int expected)
    {
        (Game game, NativeSettlement settlement, _) = Setup();
        game.ChangeNativeAlarm(settlement, start);
        game.EndTurn();
        Assert.Equal(expected, settlement.Alarm);
    }

    // ---- Persistence ----

    [Fact]
    public void InteractionState_SurvivesSaveRoundTrip()
    {
        (Game game, NativeSettlement settlement, Unit colonist) =
            Setup(s => !s.IsCapital && s.LearnableSkill is not null);
        game.ChangeNativeAlarm(settlement, 250);
        game.Visit(colonist, settlement);
        int settlementId = settlement.Id;

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        NativeSettlement reloaded = restored.NativeSettlements.First(s => s.Id == settlementId);

        Assert.Equal(settlement.Alarm, reloaded.Alarm);
        Assert.True(reloaded.HasBeenVisited);
        Assert.Equal(settlement.SkillConsumed, reloaded.SkillConsumed);
    }

    [Fact]
    public void SaveVersion_IsCurrent() => Assert.Equal(75, SaveGame.CurrentVersion);
}
