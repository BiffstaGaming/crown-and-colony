using System;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Units;
using Godot;

namespace CrownAndColony.Presentation;

// ── Europe screen drag-and-drop (Phase 2) ───────────────────────────────────────────────────────────────────────
//
// The zoned Europe screen (EuropePanel, Phase 1) is all click buttons. Phase 2 adds a drag-and-drop layer ON TOP of
// those buttons (additive — every button still works as a fallback / for accessibility / for the simple L3 path).
//
// Godot 4.6 drag-and-drop is the three Control overrides: a SOURCE answers _GetDragData (build a payload + a floating
// preview), a TARGET answers _CanDropData (is this payload acceptable here, and does the engine Check oracle allow it?)
// and _DropData (re-check the oracle, forward the single Game command, refresh the panel). The payload is a Godot
// Collections.Dictionary tagged with a "kind" so a target can reject the wrong kind cheaply before consulting the
// oracle. NO rule logic lives here — every accept/drop decision reads a Game.Check* oracle (ADR-006); a drop that the
// oracle refuses is a graceful no-op, never a throw to the UI.
//
// Payload schema (the only shapes that travel):
//   colonist : { "kind": "colonist", "unitId": int }                                   — a dock person → a ship (Board)
//                                                                                          or a passenger → docks (Disembark)
//   ship     : { "kind": "ship",     "shipId":  int }                                  — a ship card → the sail zone (Sail)
//   goods    : { "kind": "goods", "goodsId": string, "amount": int, "fromShipId": int } — a hold stack → market (Sell)
//              { "kind": "goods", "goodsId": string, "amount": int }                    — a market row → a ship (Buy; no fromShipId)

/// <summary>The payload tags and field keys for Europe-screen drag-and-drop (Phase 2). Centralised so the source and
/// target controls — and the L3 tests that drive the callbacks directly — agree on the schema.</summary>
internal static class EuropeDrag
{
    /// <summary>Payload key holding the drag kind (one of the <c>Kind*</c> constants).</summary>
    public const string Kind = "kind";
    /// <summary>Payload key holding a unit id (colonist/passenger).</summary>
    public const string UnitId = "unitId";
    /// <summary>Payload key holding a ship's unit id.</summary>
    public const string ShipId = "shipId";
    /// <summary>Payload key holding a goods id (e.g. <c>model.goods.sugar</c>).</summary>
    public const string GoodsId = "goodsId";
    /// <summary>Payload key holding a goods amount (units).</summary>
    public const string Amount = "amount";
    /// <summary>Payload key holding the id of the ship a goods stack is dragged FROM (absent when dragging from the market).</summary>
    public const string FromShipId = "fromShipId";

    /// <summary>A dock colonist or an aboard passenger.</summary>
    public const string KindColonist = "colonist";
    /// <summary>A ship in port.</summary>
    public const string KindShip = "ship";
    /// <summary>A goods stack (in a hold) or a goods row (in the market).</summary>
    public const string KindGoods = "goods";

    /// <summary>Reads the <see cref="Kind"/> tag from a drag payload, or the empty string when the payload is not a tagged dictionary.</summary>
    public static string KindOf(Variant data) =>
        data.VariantType == Variant.Type.Dictionary
        && data.AsGodotDictionary() is { } dict && dict.TryGetValue(Kind, out Variant kind)
            ? kind.AsString()
            : "";

    /// <summary>Builds a small floating <see cref="Label"/> drag preview (NOT in the scene tree — Godot frees it). Used by every drag source via <see cref="Control.SetDragPreview"/>.</summary>
    public static Control Preview(string text)
    {
        var label = new Label { Text = text };
        // Explicit dark ink on the light backing so the preview text reads regardless of which theme (if any) cascades to
        // this transient, briefly-parented control — matching the readable light cards rather than the old dark box that
        // left near-black theme text unreadable (Chris's playtest).
        label.AddThemeColorOverride("font_color", new Color(0.17f, 0.11f, 0.06f));
        // A light parchment backing so the preview reads over any zone (cards + harbour backdrop); mouse-transparent so it
        // never eats the drop event.
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.91f, 0.85f, 0.69f, 0.96f) };
        style.SetBorderWidthAll(1);
        style.BorderColor = new Color(0.48f, 0.31f, 0.19f);
        style.SetCornerRadiusAll(3);
        style.SetContentMarginAll(4);
        box.AddThemeStyleboxOverride("panel", style);
        box.AddChild(label);
        return box;
    }
}

