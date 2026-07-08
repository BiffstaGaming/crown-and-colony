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
/// (<see cref="Game.RegionSupportSummary"/>), the phase (<see cref="Game.FederationPhase"/>), the points
/// (<see cref="Game.ConventionPoints"/>), and the two action gates (<see cref="Game.CheckCallConvention"/> /
/// <see cref="Game.CheckPutToReferendum"/>). The only mutations are the forwarded commands
/// (<see cref="Game.CallConvention"/> / <see cref="Game.HoldReferendum"/>); all the rules (thresholds, the seeded
/// referendum roll, the win) live in GameLogic. Built entirely in code (no scene file), mirroring
/// <see cref="AdvisorPanel"/>; the signal-safe rebuild idiom (<c>RemoveChild</c> then <c>QueueFree</c>) refills the body
/// after an action. The six region labels are hard-coded Australian names (the panel is Australia-only).</para>
/// </summary>
public partial class FederationPanel : PanelContainer
{
    /// <summary>The node name of the dynamic body container (used by tests to locate rendered rows).</summary>
    public const string BodyName = "Body";

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
        CustomMinimumSize = new Vector2(420, 0);

        var vbox = new VBoxContainer { Name = "VBox" };
        AddChild(vbox);

        _title = new Label
        {
            Name = "FederationTitle",
            Text = "The Road to Federation",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        vbox.AddChild(_title);
        vbox.AddChild(new HSeparator());

        _body = new VBoxContainer { Name = BodyName };
        vbox.AddChild(_body);

        vbox.AddChild(new HSeparator());
        var close = new Button { Name = "CloseButton", Text = "Close" };
        close.Pressed += Hide;
        vbox.AddChild(close);
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

    /// <summary>The current phase's player-facing name (Australian narrative), read from the <see cref="Game.FederationPhase"/> oracle.</summary>
    private static string PhaseName(FederationPhase phase) => phase switch
    {
        FederationPhase.ColonialMaturity => "The colonies are growing — the movement gathers support.",
        FederationPhase.ConventionCalled => "The Federation Convention has been called — the constitution is being drafted.",
        FederationPhase.ConstitutionDrafted => "The draft constitution is complete — a referendum may be held.",
        FederationPhase.Referendum => "A Federation referendum is under way.",
        FederationPhase.Commonwealth => "The Commonwealth of Australia is proclaimed — Federation is achieved!",
        _ => "",
    };

    /// <summary>(Re)builds the dynamic body: the phase line, Convention Points, the six region support rows, and the context actions.</summary>
    private void Render()
    {
        ClearBody();

        _body.AddChild(new Label
        {
            Name = "PhaseLine",
            Text = PhaseName(_game.FederationPhase),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _body.AddChild(new Label
        {
            Name = "ConventionPoints",
            Text = $"Convention Points: {_game.ConventionPoints}",
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        _body.AddChild(new HSeparator());

        _body.AddChild(new Label { Name = "SupportHeader", Text = "Federation Support by colony:" });
        foreach ((string regionKey, int supportPercent) in _game.RegionSupportSummary())
        {
            AddRegionRow(regionKey, supportPercent);
        }
        _body.AddChild(new HSeparator());

        AddActions();
    }

    /// <summary>Adds one region's row: its Australian name, a proportional support bar, and the percentage.</summary>
    private void AddRegionRow(string regionKey, int supportPercent)
    {
        string display = RegionDisplayNames.GetValueOrDefault(regionKey, regionKey);
        var row = new HBoxContainer { Name = $"Region_{ShortKey(regionKey)}" };
        row.AddChild(new Label
        {
            Name = "Name",
            Text = display,
            CustomMinimumSize = new Vector2(150, 0),
        });
        row.AddChild(new ProgressBar
        {
            Name = "Bar",
            MinValue = 0,
            MaxValue = 100,
            Value = supportPercent,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(160, 16),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        });
        row.AddChild(new Label { Name = "Percent", Text = $"{supportPercent}%" });
        _body.AddChild(row);
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

    /// <summary>Adds one action button: enabled when its <paramref name="gate"/> allows, else disabled with the gate's reason shown beneath.</summary>
    private void AddActionButton(string name, string text, MoveCheck gate, Action onPressed)
    {
        var button = new Button { Name = name, Text = text, Disabled = !gate.Allowed };
        if (gate.Allowed)
        {
            button.Pressed += onPressed;
        }
        _body.AddChild(button);
        if (!gate.Allowed && !string.IsNullOrEmpty(gate.Reason))
        {
            _body.AddChild(new Label
            {
                Name = $"{name}Reason",
                Text = gate.Reason,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
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
