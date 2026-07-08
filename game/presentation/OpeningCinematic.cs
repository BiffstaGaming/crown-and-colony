using System.Collections.Generic;
using CrownAndColony.GameLogic.Specification;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The skippable <b>opening cinematic</b> shown when the player starts a <b>new</b> game from the main menu, after they
/// confirm the New-Game setup and before the game board appears (86d3fq1kf). It is a short, atmospheric title sequence:
/// a few narrative "story" beats — for classic, the King's charter, the ocean crossing, the landfall that set the 1492
/// expedition scene — each fading in and out over the antique New-World map backdrop and the parchment/wood skin the
/// menu already uses. The beats are <b>variant-aware</b>: the host injects the selected variant's
/// <see cref="GameVariant.OpeningBeats"/> via <see cref="SetBeats"/> before <see cref="Play"/> (the Australian
/// Federation supplies its own 1788→1901 arc), defaulting to the classic 1492 beats when none are set. It plays no audio
/// (the war/menu music track is a separate task) and adds no new art (licensing) — text on parchment with a tasteful
/// cross-fade only.
/// </summary>
/// <remarks>
/// <para><b>Presentation-only (ADR-006).</b> The cinematic owns no game rules: it shows text and then raises
/// <see cref="Finished"/> so the host (the main menu) proceeds to the existing new-game boot path. It never mutates
/// game state, threads no <c>Pending*</c> statics, and is deliberately kept <em>out</em> of
/// <see cref="GameController.StartNewGame"/> — the fast path many L3 tests take stays intact. It is an optional front
/// step only on the interactive New-Game path (main menu → New-Game dialog → <em>cinematic</em> → game).</para>
/// <para><b>Determinism (ADR-009).</b> The beats are a fixed, ordered list with fixed timings — no <c>Random</c> /
/// <c>GD.Randf()</c>. The same new game always shows the same sequence.</para>
/// <para><b>Never traps the player.</b> Any click, Esc, or the on-screen "Skip ▸" button ends the cinematic
/// immediately and raises <see cref="Finished"/>. A player who dislikes intros can also turn it off entirely via the
/// Settings "Play intro" toggle (<see cref="GameLogic.App.SettingsModel.PlayIntro"/>), in which case the interactive
/// path never instantiates this at all.</para>
/// </remarks>
public partial class OpeningCinematic : Control
{
    /// <summary>Emitted once when the cinematic ends — whether it played through, was skipped, or was clicked past. The host proceeds to the game exactly once (further advances are ignored after this fires).</summary>
    [Signal]
    public delegate void FinishedEventHandler();

    /// <summary>FreeCol's antique New-World map (GPL v2 — see <c>assets/freecol/PROVENANCE.md</c>), reused as the cinematic backdrop (the same image the main menu shows), so no new asset is added.</summary>
    private const string BackdropPath = "res://assets/freecol/ui/map.jpg";

    /// <summary>Seconds a beat takes to fade in, seconds it holds fully visible, and seconds it takes to fade out. Short enough that the whole sequence is a handful of seconds; the player can skip at any time.</summary>
    private const double FadeIn = 0.8;
    private const double Hold = 2.6;
    private const double FadeOut = 0.7;

    /// <summary>
    /// The narrative beats, in order — injected by the host from the selected variant so the intro is variant-aware
    /// (classic's 1492 expedition scene; the Australian Federation's 1788→1901 arc). Defaults to the default variant's
    /// beats (<see cref="GameVariants.Default"/> → the classic 1492 beats) so callers/tests that never set beats see the
    /// original sequence unchanged. A fixed ordered list so the sequence is deterministic (ADR-009).
    /// </summary>
    private IReadOnlyList<string> _beats = GameVariants.Default.OpeningBeats;

    /// <summary>The current beat index; advanced by the tween chain or by a click/Esc. Guards the one-shot <see cref="Finished"/>.</summary>
    private int _beatIndex;
    private bool _finished;
    private Label _beatLabel = null!;
    private Tween? _tween;

