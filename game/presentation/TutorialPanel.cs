using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The <b>guided-intro tutorial card</b> (ClickUp <c>86d3fq1h9</c>): a small, dismissible parchment card that shows one
/// contextual tip at a time as a new player reaches each early milestone (welcome → ashore → found a colony → work the
/// colony → end turn). It is the friendly echo of Col1's tutorial advisor and FreeCol's tutorial messages, kept bounded
/// and non-nagging — one tip per milestone, each shown once.
/// <para>
/// Pure presentation (ADR-006): it renders a <see cref="TutorialStep"/> it is <b>handed</b> by
/// <see cref="GameController"/> (which reads the step from <see cref="TutorialService"/>); it holds no game state, runs no
/// rule logic, and never mutates the game or the save. It is built entirely in code (no scene file), like
/// <see cref="AdvisorPanel"/>, so the host can add it anywhere and the tests can drive it directly. Hidden by default;
/// <see cref="ShowStep"/> fills and reveals it. "Got it" hides it and raises <see cref="GotIt"/> (advance one step);
/// "Skip tutorial" hides it and raises <see cref="SkipRequested"/> (turn the whole tutorial off).
/// </para>
/// </summary>
public partial class TutorialPanel : PanelContainer
{
    /// <summary>The node name of the card's title label (used by tests to read the current step's heading).</summary>
    public const string TitleName = "TutorialTitle";

    /// <summary>The node name of the card's body label (used by tests to read the current step's instruction).</summary>
    public const string BodyName = "TutorialBody";

    /// <summary>The node name of the "Got it" (advance) button.</summary>
    public const string GotItButtonName = "GotItButton";

    /// <summary>The node name of the "Skip tutorial" (disable) button.</summary>
    public const string SkipButtonName = "SkipButton";

    /// <summary>Emitted when the player presses "Got it" — advance to the next tutorial step.</summary>
    [Signal]
    public delegate void GotItEventHandler();

    /// <summary>Emitted when the player presses "Skip tutorial" — turn the whole guided intro off (persisted preference).</summary>
    [Signal]
    public delegate void SkipRequestedEventHandler();

    private Label _title = null!;
    private Label _body = null!;

    /// <summary>The kind of the step currently displayed (or <c>null</c> when the card is hidden). Exposed for tests/observers.</summary>
    public TutorialStepKind? CurrentKind { get; private set; }

    /// <summary>Builds the card's node tree (title, body, "Got it" / "Skip tutorial" buttons) and applies the parchment look.</summary>
    public override void _Ready()
    {
        Name = "TutorialPanel";
        Theme = ColonyTheme.GetInGame();
        AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        Visible = false;
        // A readable card, wider than the corner advisor hint so the two-sentence tips wrap tidily; capped so it never
        // dominates the screen (it sits top-centre, clear of the advisor card top-left and the bottom HUD).
        CustomMinimumSize = new Vector2(420, 0);

        var vbox = new VBoxContainer { Name = "VBox" };
        vbox.AddThemeConstantOverride("separation", 8);
        AddChild(vbox);

        _title = new Label
        {
            Name = TitleName,
            Text = "Tutorial",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _title.AddThemeFontSizeOverride("font_size", 20);
        vbox.AddChild(_title);

        _body = new Label
        {
            Name = BodyName,
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        vbox.AddChild(_body);

        var buttons = new HBoxContainer { Name = "Buttons" };
        buttons.AddThemeConstantOverride("separation", 12);
        buttons.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(buttons);

        var gotIt = new Button { Name = GotItButtonName, Text = "Got it" };
        gotIt.Pressed += () => EmitSignal(SignalName.GotIt);
        buttons.AddChild(gotIt);

        var skip = new Button { Name = SkipButtonName, Text = "Skip tutorial" };
        skip.Pressed += () => EmitSignal(SignalName.SkipRequested);
        buttons.AddChild(skip);
    }

    /// <summary>
    /// Fills the card with <paramref name="step"/>'s title + body and reveals it. Re-calling replaces the content, so the
    /// host can refresh it whenever the tutorial advances.
    /// </summary>
    /// <param name="step">The step to display, chosen by <see cref="TutorialService"/>.</param>
    public void ShowStep(TutorialStep step)
    {
        _title.Text = step.Title;
        _body.Text = step.Body;
        CurrentKind = step.Kind;
        Visible = true;
    }

    /// <summary>Hides the card (nothing to show — the tutorial is complete, disabled, or between steps). Leaves no content behind.</summary>
    public void HideCard()
    {
        Visible = false;
        CurrentKind = null;
    }
}
