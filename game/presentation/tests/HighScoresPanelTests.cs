using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="HighScoresPanel"/> (<c>86d3c9xb1</c>): the panel opens over
/// a supplied leaderboard, renders a row per entry best-first (with the honorific, score and result), shows the
/// empty-state placeholder for a fresh leaderboard, and Closes. Presentation-only (ADR-006): the panel is handed an
/// already-ranked list (the ranking + store rules are unit-tested in GameLogic) so the scene test does no file I/O —
/// it never reads or writes the real <c>user://highscores.json</c>, keeping it isolated and deterministic.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HighScoresPanelTests
{
    private static HighScore Entry(int score, bool won) => new(
        PlayerName: "Dutch",
        NationId: "model.nation.dutch",
        NationTypeId: "model.nationType.trade",
        Score: score,
        Level: ScoreLevels.ForScore(score),
        Difficulty: "model.difficulty.medium",
        UnitCount: 4,
        ColonyCount: 3,
        RetirementTurn: 60,
        IndependenceTurn: won ? 58 : -1,
        Won: won,
        DateUtc: DateTime.UtcNow,
        GameId: Guid.NewGuid().ToString());

    [TestCase]
    public async Task Open_ListsEntriesBestFirst_WithHonorificAndResult_AndCloses()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        var panel = controller.GetNode<HighScoresPanel>("UI/HighScoresPanel");
        // Drive the panel directly with a fixture leaderboard (no file I/O): a high won game and a lower lost one.
        panel.Open(new List<HighScore> { Entry(21000, won: true), Entry(800, won: false) });
        await runner.SimulateFrames(1);

        AssertThat(((PanelContainer)panel).Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/HighScoresPanel/VBox/Title").Text).Contains("High Scores");

        var dynamic = controller.GetNode<VBoxContainer>("UI/HighScoresPanel/VBox/Scroll/Dynamic");
        var first = dynamic.GetNodeOrNull<Label>("Score1");
        AssertThat(first).IsNotNull();
        AssertThat(first!.Text).Contains("21000");
        AssertThat(first.Text).Contains("Mountain Range"); // 21000 → MountainRange honorific
        AssertThat(first.Text).Contains("Won");
        AssertThat(first.Text).Contains("Dutch");

        var second = dynamic.GetNodeOrNull<Label>("Score2");
        AssertThat(second).IsNotNull();
        AssertThat(second!.Text).Contains("800");
        AssertThat(second.Text).Contains("Lost");

        controller.GetNode<Button>("UI/HighScoresPanel/VBox/CloseButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(((PanelContainer)panel).Visible).IsFalse();
    }

    [TestCase]
    public async Task Open_EmptyLeaderboard_ShowsPlaceholder()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        var panel = controller.GetNode<HighScoresPanel>("UI/HighScoresPanel");
        panel.Open(new List<HighScore>());
        await runner.SimulateFrames(1);

        AssertThat(((PanelContainer)panel).Visible).IsTrue();
        var dynamic = controller.GetNode<VBoxContainer>("UI/HighScoresPanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("Empty")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("Score1")).IsNull(); // no rows
    }

    [TestCase]
    public async Task HighScoresButton_OpensThePanel()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.GetNode<Button>("UI/HighScoresButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The button loads the persisted leaderboard (possibly empty) and shows the panel — we only assert it opened.
        AssertThat(controller.GetNode<PanelContainer>("UI/HighScoresPanel").Visible).IsTrue();
    }
}
