using System;
using System.Linq;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Units;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The on-map native-settlement interaction panel (slice 1c — native UI): opened by clicking a
/// discovered native settlement, it offers the peaceful actions over the already-shipped logic —
/// speak with the chief (<see cref="Game.CheckVisit"/>/<see cref="Game.Visit(Unit, NativeSettlement)"/>)
/// and learn the settlement's skill (<see cref="Game.CheckLearnSkill"/>/<see cref="Game.LearnSkill"/>) —
/// plus the option to attack it (<see cref="Game.CheckAttackSettlement"/>/<see cref="Game.AttackSettlement(Unit, World.Position)"/>),
/// each shown only when the acting unit is allowed it. Presentation only (ADR-006): every action and every
/// gate is a Game oracle; the panel renders state and forwards clicks.
/// </summary>
public partial class NativeSettlementPanel : PanelContainer
{
    private Game _game = null!;
    private NativeSettlement _settlement = null!;
    private int _actingUnitId; // 0 = no acting unit (unit ids start at 1)
    private Action<string> _onAction = _ => { };
    private string _outcome = "";

    /// <summary>
    /// Opens the panel for a settlement, acting with the unit of id <paramref name="actingUnitId"/> (0 = none).
    /// <paramref name="onAction"/> runs after every action with its one-line outcome — the controller uses it to
    /// surface a status notice and to re-sync the map selection (an action may spend, upgrade, or destroy the unit).
    /// </summary>
    public void Open(Game game, NativeSettlement settlement, int actingUnitId, Action<string> onAction)
    {
        _game = game;
        _settlement = settlement;
        _actingUnitId = actingUnitId;
        _onAction = onAction;
        _outcome = "";
        Rebuild();
        Show();
    }

    /// <summary>The acting unit, re-resolved by id each rebuild (a learned colonist is swapped for a new object keeping its id).</summary>
    private Unit? ActingUnit => _game.Units.FirstOrDefault(u => u.Id == _actingUnitId && u.IsOnMap);

    private void Changed()
    {
        _onAction(_outcome); // controller surfaces the outcome + re-syncs selection (the unit may be gone/upgraded)
        Rebuild();           // re-gate this panel's own buttons (or hide if the settlement was just sacked)
    }

    private static string Short(string id) => id[(id.LastIndexOf('.') + 1)..];

    private static string Title(string id)
    {
        string s = Short(id);
        return s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
    }

    private void Rebuild()
    {
        // The settlement may have just been destroyed by an attack launched from this very panel.
        if (!_game.NativeSettlements.Contains(_settlement))
        {
            Hide();
            return;
        }

        string nation = Title(_settlement.NationTypeId);
        string type = Short(_settlement.SettlementTypeId);
        GetNode<Label>("VBox/NativeTitle").Text = _settlement.IsCapital ? $"{nation} {type} ★" : $"{nation} {type}";

        string teaches = _settlement.LearnableSkill is { } skill && !_settlement.SkillConsumed ? Short(skill) : "nothing";
        GetNode<Label>("VBox/NativeInfo").Text =
            $"Mood: {_settlement.AlarmLevel}   |   Teaches: {teaches}\n" +
            (_settlement.HasBeenVisited ? "You have spoken with this chief.\n" : "") +
            _outcome;

        BuildActions();
    }

    private void BuildActions()
    {
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free();
        }

        Unit? unit = ActingUnit;
        if (unit is null)
        {
            dynamic.AddChild(Hint("Select a unit, then click the settlement, to interact."));
            return;
        }
        if (unit.Position != _settlement.Position && !unit.Position.IsAdjacentTo(_settlement.Position))
        {
            dynamic.AddChild(Hint($"Move your {unit.Type.ShortName} next to the settlement to interact."));
            return;
        }

        bool any = false;
        if (_game.CheckVisit(unit, _settlement).Allowed)
        {
            any = true;
            dynamic.AddChild(ActionButton("Speak", "Speak with chief", () =>
            {
                int gift = _game.Visit(unit, _settlement);
                _outcome = gift > 0
                    ? $"The chief shared tales of nearby lands and gave {gift} gold."
                    : "The chief shared tales of nearby lands.";
                Changed();
            }));
        }
        if (_game.CheckLearnSkill(unit, _settlement).Allowed)
        {
            any = true;
            dynamic.AddChild(ActionButton("Learn", $"Learn {Short(_settlement.LearnableSkill!)}", () =>
            {
                Unit expert = _game.LearnSkill(unit, _settlement);
                _outcome = $"Your colonist trained as a {Short(expert.Type.Id)}.";
                Changed();
            }));
        }
        if (_game.CheckAttackSettlement(unit, _settlement.Position).Allowed)
        {
            any = true;
            dynamic.AddChild(ActionButton("Attack", "Attack settlement", () =>
            {
                CombatResult result = _game.AttackSettlement(unit, _settlement.Position);
                _outcome = result is CombatResult.GreatWin or CombatResult.Win
                    ? "The settlement was sacked!"
                    : "Your assault was repelled.";
                Changed();
            }));
        }
        if (!any)
        {
            dynamic.AddChild(Hint("There is nothing to do here right now."));
        }
    }

    private static Label Hint(string text) =>
        new() { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };

    private static Button ActionButton(string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        return button;
    }
}
