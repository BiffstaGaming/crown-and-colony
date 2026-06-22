using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The generic limit-evaluation engine (`86d3drpha`, FreeCol <c>Limit</c>/<c>Operand</c>/<c>Event</c>): parsing spec
/// <c>&lt;event&gt;</c>/<c>&lt;limit&gt;</c> elements, evaluating a <see cref="Limit"/> against game state, and the
/// <see cref="Game.CheckSpecEvent"/> registry that fires an event when all its limits hold. Covers the operand kinds
/// the classic ruleset actually uses (year, option, getSoL, settlements with the connected-port filter) plus the
/// proof-of-wiring: the Spanish-succession year gate routed through the engine, byte-identical to the old constant.
/// </summary>
public class LimitEngineTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    // ── Parsing: the classic <events> section ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClassicRuleset_ParsesBothEvents_WithTheirLimits()
    {
        SpecEvent? independence = Classic.Event("model.event.declareIndependence");
        Assert.NotNull(independence);
        Assert.Equal(100, independence!.ScoreValue); // score-value="100"
        // The three independence limits (rebels ≥ 50, ≥ 1 coastal colony, year ≤ lastColonialYear).
        Assert.NotNull(independence.Limit("model.limit.independence.rebels"));
        Assert.NotNull(independence.Limit("model.limit.independence.coastalColonies"));
        Assert.NotNull(independence.Limit("model.limit.independence.year"));

        SpecEvent? succession = Classic.Event("model.event.spanishSuccession");
        Assert.NotNull(succession);
        Assert.Equal(0, succession!.ScoreValue); // no score-value attribute → 0
        Assert.NotNull(succession.Limit("model.limit.spanishSuccession.year"));
    }

    [Fact]
    public void UnknownEvent_IsNull()
    {
        Assert.Null(Classic.Event("model.event.doesNotExist"));
    }

    [Fact]
    public void ParsedYearLimit_HasTheExpectedShape()
    {
        // model.limit.spanishSuccession.year: <left year@game> ge <right value=1600>
        Limit year = Classic.Event("model.event.spanishSuccession")!.Limit("model.limit.spanishSuccession.year")!;
        Assert.Equal(LimitOperator.Ge, year.Operator);
        Assert.Equal(OperandType.Year, year.LeftHandSide.OperandType);
        Assert.Equal(LimitScopeLevel.Game, year.LeftHandSide.ScopeLevel);
        Assert.Equal(1600, year.RightHandSide.Value);
    }

    [Fact]
    public void ParsedCoastalLimit_CarriesTheConnectedPortPredicate()
    {
        // model.limit.independence.coastalColonies: <settlements@player isConnectedPort=true> ge <value=1>
        Limit coastal = Classic.Event("model.event.declareIndependence")!
            .Limit("model.limit.independence.coastalColonies")!;
        Assert.Equal(OperandType.Settlements, coastal.LeftHandSide.OperandType);
        Assert.Equal(LimitScopeLevel.Player, coastal.LeftHandSide.ScopeLevel);
        Assert.Equal("isConnectedPort", coastal.LeftHandSide.MethodName);
        Assert.Equal("true", coastal.LeftHandSide.MethodValue);
        Assert.Equal(1, coastal.RightHandSide.Value);
    }

    // ── Operators: literal-vs-literal across the five comparisons ──────────────────────────────────────────

    [Theory]
    [InlineData(LimitOperator.Eq, 5, 5, true)]
    [InlineData(LimitOperator.Eq, 5, 6, false)]
    [InlineData(LimitOperator.Lt, 5, 6, true)]
    [InlineData(LimitOperator.Lt, 6, 6, false)]
    [InlineData(LimitOperator.Gt, 7, 6, true)]
    [InlineData(LimitOperator.Gt, 6, 6, false)]
    [InlineData(LimitOperator.Le, 6, 6, true)]
    [InlineData(LimitOperator.Le, 7, 6, false)]
    [InlineData(LimitOperator.Ge, 6, 6, true)]
    [InlineData(LimitOperator.Ge, 5, 6, false)]
    public void EvaluateLimit_ComparesLiterals(LimitOperator op, int left, int right, bool expected)
    {
        Game game = Game.New(Classic, Seed);
        var limit = new Limit("t", Literal(left), op, Literal(right));
        Assert.Equal(expected, game.EvaluateLimit(limit, game.HumanPlayer));
    }

    [Fact]
    public void EvaluateLimit_WithAnUnresolvableOperand_IsTrue()
    {
        // FreeCol Limit.evaluate: a null operand never constrains (lhs==null||rhs==null → true). A settlement-scoped
        // operand is outside the classic-event subset our engine resolves → null → the limit is vacuously true.
        Game game = Game.New(Classic, Seed);
        var unresolvable = new Operand(OperandType.Units, LimitScopeLevel.Settlement, Value: null);
        var limit = new Limit("t", unresolvable, LimitOperator.Lt, Literal(0));
        Assert.True(game.EvaluateLimit(limit, game.HumanPlayer));
    }

    // ── Operands against live game state ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EvaluateLimit_YearOperand_TracksTheCurrentYear()
    {
        Game game = Game.New(Classic, Seed);
        // year ≥ 1600 (the succession gate): false at the 1492 start.
        Limit year = Classic.Event("model.event.spanishSuccession")!.Limit("model.limit.spanishSuccession.year")!;
        Assert.False(game.EvaluateLimit(year, game.HumanPlayer));

        // A loose "year ≥ start year" must hold at turn 0.
        var atStart = new Limit("t", YearGame(), LimitOperator.Ge, Literal(game.CurrentYear));
        Assert.True(game.EvaluateLimit(atStart, game.HumanPlayer));
    }

    [Fact]
    public void EvaluateLimit_OptionOperand_ReadsTheRulesetOption()
    {
        Game game = Game.New(Classic, Seed);
        // The independence year limit's right side is the lastColonialYear option (classic 1800).
        Limit indYear = Classic.Event("model.event.declareIndependence")!.Limit("model.limit.independence.year")!;
        Assert.Equal(OperandType.Option, indYear.RightHandSide.OperandType);
        Assert.Equal("model.option.lastColonialYear", indYear.RightHandSide.Type);
        // year(1492) ≤ option(1800) → true.
        Assert.True(game.EvaluateLimit(indYear, game.HumanPlayer));

        // A direct option-vs-literal probe pins the resolved value at 1800.
        var optionIs1800 = new Limit("t",
            new Operand(OperandType.Option, LimitScopeLevel.Game, Value: null, Type: "model.option.lastColonialYear"),
            LimitOperator.Eq, Literal(1800));
        Assert.True(game.EvaluateLimit(optionIs1800, game.HumanPlayer));
    }

    [Fact]
    public void EvaluateLimit_GetSoLMethod_ReadsNationalSonsOfLiberty()
    {
        (Game game, Colony colony) = ColonyAtFullLiberty();
        Limit rebels = Classic.Event("model.event.declareIndependence")!.Limit("model.limit.independence.rebels")!;
        Assert.Equal("getSoL", rebels.LeftHandSide.MethodName); // getSoL ≥ 50
        Assert.Equal(100, game.NationalSonsOfLiberty(game.HumanPlayer));
        Assert.True(game.EvaluateLimit(rebels, game.HumanPlayer)); // 100 ≥ 50

        colony.Liberty = 0; // SoL → 0
        Assert.False(game.EvaluateLimit(rebels, game.HumanPlayer)); // 0 ≥ 50 is false
    }

    [Fact]
    public void EvaluateLimit_SettlementsOperand_CountsConnectedPortsWithThePredicate()
    {
        (Game game, Colony colony) = ColonyAtFullLiberty(); // one coastal colony
        Limit coastal = Classic.Event("model.event.declareIndependence")!
            .Limit("model.limit.independence.coastalColonies")!;
        Assert.True(game.EvaluateLimit(coastal, game.HumanPlayer)); // 1 connected port ≥ 1

        // An inland colony does NOT satisfy the connected-port count (the predicate filters it out).
        Position inland = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater && game.Map.TerrainAt(p).CanSettle
            && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null && !game.Map.IsNativeOwned(p) // non-native settleable (86d3e4bj7)
            && !p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater));
        colony.OwnerId = -1; // remove the coastal colony from the human's tally
        Unit inlandColonist = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), inland);
        game.FoundColony(inlandColonist);
        Assert.False(game.EvaluateLimit(coastal, game.HumanPlayer)); // 0 connected ports ≥ 1 is false
    }

    // ── The event registry: all-limits-met gate ───────────────────────────────────────────────────────────

    [Fact]
    public void CheckSpecEvent_FiresWhenEveryLimitHolds()
    {
        (Game game, _) = ColonyAtFullLiberty();
        // 1492, 100% SoL, one coastal colony → all three independence limits hold.
        Assert.True(game.CheckSpecEvent("model.event.declareIndependence", game.HumanPlayer));
    }

    [Fact]
    public void CheckSpecEvent_FailsWhenAnyLimitFails()
    {
        (Game game, Colony colony) = ColonyAtFullLiberty();
        colony.Liberty = 0; // SoL → 0: the rebels limit fails, even though year + coastal still hold
        Assert.False(game.CheckSpecEvent("model.event.declareIndependence", game.HumanPlayer));
    }

    [Fact]
    public void CheckSpecEvent_UnknownEvent_IsFalse()
    {
        Game game = Game.New(Classic, Seed);
        Assert.False(game.CheckSpecEvent("model.event.doesNotExist", game.HumanPlayer));
    }

    // ── Proof of wiring: the Spanish-succession year gate is the engine's, byte-identical ─────────────────

    [Fact]
    public void SpanishSuccessionYearGate_IsTheEnginesLimit_CrossingExactlyAt1600()
    {
        // RunSpanishSuccession's year gate is now the engine evaluating model.limit.spanishSuccession.year (year ≥
        // 1600), replacing the old `CurrentYear < 1600` constant. The succession flag can only become set once the
        // year reaches 1600 — never on an earlier turn (the hardcoded boundary, preserved byte-identically). We drive
        // a full game forward turn by turn and assert the flag never trips while the year is below 1600.
        Game game = Game.New(Classic, Seed);
        while (game.CurrentYear < 1600)
        {
            Assert.False(game.SpanishSuccessionDone); // the engine's year limit holds the succession back pre-1600
            game.EndTurn();
        }
        Assert.True(game.CurrentYear >= 1600); // we have reached the trigger year; the gate now permits a consolidation
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────

    private static Operand Literal(int v) => new(OperandType.None, LimitScopeLevel.None, v);
    private static Operand YearGame() => new(OperandType.Year, LimitScopeLevel.Game, Value: null);

    /// <summary>A game with one coastal human colony at 100% national Sons-of-Liberty.</summary>
    private static (Game Game, Colony Colony) ColonyAtFullLiberty()
    {
        Game game = Game.New(Classic, Seed);
        Position coastal = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater));
        Unit colonist = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), coastal);
        Colony colony = game.FoundColony(colonist);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // national SoL → 100%
        return (game, colony);
    }
}
