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
            // path callers rely on (e.g. an Australian Pioneer with no portrait renders text-only, WS1.4).
            // NOTE: this used to probe `henryParkes`, which became a REAL asset when the 2026-07-26 portrait set landed.
            // It now probes `jamesRuse` — the one Pioneer who is deliberately portrait-less (no authenticated likeness of
            // him exists), so the case stays genuinely absent and meaningful rather than merely "not sourced yet".
            ColonyArt.VariantArtRoot = "australia";
            AssertThat(ColonyArt.FatherPortrait("jamesRuse")).IsNull(); // no australia portrait by design, and no FreeCol one
            ColonyArt.VariantArtRoot = null;
            AssertThat(ColonyArt.FatherPortrait("jamesRuse")).IsNull();
        }
        finally
        {
            ColonyArt.VariantArtRoot = prev;
        }
    }

    [TestCase]
    public void WilliamBarakPortrait_LoadsUnderTheAustraliaRoot()
    {
        // WS1.4 close-out: Barak's portrait is the one supplied Australian Pioneer image (Carl Walter 1866, PD-Australia;
        // signed off 2026-07-12). Guard that it resolves through the variant art seam — a rename/removal fails here.
        string? prev = ColonyArt.VariantArtRoot;
        try
        {
            ColonyArt.VariantArtRoot = "australia";
            AssertThat(ColonyArt.FatherPortrait("williamBarak")).IsNotNull();
        }
        finally
        {
            ColonyArt.VariantArtRoot = prev;
        }
    }

    /// <summary>
    /// WS1.4: every Australian Pioneer resolves a portrait through the variant art seam — a renamed, missing or
    /// un-imported file fails here rather than silently rendering the Pioneer text-only in the Convention dialog.
    /// <b>James Ruse is the one deliberate exception</b>: no authenticated likeness of him is known to exist, so he
    /// renders text-only rather than carrying a misattributed image (recorded in the Asset Register). He is asserted
    /// <em>absent</em> so that adding a file for him without the provenance work also trips this test.
    /// </summary>
    [TestCase]
    public void EveryAustralianPioneerPortrait_LoadsUnderTheAustraliaRoot_ExceptJamesRuse()
    {
        string[] pioneers =
        [
            "arthurPhillip", "carolineChisholm", "catherineHelenSpence", "charlesSturt", "charlesTodd",
            "edmundBarton", "edwardHargraves", "elizabethMacarthur", "georgeFifeAngas", "henryParkes",
            "johnMcDouallStuart", "johnQuick", "lachlanMacquarie", "louisaLawson", "ludwigLeichhardt",
            "maryLee", "maryReibey", "matthewFlinders", "peterLalor", "samuelGriffith",
            "sidneyKidman", "thomasSutcliffeMort", "williamBarak", "williamJervois",
        ];

        string? prev = ColonyArt.VariantArtRoot;
        try
        {
            ColonyArt.VariantArtRoot = "australia";
            foreach (string pioneer in pioneers)
            {
                AssertThat(ColonyArt.FatherPortrait(pioneer)).OverrideFailureMessage(
                    $"Australian Pioneer portrait '{pioneer}.jpg' did not load from res://assets/australia/fathers/").IsNotNull();
            }
            AssertThat(ColonyArt.FatherPortrait("jamesRuse")).IsNull(); // no authenticated likeness exists — text-only by design
        }
        finally
        {
            ColonyArt.VariantArtRoot = prev;
        }
    }
}
