using System;
using System.Globalization;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The pre-combat odds modal (`86d3c9xmw`): before a human-ordered attack is rolled, this panel shows the
/// attacker's and defender's final combat powers and the attacker's win chance, and lets the player commit to
/// the attack or back out. Presentation only (ADR-006): every figure comes from the side-effect-free
/// <see cref="Game.CombatOddsAgainst"/> oracle (no RNG drawn, no war declared, no movement spent until the
/// player presses <b>Attack</b>); this panel only displays them and forwards the decision through
/// <see cref="Open"/>'s callback.
/// </summary>
public partial class PreCombatPanel : PanelContainer
{
    private Action _onConfirm = () => { };

    public override void _Ready()
    {
        GetNode<Button>("VBox/Buttons/AttackButton").Pressed += Confirm;
        GetNode<Button>("VBox/Buttons/CancelButton").Pressed += Hide;
        Hide();
    }

    /// <summary>
    /// Opens the modal for an attack whose previewed <paramref name="odds"/> are shown. <paramref name="onConfirm"/>
    /// runs only if the player presses <b>Attack</b>; pressing <b>Cancel</b> (or closing) does nothing.
    /// </summary>
    public void Open(Game.CombatOdds odds, Action onConfirm)
    {
        _onConfirm = onConfirm;
        GetNode<Label>("VBox/Title").Text = "Attack?";
        GetNode<Label>("VBox/Info").Text = DescribeOdds(odds);
        Show();
    }

    /// <summary>The three-line odds readout: attacker power, defender power, and win chance as a whole-percent.</summary>
    public static string DescribeOdds(Game.CombatOdds odds)
    {
        string attack = odds.AttackPower.ToString("0.0", CultureInfo.InvariantCulture);
        string defence = odds.DefencePower.ToString("0.0", CultureInfo.InvariantCulture);
        int winPercent = (int)Math.Round(odds.WinProbability * 100);
        return $"Your strength: {attack}\nEnemy strength: {defence}\nChance to win: {winPercent}%";
    }

    private void Confirm()
    {
        Hide();
        _onConfirm();
    }
}
