using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The <b>Australian-Federation</b> screen (Phase-4a, ADR-021; design doc
/// <c>docs/australian_federation_mode_md/05_Federation_Victory_System.md</c> and
/// <c>docs/systems/federation-victory.md</c>): the human's dashboard for the Federation victory path — the six colonies'
/// Federation Support, the movement's phase, the banked Convention Points, and the two player-driven actions that walk
/// the loop (<b>Call the Federation Convention</b> and <b>Put Federation to a referendum</b>). Opened from an
/// Australia-only HUD button; the button and this panel exist only when the ruleset enables the Federation victory
/// (<see cref="Specification.Ruleset.VictoryFederation"/> — false for classic, so the classic HUD is untouched).
///
/// <para>Pure presentation (ADR-006): every reading comes from a <see cref="Game"/> oracle — per-region support
/// (<see cref="Game.RegionSupportSummary"/>), the campaign stage (<see cref="Game.CurrentFederationStage"/> — the six
/// named design phases), the points
/// (<see cref="Game.ConventionPoints"/>), each region's own referendum target (<see cref="Game.ReferendumTargetFor"/>) and the two
/// action gates (<see cref="Game.CheckCallConvention"/> / <see cref="Game.CheckPutToReferendum"/>). The only mutations are
/// the forwarded commands (<see cref="Game.CallConvention"/> / <see cref="Game.HoldReferendum"/>); all the rules
/// (thresholds, the seeded referendum roll, the win) live in GameLogic. Built entirely in code (no scene file), mirroring
/// <see cref="AdvisorPanel"/>; the signal-safe rebuild idiom (<c>RemoveChild</c> then <c>QueueFree</c>) refills the body
/// after an action. The six region labels are hard-coded Australian names (the panel is Australia-only).</para>
///
/// <para><b>Designed treatment (WS2.7).</b> On the shared parchment/wood look, the panel adds real hierarchy: the title +
/// support header use the theme's <c>ColonyTitle</c>/<c>SectionHeader</c> variations; a five-step <b>phase tracker</b> lights
/// the current stage of the road to Federation; each colony's support is a <b>styled gauge</b> (a recessed parchment trough
/// with a support-coloured fill — barn-red / ochre / federation-green by how it sits against the referendum bar — and a gold
/// <b>threshold marker</b> at the bar); and the Convention Points sit in a gold-edged chip. Colours are re-declared locally
/// from <see cref="ColonyTheme"/>'s palette (which is private), the established <see cref="EuropePanel"/> pattern.</para>
/// </summary>
public partial class FederationPanel : PanelContainer
{
    /// <summary>The node name of the dynamic body container (used by tests to locate rendered rows).</summary>
    public const string BodyName = "Body";

    // ── Palette — re-declared locally from ColonyTheme (whose palette is private), the EuropePanel precedent. Keep in sync. ──
    private static readonly Color Parchment = Color.FromHtml("e8d9b0");
    private static readonly Color ParchmentDark = Color.FromHtml("d9c290");
    private static readonly Color ParchmentEdge = Color.FromHtml("c2a86a");
    private static readonly Color WoodDark = Color.FromHtml("4a2e1a");
    private static readonly Color WoodMid = Color.FromHtml("7a4f30");
    private static readonly Color Ink = Color.FromHtml("2b1d10");
    private static readonly Color Gold = Color.FromHtml("c9a24b");
    private static readonly Color TextOnWood = Color.FromHtml("f2e2c2");

    // Support-gauge fill colours (how the colony sits against the referendum bar): barn-red low, ochre climbing, green ready.
    private static readonly Color SupportLow = new(0.60f, 0.20f, 0.14f);   // deep barn-red — well short of the bar
    private static readonly Color SupportMid = new(0.78f, 0.58f, 0.24f);   // ochre/amber — climbing toward the bar
    private static readonly Color SupportReady = new(0.32f, 0.48f, 0.24f); // muted federation-green — at/over the referendum bar

    /// <summary>The six narrative stages of the road to Federation (design doc 05), in journey order, for the step tracker (WS3.8).</summary>
    private static readonly (FederationStage Stage, string Label)[] StageSteps =
    {
        (FederationStage.ColonialMaturity, "Maturity"),
        (FederationStage.FederationMovement, "Movement"),
        (FederationStage.ConventionProcess, "Convention"),
        (FederationStage.DraftConstitution, "Constitution"),
        (FederationStage.Referendum, "Referendum"),
        (FederationStage.Commonwealth, "Commonwealth"),
    };

    /// <summary>The six colony regions' display names, keyed by their <c>model.region.*</c> key (Australia-only; hard-coded).</summary>
    private static readonly IReadOnlyDictionary<string, string> RegionDisplayNames = new Dictionary<string, string>
    {
        ["model.region.newSouthWales"] = "New South Wales",
        ["model.region.victoria"] = "Victoria",
        ["model.region.queensland"] = "Queensland",
        ["model.region.southAustralia"] = "South Australia",
        ["model.region.tasmania"] = "Tasmania",
        ["model.region.westernAustralia"] = "Western Australia",
    };

    private Game _game = null!;
    private Action _onChange = () => { };
    private Label _title = null!;
    private VBoxContainer _body = null!;

    /// <summary>Builds the panel's fixed shell (title + dynamic body + Close) and applies the parchment look. Hidden by default.</summary>
    public override void _Ready()
    {
        Name = "FederationPanel";
        Theme = ColonyTheme.GetInGame();
        AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        Visible = false;
        CustomMinimumSize = new Vector2(440, 0);
        // Centre + height-cap the panel: the feature-rich body (phase tracker, drafting, six support gauges, actions) can
        // exceed the viewport, so the body scrolls inside a capped, centred shell with the Title + Close pinned outside it —
        // otherwise Close is pushed off-screen (the godot-overlay-modal-pattern). Re-laid-out on show + on viewport resize.
        GetViewport().SizeChanged += Relayout;

        var vbox = new VBoxContainer { Name = "VBox" };
        vbox.AddThemeConstantOverride("separation", 8);
        AddChild(vbox);

        _title = new Label
        {
            Name = "FederationTitle",
            Text = "The Road to Federation",
            HorizontalAlignment = HorizontalAlignment.Center,
            ThemeTypeVariation = "ColonyTitle", // the theme's display-title variation (gold halo, large)
        };
        vbox.AddChild(_title);
        vbox.AddChild(new HSeparator());

        _body = new VBoxContainer { Name = BodyName, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 8);
        var scroll = new ScrollContainer
        {
            Name = "Scroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, // vertical-only; the body never scrolls sideways
        };
        scroll.AddChild(_body);
        vbox.AddChild(scroll);

        vbox.AddChild(new HSeparator());
        var close = new Button { Name = "CloseButton", Text = "Close" };
        close.Pressed += Hide;
        vbox.AddChild(close);
    }

    /// <summary>The non-scrolling shell height (title + two separators + Close + the VBox separations) — the body height is added on top, capped at the viewport.</summary>
    private const float PanelChromeHeight = 120f;

    /// <summary>
    /// Centres the panel over the viewport and caps its height so the body scrolls rather than pushing Close off-screen
    /// (the code-built-overlay contract). Sizes to the body's content plus the fixed chrome when that fits, else to the
    /// viewport (leaving the scroll to take the overflow). Called deferred after each render + on viewport resize.
    /// </summary>
    private void Relayout()
    {
        Vector2 vp = GetViewportRect().Size;
        float width = Mathf.Min(460f, Mathf.Max(440f, vp.X - 24f));
        float height = Mathf.Min(_body.GetCombinedMinimumSize().Y + PanelChromeHeight, vp.Y - 24f);
        Size = new Vector2(width, height);
        Position = new Vector2(Mathf.Max(0f, (vp.X - width) / 2f), Mathf.Max(12f, (vp.Y - height) / 2f));
    }

    /// <summary>
    /// Opens the Federation screen for the human on <paramref name="game"/>, rendering the current state and actions.
    /// <paramref name="onChange"/> runs after a successful action (Call Convention / referendum) so the host refreshes —
    /// a carried referendum wins the game, so the HUD/end-turn state can change.
    /// </summary>
    /// <param name="game">The running game (its Federation oracles are read).</param>
    /// <param name="onChange">Host refresh, run after a successful action.</param>
    public void Open(Game game, Action onChange)
    {
        _game = game;
        _onChange = onChange;
        Render();
        Show();
    }

    /// <summary>The design phase number + name for the era indicator (WS3.8 — doc 05's six named phases).</summary>
    private static (int Number, string Name) StageHeading(FederationStage stage) => stage switch
    {
        FederationStage.ColonialMaturity => (1, "Colonial Maturity"),
        FederationStage.FederationMovement => (2, "Federation Movement"),
        FederationStage.ConventionProcess => (3, "Convention Process"),
        FederationStage.DraftConstitution => (4, "Draft Constitution"),
        FederationStage.Referendum => (5, "Referendums"),
        FederationStage.Commonwealth => (6, "Commonwealth"),
        _ => (0, ""),
    };

    /// <summary>The current stage's player-facing narrative sentence (WS3.8), read from the <see cref="Game.CurrentFederationStage"/> oracle. The Referendum stage folds in design Phase 6 (Failure and Retry) after a failed vote.</summary>
    private string StageNarrative(FederationStage stage) => stage switch
    {
        FederationStage.ColonialMaturity => "The colonies are growing — nationhood is still a distant idea.",
        FederationStage.FederationMovement => "The Federation movement is astir — leagues and orators press the cause across the colonies.",
        FederationStage.ConventionProcess => "The Federation Convention has been called — the constitution is being drafted.",
        FederationStage.DraftConstitution => "The draft constitution is complete — a referendum may be held.",
        FederationStage.Referendum => _game.ReferendumAttempts > 0 && !_game.ReferendumCarried
            ? "The last referendum failed — the movement must rebuild support before trying again."
            : "A Federation referendum is under way.",
        FederationStage.Commonwealth => "The Commonwealth of Australia is proclaimed — Federation is achieved!",
        _ => "",
    };

    /// <summary>(Re)builds the dynamic body: the phase tracker + narrative, Convention Points, the six styled support gauges, and the context actions.</summary>
    private void Render()
    {
        ClearBody();

        FederationStage stage = _game.CurrentFederationStage; // WS3.8: the design's six named phases, mapped from the mechanical state
        _body.AddChild(BuildPhaseTracker(stage));

        // Era indicator: names the current design phase ("Phase 2 · Federation Movement") above the narrative line.
        (int number, string name) = StageHeading(stage);
        if (number > 0)
        {
            _body.AddChild(new Label
            {
                Name = "StageName",
                Text = $"Phase {number} · {name}",
                HorizontalAlignment = HorizontalAlignment.Center,
                ThemeTypeVariation = "SectionHeader", // the theme's section-header variation (gold, larger)
            });
        }

        var phaseLine = new Label
        {
            Name = "PhaseLine",
            Text = StageNarrative(stage),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        phaseLine.AddThemeColorOverride("font_color", new Color(Ink, 0.82f)); // slightly recessed under the tracker
        _body.AddChild(phaseLine);

        _body.AddChild(BuildConventionPointsChip(_game.ConventionPoints));

        // WS3.3: while a convention is drafting the constitution, show the Draft-Constitution section — a progress gauge
        // toward the ≥80% completion gate and one row per curated clause (name, cost, and a Draft button / drafted tick).
        if (_game.FederationPhase == FederationPhase.ConventionCalled)
        {
            _body.AddChild(new HSeparator());
            BuildDraftConstitution();
        }

        _body.AddChild(new HSeparator());

        _body.AddChild(new Label
        {
            Name = "SupportHeader",
            Text = "Federation Support by colony",
            ThemeTypeVariation = "SectionHeader", // the theme's section-header variation
        });
        foreach ((string regionKey, int supportPercent) in _game.RegionSupportSummary())
        {
            AddRegionRow(regionKey, supportPercent);
        }
        _body.AddChild(new HSeparator());

        AddActions();

        Callable.From(Relayout).CallDeferred(); // re-cap + re-centre once the rebuilt body has laid out to its real height
    }

    /// <summary>
    /// The six-step phase tracker (WS3.8 — doc 05's named stages): one chip per stage of the road to Federation, in journey
    /// order. The stage the movement has reached is lit (wood face + gold edge, cream text); completed stages read as filled
    /// dark wood; stages still ahead sit as dim recessed parchment — so the player sees at a glance how far the movement has
    /// come. Driven by <see cref="Game.CurrentFederationStage"/> (which splits Maturity vs Movement by the calendar).
    /// </summary>
    private Control BuildPhaseTracker(FederationStage current)
    {
        int currentIndex = Array.FindIndex(StageSteps, s => s.Stage == current);
        var tracker = new HBoxContainer { Name = "PhaseTracker", Alignment = BoxContainer.AlignmentMode.Center };
        tracker.AddThemeConstantOverride("separation", 4);

        for (int i = 0; i < StageSteps.Length; i++)
        {
            bool active = i == currentIndex;
            bool done = i < currentIndex;
            Color bg = active ? WoodMid : done ? WoodDark : ParchmentDark;
            Color border = active ? Gold : ParchmentEdge;

            var chip = new PanelContainer { Name = $"Phase_{StageSteps[i].Stage}" };
            chip.AddThemeStyleboxOverride("panel", Flat(bg, border, active ? 2 : 1, 4, 6, 3));

            var label = new Label { Text = StageSteps[i].Label, HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeColorOverride("font_color", active || done ? TextOnWood : new Color(Ink, 0.62f)); // future stages dimmed but legible
            label.AddThemeFontSizeOverride("font_size", 13);
            chip.AddChild(label);
            tracker.AddChild(chip);
        }
        return tracker;
    }

    /// <summary>The Convention Points readout — a compact gold-edged parchment chip with a caption and a large point value,
    /// giving the banked national points a designed emphasis rather than a line of plain text. Named <c>ConventionPoints</c>.</summary>
    private Control BuildConventionPointsChip(int points)
    {
        var chip = new PanelContainer { Name = "ConventionPoints", SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
        chip.AddThemeStyleboxOverride("panel", Flat(ParchmentDark, Gold, 1, 5, 12, 4));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        var caption = new Label { Text = "Convention Points", VerticalAlignment = VerticalAlignment.Center };
        caption.AddThemeColorOverride("font_color", new Color(Ink, 0.85f));
        row.AddChild(caption);

        var value = new Label { Text = points.ToString(), VerticalAlignment = VerticalAlignment.Center };
        value.AddThemeColorOverride("font_color", WoodDark);
        value.AddThemeFontSizeOverride("font_size", 22);
        row.AddChild(value);

        chip.AddChild(row);
        return chip;
    }

    /// <summary>
    /// Adds one region's row: its Australian name, a styled support <b>gauge</b> (recessed parchment trough, support-coloured
    /// fill, gold referendum-bar marker), and the percentage. The gauge is a <see cref="ProgressBar"/> named <c>Bar</c> (as
    /// before) with <c>background</c>/<c>fill</c> stylebox overrides; a colony at/over the referendum bar reads green.
    /// </summary>
    private void AddRegionRow(string regionKey, int supportPercent)
    {
        string display = RegionDisplayNames.GetValueOrDefault(regionKey, regionKey);
        int target = _game.ReferendumTargetFor(regionKey); // WS3.2: this region's own referendum bar (NSW 57 … Tas/Vic 94), not a uniform 50
        var row = new HBoxContainer { Name = $"Region_{ShortKey(regionKey)}" };
        row.AddThemeConstantOverride("separation", 10);

        row.AddChild(new Label
        {
            Name = "Name",
            Text = display,
            CustomMinimumSize = new Vector2(140, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var bar = new ProgressBar
        {
            Name = "Bar",
            MinValue = 0,
            MaxValue = 100,
            Value = supportPercent,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(180, 18),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        Color fill = SupportColor(supportPercent, target);
        bar.AddThemeStyleboxOverride("background", Flat(ParchmentDark, ParchmentEdge, 1, 4)); // recessed trough (BuildingCell recipe)
        bar.AddThemeStyleboxOverride("fill", Flat(fill, fill.Darkened(0.25f), 1, 4));

        // The referendum bar: a thin gold marker at this region's own target %, anchored as a fraction of the gauge width so
        // it stays on the bar however wide the row grows. A colony's fill reaching the marker (and turning green) is
        // "referendum-ready" — so the marker sits at NSW 57% but Tasmania/Victoria 94% (WS3.2 per-region targets).
        float threshold = target / 100f;
        var marker = new ColorRect
        {
            Name = "ReferendumMark",
            Color = new Color(Gold, 0.95f),
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = threshold,
            AnchorRight = threshold,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            OffsetLeft = -1f,
            OffsetRight = 1f,
            OffsetTop = 2f,
            OffsetBottom = -2f,
        };
        bar.AddChild(marker);
        row.AddChild(bar);

        row.AddChild(new Label
        {
            Name = "Percent",
            Text = $"{supportPercent}%",
            CustomMinimumSize = new Vector2(42, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _body.AddChild(row);
    }

    /// <summary>The support-gauge fill colour for a support percentage, measured against that region's own referendum
    /// <paramref name="bar"/> (WS3.2): barn-red well below, ochre approaching (from half the bar up), federation-green at or
    /// over the bar. A region with a 94% target (Tasmania/Victoria) stays red/ochre far longer than a 57% one (NSW).</summary>
    private static Color SupportColor(int supportPercent, int bar)
    {
        if (supportPercent >= bar)
        {
            return SupportReady;
        }
        return supportPercent >= bar / 2 ? SupportMid : SupportLow;
    }

    /// <summary>Adds the context-sensitive action buttons, each gated on its GameLogic oracle (a closed gate shows a disabled button with its reason).</summary>
    private void AddActions()
    {
        MoveCheck convention = _game.CheckCallConvention();
        MoveCheck referendum = _game.CheckPutToReferendum();

        // Call Convention — offered while the convention has not yet been called.
        if (_game.FederationPhase == FederationPhase.ColonialMaturity)
        {
            AddActionButton("CallConventionButton", "Call the Federation Convention", convention, OnCallConvention);
        }

        // Put to Referendum — offered once the constitution is drafted (or after a failed referendum, for a retry).
        if (_game.FederationPhase is FederationPhase.ConstitutionDrafted or FederationPhase.Referendum)
        {
            AddActionButton("ReferendumButton", "Put Federation to a Referendum", referendum, OnHoldReferendum);
        }
    }

    /// <summary>Adds one action button: enabled (and gold-emphasised) when its <paramref name="gate"/> allows, else disabled with the gate's reason shown beneath.</summary>
    private void AddActionButton(string name, string text, MoveCheck gate, Action onPressed)
    {
        var button = new Button { Name = name, Text = text, Disabled = !gate.Allowed };
        if (gate.Allowed)
        {
            button.Pressed += onPressed;
            // The one available action reads as the call-to-action: a gold-edged wood face, cream text.
            button.AddThemeStyleboxOverride("normal", Flat(WoodMid, Gold, 2, 5, 12, 6));
            button.AddThemeStyleboxOverride("hover", Flat(WoodMid.Lightened(0.12f), Gold, 2, 5, 12, 6));
            button.AddThemeStyleboxOverride("pressed", Flat(WoodDark, Gold, 2, 5, 12, 6));
            button.AddThemeColorOverride("font_color", TextOnWood);
            button.AddThemeColorOverride("font_hover_color", TextOnWood);
        }
        _body.AddChild(button);
        if (!gate.Allowed && !string.IsNullOrEmpty(gate.Reason))
        {
            var reason = new Label
            {
                Name = $"{name}Reason",
                Text = gate.Reason,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            reason.AddThemeColorOverride("font_color", new Color(Ink, 0.7f)); // a quiet, muted "why it's locked" note
            _body.AddChild(reason);
        }
    }

    /// <summary>Forwards <see cref="Game.CallConvention"/>, refreshes the host, and rebuilds the body to the new phase.</summary>
    private void OnCallConvention()
    {
        if (_game.CallConvention())
        {
            _onChange();
        }
        Render();
    }

    /// <summary>Forwards <see cref="Game.HoldReferendum"/> (a seeded pass/fail roll), refreshes the host, and rebuilds the body.</summary>
    private void OnHoldReferendum()
    {
        _game.HoldReferendum();
        _onChange(); // a carried referendum wins next turn; a failed one shed support — the host view changes either way
        Render();
    }

    /// <summary>
    /// WS3.3: the <b>Draft Constitution</b> section — a styled progress gauge toward the design's ≥80% completion gate
    /// (with a gold marker at the gate, read from <see cref="Game.ConstitutionDraftThreshold"/>) plus one row per curated
    /// clause. Each row shows the clause name, its meter weight and cost, and either a <b>Draft</b> button (gold when
    /// affordable, else disabled with the gate reason as tooltip) or a "drafted" tick. Pure presentation — the rows read
    /// <see cref="Game.ConstitutionClauses"/> and forward <see cref="Game.DraftClause"/>.
    /// </summary>
    private void BuildDraftConstitution()
    {
        _body.AddChild(new Label { Name = "DraftHeader", Text = "Draft Constitution", ThemeTypeVariation = "SectionHeader" });

        int progress = _game.ConstitutionProgressPercent;
        int threshold = _game.ConstitutionDraftThreshold;
        var gauge = new ProgressBar
        {
            Name = "ConstitutionProgressBar",
            MinValue = 0,
            MaxValue = 100,
            Value = progress,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(200, 18),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        Color fill = progress >= threshold ? SupportReady : SupportMid; // green once the ≥80% gate is met, ochre while drafting
        gauge.AddThemeStyleboxOverride("background", Flat(ParchmentDark, ParchmentEdge, 1, 4));
        gauge.AddThemeStyleboxOverride("fill", Flat(fill, fill.Darkened(0.25f), 1, 4));
        gauge.AddChild(new ColorRect
        {
            Name = "DraftThresholdMark",
            Color = new Color(Gold, 0.95f),
            MouseFilter = MouseFilterEnum.Ignore,
            AnchorLeft = threshold / 100f,
            AnchorRight = threshold / 100f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            OffsetLeft = -1f,
            OffsetRight = 1f,
            OffsetTop = 2f,
            OffsetBottom = -2f,
        });
        _body.AddChild(gauge);
        _body.AddChild(new Label
        {
            Name = "ConstitutionProgress",
            Text = $"{progress}% drafted (need {threshold}%)",
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        foreach ((string Id, string DisplayName, int WeightPercent, int ConventionPointCost, int GoldCost, bool IsDrafted, MoveCheck DraftCheck) clause in _game.ConstitutionClauses())
        {
            AddClauseRow(clause);
        }
    }

    /// <summary>One Draft-Constitution clause row (WS3.3): name + weight, cost, and a Draft button (or a drafted tick).</summary>
    private void AddClauseRow((string Id, string DisplayName, int WeightPercent, int ConventionPointCost, int GoldCost, bool IsDrafted, MoveCheck DraftCheck) clause)
    {
        var row = new HBoxContainer { Name = $"Clause_{clause.Id}" };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Label
        {
            Name = "Name",
            Text = $"{clause.DisplayName}  ({clause.WeightPercent}%)",
            CustomMinimumSize = new Vector2(190, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.AddChild(new Label
        {
            Name = "Cost",
            Text = clause.GoldCost > 0 ? $"{clause.ConventionPointCost} pts · {clause.GoldCost} gold" : $"{clause.ConventionPointCost} pts",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (clause.IsDrafted)
        {
            var tick = new Label { Name = "Drafted", Text = "✓ drafted", VerticalAlignment = VerticalAlignment.Center };
            tick.AddThemeColorOverride("font_color", SupportReady);
            row.AddChild(tick);
        }
        else
        {
            var button = new Button { Name = "DraftButton", Text = "Draft", Disabled = !clause.DraftCheck.Allowed };
            if (clause.DraftCheck.Allowed)
            {
                string id = clause.Id;
                button.Pressed += () => OnDraftClause(id);
                button.AddThemeStyleboxOverride("normal", Flat(WoodMid, Gold, 2, 5, 10, 4));
                button.AddThemeStyleboxOverride("hover", Flat(WoodMid.Lightened(0.12f), Gold, 2, 5, 10, 4));
                button.AddThemeColorOverride("font_color", TextOnWood);
            }
            else if (!string.IsNullOrEmpty(clause.DraftCheck.Reason))
            {
                button.TooltipText = clause.DraftCheck.Reason; // why it's locked (insufficient points/gold, already drafted)
            }
            row.AddChild(button);
        }
        _body.AddChild(row);
    }

    /// <summary>Forwards <see cref="Game.DraftClause"/> (spends the cost, applies the one-off effect), refreshes the host, and rebuilds the body.</summary>
    private void OnDraftClause(string clauseId)
    {
        if (_game.DraftClause(clauseId))
        {
            _onChange();
        }
        Render();
    }

    /// <summary>A house-style <see cref="StyleBoxFlat"/> (rounded, anti-aliased, coloured border) matching the theme's cells/buttons.</summary>
    private static StyleBoxFlat Flat(Color bg, Color border, int borderWidth, int radius, int marginH = 0, int marginV = 0)
    {
        var box = new StyleBoxFlat { BgColor = bg, BorderColor = border, AntiAliasing = true };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        if (marginH > 0 || marginV > 0)
        {
            box.ContentMarginLeft = box.ContentMarginRight = marginH;
            box.ContentMarginTop = box.ContentMarginBottom = marginV;
        }
        return box;
    }

    /// <summary>The short suffix of a <c>model.region.*</c> key (e.g. <c>newSouthWales</c>), for stable node names tests can locate.</summary>
    private static string ShortKey(string regionKey) => regionKey[(regionKey.LastIndexOf('.') + 1)..];

    /// <summary>Empties the dynamic body with the signal-safe idiom (detach now, free deferred) — an action button's own handler drives the rebuild.</summary>
    private void ClearBody()
    {
        foreach (Node child in _body.GetChildren())
        {
            _body.RemoveChild(child);
            child.QueueFree();
        }
    }
}
