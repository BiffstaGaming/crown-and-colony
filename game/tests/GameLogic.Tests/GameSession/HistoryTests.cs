using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The human player's notable-event history (`86d3c9x53` — the History report's source): founding a colony, entering
/// war with a rival, and electing a Founding Father each append a turn-stamped, player-facing <see cref="HistoryEvent"/>
/// to <see cref="Game.History"/>. In-memory only this wave (a persisted log is a follow-up). Read-only for the report.
/// </summary>
public class HistoryTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private static int ForeignPowerId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    [Fact]
    public void History_StartsEmpty()
    {
        var game = Game.New(Classic, seed: 7);
        Assert.Empty(game.History);
    }

    [Fact]
    public void FoundingAColony_RecordsAColonyFoundedEvent()
    {
        var game = Game.New(Classic, seed: 424242);
        Colony colony = game.FoundColony(game.PlayerUnits.First());

        HistoryEvent e = Assert.Single(game.History);
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

        HistoryEvent e = Assert.Single(game.History);
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

        Assert.Empty(game.History); // a war the human is not party to is off the human's history
    }
}
