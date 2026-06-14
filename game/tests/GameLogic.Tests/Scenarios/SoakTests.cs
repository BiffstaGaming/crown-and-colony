using System.Diagnostics;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Scenarios;

/// <summary>
/// L5 soak tests (docs/TESTING.md): long multi-seed runs with invariants and a
/// performance budget. Excluded from the per-push suite (Category=Soak) and run
/// by the nightly workflow.
/// </summary>
[Trait("Category", "Soak")]
public class SoakTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void TwentyFiveSeeds_TwoHundredTurns_InvariantsAlwaysHold()
    {
        bool aForeignEconomyWasActive = false;
        for (ulong seed = 1000; seed < 1025; seed++)
        {
            Game game = PlayGame(seed, turns: 200);

            // End-state invariants — over ALL colonies, the human's and the foreign powers' alike (FP-5).
            Assert.All(game.Colonies, c =>
            {
                Assert.True(c.Population >= 1, $"seed {seed}: colony starved out");
                Assert.All(c.Stores.Values, v => Assert.True(v >= 0));
                Assert.True(c.TileWorkers.Count + c.BuildingWorkers.Values.Sum() <= c.Population,
                    $"seed {seed}: assignments exceed population");
            });
            Assert.All(game.Explored, p => Assert.True(game.Map.InBounds(p)));

            // FP-5 economy invariants: no colonial player runs its treasury into debt (the overspend guards
            // hold), and the foreign economies are bounded — neither stalling nor running away.
            Assert.All(game.Players, p =>
                Assert.True(p.Gold >= 0, $"seed {seed}: player {p.PlayerId} treasury went negative ({p.Gold})"));
            var foreignPowers = game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).ToList();
            if (foreignPowers.Any(p => p.Gold > 0 || p.Market.SaveDeltas().Count > 0 || p.Congress.Count > 0
                    || game.Units.Any(u => u.OwnerId == p.PlayerId && u.Location == UnitLocation.InEurope)))
            {
                aForeignEconomyWasActive = true;
            }

            // The whole end state — every player's market/gold/dock/RNG and the foreign colonies — survives a
            // save/load round-trip byte-identically (the per-player streams stay isolated, the human included).
            string json = SaveGame.From(game).ToJson();
            Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
        }

        Assert.True(aForeignEconomyWasActive, "no foreign power ran an economy across 25 seeds — the AI stalled");
    }

    [Fact]
    public void NativeRaids_StayBounded_Deterministic_AndNeverSoftlock()
    {
        // Hold every native nation at maximum alarm for the whole game so the raid AI runs at full pressure,
        // and assert it never runs away or wedges: the turn always advances, braves never multiply, no store or
        // treasury goes negative — and a game-long onslaught is still deterministic and round-trips byte-identically.
        for (ulong seed = 2000; seed < 2008; seed++)
        {
            Game game = Game.New(Classic, seed);
            Game twin = Game.New(Classic, seed); // determinism witness: same seed, same provocation
            int braveStart = game.NativeUnits.Count();

            for (int turn = 0; turn < 150; turn++)
            {
                Enrage(game);
                Enrage(twin);

                int turnBefore = game.Turn;
                game.EndTurn();
                twin.EndTurn();

                Assert.Equal(turnBefore + 1, game.Turn); // no softlock: the world always advances
                Assert.True(game.NativeUnits.Count() <= braveStart, $"seed {seed}: braves multiplied");
                Assert.All(game.Colonies, c => Assert.All(c.Stores.Values, v =>
                    Assert.True(v >= 0, $"seed {seed}: colony store went negative")));
                Assert.All(game.Players, p =>
                    Assert.True(p.Gold >= 0, $"seed {seed}: player {p.PlayerId} treasury went negative"));
            }

            Assert.Equal(SaveGame.From(game).ToJson(), SaveGame.From(twin).ToJson());     // deterministic under onslaught
            string json = SaveGame.From(game).ToJson();
            Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson()); // byte-identical round-trip
        }

        static void Enrage(Game game)
        {
            foreach (NativeSettlement s in game.NativeSettlements)
            {
                game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm);
            }
        }
    }

    [Fact]
    public void TurnProcessing_StaysWithinPerformanceBudget()
    {
        // Budget: a turn with an active colony must average < 2 ms (the UI calls
        // EndTurn synchronously; even late-game turn counts must feel instant).
        Game game = PlayGame(seed: 7777, turns: 10);

        var stopwatch = Stopwatch.StartNew();
        const int ticks = 1000;
        for (int i = 0; i < ticks; i++)
        {
            game.EndTurn();
        }
        stopwatch.Stop();

        double average = stopwatch.Elapsed.TotalMilliseconds / ticks;
        Assert.True(average < 2.0, $"average EndTurn took {average:F3} ms (budget 2 ms)");
    }

    /// <summary>Plays a game: wander a few turns, found a colony, manage it greedily.</summary>
    private static Game PlayGame(ulong seed, int turns)
    {
        var game = Game.New(Classic, seed);

        for (int turn = 0; turn < turns; turn++)
        {
            // Phase 1: wander until turn 5, then settle where we stand (if legal).
            // Only the player's own on-map units (never a native brave) are driven.
            Unit? unit = game.PlayerUnits.FirstOrDefault(u => u.IsOnMap);
            if (unit is not null)
            {
                if (turn >= 5 && game.CheckFoundColony(unit).Allowed)
                {
                    game.FoundColony(unit);
                }
                else
                {
                    Position? next = unit.Position.Neighbours()
                        .Where(n => game.CheckMove(unit, n).Allowed)
                        .Cast<Position?>()
                        .FirstOrDefault();
                    if (next is not null)
                    {
                        game.MoveUnit(unit, next.Value);
                    }
                }
            }

            // Greedy colony management: keep idle colonists working, build the
            // first affordable thing.
            foreach (Colony colony in game.Colonies)
            {
                game.AutoAssignIdleToFood(colony);
                if (colony.IdleColonists > 0)
                {
                    string? staffable = colony.Buildings
                        .FirstOrDefault(b => game.CheckAssignBuildingWork(colony, b).Allowed);
                    if (staffable is not null)
                    {
                        game.AssignBuildingWork(colony, staffable);
                    }
                }
                if (colony.CurrentBuild is null)
                {
                    BuildingType? target = game.Buildables(colony).FirstOrDefault();
                    if (target is not null)
                    {
                        game.SetBuild(colony, target.Id);
                    }
                }
            }

            game.EndTurn();
        }
        return game;
    }
}
