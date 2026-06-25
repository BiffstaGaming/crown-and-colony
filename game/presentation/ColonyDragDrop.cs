using Godot;

namespace CrownAndColony.Presentation;

// ── Colony screen drag-and-drop (Phase 2) ────────────────────────────────────────────────────────────────────────
//
// The colony screen (ColonyPanel) staffs tiles and buildings by clicking (the per-tile Work… picker / the building +
// button) plus a click-to-move gesture. Phase 2 adds a drag-and-drop layer ON TOP of those controls (additive — every
// click still works as a fallback / for accessibility / for the simple L3 path).
//
// It REUSES the generic, rule-free EuropeDragSource / EuropeDropTarget transport controls (a SOURCE answers
// _GetDragData with a payload + preview; a TARGET answers _CanDropData / _DropData, gating on a Game.Check* oracle and
// forwarding one Game command — ADR-006). Only the payload schema is colony-specific, defined here.
//
// Payload schema (the only shape that travels):
//   worker : { "kind": "worker", "unitId": int }   — an idle/assigned colonist chip → a tile (AssignWork) or a
//                                                     building (AssignBuildingWork). The colony's idle colonists are a
//                                                     count + a non-free-type overlay, NOT individual map units, and the
//                                                     engine assign-work commands auto-pick an idle colonist — so
//                                                     "unitId" is the dragged chip's own identity (its idle-row index),
//                                                     used for the preview/round-trip, never to pick the worker.

/// <summary>The payload tag and field keys for colony-screen drag-and-drop (Phase 2), and a typed factory. Centralised so
/// the source/target controls — and the L3 tests that drive the callbacks directly — agree on the schema. Mirrors
/// <see cref="EuropeDrag"/> (same transport, colony-specific payload).</summary>
internal static class ColonyDrag
{
    /// <summary>Payload key holding the drag kind (currently only <see cref="KindWorker"/>).</summary>
    public const string Kind = "kind";
    /// <summary>Payload key holding the dragged colonist chip's identity (its idle-row index — see the file header; not a map unit id).</summary>
    public const string UnitId = "unitId";

    /// <summary>An idle (or assigned) colonist chip being dragged onto a tile or a building.</summary>
    public const string KindWorker = "worker";

    /// <summary>Builds the worker drag payload for the colonist chip identified by <paramref name="unitId"/>.</summary>
    public static Godot.Collections.Dictionary Worker(int unitId) => new()
    {
        [Kind] = KindWorker,
        [UnitId] = unitId,
    };

    /// <summary>Reads the <see cref="Kind"/> tag from a drag payload, or the empty string when the payload is not a tagged dictionary.</summary>
    public static string KindOf(Variant data) =>
        data.VariantType == Variant.Type.Dictionary
        && data.AsGodotDictionary() is { } dict && dict.TryGetValue(Kind, out Variant kind)
            ? kind.AsString()
            : "";

    /// <summary>Whether <paramref name="data"/> is a worker payload (the only kind a colony tile/building drop accepts).</summary>
    public static bool IsWorker(Variant data) => KindOf(data) == KindWorker;

    /// <summary>Builds a small floating <see cref="Label"/> drag preview (NOT in the scene tree — Godot frees it), mirroring <see cref="EuropeDrag.Preview"/>. Used by every colony drag source via <see cref="Control.SetDragPreview"/>.</summary>
    public static Control Preview(string text) => EuropeDrag.Preview(text);
}
