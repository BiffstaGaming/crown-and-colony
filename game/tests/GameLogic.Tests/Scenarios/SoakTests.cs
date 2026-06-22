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
    public void ForeignWar_StaysBounded_Deterministic_AndNeverSoftlocks()
    {
        // Hold every foreign power at war with the human for the whole game so the retaliation AI runs at full
        // pressure: assert it never wedges or runs away (the turn always advances, no negative store/treasury),
        // and a game-long foreign onslaught is still deterministic and round-trips byte-identically.
        for (ulong seed = 3000; seed < 3006; seed++)
        {
            Game game = Game.New(Classic, seed);
            Game twin = Game.New(Classic, seed); // determinism witness: same seed, same provocation

            for (int turn = 0; turn < 120; turn++)
            {
                DeclareWarOnHuman(game);
                DeclareWarOnHuman(twin);

                int turnBefore = game.Turn;
                game.EndTurn();
                twin.EndTurn();

                Assert.Equal(turnBefore + 1, game.Turn); // no softlock: the world always advances
                Assert.All(game.Colonies, c => Assert.All(c.Stores.Values, v =>
                    Assert.True(v >= 0, $"seed {seed}: colony store went negative")));
                Assert.All(game.Players, p =>
                    Assert.True(p.Gold >= 0, $"seed {seed}: player {p.PlayerId} treasury went negative"));
            }

            Assert.Equal(SaveGame.From(game).ToJson(), SaveGame.From(twin).ToJson());     // deterministic under war
            string json = SaveGame.From(game).ToJson();
            Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson()); // round-trips
        }

        static void DeclareWarOnHuman(Game game)
        {
            foreach (Player power in game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial))
            {
                game.SetStance(power.PlayerId, game.HumanPlayer.PlayerId, Stance.War);
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

    /// <summary>
    /// L5 AI-autoplay performance-budget gate (docs/TESTING.md L5, kanban 86d3dzdzr).
    ///
    /// Runs a seeded full-game AI autoplay — every player but the idle human is AI, and a real
    /// <see cref="Game.EndTurn"/> drives all of them (foreign-power economy + turn, native raids,
    /// the shared world) — and asserts the wall-time stays under a budget, both the per-turn average
    /// and the total. A perf regression in the AI turn loop (an O(n²) creeping into a per-turn scan,
    /// say) therefore fails the nightly even though no behaviour assertion changed.
    ///
    /// Budget rationale: on the dev box (Opus-4.8 session, 2026-06-22) a 250-turn autoplay over five
    /// seeds measured a per-turn average of ~1.5 ms (worst single seed ~1.8 ms; total ≈ 1.9 s for the
    /// 1250 turns). The ceilings here are ~4× that — 6.0 ms/turn and 8.0 s total — generous enough that
    /// a slower/noisier CI shared runner can't flake the gate, yet still tight enough to catch a real
    /// order-of-magnitude regression. (4× matches the headroom the task floated; the AI turn is heavier
    /// than the idle-tick budget below because every foreign power runs its economy + turn each round.)
    ///
    /// Determinism: fixed seeds, and the human is never driven, so this neither draws the human's stream 0
    /// nor mutates any game state that the invariant/round-trip soak tests assert. It only times them.
    /// </summary>
    [Fact]
    public void AiAutoplay_TurnTime_StaysWithinPerformanceBudget()
    {
        const int turnsPerSeed = 250;
        const double perTurnBudgetMs = 6.0;   // ~4× the measured ~1.5 ms/turn dev-box average
        const double totalBudgetSec = 8.0;    // ~4× the measured ~1.9 s total over 5 seeds (1250 turns)

        int totalTurns = 0;
        double worstSeedAvgMs = 0.0;
        var overall = Stopwatch.StartNew();

        // Five independent seeds so the budget reflects the spread of map/economy shapes, not one lucky layout.
        for (ulong seed = 9000; seed < 9005; seed++)
        {
            // All-AI autoplay: build a normal game (human + foreign powers + native nations) and tick whole
            // turns without ever driving the human. EndTurn runs every AI economy/turn and the shared world.
            Game game = Game.New(Classic, seed);

            var seedWatch = Stopwatch.StartNew();
            for (int turn = 0; turn < turnsPerSeed; turn++)
            {
                game.EndTurn();
            }
            seedWatch.Stop();

            double seedAvgMs = seedWatch.Elapsed.TotalMilliseconds / turnsPerSeed;
            worstSeedAvgMs = Math.Max(worstSeedAvgMs, seedAvgMs);
            totalTurns += turnsPerSeed;
        }

        overall.Stop();
        double averageMs = overall.Elapsed.TotalMilliseconds / totalTurns;

        Assert.True(averageMs < perTurnBudgetMs,
            $"AI-autoplay average EndTurn took {averageMs:F3} ms over {totalTurns} turns " +
            $"(worst-seed avg {worstSeedAvgMs:F3} ms; budget {perTurnBudgetMs} ms)");
        Assert.True(overall.Elapsed.TotalSeconds < totalBudgetSec,
            $"AI-autoplay total took {overall.Elapsed.TotalSeconds:F2} s over {totalTurns} turns " +
            $"(budget {totalBudgetSec} s)");
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