/// <summary>
/// A drag-source wrapper Control: it makes its child draggable by answering <see cref="Control._GetDragData"/> with a
/// payload built by the supplied factory (and a floating preview). It is a thin, rule-free transport node — the engine
/// decides what a drop means, not this (ADR-006). The wrapped child (a label/card) is added as a normal child so the
/// existing click buttons inside it keep working (drag is additive).
/// </summary>
public partial class EuropeDragSource : Control
{
    private Func<Variant> _payload = () => default;
    private Func<string> _previewText = () => "";
    private Action? _onClick;

    /// <summary>
    /// Configures the source: <paramref name="payload"/> builds the drag payload (return <c>default</c> for "nothing
    /// draggable"), <paramref name="previewText"/> the floating-preview caption. Returns <c>this</c> for fluent wiring.
    /// </summary>
    public EuropeDragSource Configure(Func<Variant> payload, Func<string> previewText)
    {
        _payload = payload;
        _previewText = previewText;
        MouseFilter = MouseFilterEnum.Pass; // children (buttons) still get their clicks; this still starts a drag
        return this;
    }

    /// <summary>
    /// Adds a single-CLICK action to this source (alongside its drag): a left-button release that did not turn into a
    /// drag fires <paramref name="onClick"/>. Used so a ship card both <b>selects on a single click</b> and <b>sails on a
    /// drag</b> from the same control (Chris's playtest — no separate Select button). Returns <c>this</c> for fluent
    /// wiring. The wrapped child's decoration must be <see cref="MouseFilterEnum.Ignore"/> so the input reaches here.
    /// </summary>
    public EuropeDragSource OnClick(Action onClick)
    {
        _onClick = onClick;
        return this;
    }

    /// <summary>Adds <paramref name="child"/> as the wrapped content and returns <c>this</c> (fluent — sized to fill the source).</summary>
    public EuropeDragSource WithChild(Control child)
    {
        AddChild(child);
        return this;
    }

    /// <summary>Builds the drag payload + preview, or returns <c>default</c> when nothing is draggable here (Godot issue 78507: C# this override is non-nullable, so <c>default</c> = "no drag").</summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        Variant payload = BuildDragPayload();
        if (payload.VariantType != Variant.Type.Nil)
        {
            // SetDragPreview is only valid while the viewport reports a live drag (it asserts gui_is_dragging()); Godot
            // calls this override at the START of a real drag, so that holds here. NOTE: driving this override directly
            // (outside a real drag) would trip that assert and orphan the preview Control — L3 tests must therefore call
            // BuildDragPayload() instead of _GetDragData().
            SetDragPreview(EuropeDrag.Preview(_previewText()));
        }
        return payload;
    }

    /// <summary>
    /// Builds the drag payload only (no preview), or <c>default</c> when nothing is draggable here. This is the rule-free
    /// transport half of <see cref="_GetDragData"/> WITHOUT the <see cref="Control.SetDragPreview"/> side effect. L3 tests
    /// drive drag-and-drop by invoking the callbacks directly (Godot can't synthesise a real mouse-drag headlessly), and
    /// <see cref="Control.SetDragPreview"/> asserts <c>gui_is_dragging()</c> — false outside a real drag — which logs a
    /// viewport error and orphans the preview Control. Tests call this seam so the payload is exercised without that leak;
    /// production still routes through <see cref="_GetDragData"/> and gets its preview during a genuine drag.
    /// </summary>
    public Variant BuildDragPayload() => _payload();

    /// <summary>
    /// Fires the configured <see cref="OnClick"/> action on a left-button release that wasn't consumed by a drag (Godot
    /// dispatches a drag through <see cref="_GetDragData"/> on press-and-move and suppresses the release click, so a plain
    /// click here means "no drag happened"). No-op when no click action is configured.
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (_onClick is { } onClick
            && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            onClick();
            AcceptEvent();
        }
    }
}

