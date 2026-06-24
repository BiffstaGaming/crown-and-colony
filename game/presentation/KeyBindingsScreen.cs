using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.App;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The key-rebinding screen, shown as an overlay by the <see cref="SettingsScreen"/>. It lists every rebindable action
/// (from <see cref="KeyBindingsModel.Actions"/>) with its current key, lets the player capture a replacement key, and
/// — on <b>Back</b> — persists the overrides to <c>user://settings.cfg</c> and emits <see cref="Closed"/> for the host
/// to dismiss it. Each rebind applies to the global <c>InputMap</c> live (so the new key works immediately), via
/// <see cref="KeyBindingsService"/>.
/// </summary>
/// <remarks>
/// Presentation-only (ADR-006): no game rules. Same parchment/wood look as the menu and settings screens. Persistence
/// is <c>settings.cfg</c> (a <c>[keybindings]</c> section), <b>not</b> the game save — ADR-009, no save-format change.
/// While a row is "listening" the next key press is captured as the new binding (Esc cancels the capture); pure
/// modifier keys are ignored so the player can hold Ctrl for a Ctrl-chord. A captured key that collides with another
/// action is accepted (the player's choice) but flagged in the hint line.
/// </remarks>
public partial class KeyBindingsScreen : Control
{
    /// <summary>Emitted when the player presses Back (after the overrides are saved). The host removes the overlay.</summary>
    [Signal]
    public delegate void ClosedEventHandler();

    private const string BackdropPath = "res://assets/freecol/ui/map.jpg";
    private const string DefaultHint = "Click Rebind, then press the new key. Esc cancels.";

    private KeyBindingsModel _model = null!;
    private VBoxContainer _rows = null!;
    private Label _hint = null!;

    // The action currently capturing a key (null = not listening). While set, _Input swallows the next key as the new
    // binding for this action.
    private string? _listeningAction;
    private Button? _listeningButton;

    /// <summary>Builds the look, loads the current bindings, and populates one row per rebindable action.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get();
        if (ResourceLoader.Exists(BackdropPath))
        {
            GetNode<TextureRect>("Background").Texture = GD.Load<Texture2D>(BackdropPath);
        }
        GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        if (ColonyArt.ColonyBorder() is { } border)
        {
            GetNode<NinePatchRect>("Border").Texture = border;
        }

        _rows = GetNode<VBoxContainer>("Panel/VBox/Scroll/Rows");
        _hint = GetNode<Label>("Panel/VBox/Hint");
        _model = KeyBindingsService.Load(); // the current overrides (already applied to the InputMap on boot)

        BuildRows();

