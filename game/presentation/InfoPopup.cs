using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A reusable modal information popup — a dimmed backdrop + a parchment panel with a title, a wrapped message, and an
/// OK button — that floats over whatever screen summoned it. For one-off event / information notices ("Game saved",
/// "No saved games", …). It emits <see cref="Closed"/> when dismissed; the host (or the static <see cref="Show"/>
/// helper) removes it. Presentation-only (ADR-006); shares the parchment/wood look via <see cref="ColonyTheme"/>.
/// </summary>
public partial class InfoPopup : Control
{
    /// <summary>The popup scene, instantiated by <see cref="Show"/>.</summary>
    public const string ScenePath = "res://scenes/InfoPopup.tscn";

    /// <summary>Emitted when the player dismisses the popup (OK).</summary>
    [Signal]
    public delegate void ClosedEventHandler();

    /// <summary>Applies the look and wires OK to <see cref="Closed"/>.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get();
        GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        if (ColonyArt.ColonyBorder() is { } border)
        {
            GetNode<NinePatchRect>("Border").Texture = border;
        }
        GetNode<Button>("Panel/VBox/OkButton").Pressed += () => EmitSignal(SignalName.Closed);
    }

    /// <summary>Sets the popup's heading and body text. Call once the popup is in the tree (e.g. via <see cref="Show"/>).</summary>
    public void Configure(string title, string message)
    {
        GetNode<Label>("Panel/VBox/Title").Text = title;
        GetNode<Label>("Panel/VBox/Message").Text = message;
    }

    /// <summary>
    /// Convenience: instantiates the popup over <paramref name="host"/>, shows the given text, and frees it again when
    /// the player presses OK. Returns the instance so callers can also observe <see cref="Closed"/>.
    /// </summary>
    public static InfoPopup Show(Node host, string title, string message)
    {
        var popup = GD.Load<PackedScene>(ScenePath).Instantiate<InfoPopup>();
        popup.Closed += popup.QueueFree;
        host.AddChild(popup); // runs _Ready, so the labels exist for Configure
        popup.Configure(title, message);
        return popup;
    }
}