/// <summary>
/// A drop-target wrapper Control: it accepts a drag payload when the supplied <paramref name="canDrop"/> predicate says
/// so (which itself reads a <see cref="Game"/> Check oracle — ADR-006) and, on drop, runs <paramref name="onDrop"/>
/// (which re-checks the oracle, forwards the single Game command, and refreshes the panel). Rule-free transport; a
/// refused drop is a graceful no-op, never a throw.
/// </summary>
/// <remarks>
/// <para><b>It is a sizing PARENT, not an overlay.</b> The target is meant to <em>wrap</em> the zone's content (its
/// card/cell) as that content's parent, so the content's interactive buttons are this control's DESCENDANTS — rendered
/// ON TOP of the <see cref="MouseFilterEnum.Pass"/> target — and therefore receive real mouse clicks, while a drag-hover
/// over the surrounding (non-button) area still reaches this target. (A later-added sibling overlay with
/// <c>MouseFilter.Pass</c> would forward a click to the parent, NOT down to the button beneath it — leaving the button
/// dead to real clicks. That is the bug this design avoids.)</para>
/// <para>A bare <see cref="Control"/> does NOT aggregate its children's minimum sizes the way a
/// <see cref="Container"/> does (<see cref="Control._GetMinimumSize"/> returns only its own
/// <see cref="Control.CustomMinimumSize"/>), so a content child placed inside one would let the parent collapse to
/// nothing. <see cref="SetContent"/> + the <see cref="_GetMinimumSize"/> override below make this target behave like a
/// single-child container: it adopts its content's combined minimum size and fills that content to its own rect.</para>
/// </remarks>
public partial class EuropeDropTarget : Control
{
    private Func<Variant, bool> _canDrop = _ => false;
    private Action<Variant> _onDrop = _ => { };

    /// <summary>Configures the target's accept predicate and drop action. Returns <c>this</c> for fluent wiring.</summary>
    public EuropeDropTarget Configure(Func<Variant, bool> canDrop, Action<Variant> onDrop)
    {
        _canDrop = canDrop;
        _onDrop = onDrop;
        MouseFilter = MouseFilterEnum.Pass; // children (buttons) still get their clicks; this still receives drops
        return this;
    }

    /// <summary>
    /// Adds <paramref name="content"/> as this target's single content child, anchored <see cref="LayoutPreset.FullRect"/>
    /// so it fills the target, and re-asks for this target's minimum size whenever the content's minimum changes (so a
    /// content card/cell never collapses the target — the bug this fixes). Use this instead of a bare
    /// <see cref="Node.AddChild(Node, bool, Node.InternalMode)"/> so the target sizes like a single-child container.
    /// Returns <c>this</c> for fluent wiring.
    /// </summary>
    public EuropeDropTarget SetContent(Control content)
    {
        AddChild(content);
        content.SetAnchorsPreset(LayoutPreset.FullRect); // the content fills the drop target's whole rect
        // A Container re-queries its min size when a child's changes; a plain Control does not — wire it explicitly so a
        // late-laid-out card (icons/labels measured after the first frame) still drives this target's height.
        content.MinimumSizeChanged += UpdateMinimumSize;
        UpdateMinimumSize();
        return this;
    }

    /// <summary>
    /// This target's minimum size: the per-axis maximum of its visible <see cref="Control"/> children's combined minimum
    /// sizes (so it sizes like a single-child container around its content), or <see cref="Control.CustomMinimumSize"/>
    /// when it has none. Overriding this is what stops a content card/cell placed inside the (otherwise plain-Control)
    /// drop target from collapsing to nothing.
    /// </summary>
    public override Vector2 _GetMinimumSize()
    {
        var min = Vector2.Zero;
        foreach (Node child in GetChildren())
        {
            if (child is Control { Visible: true } control)
            {
                Vector2 childMin = control.GetCombinedMinimumSize();
                min = new Vector2(Mathf.Max(min.X, childMin.X), Mathf.Max(min.Y, childMin.Y));
            }
        }
        return min;
    }

    /// <summary>Whether this target accepts <paramref name="data"/> here — delegates to the configured predicate (which gates on a <see cref="Game"/> oracle).</summary>
    public override bool _CanDropData(Vector2 atPosition, Variant data) => _canDrop(data);

    /// <summary>Performs the drop — delegates to the configured action (re-checks the oracle, forwards one command, refreshes).</summary>
    public override void _DropData(Vector2 atPosition, Variant data) => _onDrop(data);
}