        GetNode<Button>("Panel/VBox/Buttons/ResetAllButton").Pressed += OnResetAll;
        GetNode<Button>("Panel/VBox/Buttons/BackButton").Pressed += OnBack;
    }

    /// <summary>(Re)builds the action rows from the model — a label, the current-key value, a Rebind and a Reset button.</summary>
    private void BuildRows()
    {
        foreach (Node child in _rows.GetChildren())
        {
            child.QueueFree();
        }
        foreach (KeyBindingsModel.ActionDef def in KeyBindingsModel.Actions.Where(a => a.Rebindable))
        {
            var row = new HBoxContainer { Name = $"Row_{def.ActionId}" };
            row.AddThemeConstantOverride("separation", 12);

            var label = new Label { Text = def.Label, CustomMinimumSize = new Vector2(220, 0) };
            row.AddChild(label);

            var value = new Label
            {
                Name = "Value",
                Text = KeyBindingsService.Describe(_model.ChordFor(def.ActionId)),
                CustomMinimumSize = new Vector2(110, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            row.AddChild(value);

            var rebind = new Button { Name = "Rebind", Text = "Rebind" };
            string actionId = def.ActionId;
            rebind.Pressed += () => StartListening(actionId, rebind);
            row.AddChild(rebind);

            var reset = new Button { Name = "Reset", Text = "Reset" };
            reset.Pressed += () => ResetOne(actionId);
            row.AddChild(reset);

            _rows.AddChild(row);
        }
    }

    /// <summary>Puts a row into "listening" mode: the next key press (Esc cancels) becomes the new binding for the action.</summary>
    private void StartListening(string actionId, Button button)
    {
        // If another row was already listening, restore its label first.
        CancelListening();
        _listeningAction = actionId;
        _listeningButton = button;
        button.Text = "Press a key…";
        _hint.Text = $"Press the new key for \"{LabelOf(actionId)}\" (Esc to cancel).";
    }

    private void CancelListening()
    {
        if (_listeningButton is { } b)
        {
            b.Text = "Rebind";
        }
        _listeningAction = null;
        _listeningButton = null;
    }

    /// <summary>While listening, captures the next key as the new binding (Esc cancels; pure modifier presses are ignored).</summary>
    public override void _Input(InputEvent @event)
    {
        if (_listeningAction is not { } actionId || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        GetViewport().SetInputAsHandled(); // swallow the capture key so it can't also trigger a hotkey/UI action

        if (key.Keycode == Key.Escape)
        {
            CancelListening();
            _hint.Text = DefaultHint;
            return;
        }
        if (IsModifierOnly(key.Keycode))
        {
            return; // wait for the real key — let the player hold Ctrl for a Ctrl-chord
        }

        var chord = new KeyBindingsModel.KeyChord((long)key.Keycode, key.CtrlPressed);
        KeyBindingsService.Rebind(_model, actionId, chord); // records the override + updates the InputMap live
        CancelListening();
        RefreshValues();

        // Flag a collision with another action (allowed — the player's choice — but worth surfacing).
        string? clash = KeyBindingsModel.Actions
            .Where(a => a.ActionId != actionId && _model.ChordFor(a.ActionId) == chord)
            .Select(a => a.Label)
            .FirstOrDefault();
        _hint.Text = clash is null
            ? $"\"{LabelOf(actionId)}\" is now {KeyBindingsService.Describe(chord)}."
            : $"\"{LabelOf(actionId)}\" is now {KeyBindingsService.Describe(chord)} — also used by \"{clash}\".";
    }

    private void ResetOne(string actionId)
    {
        KeyBindingsService.Rebind(_model, actionId, KeyBindingsModel.DefaultFor(actionId)); // back to default (clears the override)
        RefreshValues();
        _hint.Text = $"\"{LabelOf(actionId)}\" reset to {KeyBindingsService.Describe(KeyBindingsModel.DefaultFor(actionId))}.";
    }

    private void OnResetAll()
    {
        foreach (KeyBindingsModel.ActionDef def in KeyBindingsModel.Actions)
        {
            KeyBindingsService.Rebind(_model, def.ActionId, def.DefaultChord);
        }
        RefreshValues();
        _hint.Text = "All key bindings reset to defaults.";
    }

    /// <summary>Re-reads every row's current-key label from the model (after a rebind / reset).</summary>
    private void RefreshValues()
    {
        foreach (KeyBindingsModel.ActionDef def in KeyBindingsModel.Actions.Where(a => a.Rebindable))
        {
            if (_rows.GetNodeOrNull<Label>($"Row_{def.ActionId}/Value") is { } value)
            {
                value.Text = KeyBindingsService.Describe(_model.ChordFor(def.ActionId));
            }
        }
    }

    /// <summary>Persists the overrides and asks the host to dismiss the screen (via the <see cref="Closed"/> signal).</summary>
    private void OnBack()
    {
        CancelListening();
        KeyBindingsService.Save(_model);
        EmitSignal(SignalName.Closed);
    }

    private static string LabelOf(string actionId) =>
        KeyBindingsModel.Actions.FirstOrDefault(a => a.ActionId == actionId)?.Label ?? actionId;

    // The keys that are modifiers themselves — pressing one alone should not become a binding.
    private static bool IsModifierOnly(Key code) =>
        code is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta or Key.Capslock;
}
