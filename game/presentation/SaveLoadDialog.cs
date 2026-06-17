using System;
using CrownAndColony.GameLogic.Persistence;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A reusable save/load slot dialog overlay — five named slots (<c>user://saves/slotN.json</c>), each shown as
/// "empty" or "Turn N". In <see cref="Mode.Load"/> empty slots are disabled. Choosing a slot invokes the host's
/// callback (which performs the actual save or load, plus any feedback) and then closes the dialog via
/// <see cref="Closed"/>. Presentation-only (ADR-006); shares the parchment/wood look via <see cref="ColonyTheme"/>.
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

    /// <summary>The full path of save slot <paramref name="index"/> (1-based).</summary>
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
            string path = SlotPath(i);
            bool filled = FileAccess.FileExists(path);
            var button = new Button
            {
                Text = SlotLabel(i, path, filled),
                Disabled = _mode == Mode.Load && !filled, // an empty slot can't be loaded
            };
            button.Pressed += () => Choose(path);
            list.AddChild(button);
        }
    }

    private static string SlotLabel(int slot, string path, bool filled)
    {
        if (!filled)
        {
            return $"Slot {slot} — empty";
        }
        try
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            SaveGame save = SaveGame.FromJson(file.GetAsText());
            return $"Slot {slot} — Turn {save.Turn}";
        }
        catch (System.Text.Json.JsonException)
        {
            return $"Slot {slot} — (unreadable)";
        }
    }

    private void Choose(string path)
    {
        _onChoose?.Invoke(path);
        EmitSignal(SignalName.Closed);
    }
}
