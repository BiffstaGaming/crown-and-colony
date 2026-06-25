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
        // A faint backing so the preview reads over any zone; mouse-transparent so it never eats the drop event.
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0.20f, 0.14f, 0.08f, 0.92f) };
        style.SetBorderWidthAll(1);
        style.BorderColor = new Color(0.55f, 0.44f, 0.28f);
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

    /// <summary>Adds <paramref name="child"/> as the wrapped content and returns <c>this</c> (fluent — sized to fill the source).</summary>
    public EuropeDragSource WithChild(Control child)
    {
        AddChild(child);
        return this;
    }

    /// <summary>Builds the drag payload + preview, or returns <c>default</c> when nothing is draggable here (Godot issue 78507: C# this override is non-nullable, so <c>default</c> = "no drag").</summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        Variant payload = _payload();
        if (payload.VariantType == Variant.Type.Nil)
        {
            return default;
        }
        SetDragPreview(EuropeDrag.Preview(_previewText()));
        return payload;
    }
}

/// <summary>
/// A drop-target wrapper Control: it accepts a drag payload when the supplied <paramref name="canDrop"/> predicate says
/// so (which itself reads a <see cref="Game"/> Check oracle — ADR-006) and, on drop, runs <paramref name="onDrop"/>
/// (which re-checks the oracle, forwards the single Game command, and refreshes the panel). Rule-free transport; a
/// refused drop is a graceful no-op, never a throw.
/// </summary>
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

    /// <summary>Whether this target accepts <paramref name="data"/> here — delegates to the configured predicate (which gates on a <see cref="Game"/> oracle).</summary>
    public override bool _CanDropData(Vector2 atPosition, Variant data) => _canDrop(data);

    /// <summary>Performs the drop — delegates to the configured action (re-checks the oracle, forwards one command, refreshes).</summary>
    public override void _DropData(Vector2 atPosition, Variant data) => _onDrop(data);
}
