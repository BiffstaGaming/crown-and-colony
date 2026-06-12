using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Scenarios;

/// <summary>
/// L2 scenario tests (docs/TESTING.md): whole-game runs through the real engine
/// loop, asserting outcomes and invariants — not single methods.
/// </summary>
public class WalkingSkeletonScenarioTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void TenTurnsOfWandering_InvariantsHold()
    {
        // Play 10 turns: each turn, move the unit to any legal neighbour until
        // movement runs out, then end the turn. The game must never enter an
        // illegal state.
        var game = Game.New(Classic, seed: 2026);
        Unit unit = game.Units[0];

        for (int turn = 1; turn <= 10; turn++)
        {
            Assert.Equal(turn, game.Turn);

            while (unit.MovementLeft > 0)
            {
                Position? next = unit.Position.Neighbours()
                    .Where(n => game.CheckMove(unit, n).Allowed)
                    .Cast<Position?>()
                    .FirstOrDefault();
                if (next is null)
                {
                    break; // boxed in — legal, just stop
                }
                game.MoveUnit(unit, next.Value);

                // Invariants after every single move:
                Assert.True(game.Map.InBounds(unit.Position));
                Assert.False(game.Map.TerrainAt(unit.Position).IsWater);
                Assert.InRange(unit.MovementLeft, 0, Unit.BaseMovementPoints);
            }

            game.EndTurn();
        }

        Assert.Equal(11, game.Turn);
    }

    [Fact]
    public void SameSeed_TwoGames_PlayedIdentically_StayIdentical()
    {
        // Full determinism: two games from the same seed, driven by the same
        // decisions, must remain byte-for-byte identical (compared via save JSON).
        Game a = Game.New(Classic, seed: 555);
        Game b = Game.New(Classic, seed: 555);

        for (int turn = 0; turn < 5; turn++)
        {
            PlayGreedyTurn(a);
            PlayGreedyTurn(b);
        }

        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
    }

    [Fact]
    public void SaveMidGame_LoadAndContinue_MatchesUninterruptedRun()
    {
        // The save/load acid test: interrupting a game must not change its future.
        Game uninterrupted = Game.New(Classic, seed: 314);
        Game toInterrupt = Game.New(Classic, seed: 314);

        for (int turn = 0; turn < 3; turn++)
        {
            PlayGreedyTurn(uninterrupted);
            PlayGreedyTurn(toInterrupt);
        }

        // Save + reload one of them mid-game.
        Game resumed = SaveGame.FromJson(SaveGame.From(toInterrupt).ToJson()).Restore(Classic);

        for (int turn = 0; turn < 3; turn++)
        {
            PlayGreedyTurn(uninterrupted);
            PlayGreedyTurn(resumed);
        }

        Assert.Equal(SaveGame.From(uninterrupted).ToJson(), SaveGame.From(resumed).ToJson());
    }

    /// <summary>Moves the first unit greedily (first legal neighbour each step), then ends the turn.</summary>
    private static void PlayGreedyTurn(Game game)
    {
        Unit unit = game.Units[0];
        while (unit.MovementLeft > 0)
        {
            Position? next = unit.Position.Neighbours()
                .Where(n => game.CheckMove(unit, n).Allowed)
                .Cast<Position?>()
                .FirstOrDefault();
            if (next is null)
            {
                break;
            }
            game.MoveUnit(unit, next.Value);
        }
        game.EndTurn();
    }
}
