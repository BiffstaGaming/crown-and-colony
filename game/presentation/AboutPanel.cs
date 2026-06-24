using CrownAndColony.GameLogic.App;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The About screen, shown as an overlay by the main menu and the in-game pause menu. It is informational only: it
/// displays the game's name and version (from <see cref="AppInfo"/>), a short GPL v2 disclaimer pointing at the
/// bundled <c>LICENSE</c> and <c>CREDITS.md</c>, a copyright line, the public repository link, and a pointer to the
/// player manual (<c>docs/MANUAL.md</c>). <b>Back</b> (or Esc, handled by the host) emits <see cref="Closed"/> so the
/// host can dismiss it. Same FreeCol map backdrop + parchment/wood framing as the settings screen
/// (presentation-only, ADR-006). FreeCol reference: <c>AboutPanel.java</c>.
/// </summary>
public partial class AboutPanel : Control
{
    /// <summary>Emitted when the player presses Back. The host removes the overlay.</summary>
    [Signal]
    public delegate void ClosedEventHandler();

    private const string BackdropPath = "res://assets/freecol/ui/map.jpg";

    /// <summary>Builds the look, fills in the version-dependent text, and wires the Back button.</summary>
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

        // Title is the product name; the version + the body text are built from AppInfo so they never drift from the
        // compiled-in version. (Authored placeholder text in the scene is overwritten here.)
        GetNode<Label>("Panel/VBox/Title").Text = AppInfo.Name;
        GetNode<Label>("Panel/VBox/Version").Text = $"Version {AppInfo.Version}";
        GetNode<RichTextLabel>("Panel/VBox/Body").Text = BuildBody();

        GetNode<Button>("Panel/VBox/BackButton").Pressed += OnBack;
    }

    // The informational body: GPL v2 disclaimer (→ LICENSE + CREDITS.md), copyright, repo link, manual pointer.
    private static string BuildBody() =>
        $"{AppInfo.Name} is free software, licensed under the GNU General Public License, version 2 (GPL v2). " +
        "It comes with absolutely no warranty. See the bundled [b]LICENSE[/b] file for the full terms and " +
        "[b]CREDITS.md[/b] for third-party asset attributions.\n\n" +
        "Copyright (C) 2026 the Crown & Colony contributors.\n\n" +
        $"Source code: {AppInfo.RepositoryUrl}\n\n" +
        "Player manual: see [b]docs/MANUAL.md[/b].";

    /// <summary>Asks the host to dismiss the screen (via the <see cref="Closed"/> signal).</summary>
    private void OnBack() => EmitSignal(SignalName.Closed);
}
