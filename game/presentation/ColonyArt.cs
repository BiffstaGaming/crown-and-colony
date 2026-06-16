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

    /// <summary>The colony settlement sprite drawn at the centre of the tiles grid.</summary>
    public static Texture2D? ColonyIcon() => Load("settlements/small.png");

    /// <summary>A building's FreeCol image (falls back to the generic <c>default.png</c> for an unmapped building).</summary>
    public static Texture2D? BuildingImage(string buildingShortName) =>
        Load($"buildings/{BuildingFile.GetValueOrDefault(buildingShortName, "default")}.png");

    /// <summary>A goods icon for the production/warehouse bars (FreeCol's goods files share our short names).</summary>
    public static Texture2D? GoodsIcon(string goodsShortName) => Load($"goods/{goodsShortName}.png");

    /// <summary>A unit sprite (e.g. a colonist on a worked tile), or null if that type has no art.</summary>
    public static Texture2D? UnitIcon(string shortName) => Load($"units/{shortName}.png");

    private static Texture2D? Load(string relativePath)
    {
        string path = $"res://assets/freecol/{relativePath}";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }
}
