using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The human nation's year-by-year <b>demographic series</b> (`86d3fq27v` — the end-game demographics graph's source):
/// at each year-rollover <see cref="Game.EndTurn"/> appends one <see cref="DemographicSnapshot"/> (population / gold /
/// score) to <see cref="Game.Demographics"/>. One snapshot per calendar year (the post-1600 two-turns-per-year era still
/// records once), RNG-free (ADR-009), and persisted from save v65 (round-trips; pre-v65 saves load empty). Read-only for
/// the report.
/// </summary>
public class DemographicsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Colonist = "model.unit.freeColonist";

    /// <summary>A 3×3 game started at <paramref name="turn"/> with a fixed RNG, two on-map colonists, and no colonies.</summary>
    private static Game AtTurn(int turn) => new SaveGame
    {
        Turn = turn, RandomStateValue = 1, RandomIncrement = 1,
        MapWidth = 3, MapHeight = 3,
        Terrain =
        [
            "model.tile.ocean", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.plains",
            "model.tile.plains", "model.tile.plains", "model.tile.mountains",
        ],
        Units = [new SavedUnit(1, Colonist, 1, 1, 3), new SavedUnit(2, Colonist, 2, 2, 3)],
        Explored = [],
    }.Restore(Classic);

    [Fact]
    public void NewGame_StartsWithNoDemographics()
    {
        var game = Game.New(Classic, seed: 7);
        Assert.Empty(game.Demographics); // nothing recorded until the first year rolls over at EndTurn
    }

    [Fact]
    public void EndTurn_RecordsOneSnapshotPerYear_WithThePopulationGoldAndScore()
    {
        Game game = AtTurn(1); // turn 1 = 1492 (pre-1600 → one turn per year)
        int yearOfTurn1 = game.CurrentYear;

        game.EndTurn();

        DemographicSnapshot snap = Assert.Single(game.Demographics);
        Assert.Equal(yearOfTurn1, snap.Year);          // the snapshot is stamped with the year that just ended (1492)
        Assert.Equal(game.HumanPopulation, snap.Population); // no colonies → population 0, but read off the same oracle
        Assert.Equal(game.Gold, snap.Gold);
        Assert.Equal(game.Score, snap.Score);
    }

    [Fact]
    public void EndTurn_AppendsAnEntryEachYear_InOrder()
    {
        Game game = AtTurn(1);
        game.EndTurn(); // year 1492
        game.EndTurn(); // year 1493
        game.EndTurn(); // year 1494

        Assert.Equal(3, game.Demographics.Count);
        Assert.Equal([1492, 1493, 1494], game.Demographics.Select(d => d.Year));
    }

    [Fact]
    public void PostSeasonYear_TwoTurnsPerYear_RecordOnlyOneSnapshotForThatYear()
    {
        // Classic splits into two seasons from 1600. Turn 109 = linear year 1600 season Spring; turn 110 = 1600 Autumn;
        // turn 111 = 1601. So ending turns 109 and 110 are BOTH year 1600 — the dedup must keep a single 1600 entry.
        Game game = AtTurn(109);
        Assert.Equal(1600, game.CurrentYear);

        game.EndTurn(); // ends 1600 (Spring) → records 1600
        Assert.Equal(1600, game.CurrentYear); // still 1600 (now Autumn)
        game.EndTurn(); // ends 1600 (Autumn) → dedup: no second 1600 entry
        Assert.Equal(1601, game.CurrentYear);
        game.EndTurn(); // ends 1601 → records 1601

        Assert.Equal([1600, 1601], game.Demographics.Select(d => d.Year)); // one 1600, then 1601
    }

    // ── Determinism (ADR-009): recording the series must not perturb the random stream ───────────────────────

    [Fact]
    public void RecordingDemographics_DoesNotPerturbTheRandomStream()
    {
        // Two identical games: ending a turn records a snapshot. The RNG state after the turn must be identical to a game
        // whose snapshot we then strip — i.e. the recording reads totals only, never draws. We prove it by comparing the
        // post-EndTurn RandomState across a save/reload that drops the series: the continuation is byte-identical.
        Game a = AtTurn(1);
        Game b = AtTurn(1);
        a.EndTurn();
        b.EndTurn();

        Assert.Equal(a.RandomState, b.RandomState);        // same future random sequence (the record drew nothing)
        Game reloadedNoSeries = (SaveGame.From(b) with { Demographics = null }).Restore(Classic);
        Assert.Equal(a.RandomState, reloadedNoSeries.RandomState); // dropping the series does not change the stream
    }

    // ── Persistence (save v65): the series survives save/load; empty omits; default round-trips byte-identically ──

    [Fact]
    public void Demographics_SurviveSaveLoad_InOrderWithEveryFigure()
    {
        Game game = AtTurn(1);
        game.EndTurn(); // 1492
        game.EndTurn(); // 1493
        Assert.NotEmpty(game.Demographics);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(game.Demographics.Count, loaded.Demographics.Count);
        Assert.Equal(
            game.Demographics.Select(d => (d.Year, d.Population, d.Gold, d.Score)),
            loaded.Demographics.Select(d => (d.Year, d.Population, d.Gold, d.Score)));
    }

    [Fact]
    public void EmptyDemographics_IsOmittedFromTheSave()
    {
        // Omit-when-empty: a fresh game whose first year has not rolled over has no snapshots, so the field is absent and
        // the save stays byte-identical to v64 (no Demographics token).
        var game = Game.New(Classic, seed: 7);
        Assert.Empty(game.Demographics);

        string json = SaveGame.From(game).ToJson();
        Assert.DoesNotContain("\"Demographics\"", json); // WhenWritingNull → the field is absent
        Game loaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Empty(loaded.Demographics);
    }

    [Fact]
    public void Demographics_RoundTripByteIdentical()
    {
        Game game = AtTurn(1);
        game.EndTurn();
        string json = SaveGame.From(game).ToJson();
        Assert.Contains("\"Demographics\"", json); // a year has rolled over, so the field is present
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }
}
