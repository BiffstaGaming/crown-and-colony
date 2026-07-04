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
        ColonyArt.FramePanel(this); // parchment image frame + dark-ink theme (not Godot's transparent default)
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
            dynamic.RemoveChild(child); child.QueueFree(); // detach now (signal-safe), free deferred — avoids freed-while-emitting when a child button's handler drives the rebuild
        }

        string current = _game.CurrentFather is { } cf ? Naming.Humanize(_game.Ruleset.Father(cf).ShortName) : "(none)";
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

            // One row per offered father: their portrait (if FreeCol ships one) left of the choose button, so the
            // congress reads like a row of candidates rather than a list of names. Degrades to button-only with no art.
            var row = new HBoxContainer { Name = $"Offer_{father.ShortName}" };
            if (FatherPortrait(father.ShortName) is { } control)
            {
                row.AddChild(control);
            }

            var choose = new Button
            {
                Name = $"Choose_{father.ShortName}",
                Text = isCurrent ? $"{Naming.Humanize(father.ShortName)}  ({father.Type})  — recruiting" : $"{Naming.Humanize(father.ShortName)}  ({father.Type})",
                Disabled = isCurrent,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            choose.Pressed += () =>
            {
                _game.ChooseFather(fatherId);
                _onChange();
                Rebuild();
            };
            row.AddChild(choose);
            dynamic.AddChild(row);
        }
    }

    /// <summary>
    /// A <see cref="TextureRect"/> showing a father's portrait at a fixed thumbnail size (keeping the source 200×237
    /// aspect), or null when that father has no FreeCol portrait so the caller renders text-only. Named
    /// <c>Portrait_&lt;shortName&gt;</c> so the L3 test can assert a portrait control is present beside the text.
    /// </summary>
    private static TextureRect? FatherPortrait(string shortName)
    {
        if (ColonyArt.FatherPortrait(shortName) is not { } tex)
        {
            return null;
        }
        return new TextureRect
        {
            Name = $"Portrait_{shortName}",
            Texture = tex,
            // The source art is 200×237; without IgnoreSize a TextureRect grows to that natural size (CustomMinimumSize
            // is only a floor). IgnoreSize makes it take exactly the min size we set, and KeepAspectCentered keeps the
            // face un-stretched inside that thumbnail box. Fixed (not Expand) so it stays a thumbnail beside the button.
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(PortraitWidth, PortraitHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
    }

    /// <summary>Portrait thumbnail width in the congress dialog (the source art is 200×237, scaled to keep that aspect).</summary>
    private const int PortraitWidth = 56;

    /// <summary>Portrait thumbnail height in the congress dialog (56 × 237/200 ≈ 66, rounded to keep the source aspect).</summary>
    private const int PortraitHeight = 66;
}