    /// <summary>Builds the look (backdrop + parchment panel + a large centred beat label + a Skip button) and starts the first beat's fade-in. Starts hidden until <see cref="Play"/> is called so a host can add it before showing it.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get();
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the whole surface takes clicks (click-anywhere-to-advance)
        GetViewport().SizeChanged += FillViewport; // keep covering the whole window when it resizes

        // Backdrop: the antique map, darkened by a translucent vignette so the cream beat text reads clearly over it
        // (the same image + dim treatment the menu uses). A missing asset (CI before import) just leaves the dim fill.
        var background = new TextureRect
        {
            Name = "Background",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        if (ResourceLoader.Exists(BackdropPath))
        {
            background.Texture = GD.Load<Texture2D>(BackdropPath);
        }
        AddChild(background);

        var vignette = new ColorRect
        {
            Name = "Vignette",
            Color = new Color(0.03f, 0.05f, 0.09f, 0.62f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        vignette.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(vignette);

        // The beat text sits on a centred parchment panel (the shared skin), large and centred, wrapped so long
        // sentences break tidily. Alpha is driven by the tween (starts fully transparent → fades in).
        var centre = new CenterContainer { Name = "Centre", MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        // A dark translucent backing (NOT the light parchment skin) so the cream beat text reads clearly — cream on
        // parchment was near-invisible (86d3jy0rn). Dark box + cream text is high-contrast and reads as a title card.
        var panel = new PanelContainer { Name = "Panel", MouseFilter = MouseFilterEnum.Ignore };
        var backing = new StyleBoxFlat { BgColor = new Color(0.04f, 0.05f, 0.09f, 0.74f) };
        backing.SetContentMarginAll(30);
        backing.SetCornerRadiusAll(10);
        backing.SetBorderWidthAll(2);
        backing.BorderColor = new Color(0.62f, 0.5f, 0.32f, 0.55f); // faint warm gilt edge, matching the wood palette
        panel.AddThemeStyleboxOverride("panel", backing);
        panel.CustomMinimumSize = new Vector2(620, 0);
        centre.AddChild(panel);

        _beatLabel = new Label
        {
            Name = "BeatLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(560, 160),
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0), // start invisible; the tween fades the first beat in
        };
        _beatLabel.AddThemeFontSizeOverride("font_size", 24);
        _beatLabel.AddThemeColorOverride("font_color", new Color("#F3E4C3")); // warm cream, matching the HUD strip
        _beatLabel.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.7f));
        _beatLabel.AddThemeConstantOverride("outline_size", 4);
        panel.AddChild(_beatLabel);

        // The always-visible "Skip ▸" affordance in the bottom-right, plus a hint that a click/Esc also skips.
        var skip = new Button { Name = "SkipButton", Text = "Skip ▸" };
        skip.SetAnchorsPreset(LayoutPreset.BottomRight);
        skip.OffsetLeft = -132;
        skip.OffsetTop = -60;
        skip.OffsetRight = -20;
        skip.OffsetBottom = -20;
        skip.GrowHorizontal = GrowDirection.Begin;
        skip.GrowVertical = GrowDirection.Begin;
        skip.Pressed += Skip;
        AddChild(skip);

        var hint = new Label
        {
            Name = "Hint",
            Text = "Click or press Esc to skip",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0.7f),
        };
        hint.SetAnchorsPreset(LayoutPreset.CenterBottom);
        hint.OffsetTop = -48;
        hint.OffsetBottom = -20;
        hint.OffsetLeft = -160;
        hint.OffsetRight = 160;
        hint.GrowHorizontal = GrowDirection.Both;
        hint.GrowVertical = GrowDirection.Begin;
        AddChild(hint);

