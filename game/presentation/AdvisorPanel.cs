using System.Collections.Generic;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The <b>active-unit advisor</b> HUD hint (parity <c>86d3fq1re</c>): a small, dismissible parchment card that surfaces
/// the handful of concrete recommendations the <see cref="Game.AdviseUnit"/> oracle computes for the player's selected
/// unit ("You could found a colony here", "Build a road here", "Learn expert farmer at this native settlement",
/// "No orders — fortify, sentry, or move"). It is the lightweight echo of Col1's advisor characters who volunteered
/// advice; ours is curated and bounded (a fuller advisor engine is a follow-up).
/// <para>
/// Pure presentation (ADR-006): it renders an already-computed <see cref="AdvisorRecommendation"/> list it is
/// <b>handed</b> — it holds no game state, runs no rule logic, and never mutates the game or the save. It is built
/// entirely in code (no scene file), so the host can add it anywhere and the tests can drive it directly. Hidden by
/// default; <see cref="Show(IReadOnlyList{AdvisorRecommendation})"/> fills and reveals it, the Close button (or
/// <see cref="Dismiss"/>) hides it and raises <see cref="Dismissed"/>. An empty list hides the card (nothing to advise).
/// </para>
/// </summary>
public partial class AdvisorPanel : PanelContainer
{
    /// <summary>The node name of the dynamic recommendation-rows container (used by tests to locate rendered rows).</summary>
    public const string RowsContainerName = "Rows";

    /// <summary>Emitted when the player dismisses the advisor (Close button or <see cref="Dismiss"/>).</summary>
    [Signal]
    public delegate void DismissedEventHandler();

    private VBoxContainer _rows = null!;
    private Label _title = null!;

    /// <summary>Builds the card's node tree (title, rows container, close button) and applies the parchment look.</summary>
    public override void _Ready()
    {
        Name = "AdvisorPanel";
        Theme = ColonyTheme.GetInGame();
        AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        Visible = false;
        // Keep the card compact so it reads as a corner hint, not a full panel.
        CustomMinimumSize = new Vector2(280, 0);

        var vbox = new VBoxContainer { Name = "VBox" };
        AddChild(vbox);

        _title = new Label
        {
            Name = "AdvisorTitle",
            Text = "Advisor",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        vbox.AddChild(_title);

        _rows = new VBoxContainer { Name = RowsContainerName };
        vbox.AddChild(_rows);

        var close = new Button { Name = "CloseButton", Text = "Dismiss" };
        close.Pressed += Dismiss;
        vbox.AddChild(close);
    }

    /// <summary>
    /// Fills the card with <paramref name="recommendations"/> (one row each, an icon-less bulleted line) and reveals it.
    /// An empty list means there is nothing to advise, so the card stays hidden. Re-calling replaces the rows, so the
    /// host can refresh it whenever the selected unit changes.
    /// </summary>
    /// <param name="recommendations">The advice to display, already computed by <see cref="Game.AdviseUnit"/>.</param>
    public void Show(IReadOnlyList<AdvisorRecommendation> recommendations)
    {
        Render(recommendations);
        Visible = recommendations.Count > 0;
    }

    /// <summary>Hides the card and raises <see cref="Dismissed"/> (the Close button's handler).</summary>
    public void Dismiss()
    {
        Hide();
        EmitSignal(SignalName.Dismissed);
    }

    // Rebuilds the recommendation rows. Signal-safe rebuild (detach now, free deferred) — mirrors the other panels.
    private void Render(IReadOnlyList<AdvisorRecommendation> recommendations)
    {
        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        for (int i = 0; i < recommendations.Count; i++)
        {
            AdvisorRecommendation rec = recommendations[i];
            _rows.AddChild(new Label
            {
                Name = $"Advice_{rec.Kind}",
                Text = $"• {rec.Text}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
        }
    }
}
