using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Persistence;

/// <summary>
/// L1 unit tests (docs/TESTING.md) for the high-score leaderboard (<c>86d3c9xb1</c>) — a faithful port of FreeCol's
/// <c>HighScore</c> machinery (<c>checkHighScore</c> / <c>tidyScores</c> / <c>newHighScore</c> / load/save) and the
/// <see cref="ScoreLevel"/> thresholds. Covers ranking, the 10-entry cap, same-game de-duplication, JSON round-trip,
/// corrupt-file tolerance, the honorific bands, and <see cref="Game.RecordHighScore"/> being a pure, RNG-free read.
/// The store is engine-free, so this needs no Godot runtime; it never touches the game-save format (still v52).
/// </summary>
public class HighScoreStoreTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static HighScore Entry(int score, string gameId = "", bool won = true, DateTime? date = null) =>
        new(
            PlayerName: "Dutch",
            NationId: "model.nation.dutch",
            NationTypeId: "model.nationType.trade",
            Score: score,
            Level: ScoreLevels.ForScore(score),
            Difficulty: "model.difficulty.medium",
            UnitCount: 3,
            ColonyCount: 2,
            RetirementTurn: 50,
            IndependenceTurn: won ? 48 : -1,
            Won: won,
            DateUtc: date ?? DateTime.UtcNow,
            GameId: gameId);

    // ── ScoreLevel thresholds (FreeCol ScoreLevel) ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(40000, ScoreLevel.Continent)]
    [InlineData(39999, ScoreLevel.Country)]
    [InlineData(20000, ScoreLevel.MountainRange)]
    [InlineData(200, ScoreLevel.InfectiousDisease)] // exactly meets the 200 band
    [InlineData(199, ScoreLevel.ParasiticWorm)] // below 200, next band down is the 0-floor worm
    [InlineData(0, ScoreLevel.ParasiticWorm)]
    [InlineData(-5, ScoreLevel.ParasiticWorm)] // negative floors to the worm
    public void ScoreLevel_ForScore_PicksHighestBandMet(int score, ScoreLevel expected) =>
        Assert.Equal(expected, ScoreLevels.ForScore(score));

    // ── Ranking + the 10-entry cap (tidyScores) ──────────────────────────────────────────────────────────

    [Fact]
    public void Tidy_SortsDescending_AndCapsAtTen()
    {
        // Eleven distinct ascending scores → keep the top ten, descending.
        IEnumerable<HighScore> raw = Enumerable.Range(1, 11).Select(i => Entry(i * 100));
        IReadOnlyList<HighScore> tidied = HighScoreStore.Tidy(raw);

        Assert.Equal(HighScoreStore.MaxEntries, tidied.Count);
        Assert.Equal(tidied.OrderByDescending(s => s.Score).Select(s => s.Score), tidied.Select(s => s.Score));
        Assert.Equal(1100, tidied[0].Score);   // highest first
        Assert.Equal(200, tidied[^1].Score);    // the worst (100) was dropped by the cap
        Assert.DoesNotContain(tidied, s => s.Score == 100);
    }

    // ── checkHighScore / Add (FreeCol newHighScore) ──────────────────────────────────────────────────────

    [Fact]
    public void Add_FirstScore_IsAccepted()
    {
        (IReadOnlyList<HighScore> scores, bool added) = HighScoreStore.Add(Entry(500), new List<HighScore>());
        Assert.True(added);
        Assert.Single(scores);
        Assert.Equal(500, scores[0].Score);
    }

    [Fact]
    public void Add_OnFullBoard_RejectsWhenNotBetterThanWorst()
    {
        IReadOnlyList<HighScore> full = HighScoreStore.Tidy(Enumerable.Range(1, 10).Select(i => Entry(i * 1000)));
        // Worst entry is 1000; a 500 does not qualify.
        (IReadOnlyList<HighScore> after, bool added) = HighScoreStore.Add(Entry(500), full);
        Assert.False(added);
        Assert.Equal(full.Select(s => s.Score), after.Select(s => s.Score));
    }

    [Fact]
    public void Add_OnFullBoard_ReplacesWorstWhenBetter()
    {
        IReadOnlyList<HighScore> full = HighScoreStore.Tidy(Enumerable.Range(1, 10).Select(i => Entry(i * 1000)));
        (IReadOnlyList<HighScore> after, bool added) = HighScoreStore.Add(Entry(5500), full);
        Assert.True(added);
        Assert.Equal(HighScoreStore.MaxEntries, after.Count); // still capped
        Assert.Contains(after, s => s.Score == 5500);
        Assert.DoesNotContain(after, s => s.Score == 1000); // the old worst is gone
    }

    [Fact]
    public void Add_NegativeScore_OnEmptyBoard_IsRejected()
    {
        (IReadOnlyList<HighScore> scores, bool added) = HighScoreStore.Add(Entry(-1), new List<HighScore>());
        Assert.False(added);
        Assert.Empty(scores);
    }

    [Fact]
    public void Add_SameGame_HigherScore_ReplacesRatherThanDuplicates()
    {
        const string gameId = "game-A";
        (IReadOnlyList<HighScore> scores, _) = HighScoreStore.Add(Entry(1000, gameId), new List<HighScore>());
        (scores, bool added) = HighScoreStore.Add(Entry(2000, gameId), scores);

        Assert.True(added);
        Assert.Single(scores); // de-duplicated by game id — not two rows
        Assert.Equal(2000, scores[0].Score);
    }

    // ── JSON persistence (loadHighScores / saveHighScores) ───────────────────────────────────────────────

    [Fact]
    public void Json_RoundTrips_AllFields()
    {
        var entry = Entry(12345, gameId: "round-trip");
        IReadOnlyList<HighScore> loaded = HighScoreStore.Load(HighScoreStore.ToJson(new[] { entry }));

        Assert.Single(loaded);
        HighScore got = loaded[0];
        Assert.Equal(entry.PlayerName, got.PlayerName);
        Assert.Equal(entry.NationId, got.NationId);
        Assert.Equal(entry.NationTypeId, got.NationTypeId);
        Assert.Equal(entry.Score, got.Score);
        Assert.Equal(entry.Level, got.Level);
        Assert.Equal(entry.Difficulty, got.Difficulty);
        Assert.Equal(entry.UnitCount, got.UnitCount);
        Assert.Equal(entry.ColonyCount, got.ColonyCount);
        Assert.Equal(entry.RetirementTurn, got.RetirementTurn);
        Assert.Equal(entry.IndependenceTurn, got.IndependenceTurn);
        Assert.Equal(entry.Won, got.Won);
        Assert.Equal(entry.GameId, got.GameId);
    }

    [Fact]
    public void Json_SerializesScoreLevelByName_ForStability()
    {
        string json = HighScoreStore.ToJson(new[] { Entry(20000) }); // MountainRange
        Assert.Contains("MountainRange", json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not valid json")]
    [InlineData("[ { \"broken\": ")]
    public void Load_MissingOrCorrupt_YieldsEmpty_NeverThrows(string? json)
    {
        IReadOnlyList<HighScore> loaded = HighScoreStore.Load(json);
        Assert.Empty(loaded);
    }

    [Fact]
    public void Load_ReturnsCanonicalOrder_EvenIfFileWasUnsorted()
    {
        // A hand-rolled file with ascending scores must load descending.
        string json = HighScoreStore.ToJson(new[] { Entry(100), Entry(900), Entry(400) });
        // ToJson already tidies, but Load must also tidy regardless of input order — assert the result is descending.
        IReadOnlyList<HighScore> loaded = HighScoreStore.Load(json);
        Assert.Equal(new[] { 900, 400, 100 }, loaded.Select(s => s.Score));
    }

    // ── Game.RecordHighScore factory (FreeCol new HighScore(player)) ──────────────────────────────────────

    [Fact]
    public void RecordHighScore_CapturesGameState_AndIsPure()
    {
        Game game = Game.New(Classic, seed: 0x5C0E_5EEDUL);
        Player human = game.HumanPlayer;
        int scoreBefore = game.PlayerScore(human);

        HighScore hs = game.RecordHighScore(human, won: false, gameId: "g1");

        Assert.Equal(scoreBefore, hs.Score);
        Assert.Equal(ScoreLevels.ForScore(scoreBefore), hs.Level);
        Assert.Equal(game.Turn, hs.RetirementTurn);
        Assert.Equal(-1, hs.IndependenceTurn); // not independent
        Assert.False(hs.Won);
        Assert.Equal("g1", hs.GameId);
        Assert.Equal("model.difficulty.medium", hs.Difficulty);

        // Pure read (ADR-009): the score is unchanged by recording.
        Assert.Equal(scoreBefore, game.PlayerScore(human));
    }

    [Fact]
    public void RecordHighScore_DerivesPlayerNameFromNation()
    {
        Game game = Game.New(Classic, seed: 7, humanNationId: "model.nation.dutch");
        HighScore hs = game.RecordHighScore(game.HumanPlayer, won: true);
        Assert.Equal("Dutch", hs.PlayerName);
        Assert.Equal("model.nation.dutch", hs.NationId);
    }
}
