using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="VictoryPanel"/> (86d3c9xc6): once the game has a
/// <see cref="Game.Winner"/>, the screen opens, names the winner, shows the final score broken down, lists the
/// end-game stats, and Closes. Presentation-only (ADR-006) — the win + scoring are decided in GameLogic; the panel
/// only reads oracles. The winning state is built through the public save layer (rewriting the human to
/// <see cref="PlayerType.Independent"/>), so the test needs no internal access.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class VictoryPanelTests
{
    [TestCase]
    public async Task Victory_OpensTheScreen_ShowsScoreAndStats_AndCloses()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Found a colony, then via the public save layer make the human an Independent nation (the REF-defeat win):
        // Game.Winner then names the human, which is what the victory screen reads.
        Game game = GetGame(controller);
        Colony founded = game.FoundColony(game.Units[0]);
        SaveGame save = SaveGame.From(game);
        var players = save.Players!
            .Select(p => p.IsHuman ? p with { PlayerType = (int)PlayerType.Independent } : p)
            .ToList();
        game = (save with { Players = players }).Restore(game.Ruleset);
        SetGame(controller, game);
        AssertThat(game.Winner).IsNotNull(); // sanity: the rewrite produced a winning state

        controller.OpenVictoryPanel();
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/VictoryPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/VictoryPanel/VBox/VictoryTitle").Text).Contains("victorious");

        var dynamic = controller.GetNode<VBoxContainer>("UI/VictoryPanel/VBox/Dynamic");
        // The score header carries the winner's total; the breakdown + stats lines are named for the test.
        var scoreHeader = dynamic.GetNodeOrNull<Label>("ScoreHeader");
        AssertThat(scoreHeader).IsNotNull();
        AssertThat(scoreHeader!.Text).Contains(game.PlayerScore(game.Winner!).ToString());
        AssertThat(dynamic.GetNodeOrNull("ScoreLiberty")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("ScoreBonus")).IsNotNull(); // first-place independence bonus row
        var colonies = dynamic.GetNodeOrNull<Label>("StatColonies");
        AssertThat(colonies).IsNotNull();
        AssertThat(colonies!.Text).Contains("1"); // the one founded colony
        AssertThat(dynamic.GetNodeOrNull("StatTurns")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("StatYear")).IsNotNull();

        controller.GetNode<Button>("UI/VictoryPanel/VBox/CloseButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase]
    public async Task NoWinner_OpenIsANoOp_PanelStaysHidden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.OpenVictoryPanel(); // turn 1, game still running → Winner is null
        await runner.SimulateFrames(1);

        AssertThat(controller.GetNode<PanelContainer>("UI/VictoryPanel").Visible).IsFalse();
    }

    private static Game GetGame(GameController controller) =>
        (Game)controller
            .GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, game);
}
