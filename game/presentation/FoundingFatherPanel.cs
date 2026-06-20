using System;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The Founding Father choice dialog (FreeCol's "Choose Founding Father", `86d3c9xet`): shows the fathers currently
/// offered to the human's Continental Congress (one per category), the father presently being recruited, and the
/// liberty needed to elect the next one; choosing an offered father sets the recruitment target. Pure presentation
/// (ADR-006) — reads `Game.OfferedFathers`/`CurrentFather` and forwards to `Game.ChooseFather`; the election,
/// age-weighting and effects all live in GameLogic. The engine never picks for the human (it only elects the
/// father the human has already chosen once liberty is banked), so this is a genuine human choice.
/// </summary>
public partial class FoundingFatherPanel : PanelContainer
{
    private Game _game = null!;
    private Action _onChange = () => { };

    /// <summary>Opens the dialog. <paramref name="onChange"/> runs after a choice (to refresh the host view).</summary>
    public void Open(Game game, Action onChange)
    {
        _game = game;
        _onChange = onChange;
        Rebuild();
        Show();
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/FatherTitle").Text = "Continental Congress";
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free();
        }

        string current = _game.CurrentFather is { } cf ? _game.Ruleset.Father(cf).ShortName : "(none)";
        dynamic.AddChild(new Label { Text = $"Currently recruiting: {current}" });
        dynamic.AddChild(new Label { Text = $"Liberty to elect the next father: {_game.TotalFoundingFatherCost()}" });
        dynamic.AddChild(new HSeparator());

        if (_game.OfferedFathers.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "No fathers are on offer right now.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        foreach (string id in _game.OfferedFathers)
        {
            FoundingFather father = _game.Ruleset.Father(id);
            bool isCurrent = _game.CurrentFather == id;
            string fatherId = id;
            var choose = new Button
            {
                Name = $"Choose_{father.ShortName}",
                Text = isCurrent ? $"{father.ShortName}  ({father.Type})  — recruiting" : $"{father.ShortName}  ({father.Type})",
                Disabled = isCurrent,
            };
            choose.Pressed += () =>
            {
                _game.ChooseFather(fatherId);
                _onChange();
                Rebuild();
            };
            dynamic.AddChild(choose);
        }
    }
}
