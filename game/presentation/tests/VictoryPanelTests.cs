using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
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

    [TestCase]
    public async Task Victory_KeepPlayingButton_DisablesTheWin_AndResumes()
    {
        // 86d3fq161: the victory screen offers a "Keep Playing" button to the human winner; clicking it forwards to
        // Game.ContinuePlaying (disabling the victory conditions), so Game.Winner clears and the panel hides.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = WinningGame(controller);
        AssertThat(game.CanContinuePlaying).IsTrue();

        controller.OpenVictoryPanel();
        await runner.SimulateFrames(1);
        var dynamic = controller.GetNode<VBoxContainer>("UI/VictoryPanel/VBox/Dynamic");
        var keepPlaying = dynamic.GetNodeOrNull<Button>("Choices/KeepPlayingButton");
        AssertThat(keepPlaying).IsNotNull(); // the affordance appears for the winner

        keepPlaying!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.Winner).IsNull();                 // the win is disabled — the game proceeds
        AssertThat(game.CanContinuePlaying).IsFalse();    // already continuing — the option is spent
        AssertThat(controller.GetNode<PanelContainer>("UI/VictoryPanel").Visible).IsFalse(); // resumed
    }

    [TestCase]
    public async Task Victory_RetireButton_RecordsAndEndsTheGame()
    {
        // 86d3fq125: the victory screen offers a "Retire" button; clicking it forwards to Game.Retire (recording the
        // score and withdrawing the player), so the human becomes Retired and the panel hides.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        Game game = WinningGame(controller);

        controller.OpenVictoryPanel();
        await runner.SimulateFrames(1);
        var dynamic = controller.GetNode<VBoxContainer>("UI/VictoryPanel/VBox/Dynamic");
        var retire = dynamic.GetNodeOrNull<Button>("Choices/RetireButton");
        AssertThat(retire).IsNotNull();

        retire!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.IsHumanRetired).IsTrue(); // the player withdrew — the game is over for them
        AssertThat(controller.GetNode<PanelContainer>("UI/VictoryPanel").Visible).IsFalse();
    }

    [TestCase]
    public async Task FederationVictory_ShowsTheCommonwealthProclamation_AndTheHonestAddendum()
    {
        // WS1.2: an Australia game won by the Federation path shows the Commonwealth proclamation + the BINDING honest
        // addendum (doc 19), titled "the Commonwealth of Australia is proclaimed" — NOT the classic "swept the rivals"
        // reason or the nation-id name ("English").
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game game = Game.New(GameVariants.Australia.LoadRuleset(), 0xC0FFEEUL, mapSource: MapSource.Australia);
        // Drive the Federation phase to Commonwealth (SetFederationPhase is internal to GameLogic) → Federation is the
        // exclusive win for Australia, so Game.Winner becomes the human at the proclamation.
        typeof(Game).GetField("_federationPhase", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, FederationPhase.Commonwealth);
        AssertThat(game.Winner).IsNotNull();
        SetGame(controller, game);
        SetVariant(controller, GameVariants.Australia); // so OpenVictoryPanel threads the variant's Commonwealth text

        controller.OpenVictoryPanel();
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/VictoryPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/VictoryPanel/VBox/VictoryTitle").Text).Contains("Commonwealth of Australia");

        var dynamic = controller.GetNode<VBoxContainer>("UI/VictoryPanel/VBox/Dynamic");
        AssertThat(dynamic.GetNode<Label>("VictoryReason").Text).Contains("voted to federate");
        AssertThat(dynamic.GetNode<Label>("VictoryReason").Text).NotContains("swept"); // not the classic reason
        AssertThat(dynamic.GetNode<Label>("VictoryAddendum").Text).Contains("Aboriginal and Torres Strait Islander");
    }

    [TestCase]
    public async Task FederationVictory_ShowsTheGradedCommonwealthScorecard()
    {
        // WS3.4: the Commonwealth win carries a GRADE and the six-category scorecard behind it (design docs 05/20). A
        // freshly-proclaimed Commonwealth with nothing built lands on the "Bare Federation" floor — the end-card states
        // that plainly rather than congratulating the player for scraping in.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game game = Game.New(GameVariants.Australia.LoadRuleset(), 0xC0FFEEUL, mapSource: MapSource.Australia);
        typeof(Game).GetField("_federationPhase", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, FederationPhase.Commonwealth);
        SetGame(controller, game);
        SetVariant(controller, GameVariants.Australia);

        controller.OpenVictoryPanel();
        await runner.SimulateFrames(1);

        var dynamic = controller.GetNode<VBoxContainer>("UI/VictoryPanel/VBox/Dynamic");
        AssertThat(dynamic.GetNode<Label>("GradeHeader").Text).Contains("Bare Federation");
        AssertThat(dynamic.GetNode<Label>("ScorecardHeader").Text).Contains("/600");
        // All six doc-20 categories are itemised.
        foreach (string category in new[]
                 { "GradeFederation", "GradeEconomy", "GradeCivic", "GradeFirstNations", "GradeStability", "GradeBreadth" })
        {
            AssertThat(dynamic.GetNode<Label>(category)).IsNotNull();
        }
    }

    [TestCase]
    public async Task ClassicVictory_ShowsNoCommonwealthScorecard()
    {
        // ADR-009 / ADR-006: the graded end-card is Federation-only — a classic win must not grow one.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        WinningGame(controller);
        controller.OpenVictoryPanel();
        await runner.SimulateFrames(1);

        var dynamic = controller.GetNode<VBoxContainer>("UI/VictoryPanel/VBox/Dynamic");
        AssertThat(dynamic.GetNodeOrNull<Label>("GradeHeader")).IsNull();
        AssertThat(dynamic.GetNodeOrNull<Label>("ScorecardHeader")).IsNull();
    }

    private static void SetVariant(GameController controller, GameVariant variant) =>
        controller.GetType().GetField("_variant", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, variant);

    /// <summary>Builds a winning game (the human as an Independent nation) on the controller and returns it.</summary>
    private static Game WinningGame(GameController controller)
    {
        Game game = GetGame(controller);
        game.FoundColony(game.Units[0]);
        SaveGame save = SaveGame.From(game);
        var players = save.Players!
            .Select(p => p.IsHuman ? p with { PlayerType = (int)PlayerType.Independent } : p)
            .ToList();
        game = (save with { Players = players }).Restore(game.Ruleset);
        SetGame(controller, game);
        AssertThat(game.Winner).IsNotNull();
        return game;
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
