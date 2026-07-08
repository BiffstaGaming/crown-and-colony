using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// The Australian Federation variant (P8, ADR-018) — its skeleton slice: the variant is registered and selectable, its
/// ruleset loads, the authored Australia continent map (60×40, de-staggered from the FreeCol community map pack) imports and
/// boots a game, and a save records the variant so it reloads under the Australia ruleset. The spec starts as a copy of
/// classic; the transposability-anchor guard here protects the later reskin slices (renaming display text must keep the
/// well-known ids the engine machinery reads).
/// </summary>
public class AustraliaVariantTests
{
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();

    [Fact]
    public void Registry_ContainsAustralia_SelectableAndResolvable()
    {
        Assert.Contains(GameVariants.All, v => v.Id == "australia");
        Assert.Same(GameVariants.Australia, GameVariants.ById("australia"));
        Assert.Same(GameVariants.Australia, GameVariants.Resolve("australia"));
        Assert.Equal("Australian Federation", GameVariants.Australia.DisplayName);
        Assert.NotSame(GameVariants.Australia, GameVariants.Default); // classic stays the default
    }

    [Fact]
    public void AustraliaVariant_LoadsARuleset_KeepingTheTransposabilityAnchors()
    {
        // The spec parses (units + fathers present)…
        Assert.NotEmpty(Australia.UnitTypes);
        Assert.NotEmpty(Australia.FoundingFathers);

        // …and it still declares every structural id the variant-agnostic engine keys on (ADR-018). A later reskin
        // renames the DISPLAY of these (Liberty Bells → Civic Voice), but the ids must stay or the machinery breaks.
        foreach (string goodsId in new[]
                 {
                     "model.goods.food", "model.goods.bells", "model.goods.crosses", "model.goods.grain",
                 })
        {
            Assert.NotNull(Australia.Goods(goodsId));
        }
        Assert.NotNull(Australia.Terrain("model.tile.ocean"));
        Assert.NotNull(Australia.Terrain("model.tile.highSeas"));
    }

    [Fact]
    public void AustraliaMap_ImportsAt60x40_EveryTileResolved()
    {
        // 60×40 — de-staggered from the FreeCol 30×80 source (FreeCol y counts HALF-rows), restoring the continent's
        // real wider-than-tall proportions (Chris 2026-07-08; see data/maps/PROVENANCE.md).
        GameMap map = FixedMap.LoadAustralia(Australia);
        Assert.Equal(60, map.Width);
        Assert.Equal(40, map.Height);
        // Reaching here means all 2400 tiles resolved (the importer throws on an unknown id).
        Assert.Equal(60 * 40, map.AllPositions().Count());
    }

    [Fact]
    public void AustraliaMap_LooksLikeAustralia_AridInteriorInAWideOcean()
    {
        GameMap map = FixedMap.LoadAustralia(Australia);

        // A continent adrift in ocean: the 60×40 grid is mostly water (Tasman/Indian/Southern seas + high seas).
        int water = map.AllPositions().Count(p => map.TerrainAt(p).IsWater);
        Assert.True(water > map.Width * map.Height / 2, $"expected mostly water, got {water}/{map.Width * map.Height}");

        // …with the hallmark arid centre plus coastal/temperate variety (the real continent's terrain palette).
        var counts = map.AllPositions().GroupBy(p => map.TerrainAt(p).Id).ToDictionary(g => g.Key, g => g.Count());
        foreach (string id in new[]
                 {
                     "model.tile.desert", "model.tile.savannah", "model.tile.plains", "model.tile.mountains",
                 })
        {
            Assert.True(counts.GetValueOrDefault(id, 0) > 0, $"expected some {id} on the Australia map");
        }
    }

    [Fact]
    public void AustraliaMap_HasNoNewZealand_TheSouthEastIsOpenSea()
    {
        // The FreeCol community source included the northern tip of New Zealand off the south-east corner; Chris
        // (2026-07-08): "this is for Australia only" — it was edited out of the shipped grid (12 tiles → ocean;
        // see data/maps/PROVENANCE.md). Tasmania stays (its easternmost land is column 50); everything east of it
        // in the southern half must be open sea, so a future map re-conversion can't silently bring NZ back.
        GameMap map = FixedMap.LoadAustralia(Australia);
        for (int x = 51; x < map.Width; x++)
        {
            for (int y = 30; y < map.Height; y++)
            {
                Assert.True(map.TerrainAt(new Position(x, y)).IsWater, $"unexpected land at ({x},{y}) — New Zealand is back?");
            }
        }
    }

    [Fact]
    public void AustraliaMap_IsDeterministic_SameBytesEveryLoad()
    {
        GameMap a = FixedMap.LoadAustralia(Australia);
        GameMap b = FixedMap.LoadAustralia(Australia);
        Assert.Equal(
            a.AllPositions().Select(p => a.TerrainAt(p).Id),
            b.AllPositions().Select(p => b.TerrainAt(p).Id));
        Assert.NotNull(FixedMap.TryLoad(MapSource.Australia, Australia));
    }