        Hide();
    }

    /// <summary>
    /// Sets the narrative beats to show, in order — call before <see cref="Play"/> to make the intro variant-aware
    /// (the host passes the selected variant's <see cref="GameVariant.OpeningBeats"/>). A null or empty list is
    /// ignored, leaving the classic default in place (so the cinematic never plays an empty sequence). Presentation-only:
    /// the beats are display text; the machinery/timings are unchanged (ADR-018) and remain deterministic (ADR-009).
    /// </summary>
    public void SetBeats(IReadOnlyList<string>? beats)
    {
        if (beats is { Count: > 0 })
        {
            _beats = beats;
        }
    }

    /// <summary>
    /// Starts the cinematic: shows the surface and plays the first beat. Call after adding the node to the tree. Safe
    /// to call once; the sequence then runs to <see cref="Finished"/> or is cut short by a click / Esc / Skip.
    /// </summary>
    public void Play()
    {
        Show();
        // Cover the whole window before the first beat. This overlay is added + shown in the same frame it is built, so
        // its FullRect size hasn't been propagated from the parent yet — without this the backdrop/vignette and the
        // centred text card lay out inside a stale, smaller rect pinned top-left (the menu shows through beside it, and
        // the card isn't centred — 86d3jy0rn). Forced now and again deferred once the layout has settled.
        FillViewport();
        Callable.From(FillViewport).CallDeferred();
        GrabFocus(); // so Esc (ui_cancel) reaches this control's _UnhandledKeyInput even over the freed menu
        PlayBeat(0);
    }

    /// <summary>Pins this overlay to the whole viewport with an explicit rect (top-left anchor + size), so the backdrop,
    /// vignette and centred card always fill/centre on the real window regardless of parent-size propagation timing.</summary>
    private void FillViewport()
    {
        SetAnchorsPreset(LayoutPreset.TopLeft);
        Position = Vector2.Zero;
        Size = GetViewportRect().Size;
    }

    /// <summary>Fades beat <paramref name="index"/> in, holds it, fades it out, then chains to the next — or ends the cinematic after the last beat. A no-op once <see cref="Finished"/> has fired.</summary>
    private void PlayBeat(int index)
    {
        if (_finished)
        {
            return;
        }
        if (index >= _beats.Count)
        {
            Finish();
            return;
        }
        _beatIndex = index;
        _beatLabel.Text = _beats[index];
        _beatLabel.Modulate = new Color(1, 1, 1, 0);

        _tween?.Kill();
        _tween = CreateTween();
        _tween.TweenProperty(_beatLabel, "modulate:a", 1.0, FadeIn);
        _tween.TweenInterval(Hold);
        _tween.TweenProperty(_beatLabel, "modulate:a", 0.0, FadeOut);
        // Chain to the next beat when this one has fully faded out (the tween is deterministic; no RNG).
        _tween.TweenCallback(Callable.From(() => PlayBeat(index + 1)));
    }

    /// <summary>
    /// A click anywhere <b>advances</b> to the next beat (skipping the rest of the current one); on the last beat it
    /// ends the cinematic. This keeps a click responsive without forcing the whole thing to end on the first tap —
    /// but the explicit Skip button and Esc end it outright (see <see cref="Skip"/> / <see cref="_UnhandledKeyInput"/>).
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            AcceptEvent();
            _tween?.Kill();
            PlayBeat(_beatIndex + 1);
        }
    }

    /// <summary>Esc (Godot's <c>ui_cancel</c>) skips the whole cinematic straight into the game — the player is never trapped.</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Skip();
        }
    }

    /// <summary>Ends the cinematic immediately (the Skip button / Esc): kills any running tween and raises <see cref="Finished"/> once.</summary>
    public void Skip()
    {
        _tween?.Kill();
        Finish();
    }

    /// <summary>Raises <see cref="Finished"/> exactly once and stops accepting further advances.</summary>
    private void Finish()
    {
        if (_finished)
        {
            return;
        }
        _finished = true;
        EmitSignal(SignalName.Finished);
    }
}
