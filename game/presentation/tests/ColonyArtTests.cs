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

    // ─────────────────────────── WS2.1 — the Australian visual skin ───────────────────────────

    /// <summary>
    /// WS2.1: the theme is a <b>re-tone of one design language</b>, not two designs — switching the skin must actually
    /// change the palette (otherwise the whole art direction is a no-op), while leaving the structure intact.
    /// </summary>
    [TestCase]
    public void AustralianSkin_RetonesTheTheme_AndClassicIsUnchanged()
    {
        ColonyTheme.Skin prev = ColonyTheme.ActiveSkin;
        try
        {
            ColonyTheme.ActiveSkin = ColonyTheme.Skin.Classic;
            Color classicButton = ColonyTheme.Get().GetColor("font_pressed_color", "Button");
            Godot.StyleBox classicNormal = ColonyTheme.Get().GetStylebox("normal", "Button");

            ColonyTheme.ActiveSkin = ColonyTheme.Skin.Australia;
            Color australiaButton = ColonyTheme.Get().GetColor("font_pressed_color", "Button");
            Godot.StyleBox australiaNormal = ColonyTheme.Get().GetStylebox("normal", "Button");

            // The accent moves from gold to Federation blue — the single clearest tell that a screen is Australian.
            AssertThat(australiaButton.B > australiaButton.R).IsTrue();  // blue-dominant
            AssertThat(classicButton.R > classicButton.B).IsTrue();      // gold is red/warm-dominant
            // …but the structure is the same design: both skins still register a wood button box.
            AssertThat(classicNormal).IsNotNull();
            AssertThat(australiaNormal).IsNotNull();

            // Switching back must restore the classic palette exactly — the goldens depend on it.
            ColonyTheme.ActiveSkin = ColonyTheme.Skin.Classic;
            AssertThat(ColonyTheme.Get().GetColor("font_pressed_color", "Button")).IsEqual(classicButton);
        }
        finally
        {
            ColonyTheme.ActiveSkin = prev;
        }
    }

    /// <summary>WS2.1: each of the six Federation colonies gets its own colour, and no two collide — six identical rows would defeat the point.</summary>
    [TestCase]
    public void EachColonyRegion_HasADistinctColour()
    {
        string[] regions =
        [
            "model.region.newSouthWales", "model.region.victoria", "model.region.queensland",
            "model.region.southAustralia", "model.region.tasmania", "model.region.westernAustralia",
        ];

        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (string region in regions)
        {
            Color c = ColonyTheme.ColonyRegionColor(region);
            AssertThat(seen.Add(c.ToHtml())).OverrideFailureMessage($"{region} duplicates another colony's colour").IsTrue();
        }

        // An unknown region must fall back rather than throw (a variant may add regions later).
        AssertThat(ColonyTheme.ColonyRegionColor("model.region.nowhere")).IsNotNull();
    }

    // ─────────────────────────── WS2.5 — the map's terrain goes through the variant seam ───────────────────────────

    /// <summary>
    /// <b>The bug this guards.</b> <see cref="MapView"/> loaded its terrain with a hard-coded
    /// <c>res://assets/freecol/</c> path, so the map — by far the largest art surface in the game — silently bypassed
    /// the WS1.3 variant art seam. Australian terrain art could be supplied in full and the map would still draw the
    /// FreeCol tiles. Terrain now loads through <see cref="ColonyArt.LoadTexture"/> like everything else.
    /// </summary>
    [TestCase]
    public void TerrainArt_ResolvesThroughTheVariantSeam_NotAHardCodedFreeColPath()
    {
        string? prev = ColonyArt.VariantArtRoot;
        try
        {
            // The Australian terrain exists, so the seam must return it — not the FreeCol original.
            ColonyArt.VariantArtRoot = "australia";
            AssertThat(Godot.ResourceLoader.Exists("res://assets/australia/terrain/desert/center0.png"))
                .OverrideFailureMessage("the Australian desert tile is missing — the re-tone output was not committed/imported").IsTrue();
            AssertThat(ColonyArt.LoadTexture("terrain/desert/center0.png")).IsNotNull();
            AssertThat(ColonyArt.LoadTexture("forest/broadleaf/broadleaf.png")).IsNotNull();

            // Classic still resolves the FreeCol originals — the seam falls back, it does not redirect.
            ColonyArt.VariantArtRoot = null;
            AssertThat(ColonyArt.LoadTexture("terrain/desert/center0.png")).IsNotNull();

            // A terrain Australia deliberately does NOT re-tone still resolves (falls back to FreeCol) under both roots.
            ColonyArt.VariantArtRoot = "australia";
            AssertThat(ColonyArt.LoadTexture("terrain/ocean/center0.png")).IsNotNull();
        }
        finally
        {
            ColonyArt.VariantArtRoot = prev;
        }
    }

    /// <summary>
    /// WS2.5b: top-down gets <b>native square</b> tiles rather than a de-skewed diamond. Art drawn as diamonds cannot
    /// tile as squares, so the warp shows seams and repeating diagonal artefacts — a real defect in what is expected to
    /// become the game's main view. Guards that the square tiles exist for Australia, that two variants are supplied
    /// (one tile repeated across a biome shows an obvious grid), and that classic ships none so it still de-skews.
    /// </summary>
    [TestCase]
    public void TopDownSquareTiles_ExistForAustralia_AndClassicFallsBackToTheDeskew()
    {
        string? prev = ColonyArt.VariantArtRoot;
        try
        {
            ColonyArt.VariantArtRoot = "australia";
            foreach (string terrain in new[] { "desert", "grassland", "plains", "savannah", "hills" })
            {
                AssertThat(ColonyArt.LoadTexture($"terrain/{terrain}/top0.png"))
                    .OverrideFailureMessage($"the top-down square tile for {terrain} is missing").IsNotNull();
                AssertThat(ColonyArt.LoadTexture($"terrain/{terrain}/top1.png"))
                    .OverrideFailureMessage($"{terrain} needs a SECOND top-down variant or the biome tiles visibly repeat").IsNotNull();
            }

            // Water gets square tiles too: the de-skewed FreeCol ocean showed diagonal wave artefacts across a big
            // share of the screen, since the Australia map is mostly coastline.
            AssertThat(ColonyArt.LoadTexture("terrain/ocean/top0.png")).IsNotNull();
            AssertThat(ColonyArt.LoadTexture("terrain/highSeas/top0.png")).IsNotNull();

            // WS2.5c: forests get a full canopy tile, so the iso side-view tree overlay is suppressed for them.
            AssertThat(ColonyArt.LoadTexture("terrain/broadleafForest/top0.png")).IsNotNull();
            AssertThat(ColonyArt.LoadTexture("terrain/scrubForest/top0.png")).IsNotNull();

            // Arctic has none on purpose — it must fall back to the de-skewed diamond, not vanish.
            AssertThat(ColonyArt.LoadTexture("terrain/arctic/top0.png")).IsNull();
            AssertThat(ColonyArt.LoadTexture("terrain/arctic/center0.png")).IsNotNull();

            // Classic ships no square tiles at all — its top-down keeps de-skewing, unchanged.
            ColonyArt.VariantArtRoot = null;
            AssertThat(ColonyArt.LoadTexture("terrain/desert/top0.png")).IsNull();
            AssertThat(ColonyArt.LoadTexture("terrain/desert/center0.png")).IsNotNull();
        }
        finally
        {
            ColonyArt.VariantArtRoot = prev;
        }
    }
}
