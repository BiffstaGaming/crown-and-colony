using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// Loads and caches the FreeCol art the colony screen draws (ADR-014, GPL v2 assets under
/// <c>res://assets/freecol/</c>) — terrain diamonds, the settlement sprite, and colonist sprites — so the panel can
/// show real tiles instead of text. Kept separate from <see cref="MapView"/> (which owns the map's golden-tested
/// rendering) to avoid disturbing it; the terrain base/overlay mapping mirrors MapView's and can be unified later.
/// </summary>
public static class ColonyArt
{
    // Forests/hills/mountains have no base art of their own — they sit on a clear terrain's centre tile (matches MapView).
    private static readonly Dictionary<string, string> BaseFor = new()
    {
        ["mixedForest"] = "plains", ["coniferForest"] = "grassland", ["broadleafForest"] = "prairie",
        ["tropicalForest"] = "savannah", ["wetlandForest"] = "marsh", ["rainForest"] = "swamp",
        ["scrubForest"] = "desert", ["borealForest"] = "tundra", ["hills"] = "grassland",
        ["mountains"] = "tundra", ["lake"] = "ocean", ["greatRiver"] = "ocean",
    };

    private static readonly Dictionary<string, string> OverlayFor = new()
    {
        ["mixedForest"] = "forest/mixed/mixed.png", ["coniferForest"] = "forest/conifer/conifer.png",
        ["broadleafForest"] = "forest/broadleaf/broadleaf.png", ["tropicalForest"] = "forest/tropical/tropical.png",
        ["wetlandForest"] = "forest/wetland/wetland.png", ["rainForest"] = "forest/rain/rain.png",
        ["scrubForest"] = "forest/scrub/scrub.png", ["borealForest"] = "forest/boreal/boreal.png",
        ["hills"] = "terrain/hills/hills0.png", ["mountains"] = "terrain/mountains/mountains0.png",
    };

    private static readonly Dictionary<string, Texture2D[]> _terrain = [];

    /// <summary>The texture stack for a terrain (its base centre diamond, plus a forest/hills/mountains overlay on top); empty if no art exists. Cached.</summary>
    public static Texture2D[] TerrainTextures(string terrainShortName)
    {
        if (_terrain.TryGetValue(terrainShortName, out Texture2D[]? cached))
        {
            return cached;
        }
        var stack = new List<Texture2D>();
        string baseName = BaseFor.GetValueOrDefault(terrainShortName, terrainShortName);
        if (Load($"terrain/{baseName}/center0.png") is { } baseTex)
        {
            stack.Add(baseTex);
        }
        if (OverlayFor.TryGetValue(terrainShortName, out string? overlay) && Load(overlay) is { } overlayTex)
        {
            stack.Add(overlayTex);
        }
        Texture2D[] result = [.. stack];
        _terrain[terrainShortName] = result;
        return result;
    }

    // Our camelCase building ids → FreeCol's (often pluralised) building image file names.
    private static readonly Dictionary<string, string> BuildingFile = new()
    {
        ["townHall"] = "townhall", ["carpenterHouse"] = "carpenters_house", ["lumberMill"] = "lumber_mill",
        ["church"] = "church", ["chapel"] = "chapel", ["cathedral"] = "cathedral",
        ["blacksmithHouse"] = "blacksmiths_house", ["blacksmithShop"] = "blacksmiths_shop", ["ironWorks"] = "ironworks",
        ["armory"] = "armory", ["magazine"] = "magazine", ["arsenal"] = "arsenal",
        ["weaverHouse"] = "weavers_house", ["weaverShop"] = "weavers_shop", ["textileMill"] = "textile_mill",
        ["tobacconistHouse"] = "tobacconists_house", ["tobacconistShop"] = "tobacconists_shop", ["cigarFactory"] = "cigar_factory",
        ["distillerHouse"] = "distillers_house", ["rumDistillery"] = "rum_distillery", ["rumFactory"] = "rum_factory",
        ["furTraderHouse"] = "fur_traders_house", ["furTradingPost"] = "fur_traders_shop", ["furFactory"] = "fur_factory",
        ["docks"] = "docks", ["drydock"] = "drydock", ["shipyard"] = "shipyard", ["stables"] = "stable",
        ["printingPress"] = "printing_press", ["newspaper"] = "newspaper",
        ["schoolhouse"] = "schoolhouse", ["college"] = "college", ["university"] = "university",
        ["stockade"] = "stockade", ["fort"] = "fort", ["fortress"] = "fortress",
        ["warehouse"] = "warehouse", ["warehouseExpansion"] = "warehouse_expansion",
        ["customHouse"] = "custom_house", ["depot"] = "depot",
    };

    /// <summary>The colony window's brown parchment skin (FreeCol <c>bg_paper_brown</c>), tiled as the panel fill; null if the asset is absent (the panel then falls back to a solid fill).</summary>
    public static Texture2D? PanelParchment() => Load("ui/bg_paper_brown.png");