    [Fact]
    public void AustraliaGame_BootsOnItsMap_AndTheTurnAdvances()
    {
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia);
        Assert.Equal(60, game.Map.Width);
        Assert.Equal(40, game.Map.Height);
        Assert.NotEmpty(game.PlayerUnits); // the human landed with a starting party on the Australia map

        int before = game.Turn;
        game.EndTurn();
        Assert.Equal(before + 1, game.Turn); // a full round completes on the new map
    }

    [Fact]
    public void AustraliaTimeline_StartsIn1788_AtTheFirstFleet_AndRunsToFederation()
    {
        // The variant's calendar runs 1788 (the First Fleet) → 1901 (Federation), not the classic 1492–1850
        // (86d3mm2fb). Only the gameOptions.years values change; the calendar machinery is unchanged.
        Assert.Equal(1788, Australia.Calendar.YearForTurn(1)); // turn 1 is 1788
        Assert.Equal(1899, Australia.LastColonialYear);        // the pre-Federation colonial cutoff
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia);
        Assert.Equal(1788, game.CurrentYear);                  // the booted game actually opens in 1788
    }

    [Fact]
    public void AustralianColonies_UseAustralianPlaceNames_SydneyFirst()
    {
        // The primary British-Australian power (the `english` transposability anchor) founds Australian towns in
        // historical order — Sydney (the First Fleet) first (86d3kwtrq). The New-World names are gone from its list.
        EuropeanNation british =
            Australia.EuropeanNations.First(n => n.ColonyNames.Contains("Sydney"));
        Assert.Equal("Sydney", british.ColonyNames[0]);
        foreach (string town in new[] { "Melbourne", "Adelaide", "Brisbane", "Perth", "Hobart" })
        {
            Assert.Contains(town, british.ColonyNames);
        }
        Assert.DoesNotContain("Jamestown", british.ColonyNames);
    }

    [Fact]
    public void AustraliaSave_RecordsTheVariant_AndReloadsUnderItsRuleset()
    {
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia);

        SaveGame save = SaveGame.From(game, GameVariants.Australia.Id);
        Assert.Equal("australia", save.Variant);
        SaveGame restored = SaveGame.FromJson(save.ToJson());
        Assert.Equal("australia", restored.Variant);                       // survives the JSON round-trip
        Assert.Same(GameVariants.Australia, GameVariants.Resolve(restored.Variant)); // → the Australia ruleset on load

        // The imported six-colony region layer is persisted and reloads verbatim (regions are persisted state, and
        // the Australia map now ships them — so the save must carry them, not re-derive from terrain).
        Game reloaded = restored.Restore(Australia);
        Assert.Equal(game.Map.Regions, reloaded.Map.Regions);              // table (keys/types/scores) identical
        Assert.Equal(
            game.Map.AllPositions().Select(game.Map.RegionIdAt),
            reloaded.Map.AllPositions().Select(reloaded.Map.RegionIdAt));  // per-tile ids identical
        Assert.Contains(reloaded.Map.Regions, r => r.Key == "model.region.newSouthWales");
    }

    // --- Six colony regions + start sites (86d3mm1xr) --------------------------------

    /// <summary>The six colonies' expected region keys (the milestone's six-colony requirement) + the surrounding sea.</summary>
    private static readonly string[] ColonyRegionKeys =
    {
        "model.region.newSouthWales", "model.region.victoria", "model.region.queensland",
        "model.region.southAustralia", "model.region.tasmania", "model.region.westernAustralia",
    };

    [Fact]
    public void AustraliaMap_DeclaresTheSixColonyRegions_PlusOneOcean()
    {
        GameMap map = FixedMap.LoadAustralia(Australia);

        // 7 regions: one keyed ocean (region 0) + the six colony Land regions (ids 1..6, in Federation order).
        Assert.Equal(7, map.Regions.Count);
        Assert.Equal(RegionType.Ocean, map.Regions[0].Type);
        Assert.Equal("model.region.australSea", map.Regions[0].Key);
        for (int i = 0; i < ColonyRegionKeys.Length; i++)
        {
            Region colony = map.Regions[i + 1];
            Assert.Equal(RegionType.Land, colony.Type);
            Assert.Equal(ColonyRegionKeys[i], colony.Key);
        }
    }

    [Fact]
    public void AustraliaMap_AssignsEveryTile_AndAllSixColoniesCoverLand()
    {
        GameMap map = FixedMap.LoadAustralia(Australia);

        // Every in-bounds tile carries a region (the importer enforces this, but assert it end-to-end): none NoRegion.
        Assert.All(map.AllPositions(), p => Assert.NotEqual(GameMap.NoRegion, map.RegionIdAt(p)));

        // Each of the six colony keys actually owns some land (a non-empty quadrant), and its tiles are all Land.
        var keyByTile = map.AllPositions()
            .GroupBy(p => map.RegionOf(p)!.Key!)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (string key in ColonyRegionKeys)
        {
            Assert.True(keyByTile.GetValueOrDefault(key, 0) > 0, $"expected land in region {key}");
        }

        // Water tiles are the ocean region; land tiles are never the ocean region (the layer matches the coastline).
        Assert.All(map.AllPositions(), p =>
            Assert.Equal(map.TerrainAt(p).IsWater, map.RegionOf(p)!.Key == "model.region.australSea"));
    }

    [Fact]
    public void AustraliaMap_ColonyKeys_HumaniseToTheirDisplayNames()
    {
        // No i18n table yet: display text comes from Naming.Humanize on the key's camelCase suffix. Verify the
        // multi-word colony keys read as proper names (so a discovered region shows "New South Wales", not the raw id).
        Assert.Equal("New South Wales", Naming.Humanize("newSouthWales"));
        Assert.Equal("South Australia", Naming.Humanize("southAustralia"));
        Assert.Equal("Western Australia", Naming.Humanize("westernAustralia"));
        Assert.Equal("Queensland", Naming.Humanize("queensland"));
        Assert.Equal("Victoria", Naming.Humanize("victoria"));
        Assert.Equal("Tasmania", Naming.Humanize("tasmania"));
    }

    [Fact]
    public void AustraliaMap_FirstFleet_LandsInNewSouthWales()
    {
        // The map fixes the human's landfall (an imported [starts] human X Y) at the NSW coastal seed tile — Sydney
        // Cove, the First Fleet's 1788 landing. Game.New honours it (imported.HumanStart), so the human's colonists
        // land on-or-beside that exact tile, and the tile is inside the New South Wales region.
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia);
        Position nsw = AustraliaColonyStart.StartTile(AustraliaColony.NewSouthWales);

        Assert.Equal("model.region.newSouthWales", game.Map.RegionOf(nsw)!.Key);
        AssertHumanLandedAt(game, nsw);
    }

    // --- Colony start scenarios (86d3mm2ug) ------------------------------------------

    [Theory]
    [InlineData(AustraliaColony.NewSouthWales, "model.region.newSouthWales")]
    [InlineData(AustraliaColony.Victoria, "model.region.victoria")]
    [InlineData(AustraliaColony.Queensland, "model.region.queensland")]
    [InlineData(AustraliaColony.SouthAustralia, "model.region.southAustralia")]
    [InlineData(AustraliaColony.Tasmania, "model.region.tasmania")]
    [InlineData(AustraliaColony.WesternAustralia, "model.region.westernAustralia")]
    public void ColonyStart_LandsTheHuman_OnThatColonysCoast(AustraliaColony colony, string expectedKey)
    {
        // Each selectable start resolves to a coastal tile inside its own named region…
        Position start = AustraliaColonyStart.StartTile(colony);
        GameMap map = FixedMap.LoadAustralia(Australia);
        Assert.Equal(expectedKey, map.RegionOf(start)!.Key);
        Assert.Equal(expectedKey, AustraliaColonyStart.RegionKey(colony));
        Assert.False(map.TerrainAt(start).IsWater, "a colony start must be a land tile");

        // …and booting a game with that colony's import override lands the human's party on-or-beside that tile.
        MapImportResult import = AustraliaColonyStart.ImportFor(colony, Australia);
        Assert.Equal(start, import.HumanStart);
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia, importOverride: import);
        AssertHumanLandedAt(game, start);
    }

    [Fact]
    public void ColonyStart_RegistryLists_AllSixInFederationOrder()
    {
        Assert.Equal(
            new[]
            {
                AustraliaColony.NewSouthWales, AustraliaColony.Victoria, AustraliaColony.Queensland,
                AustraliaColony.SouthAustralia, AustraliaColony.Tasmania, AustraliaColony.WesternAustralia,
            },
            AustraliaColonyStart.All);
        Assert.Equal(AustraliaColony.NewSouthWales, AustraliaColonyStart.Default);
    }

    [Fact]
    public void ColonyStart_DefaultColony_MatchesTheMapsOwnFirstFleetStart()
    {
        // NSW is the map's baked-in landfall, so its override HumanStart equals the plain import's — the default colony
        // boots the same game as an ordinary Australia game (no relocation).
        MapImportResult plain = FixedMap.ImportAustralia(Australia);
        MapImportResult nsw = AustraliaColonyStart.ImportFor(AustraliaColony.NewSouthWales, Australia);
        Assert.Equal(plain.HumanStart, nsw.HumanStart);
    }

    /// <summary>Asserts the human landed on the Australia map with a land unit on <paramref name="start"/> or an adjacent tile (the ship berths on adjacent water).</summary>
    private static void AssertHumanLandedAt(Game game, Position start)
    {
        Assert.NotEmpty(game.PlayerUnits);
        Assert.Contains(game.PlayerUnits, u =>
            u.IsOnMap && !u.Type.IsNaval && (u.Position == start || u.Position.IsAdjacentTo(start)));
    }
}
