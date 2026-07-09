using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The victory / end-of-game statistics screen (FreeCol's victory <c>ModelMessage</c> + <c>ReportEndTurnPanel</c>):
/// shown when <see cref="Game.Winner"/> is set — a nation has won by defeating the Royal Expeditionary Force
/// (independence) or by an alternate victory condition (last European / last human standing). It names the winner,
/// states which condition fired, lays out the winner's final <see cref="Game.PlayerScore"/> line-by-line from the
/// <see cref="Game.ScoreBreakdown"/> oracle (unit values, colony liberty, founding fathers, gold, history points and
/// the independence bonus), and lists the end-game stats (colonies, total population, turns played, final year).
/// <para>
/// Pure presentation (ADR-006): it only reads <see cref="Game"/> oracles, never mutates state and applies no rules —
/// victory and scoring are decided in GameLogic. Built programmatically into the fixed <c>VBox/Dynamic</c> shell with
/// the signal-safe rebuild idiom (<c>RemoveChild</c> then <c>QueueFree</c>, never <c>Free</c>), mirroring
/// <see cref="ColonyReportPanel"/>.
/// </para>
/// </summary>
public partial class VictoryPanel : PanelContainer
{
    private Game _game = null!;
    private string? _commonwealthTitle;
    private string? _commonwealthProclamation;
    private string? _commonwealthAddendum;

    /// <summary>
    /// Invoked when the player chooses to <b>keep playing</b> after winning (the "Keep Playing" button). The host
    /// (<see cref="GameController"/>) forwards to <see cref="Game.ContinuePlaying"/> and refreshes — the panel itself
    /// owns no rules (ADR-006). Null until the host wires it.
    /// </summary>
    public System.Action? OnContinuePlaying { get; set; }

    /// <summary>
    /// Invoked when the player chooses to <b>retire</b> from the victory screen (the "Retire" button). The host forwards
    /// to <see cref="Game.Retire"/> (recording the high score) and ends the game; the panel only forwards (ADR-006).
    /// Null until the host wires it.
    /// </summary>
    public System.Action? OnRetire { get; set; }

    /// <summary>
    /// Opens the victory screen over <paramref name="game"/> when it has a <see cref="Game.Winner"/>. A no-op (and
    /// stays hidden) if the game is still running, so a caller can blindly offer it after a turn resolves.
    /// </summary>
    public void Open(Game game, string? commonwealthTitle = null, string? commonwealthProclamation = null, string? commonwealthAddendum = null)
    {
        ColonyArt.FramePanel(this); // parchment image frame + dark-ink theme (not Godot's transparent default)
        _game = game;
        _commonwealthTitle = commonwealthTitle;
        _commonwealthProclamation = commonwealthProclamation;
        _commonwealthAddendum = commonwealthAddendum;
        if (_game.Winner is null)
        {
            Hide();
            return;
        }
        Rebuild();
        Show();
    }

    /// <summary>True when the game was won by the Federation path — a variant that supplies the Commonwealth text and has
    /// the Federation victory enabled (Australia). When on, <see cref="Specification.Ruleset.VictoryFederation"/> is the
    /// <em>exclusive</em> win, so the winner is always the human at the Commonwealth proclamation.</summary>
    private bool IsFederationVictory => _commonwealthTitle is not null && _game.Ruleset.VictoryFederation;

    private void Rebuild()
    {
        Player winner = _game.Winner!;
        GetNode<Label>("VBox/VictoryTitle").Text = IsFederationVictory
            ? $"🏆 {_commonwealthTitle}"
            : $"🏆 {WinnerName(winner)} is victorious!";

        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            // Signal-safe rebuild (mirrors ColonyReportPanel): detach now, queue the free — never Free() synchronously,
            // which would risk freeing a node mid-signal ("object freed while a signal is being emitted").
            dynamic.RemoveChild(child);
            child.QueueFree();
        }

        // ── How the game was won ──────────────────────────────────────────────────────────────────────────
        if (IsFederationVictory)
        {
            // The Commonwealth proclamation (doc 19), then the BINDING historically-honest addendum on who the 1901
            // settlement excluded — Federation is not framed as resolving everything (docs 03/15/19).
            dynamic.AddChild(Wrapped("VictoryReason", _commonwealthProclamation ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(_commonwealthAddendum))
            {
                dynamic.AddChild(Wrapped("VictoryAddendum", _commonwealthAddendum!));
            }
        }
        else
        {
            dynamic.AddChild(new Label { Name = "VictoryReason", Text = VictoryReason(winner) });
        }
        dynamic.AddChild(new HSeparator());

        // ── Final score, itemised (the winner's PlayerScore broken down) ──────────────────────────────────
        ScoreComponents s = _game.ScoreBreakdown(winner);
        dynamic.AddChild(new Label { Name = "ScoreHeader", Text = $"— Final score: {s.Total} —" });
        ScoreLine(dynamic, "ScoreUnits", "Unit values", s.UnitValues);
        ScoreLine(dynamic, "ScoreLiberty", "Colony liberty", s.ColonyLiberty);
        ScoreLine(dynamic, "ScoreFathers", "Founding Fathers", s.FoundingFatherPoints);
        ScoreLine(dynamic, "ScoreGold", "Gold", s.GoldPoints);
        ScoreLine(dynamic, "ScoreHistory", "Exploration & history", s.HistoryPoints);
        if (s.IndependenceBonusPercent > 0)
        {
            dynamic.AddChild(new Label
            {
                Name = "ScoreBonus",
                Text = $"    Independence bonus (+{s.IndependenceBonusPercent}%): {Signed(s.IndependenceBonus)}",
            });
        }
        dynamic.AddChild(new HSeparator());

        // ── End-game statistics ───────────────────────────────────────────────────────────────────────────
        List<Colony> colonies = _game.Colonies.Where(c => c.OwnerId == winner.PlayerId).ToList();
        int population = colonies.Sum(c => c.Population);
        dynamic.AddChild(new Label { Name = "StatsHeader", Text = "— End-game statistics —" });
        dynamic.AddChild(new Label { Name = "StatColonies", Text = $"    Colonies: {colonies.Count}" });
        dynamic.AddChild(new Label { Name = "StatPopulation", Text = $"    Total population: {population}" });
        dynamic.AddChild(new Label { Name = "StatTurns", Text = $"    Turns played: {_game.Turn}" });
        dynamic.AddChild(new Label { Name = "StatYear", Text = $"    Final year: {_game.CalendarLabel}" });

        // ── End-game choices (FreeCol's victory dialog: keep playing or retire) ───────────────────────────
        dynamic.AddChild(new HSeparator());
        var choices = new HBoxContainer { Name = "Choices" };
        dynamic.AddChild(choices);

        // "Keep Playing" — only the single-player winner may continue (disables the victory conditions, FreeCol
        // continuePlaying). Hidden once the win is already disabled or the winner is not the human.
        if (_game.CanContinuePlaying)
        {
            var keepPlaying = new Button { Name = "KeepPlayingButton", Text = "Keep Playing" };
            keepPlaying.Pressed += () =>
            {
                OnContinuePlaying?.Invoke();
                Hide(); // the game proceeds — close the screen so play resumes on the final board
            };
            choices.AddChild(keepPlaying);
        }

        // "Retire" — record the (winning) high score and end the game. Always offered on the victory screen.
        var retire = new Button { Name = "RetireButton", Text = "Retire" };
        retire.Pressed += () => OnRetire?.Invoke();
        choices.AddChild(retire);
    }

    private static void ScoreLine(VBoxContainer dynamic, string name, string label, int points) =>
        dynamic.AddChild(new Label { Name = name, Text = $"    {label}: {points}" });

    /// <summary>A centred, word-wrapped paragraph label (for the long Commonwealth proclamation + honest addendum),
    /// bounded so the panel does not stretch out to a single line.</summary>
    private static Label Wrapped(string name, string text) => new()
    {
        Name = name,
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(560, 0),
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>
    /// Which victory condition fired for <paramref name="winner"/>, matching <see cref="Game.Winner"/>'s checked order
    /// (independence first, then last-European, then last-human). Read from the winner's <see cref="Player.PlayerType"/>
    /// — an Independent nation defeated the REF; otherwise it is the last European/human power standing.
    /// </summary>
    private string VictoryReason(Player winner) => winner.PlayerType == PlayerType.Independent
        ? "The Royal Expeditionary Force is broken — independence is won!"
        : "Every rival European power has been swept from the New World.";

    /// <summary>The winner's display name: its nation id tail (e.g. <c>model.nation.dutch</c> → "Dutch"), or "Your nation" for the human with no nation.</summary>
    private static string WinnerName(Player winner)
    {
        if (winner.NationId is { } id)
        {
            string tail = id[(id.LastIndexOf('.') + 1)..];
            return tail.Length > 0 ? char.ToUpperInvariant(tail[0]) + tail[1..] : tail;
        }
        return winner.IsHuman ? "Your nation" : $"Player {winner.PlayerId}";
    }

    private static string Signed(int n) => (n > 0 ? "+" : "") + n;
}