    /// <summary>
    /// The Europe harbour backdrop — FreeCol's <c>colonydocks</c> sky-and-sea scene, drawn (stretched to fill) behind the
    /// Europe screen's content cards so the harbour reads as a place rather than a flat parchment. Null if the asset is
    /// absent, in which case the Europe panel falls back to its tiled parchment fill (so it stays opaque in CI before the
    /// image is imported). Mirrors <see cref="PanelParchment"/>; the cards keep their own opaque backing on top so text
    /// stays readable over the scene (like Colonization's panels over the docks view).
    /// </summary>
    public static Texture2D? HarbourBackdrop() => Load("ui/colonydocks.png");

    /// <summary>The colony window's carved-wood frame — FreeCol's <c>carvedwoodenborder</c> edge/corner pieces composited into one 194×194 nine-patch (23px margins); null if absent.</summary>
    public static Texture2D? ColonyBorder() => Load("ui/colony_border.png");

    /// <summary>
    /// A parchment panel skin for menu-style screens: FreeCol's brown parchment tiled (not stretched — the tile is
    /// only 291×295), inset 26px so content clears the 23px carved-wood frame. Falls back to a warm solid fill if the
    /// asset is absent (keeps the panel opaque in CI). Shared by the main-menu and settings screens; the colony screen
    /// keeps its own equivalent (<c>ColonyPanel.BuildPanelBackground</c>).
    /// </summary>
    public static StyleBox ParchmentSkin()
    {
        if (PanelParchment() is { } parchment)
        {
            var skin = new StyleBoxTexture
            {
                Texture = parchment,
                AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
                AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
            };
            skin.SetContentMarginAll(26);
            return skin;
        }
        var flat = new StyleBoxFlat { BgColor = new Color(0.18f, 0.12f, 0.07f) };
        flat.SetContentMarginAll(26);
        return flat;
    }

    /// <summary>
    /// Frames a message/dialog surface in the shared parchment skin so it sits on the FreeCol paper rather than
    /// Godot's default transparent/gray box. Assigns the parchment/wood <see cref="ColonyTheme"/> (so the panel's
    /// text renders as dark ink on the parchment, not white-on-nothing) and gives the panel the tiled brown-paper
    /// <see cref="ParchmentSkin"/> background. The single call every bare message panel uses. Pass
    /// <paramref name="dense"/> for the large in-game info screens (reports / colopedia / native-settlement /
    /// negotiation / trade-routes) that want the larger, bolder in-game body text; the modal event popups keep the
    /// menu-sized baseline.
    /// </summary>
    public static void FramePanel(PanelContainer panel, bool dense = false)
    {
        panel.Theme = dense ? ColonyTheme.GetInGame() : ColonyTheme.Get();
        panel.AddThemeStyleboxOverride("panel", ParchmentSkin());
    }

    /// <summary>The colony settlement sprite drawn at the centre of the tiles grid.</summary>
    public static Texture2D? ColonyIcon() => Load("settlements/small.png");

    /// <summary>A building's FreeCol image (falls back to the generic <c>default.png</c> for an unmapped building).</summary>
    public static Texture2D? BuildingImage(string buildingShortName) =>
        Load($"buildings/{BuildingFile.GetValueOrDefault(buildingShortName, "default")}.png");

    /// <summary>A goods icon for the production/warehouse bars (FreeCol's goods files share our short names).</summary>
    public static Texture2D? GoodsIcon(string goodsShortName) => Load($"goods/{goodsShortName}.png");

    /// <summary>A unit sprite (e.g. a colonist on a worked tile), or null if that type has no art.</summary>
    public static Texture2D? UnitIcon(string shortName) => Load($"units/{shortName}.png");

    /// <summary>
    /// A Founding Father's portrait — FreeCol's <c>foundingFathers/&lt;name&gt;.jpg</c> head-and-shoulders painting
    /// (200×237), drawn beside the father's name/effect text in the Continental Congress dialog and the Colopedia
    /// Fathers tab. The file is named for the father's <see cref="GameLogic.Specification.FoundingFather.ShortName"/>
    /// (e.g. <c>hernanCortes</c>), so this is a direct lookup like <see cref="GoodsIcon"/>/<see cref="UnitIcon"/>;
    /// the four fathers FreeCol stored under a shorter file name (Brewster/Cortés/Magellan/Brébeuf) were copied to
    /// their ShortName so no per-id mapping is needed. Null if that father has no portrait, so callers degrade to
    /// text-only. Cached by Godot's resource loader.
    /// </summary>
    public static Texture2D? FatherPortrait(string fatherShortName) => Load($"fathers/{fatherShortName}.jpg");

    private static Texture2D? Load(string relativePath)
    {
        string path = $"res://assets/freecol/{relativePath}";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }
}
