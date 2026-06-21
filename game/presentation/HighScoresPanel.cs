using System.Collections.Generic;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The high-scores screen (FreeCol's <c>ReportHighScoresPanel</c>): the persisted leaderboard of completed games,
/// shown from the menu or automatically when a game ends. Each row is one <see cref="HighScore"/> — rank, the honorific
/// the score earned (<see cref="ScoreLevel"/>), the player/nation, the final score, the result (won/lost), the
/// difficulty, the turns played, and the date — listed best-score-first.
/// <para>
/// Pure presentation (ADR-006): it is handed an already-loaded, already-ranked leaderboard (the record/ranking/store
/// rules live in GameLogic's <see cref="GameLogic.Persistence.HighScoreStore"/>); the panel only lays it out. It never
/// reads or writes files — the host loads the store and passes it in via <see cref="Open"/>. Built programmatically
/// into the fixed <c>VBox/Scroll/Dynamic</c> shell with the signal-safe rebuild idiom (<c>RemoveChild</c> then
/// <c>QueueFree</c>, never <c>Free</c>), mirroring <see cref="ColonyReportPanel"/> / <see cref="VictoryPanel"/>.
/// </para>
/// </summary>
public partial class HighScoresPanel : PanelContainer
{
    private IReadOnlyList<HighScore> _scores = new List<HighScore>();

    /// <summary>
    /// Opens the high-scores screen over <paramref name="scores"/> (already loaded and ranked by
    /// <see cref="GameLogic.Persistence.HighScoreStore"/>). An empty list still opens, showing a "no scores yet"
    /// placeholder, so the menu entry is always usable.
    /// </summary>
    /// <param name="scores">The leaderboard to display, descending by score.</param>
    public void Open(IReadOnlyList<HighScore> scores)
    {
        _scores = scores;
        Rebuild();
        Show();
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/Title").Text = "🏆 High Scores";

        var dynamic = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            // Signal-safe rebuild (mirrors ColonyReportPanel/VictoryPanel): detach now, queue the free — never Free()
            // synchronously, which would risk freeing a node mid-signal.
            dynamic.RemoveChild(child);
            child.QueueFree();
        }

        if (_scores.Count == 0)
        {
            dynamic.AddChild(new Label { Name = "Empty", Text = "No games completed yet — finish a game to set a high score." });
            return;
        }

        // Header row.
        dynamic.AddChild(new Label
        {
            Name = "Header",
            Text = "#   Title              Player / Nation        Score   Result  Difficulty   Turns  Date",
        });
        dynamic.AddChild(new HSeparator());

        int rank = 1;
        foreach (HighScore s in _scores)
        {
            dynamic.AddChild(new Label { Name = $"Score{rank}", Text = FormatRow(rank, s) });
            rank++;
        }
    }

    /// <summary>One leaderboard line: rank, honorific, player, score, result, difficulty, turns and date.</summary>
    private static string FormatRow(int rank, HighScore s)
    {
        string result = s.Won ? "Won" : "Lost";
        return $"{rank,-3} {LevelLabel(s.Level),-18} {s.PlayerName,-22} {s.Score,6}  {result,-6}  "
            + $"{DifficultyLabel(s.Difficulty),-11}  {s.RetirementTurn,4}   {s.DateString}";
    }

    /// <summary>The honorific as a human label (e.g. <see cref="ScoreLevel.MountainRange"/> → "Mountain Range").</summary>
    private static string LevelLabel(ScoreLevel level) => level switch
    {
        ScoreLevel.MountainRange => "Mountain Range",
        ScoreLevel.BirdOfPrey => "Bird of Prey",
        ScoreLevel.FoulSmellingPlant => "Foul-smelling Plant",
        ScoreLevel.PoisonousPlant => "Poisonous Plant",
        ScoreLevel.SlimeMoldBeetle => "Slime-mold Beetle",
        ScoreLevel.BloodSuckingInsect => "Blood-sucking Insect",
        ScoreLevel.InfectiousDisease => "Infectious Disease",
        ScoreLevel.ParasiticWorm => "Parasitic Worm",
        _ => level.ToString(),
    };

    /// <summary>The difficulty id's display tail (e.g. <c>model.difficulty.medium</c> → "Medium").</summary>
    private static string DifficultyLabel(string difficultyId)
    {
        int dot = difficultyId.LastIndexOf('.');
        if (dot < 0 || dot + 1 >= difficultyId.Length)
        {
            return difficultyId;
        }
        string tail = difficultyId[(dot + 1)..];
        return char.ToUpperInvariant(tail[0]) + tail[1..];
    }
}
