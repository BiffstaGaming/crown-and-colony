using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 tests (docs/TESTING.md) for the variant-aware art seam (WS1.3): <see cref="ColonyArt.Load"/> tries the current
/// variant's art root under <c>res://assets/&lt;variant&gt;/</c> first and falls back to the FreeCol base art per-asset,
/// so a variant with no art yet (Australia today) degrades gracefully — every asset resolves to FreeCol until the
/// variant ships its own. Needs the Godot runtime (ResourceLoader/GD.Load).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ColonyArtTests
{
    [TestCase]
    public void VariantArtRoot_FallsBackToFreeCol_WhenTheVariantAssetIsMissing()
    {
        string? prev = ColonyArt.VariantArtRoot;
        try
        {
            // An Australia game (no Australian art shipped yet) still loads the FreeCol asset for a known short name:
            // res://assets/australia/goods/bells.png is absent → Load falls back to res://assets/freecol/goods/bells.png.
            ColonyArt.VariantArtRoot = "australia";
            AssertThat(ColonyArt.GoodsIcon("bells")).IsNotNull();
            AssertThat(ColonyArt.UnitIcon("freeColonist")).IsNotNull();
            AssertThat(ColonyArt.PanelParchment()).IsNotNull();

            // Classic (null root) loads FreeCol directly — unchanged from before the seam.
            ColonyArt.VariantArtRoot = null;
            AssertThat(ColonyArt.GoodsIcon("bells")).IsNotNull();
        }
        finally
        {
            ColonyArt.VariantArtRoot = prev;
        }
    }

    [TestCase]
    public void MissingAssetEverywhere_ReturnsNull_UnderBothRoots()
    {
        string? prev = ColonyArt.VariantArtRoot;
        try
        {
            // A genuinely-absent asset resolves to null under both a variant root and classic — the graceful-degradation
            // path callers rely on (e.g. an Australian Pioneer with no portrait yet renders text-only, WS1.4).
            ColonyArt.VariantArtRoot = "australia";
            AssertThat(ColonyArt.FatherPortrait("henryParkes")).IsNull(); // no australia portrait yet, and no FreeCol one
            ColonyArt.VariantArtRoot = null;
            AssertThat(ColonyArt.FatherPortrait("henryParkes")).IsNull();
        }
        finally
        {
            ColonyArt.VariantArtRoot = prev;
        }
    }
}
