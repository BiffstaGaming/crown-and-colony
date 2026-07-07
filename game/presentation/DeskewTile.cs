using System;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// A single terrain cell drawn <b>top-down</b> for a Control-based tile grid (the colony's 3×3 worked tiles).
///
/// <para>It reuses the isometric ground art without rotation: the base diamond texture is de-skewed onto a filled
/// square via the same 4-corner UV warp <see cref="MapView"/> uses for the main map's top-down mode (a diamond's
/// mid-edge corners map to the square's corners), so the shipped iso art reads as a flat square. Any taller
/// forest / hills / mountains overlay can't be de-skewed (it's drawn standing up), so — exactly as the map does —
/// it becomes a shrunk symbol centred on the cell.</para>
///
/// <para>Kept in sync with <see cref="MapView"/>'s <c>DiamondUVs</c> / <c>DrawDeskewed</c> / <c>DrawCentredSymbol</c>.
/// The map path is unchanged; this only exists because the colony tiles are Godot <see cref="Control"/>s (built
/// declaratively), not immediate-mode draws on the map canvas.</para>
/// </summary>
public partial class DeskewTile : Control
{
    // Diamond corner UVs, clockwise from top — the mid-points of the source texture's edges. Identical to
    // MapView.DiamondUVs; warping these onto a square's corners de-skews the diamond ground into the square.
    private static readonly Vector2[] DiamondUVs = [new(0.5f, 0f), new(1f, 0.5f), new(0.5f, 1f), new(0f, 0.5f)];

    private readonly Texture2D[] _textures;
    private readonly float _side;

    /// <param name="textures">The terrain stack from <see cref="ColonyArt.TerrainTextures"/>: index 0 is the base
    /// diamond (de-skewed), any further entries are overlays (drawn as centred symbols).</param>
    /// <param name="side">The square cell's side length in pixels.</param>
    public DeskewTile(Texture2D[] textures, float side)
    {
        _textures = textures;
        _side = side;
        CustomMinimumSize = new Vector2(side, side);
        MouseFilter = MouseFilterEnum.Ignore; // the transparent hit button above drives clicks
    }

    public override void _Draw()
    {
        if (_textures.Length == 0)
        {
            return;
        }
        // Base: de-skew the diamond ground onto the square cell (top/right/bottom/left aligned to DiamondUVs).
        Vector2[] square = [new(0f, 0f), new(_side, 0f), new(_side, _side), new(0f, _side)];
        Color[] white = [Colors.White, Colors.White, Colors.White, Colors.White];
        DrawPolygon(square, white, DiamondUVs, _textures[0]);

        // Overlays (forest / hills / mountains): a shrunk centred symbol — the tall art can't be de-skewed.
        for (int i = 1; i < _textures.Length; i++)
        {
            Texture2D overlay = _textures[i];
            Vector2 texSize = overlay.GetSize();
            float scale = _side * 0.85f / Math.Max(texSize.X, texSize.Y);
            Vector2 drawn = texSize * scale;
            DrawTextureRect(overlay, new Rect2(new Vector2(_side, _side) / 2f - (drawn / 2f), drawn), tile: false);
        }
    }
}
