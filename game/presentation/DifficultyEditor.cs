using System;
using System.Collections.Generic;
using System.Globalization;
using CrownAndColony.GameLogic.Specification;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The per-option <b>custom difficulty</b> editor (86d3fq0x7) — FreeCol's editable <c>DifficultyDialog</c> (which
/// clones the selected level into <c>model.difficulty.custom</c> and lets the player edit each option). Opened by
/// <see cref="NewGameDialog"/> when the difficulty dropdown's "Custom…" row is picked, seeded with the current base
/// level's parsed <see cref="DifficultyOptions"/> (or the player's earlier edits when re-opened). The rows are built
/// from the <see cref="DifficultyFields.All"/> schema — a <see cref="LineEdit"/> per integer field (node-named
/// <see cref="DifficultyField.NodeName"/>, e.g. <c>FoundingFatherFactorField</c>) and a <see cref="CheckBox"/> per
/// boolean — and OK folds the texts back through the schema's <c>With</c> delegates, so the editor itself knows no
/// option by name (adding an editable option is a schema entry). OK validates every integer first: any non-integer
/// text shows an inline error and keeps the editor open. Cancel discards the edits.
/// Presentation-only (ADR-006): the edited record rides <see cref="NewGameDialog.PendingDifficultyOverrides"/> into
/// the new-game host, which applies it via <see cref="Ruleset.WithDifficultyOverrides"/>; the balance formulas
/// live in GameLogic. Built programmatically (no scene file) like the other overlays; shares the parchment/wood look
/// via <see cref="ColonyTheme"/>.
/// </summary>
public partial class DifficultyEditor : Control
{
    /// <summary>The integer-field editors, keyed by the schema field's <see cref="DifficultyField.OptionId"/>.</summary>
    private readonly Dictionary<string, LineEdit> _intFields = new();

    /// <summary>The boolean-field checkboxes, keyed by the schema field's <see cref="DifficultyField.OptionId"/>.</summary>
    private readonly Dictionary<string, CheckBox> _boolFields = new();

    private Label _errorLabel = null!;
    private DifficultyOptions _seed = DifficultyOptions.ClassicMedium;
    private Action<DifficultyOptions>? _onApply;
    private Action? _onCancel;

    /// <summary>Builds the overlay (dim + parchment panel + one schema-driven row per editable field + OK/Cancel) and starts hidden.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get();
        SetAnchorsPreset(LayoutPreset.FullRect);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.5f), Name = "Dim" };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var centre = new CenterContainer();
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var panel = new PanelContainer { Name = "Panel" };
        panel.AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        centre.AddChild(panel);

        // 31 rows outgrow a small window, so they scroll in a bounded viewport (the NewGameDialog pattern; the
        // OK/Cancel buttons sit inside the scroll so they are always reachable).
        var scroll = new ScrollContainer { Name = "Scroll" };
        scroll.SetCustomMinimumSize(new Vector2(430, 560));
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        panel.AddChild(scroll);

        var vbox = new VBoxContainer { Name = "VBox", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        vbox.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(vbox);

        vbox.AddChild(new Label
        {
            Name = "Title",
            Text = "Custom difficulty",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        // One row per schema field, in schema order. The controls are node-named from the schema (NodeName — the
        // option id PascalCased, dots removed) so the L3 suite and any future code can find a field deterministically.
        foreach (DifficultyField field in DifficultyFields.All)
        {
            switch (field)
            {
                case IntDifficultyField:
                    var edit = new LineEdit { Name = field.NodeName, CustomMinimumSize = new Vector2(80, 0) };
                    _intFields[field.OptionId] = edit;
                    vbox.AddChild(LabeledRow(field.Label, edit));
                    break;
                case BoolDifficultyField:
                    var check = new CheckBox { Name = field.NodeName, Text = field.Label };
                    _boolFields[field.OptionId] = check;
                    vbox.AddChild(check);
                    break;
            }
        }

        // Inline validation error (shown by OK when a field isn't a whole number; the editor stays open).
        _errorLabel = new Label
        {
            Name = "ErrorLabel",
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(410, 0),
        };
        vbox.AddChild(_errorLabel);

        var ok = new Button { Name = "ApplyButton", Text = "OK" };
        ok.Pressed += OnApplyPressed;
        vbox.AddChild(ok);

        var cancel = new Button { Name = "CancelButton", Text = "Cancel" };
        cancel.Pressed += OnCancelPressed;
        vbox.AddChild(cancel);

        Hide();
    }

    /// <summary>
    /// Opens the editor seeded with <paramref name="seed"/> (the base level's parsed values, or the player's earlier
    /// edits when re-opened). <paramref name="onApply"/> receives the edited record when OK validates; on Cancel
    /// <paramref name="onCancel"/> fires instead and the edits are discarded. Either way the editor hides itself.
    /// </summary>
    /// <param name="seed">The values the fields start at.</param>
    /// <param name="onApply">Receives the edited <see cref="DifficultyOptions"/> on a valid OK.</param>
    /// <param name="onCancel">Fires when the player backs out (so the host can revert its difficulty dropdown).</param>
    public void Open(DifficultyOptions seed, Action<DifficultyOptions> onApply, Action onCancel)
    {
        _seed = seed;
        _onApply = onApply;
        _onCancel = onCancel;
        foreach (DifficultyField field in DifficultyFields.All)
        {
            switch (field)
            {
                case IntDifficultyField f:
                    _intFields[f.OptionId].Text = f.Get(seed).ToString(CultureInfo.InvariantCulture);
                    break;
                case BoolDifficultyField f:
                    _boolFields[f.OptionId].ButtonPressed = f.Get(seed);
                    break;
            }
        }
        _errorLabel.Visible = false;
        Show();
    }

    /// <summary>OK: validates every integer field (any non-integer → inline error, stays open), folds the values onto the seed via the schema's <c>With</c> delegates, hides, and hands the record to the opener.</summary>
    private void OnApplyPressed()
    {
        // Validate everything first so the error names every bad field at once (not just the first).
        var invalid = new List<string>();
        foreach (DifficultyField field in DifficultyFields.All)
        {
            if (field is IntDifficultyField f
                && !int.TryParse(_intFields[f.OptionId].Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                invalid.Add(f.Label);
            }
        }
        if (invalid.Count > 0)
        {
            _errorLabel.Text = $"Not a whole number: {string.Join(", ", invalid)}";
            _errorLabel.Visible = true;
            return; // stays open for correction
        }

        DifficultyOptions result = _seed;
        foreach (DifficultyField field in DifficultyFields.All)
        {
            result = field switch
            {
                IntDifficultyField f => f.With(result,
                    int.Parse(_intFields[f.OptionId].Text, NumberStyles.Integer, CultureInfo.InvariantCulture)),
                BoolDifficultyField f => f.With(result, _boolFields[f.OptionId].ButtonPressed),
                _ => result,
            };
        }
        Hide();
        _onApply?.Invoke(result);
    }

    /// <summary>Cancel: discards the edits, hides, and tells the opener (which reverts its difficulty dropdown).</summary>
    private void OnCancelPressed()
    {
        Hide();
        _onCancel?.Invoke();
    }

    private static HBoxContainer LabeledRow(string label, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        row.AddChild(new Label { Text = label, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(control);
        return row;
    }
}
