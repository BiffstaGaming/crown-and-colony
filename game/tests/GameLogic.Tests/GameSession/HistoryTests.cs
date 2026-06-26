using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The human player's notable-event history (`86d3c9x53` — the History report's source): founding a colony, entering
/// war with a rival, and electing a Founding Father each append a turn-stamped, player-facing <see cref="HistoryEvent"/>
/// to <see cref="Game.History"/>. <b>Persisted</b> from save v58 (round-trip + omit-when-empty covered below), so a
/// reloaded game keeps its history. Read-only for the report.
/// </summary>
public class HistoryTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static int ForeignPowerId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    /// <summary>The human's history events of every kind EXCEPT region discovery (which the starting fog reveal records at game start, P6).</summary>
    private static System.Collections.Generic.List<HistoryEvent> NonDiscovery(Game game) =>
        game.History.Where(h => h.Kind != HistoryEventKind.RegionDiscovered).ToList();

    [Fact]
    public void History_StartsWithOnlyDiscoveryEvents()
    {
        var game = Game.New(Classic, seed: 7);
        // From P6 the starting fog reveal discovers regions, recording RegionDiscovered events at game start. No
        // colony/war/father event has happened yet, so filtering those out leaves an empty list.
        Assert.Empty(NonDiscovery(game));
    }

    [Fact]
    public void FoundingAColony_RecordsAColonyFoundedEvent()
    {
        var game = Game.New(Classic, seed: 424242);
        Colony colony = game.FoundColony(game.PlayerUnits.First());

        HistoryEvent e = Assert.Single(NonDiscovery(game)); // the colony-founded event is the only non-discovery one
        Assert.Equal(HistoryEventKind.ColonyFounded, e.Kind);
        Assert.Equal(game.Turn, e.Turn);
        Assert.Contains(colony.Name, e.Description);
    }

    [Fact]
    public void EnteringWarWithARival_RecordsAWarEvent_Once()
    {
        var game = Game.New(Classic, seed: 7);
        int rival = ForeignPowerId(game);

        game.SetStance(game.HumanPlayer.PlayerId, rival, Stance.War);
        // A second SetStance to the same (already-war) state must not re-record.
        game.SetStance(game.HumanPlayer.PlayerId, rival, Stance.War);

        HistoryEvent e = Assert.Single(NonDiscovery(game));
        Assert.Equal(HistoryEventKind.WarDeclared, e.Kind);
        Assert.Contains("War", e.Description);
    }

    [Fact]
    public void RivalOnRivalWar_IsNotRecordedInTheHumansHistory()
    {
        var game = Game.New(Classic, seed: 7);
        var rivals = game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).ToList();
        Assert.True(rivals.Count >= 2, "test needs two rival powers");

        game.SetStance(rivals[0].PlayerId, rivals[1].PlayerId, Stance.War);

        Assert.Empty(NonDiscovery(game)); // a war the human is not party to is off the human's history (discovery events aside)
    }

    // ── Persistence (save v58): the history log survives save/load ───────────────────────────────────────

    [Fact]
    public void History_SurvivesSaveLoad_WithEveryEventAndScore()
    {
        var game = Game.New(Classic, seed: 424242);
        game.FoundColony(game.PlayerUnits.First());      // a ColonyFounded event
        game.SetStance(game.HumanPlayer.PlayerId, ForeignPowerId(game), Stance.War); // a WarDeclared event
        // The starting fog reveal also recorded scored RegionDiscovered events — so the log mixes scored + score-less.
        Assert.NotEmpty(game.History);

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        // The whole log round-trips: same count, same (kind, turn, description, score) tuple in the same order.
        Assert.Equal(game.History.Count, loaded.History.Count);
        Assert.Equal(
            game.History.Select(h => (h.Kind, h.Turn, h.Description, h.Score)),
            loaded.History.Select(h => (h.Kind, h.Turn, h.Description, h.Score)));
        // …and the score summand it feeds is preserved exactly (no re-earning, no loss).
        Assert.Equal(game.HistoryEventScore, loaded.HistoryEventScore);
        Assert.Equal(game.Score, loaded.Score);
    }

    [Fact]
    public void EmptyHistory_IsOmittedFromTheSave()
    {
        // Omit-when-empty: a SaveGame whose history log is empty writes no History token, so an event-free game stays
        // byte-identical to v57 (the field simply does not appear). A loaded such save has an empty history.
        var game = Game.New(Classic, seed: 7);
        SaveGame emptyLog = SaveGame.From(game) with { History = null };

        Assert.DoesNotContain("\"History\"", emptyLog.ToJson()); // WhenWritingNull → the field is absent
        Game loaded = SaveGame.FromJson(emptyLog.ToJson()).Restore(Classic);
        Assert.Empty(loaded.History);                            // pre-v58 / omitted → empty log
    }

    [Fact]
    public void FreshGame_HistoryRoundTripsByteIdentical()
    {
        // A fresh game's log is NOT empty (the starting fog reveal records RegionDiscovered events), but it must still
        // round-trip byte-identically: save → load → save reproduces the same JSON exactly (the log feeds no game
        // evolution, so reloading does not perturb the stream — ADR-009).
        var game = Game.New(Classic, seed: 99);
        string json = SaveGame.From(game).ToJson();
        Assert.Contains("\"History\"", json); // a fresh game DID record discovery events, so the field is present
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }
}
