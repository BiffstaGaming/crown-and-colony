using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The temporary-modifier TTL system (86d3drpgz): a duration-bounded modifier
/// (FreeCol <c>Modifier</c> with <c>firstTurn</c>/<c>lastTurn</c>, built by
/// <c>makeTimedModifier</c>) applies while active and is stripped once it expires —
/// checked once per turn in the <see cref="Game.EndTurn"/> tick. Classic ships none of
/// these, so the registry stays empty in the default game (verified below) and the
/// default game is byte-identical (ADR-009).
/// </summary>
public class TemporaryModifierTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Bells = "model.goods.bells";

    // ───────────────────────── window math (FreeCol Feature.appliesTo / isOutOfDate) ─────────────────────────

    [Fact]
    public void MakeTimed_BoundsTheWindowInclusiveOfBothEnds()
    {
        // Active from turn 5 for 3 turns → turns 5, 6, 7 (lastTurn = start + duration - 1).
        var modifier = TemporaryModifier.MakeTimed(
            new FatherModifier(Bells, ModifierType.Percentage, 50, 0), duration: 3, start: 5);

        Assert.Equal(5, modifier.FirstTurn);
        Assert.Equal(7, modifier.LastTurn);

        Assert.False(modifier.AppliesTo(4)); // before the window
        Assert.True(modifier.AppliesTo(5));  // first active turn
        Assert.True(modifier.AppliesTo(7));  // last active turn
        Assert.False(modifier.AppliesTo(8)); // after the window
    }

    [Fact]
    public void IsOutOfDate_IsTrueOnlyAfterTheLastTurn()
    {
        var modifier = TemporaryModifier.MakeTimed(
            new FatherModifier(Bells, ModifierType.Percentage, 50, 0), duration: 2, start: 10);
        // Active on turns 10 and 11.
        Assert.False(modifier.IsOutOfDate(10)); // first active turn
        Assert.False(modifier.IsOutOfDate(11)); // still active on the last turn
        Assert.True(modifier.IsOutOfDate(12));  // expired the turn after lastTurn
    }

    [Fact]
    public void MakeTimed_RejectsADurationBelowOne()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            TemporaryModifier.MakeTimed(new FatherModifier(Bells, ModifierType.Percentage, 50, 0), duration: 0, start: 1));
    }

    // ───────────────────────── applies while active, expires on its last turn ─────────────────────────

    [Fact]
    public void ActiveModifier_FoldsIntoGoodsModifiers_ThenStopsOnceExpired()
    {
        Game game = EmptyGame(); // Turn 1, no colony, no congress
        // A +100% bells modifier active for this turn and the next (turns 1 and 2).
        game.RegisterTemporaryModifier(TemporaryModifier.MakeTimed(
            new FatherModifier(Bells, ModifierType.Additive, 100, 0), duration: 2, start: 1));

        // Turn 1: active → folds (100 base + 100).
        Assert.Equal(1, game.Turn);
        Assert.Equal(200, game.ApplyGoodsModifiers(Bells, 100));
        Assert.Single(game.ActiveTemporaryModifiers(Bells));

        game.EndTurn(); // → Turn 2; lastTurn is 2, so still active (not yet out of date)
        Assert.Equal(2, game.Turn);
        Assert.Single(game.TemporaryModifiers);             // not stripped — still in window
        Assert.Equal(200, game.ApplyGoodsModifiers(Bells, 100)); // still folds on its last turn

        game.EndTurn(); // → Turn 3; now turn > lastTurn → stripped
        Assert.Equal(3, game.Turn);
        Assert.Empty(game.TemporaryModifiers);              // the EndTurn tick removed it
        Assert.Empty(game.ActiveTemporaryModifiers(Bells));
        Assert.Equal(100, game.ApplyGoodsModifiers(Bells, 100)); // base unchanged once expired
    }

    [Fact]
    public void ActiveModifier_OnlyFoldsForItsOwnTarget()
    {
        Game game = EmptyGame();
        game.RegisterTemporaryModifier(TemporaryModifier.MakeTimed(
            new FatherModifier(Bells, ModifierType.Additive, 100, 0), duration: 1, start: 1));

        Assert.Equal(100, game.ApplyGoodsModifiers("model.goods.crosses", 100)); // different target → untouched
        Assert.Empty(game.ActiveTemporaryModifiers("model.goods.crosses"));
    }

    // ───────────────────────── the default game registers none (byte-identical, ADR-009) ─────────────────────────

    [Fact]
    public void DefaultGame_RegistersNoTemporaryModifiers_OverManyTurns()
    {
        Game game = EmptyGame();
        Assert.Empty(game.TemporaryModifiers);
        for (int i = 0; i < 10; i++)
        {
            game.EndTurn();
            Assert.Empty(game.TemporaryModifiers); // classic never registers one — registry stays empty every turn
        }
        Assert.Equal(100, game.ApplyGoodsModifiers(Bells, 100)); // no temporary fold → base returned unchanged
    }

    // ───────────────────────── helpers ─────────────────────────

    /// <summary>A bare 3×3-plains game at turn 1 with no colonies, units, or Congress (the classic default — no modifiers).</summary>
    private static Game EmptyGame()
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", 9)],
            Units = [],
            Explored = [],
            Colonies = [],
        };
        return save.Restore(Classic);
    }
}
