using System;
using CrownAndColony.GameLogic.Persistence;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A reusable save/load slot dialog overlay — five named manual slots (<c>user://saves/slotN.json</c>) plus the
/// read-only <b>autosave</b> entry (<c>user://saves/autosave.json</c>). Each slot is shown as "empty" or
/// "Turn N — &lt;saved date/time&gt;" (the timestamp read from the file's modification time, so no save-header change is
/// needed). In <see cref="Mode.Load"/> empty slots are disabled. Choosing a slot invokes the host's callback (which
/// performs the actual save or load, plus any feedback) and then closes the dialog via <see cref="Closed"/>. Filled
/// slots offer a per-slot <b>delete</b> (with confirmation), and saving over a non-empty manual slot prompts an
/// <b>overwrite confirmation</b>. The <b>autosave</b> entry is loadable but never a save target, so manual saves can
/// never overwrite it. Presentation-only (ADR-006); shares the parchment/wood look via <see cref="ColonyTheme"/>.
/// </summary>
public partial class SaveLoadDialog : Control
{
    /// <summary>The dialog scene, instantiated by the hosts (main menu / pause menu).</summary>
    public const string ScenePath = "res://scenes/SaveLoadDialog.tscn";

    private const int SlotCount = 5;

    /// <summary>Whether the dialog saves to, or loads from, the chosen slot.</summary>
    public enum Mode { Save, Load }

    /// <summary>Emitted when the dialog should be dismissed (Back, or after a slot is chosen).</summary>
    [Signal]
    public delegate void ClosedEventHandler();

    private Mode _mode;
    private Action<string>? _onChoose;

    /// <summary>Applies the look and wires Back; starts hidden.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get();
        GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        if (ColonyArt.ColonyBorder() is { } border)
        {
            GetNode<NinePatchRect>("Border").Texture = border;
        }
        GetNode<Button>("Panel/VBox/BackButton").Pressed += () => EmitSignal(SignalName.Closed);
        Hide();
    }

    /// <summary>The full path of manual save slot <paramref name="index"/> (1-based).</summary>
    public static string SlotPath(int index) => $"{GameController.SavesDir}/slot{index}.json";

    /// <summary>Opens the dialog. <paramref name="onChoose"/> receives the chosen slot's path; the dialog then closes.</summary>
    public void Open(Mode mode, Action<string> onChoose)
    {
        _mode = mode;
        _onChoose = onChoose;
        GetNode<Label>("Panel/VBox/Title").Text = mode == Mode.Save ? "Save Game" : "Load Game";
        BuildSlots();
        Show();
    }

    private void BuildSlots()
    {
        var list = GetNode<VBoxContainer>("Panel/VBox/Slots");
        foreach (Node child in list.GetChildren())
        {
            child.QueueFree();
        }
        for (int i = 1; i <= SlotCount; i++)
        {
            list.AddChild(BuildSlotRow(SlotPath(i), $"Slot {i}", deletable: true, choosableWhenFilled: true));
        }
        // The autosave is a read-only entry: loadable, never a manual-save target (so a manual Save can't overwrite it),
        // and not deletable from here (the game owns it). It only appears once it exists. (86d3f0vb8)
        if (FileAccess.FileExists(GameController.AutosavePath))
        {
            list.AddChild(BuildSlotRow(GameController.AutosavePath, "Autosave",
                deletable: false, choosableWhenFilled: _mode == Mode.Load));
        }
    }

    // Builds one slot row: a main button (load/save) that expands, plus an optional delete button shown only for a
    // filled, deletable slot. The row is an HBoxContainer named "SlotRow"; the main button is its first child.
    private HBoxContainer BuildSlotRow(string path, string name, bool deletable, bool choosableWhenFilled)
    {
        bool filled = FileAccess.FileExists(path);
        var row = new HBoxContainer { Name = "SlotRow" };

        var main = new Button
        {
            Name = "SlotButton",
            Text = SlotLabel(name, path, filled),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            // An empty slot can't be loaded; the autosave entry can't be a save target.
            Disabled = (_mode == Mode.Load && !filled) || (_mode == Mode.Save && !choosableWhenFilled),
        };
        main.Pressed += () => OnSlotPressed(path, filled);
        row.AddChild(main);

        if (filled && deletable)
        {
            var del = new Button { Name = "DeleteButton", Text = "Delete" };
            del.Pressed += () => ConfirmDelete(path, name);
            row.AddChild(del);
        }
        return row;
    }

    // Save over a non-empty manual slot → confirm first (cancellable, no-op on cancel). Load, or save into an empty
    // slot, proceeds immediately. (86d3f0vkg)
    private void OnSlotPressed(string path, bool filled)
    {
        if (_mode == Mode.Save && filled)
        {
            ConfirmOverwrite(path);
        }
        else
        {
            Choose(path);
        }
    }

    private static string SlotLabel(string name, string path, bool filled)
    {
        if (!filled)
        {
            return $"{name} — empty";
        }
        string when = SavedWhen(path);
        try
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            SaveGame save = SaveGame.FromJson(file.GetAsText());
            return $"{name} — Turn {save.Turn}{when}";
        }
        catch (System.Text.Json.JsonException)
        {
            return $"{name} — (unreadable){when}";
        }
    }

    // The "saved date/time" suffix, read from the file's modification time so no save-header/format change is needed
    // (86d3f0vkg). Empty when the engine reports no timestamp (0) — e.g. some virtual filesystems in CI.
    private static string SavedWhen(string path)
    {
        ulong unix = FileAccess.GetModifiedTime(path);
        if (unix == 0)
        {
            return string.Empty;
        }
        DateTime local = DateTimeOffset.FromUnixTimeSeconds((long)unix).LocalDateTime;
        return $"  ({local:yyyy-MM-dd HH:mm})";
    }

    // A confirmation before replacing a non-empty manual slot; Cancel is a no-op (the dialog stays open on the slots).
    private void ConfirmOverwrite(string path) => Confirm(
        "Overwrite save?",
        "This slot already has a saved game. Overwrite it?",
        "Overwrite",
        () => Choose(path));

    // A confirmation before deleting a slot's file; on confirm, remove the file and rebuild the list in place.
    private void ConfirmDelete(string path, string name) => Confirm(
        "Delete save?",
        $"Permanently delete {name}?",
        "Delete",
        () =>
        {
            if (FileAccess.FileExists(path))
            {
                DirAccess.RemoveAbsolute(path);
            }
            BuildSlots(); // refresh: the row becomes "empty" and loses its Delete button
        });

    // Shared modal yes/no confirmation (Godot ConfirmationDialog). Always-process so it works while the tree is paused
    // behind the save dialog; frees itself on either choice. Cancel is a pure no-op.
    private void Confirm(string title, string text, string okText, Action onConfirm)
    {
        var dialog = new ConfirmationDialog
        {
            Title = title,
            DialogText = text,
            OkButtonText = okText,
            CancelButtonText = "Cancel",
            Theme = ColonyTheme.Get(),
        };
        dialog.ProcessMode = ProcessModeEnum.Always;
        dialog.Confirmed += () => { dialog.QueueFree(); onConfirm(); };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void Choose(string path)
    {
        _onChoose?.Invoke(path);
        EmitSignal(SignalName.Closed);
    }
}
