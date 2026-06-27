using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The active-unit advisor oracle <see cref="Game.AdviseUnit"/> (parity <c>86d3fq1re</c>): for a constructed
/// unit/tile situation it returns the right bounded set of concrete recommendations (found-colony, improve-terrain,
/// learn-skill, idle-no-orders), each gated by the same legality oracle the action uses; an empty list when there is
/// nothing to advise; and it never mutates game state (read-only, ADR-006). The advice text itself is fixed wording
/// owned by the oracle, so the tests assert on the stable <see cref="AdvisorRecommendationKind"/> tags.
/// </summary>
public class AdvisorTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string FreeColonist = "model.unit.freeColonist";
    private const string Galleon = "model.unit.galleon";
    private const string Pioneer = "model.role.pioneer";
    private const ulong Seed = 12345;

    /// <summary>
    /// A 3×3 single-terrain map with one unit (default a free colonist on land with full moves) on the centre tile.
    /// The centre is plains by default (settleable, improvable) so the "found a colony" / "improve" advice can fire;
    /// pass <paramref name="role"/>/<paramref name="roleCount"/> to make the unit a tooled pioneer.
    /// </summary>
    private static Game GameWithUnit(
        string unitType = FreeColonist, string centreTerrain = "model.tile.plains",
        string? role = null, int? roleCount = null, int orders = 0, int movement = 3,
        bool withColony = false)
    {
        string[] terrain =
        [
            "model.tile.ocean", "model.tile.ocean",  "model.tile.ocean",
            "model.tile.plains", centreTerrain,      "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
        ];
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = terrain,
            Units = [new SavedUnit(1, unitType, 1, 1, movement, Role: role, RoleCount: roleCount, Orders: orders)],
            Explored = [],
            // A colony on the (0,2)/(2,2) corner is Chebyshev-1 from the centre, blocking founding there (spacing rule).
            Colonies = withColony ? [new SavedColony(2, "Townsville", 0, 2, 1)] : null,
        };
        return save.Restore(Classic);
    }

    private static Unit TheUnit(Game game) => game.Units.First(u => u.Id == 1);

    // ---- Found-a-colony advice ----

    [Fact]
    public void Advise_LandColonistOnFoundableTile_RecommendsFoundingAColony()
    {
        Game game = GameWithUnit();
        var advice = game.AdviseUnit(TheUnit(game));
        Assert.Contains(advice, r => r.Kind == AdvisorRecommendationKind.FoundColony);
    }

    [Fact]
    public void Advise_NextToAnExistingColony_DoesNotRecommendFounding()
    {
        // A colony one tile away blocks founding (FreeCol spacing); the found-colony advice must drop out.
        Game game = GameWithUnit(withColony: true);
        var advice = game.AdviseUnit(TheUnit(game));
        Assert.DoesNotContain(advice, r => r.Kind == AdvisorRecommendationKind.FoundColony);
    }

    // ---- Improve-terrain advice (pioneer) ----

    [Fact]
    public void Advise_TooledPioneerOnImprovableTerrain_RecommendsAnImprovement()
    {
        Game game = GameWithUnit(role: Pioneer, roleCount: 1);
        var advice = game.AdviseUnit(TheUnit(game));
        Assert.Contains(advice, r => r.Kind == AdvisorRecommendationKind.Improve);
    }

    [Fact]
    public void Advise_OnlyOneImprovementSuggested_EvenWhenSeveralApply()
    {
        // Plains takes both a road and a plow; the advisor surfaces only the first applicable (road), never two lines.
        Game game = GameWithUnit(role: Pioneer, roleCount: 1);
        var advice = game.AdviseUnit(TheUnit(game));
        Assert.Single(advice, r => r.Kind == AdvisorRecommendationKind.Improve);
    }

    [Fact]
    public void Advise_ColonistWithoutTools_DoesNotRecommendImproving()
    {
        // A plain free colonist (no pioneer role) cannot build improvements → no improve advice.
        Game game = GameWithUnit();
        var advice = game.AdviseUnit(TheUnit(game));
        Assert.DoesNotContain(advice, r => r.Kind == AdvisorRecommendationKind.Improve);
    }

    // ---- Idle-needs-orders advice ----

    [Fact]
    public void Advise_ActiveUnitWithMoves_IncludesTheIdleHint()
    {
        Game game = GameWithUnit();
        var advice = game.AdviseUnit(TheUnit(game));
        Assert.Contains(advice, r => r.Kind == AdvisorRecommendationKind.Idle);
    }

    [Fact]
    public void Advise_FortifiedUnit_OmitsTheIdleHint()
    {
        // Orders ordinal 2 = Fortified: it has a standing order, so it is not "idle, needs orders".
        Game game = GameWithUnit(orders: 2);
        var advice = game.AdviseUnit(TheUnit(game));
        Assert.DoesNotContain(advice, r => r.Kind == AdvisorRecommendationKind.Idle);
    }

    [Fact]
    public void Advise_UnitWithNoMovementLeft_OmitsTheIdleHint()
    {
        Game game = GameWithUnit(movement: 0);
        var advice = game.AdviseUnit(TheUnit(game));
        Assert.DoesNotContain(advice, r => r.Kind == AdvisorRecommendationKind.Idle);
    }

    // ---- Nothing-to-advise / scope guards ----

    [Fact]
    public void Advise_UnitInEurope_ReturnsNoAdvice()
    {
        // Location ordinal 2 = InEurope (off the map): the advisor stays silent.
        var save = new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 3, MapHeight = 3,
            Terrain = ["model.tile.ocean", "model.tile.ocean", "model.tile.ocean",
                       "model.tile.plains", "model.tile.plains", "model.tile.plains",
                       "model.tile.plains", "model.tile.plains", "model.tile.plains"],
            Units = [new SavedUnit(1, FreeColonist, 1, 1, 3, Location: 2)],
            Explored = [],
        };
        Game game = save.Restore(Classic);
        Assert.Empty(game.AdviseUnit(TheUnit(game)));
    }

    [Fact]
    public void Advise_ShipOnWater_GivesNoLandAdviceAndIsAtMostTheIdleHint()
    {
        // A galleon on ocean can't found/improve/learn; the only thing the advisor could say is the idle hint.
        var save = new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 3, MapHeight = 3,
            Terrain = ["model.tile.ocean", "model.tile.ocean", "model.tile.ocean",
                       "model.tile.ocean", "model.tile.ocean", "model.tile.ocean",
                       "model.tile.ocean", "model.tile.ocean", "model.tile.ocean"],
            Units = [new SavedUnit(1, Galleon, 1, 1, 3)],
            Explored = [],
        };
        Game game = save.Restore(Classic);
        var advice = game.AdviseUnit(TheUnit(game));
        Assert.DoesNotContain(advice, r => r.Kind == AdvisorRecommendationKind.FoundColony);
        Assert.DoesNotContain(advice, r => r.Kind == AdvisorRecommendationKind.Improve);
        Assert.DoesNotContain(advice, r => r.Kind == AdvisorRecommendationKind.LearnSkill);
    }

    // ---- Learn-a-skill advice (uses a generated map for real native settlements) ----

    [Fact]
    public void Advise_LearnerColonistNextToTeachingSettlement_RecommendsLearningTheSkill()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlementWithSkill(game, out var settlement, out Position adjacent);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), adjacent);

        var advice = game.AdviseUnit(colonist);
        Assert.Contains(advice, r => r.Kind == AdvisorRecommendationKind.LearnSkill);
    }

    [Fact]
    public void Advise_ExpertNextToTeachingSettlement_DoesNotRecommendLearning()
    {
        // An already-expert unit has no "natives" change row, so it cannot learn — no learn-skill advice.
        Game game = Game.New(Classic, Seed);
        NativeSettlementWithSkill(game, out var settlement, out Position adjacent);
        Unit expert = game.SpawnUnit(Classic.Unit("model.unit.expertFarmer"), adjacent);

        var advice = game.AdviseUnit(expert);
        Assert.DoesNotContain(advice, r => r.Kind == AdvisorRecommendationKind.LearnSkill);
    }

    [Fact]
    public void SkillDisplayName_ProducesAllLowercaseWords_ForMultiWordSkillName()
    {
        // Regression guard for the Wave 9 review finding: SkillDisplayName was only lowercasing the first character
        // (i==0) and appending subsequent chars verbatim, producing "expert Ore Miner" instead of "expert ore miner".
        Game game = Game.New(Classic, Seed);
        NativeSettlementWithSkill(game, out var settlement, out Position adjacent);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), adjacent);

        var advice = game.AdviseUnit(colonist);
        Assert.Contains(advice, r => r.Kind == AdvisorRecommendationKind.LearnSkill);
        string text = advice.First(r => r.Kind == AdvisorRecommendationKind.LearnSkill).Text;
        // The Text must be all-lowercase words: no mid-sentence uppercase after a space
        bool midSentenceUpper = text.Contains(' ') && text.Split(' ').Skip(1).Any(w => w.Length > 0 && char.IsUpper(w[0]));
        Assert.False(midSentenceUpper, $"Skill display text has mid-sentence uppercase: \"{text}\"");
    }

    // ---- Read-only guarantee ----

    [Fact]
    public void Advise_DoesNotMutateTheUnitOrGameState()
    {
        Game game = GameWithUnit(role: Pioneer, roleCount: 1);
        Unit unit = TheUnit(game);
        UnitOrders ordersBefore = unit.Orders;
        int movesBefore = unit.MovementLeft;
        bool improvingBefore = unit.IsImproving;
        int coloniesBefore = game.Colonies.Count;

        _ = game.AdviseUnit(unit);

        Assert.Equal(ordersBefore, unit.Orders);
        Assert.Equal(movesBefore, unit.MovementLeft);
        Assert.Equal(improvingBefore, unit.IsImproving);
        Assert.Equal(coloniesBefore, game.Colonies.Count);
    }

    // Finds a non-capital native settlement that teaches a learnable skill and a land tile adjacent to it.
    private static void NativeSettlementWithSkill(Game game, out CrownAndColony.GameLogic.Natives.NativeSettlement settlement, out Position adjacent)
    {
        bool HasLandNeighbour(CrownAndColony.GameLogic.Natives.NativeSettlement s) =>
            s.Position.Neighbours().Any(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater);
        settlement = game.NativeSettlements.First(s => s.LearnableSkill is not null && !s.SkillConsumed && HasLandNeighbour(s));
        adjacent = settlement.Position.Neighbours()
            .First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.NativeSettlementAt(n) is null);
    }
}
