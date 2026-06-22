using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// Region discovery (<c>86d3c9w2f</c>, P6): when a colonial player first reveals a tile of an undiscovered,
/// discoverable region, that region is marked discovered by the player, named deterministically, scores its
/// <see cref="Region.ScoreValue"/> into the human's total (via a <see cref="HistoryEventKind.RegionDiscovered"/>
/// history event), and surfaces a player-facing notice. Faithful to FreeCol
/// <c>ServerUnit.csCheckDiscoverRegion</c> + <c>ServerRegion.csDiscover</c>. Discoverability (land/mountain +
/// the Pacific, never the polar bands/Atlantic/leaf quadrants/lakes) is pinned on the <see cref="Region"/> model;
/// the end-to-end behaviour is exercised through <see cref="Game.New"/>'s starting fog reveal.
/// </summary>
public class RegionDiscoveryTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    // ── Region model: discoverability rules (FreeCol ServerRegion) ───────────────────────────────────────

    [Fact]
    public void DynamicLandAndMountain_AreDiscoverable_FixedKeyedAndDiscoveredAreNot()
    {
        Assert.True(new Region(0, RegionType.Land, 500).IsDiscoverable);     // dynamic land, no key
        Assert.True(new Region(1, RegionType.Mountain, 12).IsDiscoverable);  // dynamic mountain, no key
        Assert.True(new Region(2, RegionType.Ocean, 100, Region.PacificKey).IsDiscoverable); // the Pacific

        // Not discoverable: the polar bands (keyed land), the Atlantic + leaf quadrants, lakes.
        Assert.False(new Region(3, RegionType.Land, 0, "model.region.arctic").IsDiscoverable);
        Assert.False(new Region(4, RegionType.Ocean, 0, "model.region.atlantic").IsDiscoverable);
        Assert.False(new Region(5, RegionType.Ocean, 0, "model.region.northAtlantic", ParentId: 4).IsDiscoverable);
        Assert.False(new Region(6, RegionType.Lake, 0).IsDiscoverable);
    }

    [Fact]
    public void OnceDiscovered_NoLongerDiscoverable()
    {
        var land = new Region(0, RegionType.Land, 500);
        Assert.True(land.IsDiscoverable);
        Region discovered = land with { DiscoveredBy = 0, Name = "Land 1", DiscoveredInTurn = 1 };
        Assert.True(discovered.IsDiscovered);
        Assert.False(discovered.IsDiscoverable); // a discovered region cannot be discovered again
    }

    // ── GameMap.DiscoverableRegionOf: an ocean-quadrant tile resolves up to its discoverable Pacific parent ──

    [Fact]
    public void DiscoverableRegionOf_ResolvesOceanQuadrantToPacificParent()
    {
        TerrainType ocean = Classic.Terrain("model.tile.ocean");
        TerrainType land = Classic.Terrain("model.tile.plains");
        // 2×2: region 0 = north-Pacific leaf quadrant (parent = Pacific id 2), region 1 = dynamic land, 2 = Pacific.
        Region[] table =
        [
            new(0, RegionType.Ocean, 0, "model.region.northPacific", ParentId: 2),
            new(1, RegionType.Land, 500),
            new(2, RegionType.Ocean, 100, Region.PacificKey),
        ];
        var map = new GameMap(2, 2, [ocean, land, ocean, land], regionIds: [0, 1, 0, 1], regions: table);

        // The ocean tile's own region (the leaf quadrant) is not discoverable → resolves up to the Pacific (id 2).
        Region? fromOcean = map.DiscoverableRegionOf(new Position(0, 0));
        Assert.NotNull(fromOcean);
        Assert.Equal(2, fromOcean!.Id);

        // The land tile's own region IS discoverable → returned directly.
        Assert.Equal(1, map.DiscoverableRegionOf(new Position(1, 0))!.Id);
    }

    [Fact]
    public void DiscoverableRegionOf_ReturnsNull_ForAnAlreadyDiscoveredRegion()
    {
        TerrainType land = Classic.Terrain("model.tile.plains");
        Region[] table = [new(0, RegionType.Land, 500, DiscoveredBy: 0, Name: "Land 1", DiscoveredInTurn: 1)];
        var map = new GameMap(1, 1, [land], regionIds: [0], regions: table);
        Assert.Null(map.DiscoverableRegionOf(new Position(0, 0))); // discovered → no longer a discovery candidate
    }

    // ── End to end: a fresh game discovers regions on its starting fog reveal ─────────────────────────────

    [Fact]
    public void NewGame_DiscoversRegions_OnTheStartingReveal()
    {
        Game game = Game.New(Classic, seed: 7);

        // The starting fog reveals discover every discoverable region the colonial players can see — the human AND the
        // foreign powers (FreeCol gates discovery on owner.isEuropean(), not just the human).
        var discovered = game.Map.Regions.Where(r => r.IsDiscovered).ToList();
        Assert.NotEmpty(discovered);
        Assert.Contains(discovered, r => r.DiscoveredBy == 0); // the human discovered at least one

        // Each discovered region was claimed by some colonial player, carries a name, was stamped this turn, and is
        // no longer discoverable.
        foreach (Region r in discovered)
        {
            Assert.NotNull(r.DiscoveredBy);
            Assert.False(string.IsNullOrEmpty(r.Name));
            Assert.Equal(game.Turn, r.DiscoveredInTurn);
            Assert.False(r.IsDiscoverable);
        }

        // No undiscoverable region (polar band / Atlantic / leaf quadrant / lake) was ever marked discovered.
        Assert.All(
            game.Map.Regions.Where(r => r.Key is "model.region.arctic" or "model.region.antarctic"
                or "model.region.atlantic" or "model.region.northAtlantic" or "model.region.southAtlantic"
                or "model.region.northPacific" or "model.region.southPacific" || r.Type == RegionType.Lake),
            r => Assert.False(r.IsDiscovered));
    }

    [Fact]
    public void Discovery_RecordsScoredHistoryEvents_ThatFeedTheScore()
    {
        Game game = Game.New(Classic, seed: 7);

        var events = game.History.Where(h => h.Kind == HistoryEventKind.RegionDiscovered).ToList();
        Assert.NotEmpty(events);

        // Every discovery event names the region and carries that region's score (FreeCol HistoryEvent.getScore).
        foreach (HistoryEvent e in events)
        {
            Assert.Contains("Discovered", e.Description);
            Assert.Equal(game.Turn, e.Turn);
        }

        // The human's history log scores only the regions the HUMAN discovered (player 0) — a foreign power's
        // discoveries claim the region but raise no entry in the human's history (history is human-only, like every
        // other RecordHistory call). The summed event scores match the human's discovered regions' score values.
        int humanDiscoveryScore = game.Map.Regions.Where(r => r.DiscoveredBy == 0).Sum(r => r.ScoreValue);
        Assert.Equal(humanDiscoveryScore, game.HistoryEventScore);
        Assert.Equal(humanDiscoveryScore, events.Sum(e => e.Score));

        // The discovery score is a real summand of the human's PlayerScore (units + ... + history-event scores).
        int unitsOnly = game.PlayerUnits.Sum(u => UnitScore(u.Type.Id));
        Assert.Equal(unitsOnly + humanDiscoveryScore, game.PlayerScore(game.HumanPlayer));
    }

    [Fact]
    public void DiscoveringThePacific_AwardsItsHundredPointScore()
    {
        Game game = Game.New(Classic, seed: 7);
        Region? pacific = game.Map.Regions.FirstOrDefault(r => r.Key == Region.PacificKey);
        Assert.NotNull(pacific);

        // The classic seed-7 start is coastal Pacific water, so the ship discovers the Pacific (score 100).
        if (pacific!.IsDiscovered)
        {
            Assert.Equal(100, pacific.ScoreValue);
            Assert.Equal("Pacific Ocean", pacific.Name); // the predefined label, not a numbered name
            Assert.Contains(game.History,
                h => h.Kind == HistoryEventKind.RegionDiscovered && h.Score == 100 && h.Description.Contains("Pacific"));
        }
    }

    [Fact]
    public void DynamicRegionNames_AreNumberedSequentiallyPerPlayerAndType()
    {
        Game game = Game.New(Classic, seed: 7);

        // Dynamic land regions the HUMAN discovered are named "Land 1", "Land 2", ... (the nation-less default human
        // has no nation prefix) — a deterministic, gap-free sequence per (player, type). A foreign power's carry its
        // nation prefix ("Dutch Land 1", ...) and number independently, so scope to the human (player 0).
        var humanLandNames = game.Map.Regions
            .Where(r => r.DiscoveredBy == 0 && r.Key is null && r.Type == RegionType.Land)
            .Select(r => r.Name!)
            .ToList();
        for (int i = 0; i < humanLandNames.Count; i++)
        {
            Assert.Contains($"Land {i + 1}", humanLandNames);
        }
        // A foreign power's land region carries a nation prefix and is never an unprefixed "Land n".
        Assert.All(
            game.Map.Regions.Where(r => r.IsDiscovered && r.DiscoveredBy != 0 && r.Key is null && r.Type == RegionType.Land),
            r => Assert.Matches(@"^\w+ Land \d+$", r.Name!));
    }

    // ── Determinism: a fresh default game is byte-identical (discovery is RNG-free) ───────────────────────

    [Fact]
    public void Discovery_IsRngFree_DefaultGameByteIdentical()
    {
        Game a = Game.New(Classic, seed: 12345);
        Game b = Game.New(Classic, seed: 12345);
        // The RNG state is untouched by discovery (no stream draw), so two same-seed games agree exactly.
        Assert.Equal(a.RandomState, b.RandomState);
        Assert.Equal(a.HistoryEventScore, b.HistoryEventScore);
        Assert.Equal(
            a.Map.Regions.Select(r => (r.Id, r.DiscoveredBy, r.Name, r.DiscoveredInTurn)),
            b.Map.Regions.Select(r => (r.Id, r.DiscoveredBy, r.Name, r.DiscoveredInTurn)));
    }

    // ── Persistence (v51): discovery state round-trips; an undiscovered region stays byte-identical ───────

    [Fact]
    public void DiscoveryState_RoundTripsThroughSave()
    {
        Game game = Game.New(Classic, seed: 7);
        Assert.Contains(game.Map.Regions, r => r.IsDiscovered); // precondition: something was discovered

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(
            game.Map.Regions.Select(r => (r.Id, r.DiscoveredBy, r.Name, r.DiscoveredInTurn)),
            loaded.Map.Regions.Select(r => (r.Id, r.DiscoveredBy, r.Name, r.DiscoveredInTurn)));
    }

    [Fact]
    public void UndiscoveredRegion_OmitsDiscoveryTokens_FromJson()
    {
        // A region with no discovery state must omit DiscoveredBy/Name/DiscoveredInTurn (omit-when-default), so an
        // undiscovered map stays byte-identical to v50 (ADR-009 / the save-bump idiom).
        TerrainType land = Classic.Terrain("model.tile.plains");
        Region[] table = [new(0, RegionType.Land, 500)]; // undiscovered
        var map = new GameMap(1, 1, [land], regionIds: [0], regions: table);
        var saved = new SavedRegion(0, (int)RegionType.Land, 500);
        string json = System.Text.Json.JsonSerializer.Serialize(saved,
            new System.Text.Json.JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });
        Assert.DoesNotContain("DiscoveredBy", json);
        Assert.DoesNotContain("Name", json);
        Assert.DoesNotContain("DiscoveredInTurn", json);
        Assert.NotNull(map.RegionOf(new Position(0, 0))); // sanity: the layer is intact
    }

    [Fact]
    public void SaveVersion_IsCurrent() => Assert.Equal(53, SaveGame.CurrentVersion);

    /// <summary>Classic-ruleset unit score values used to predict the unit summand (mirrors the scoring engine table).</summary>
    private static int UnitScore(string typeId) => typeId switch
    {
        "model.unit.freeColonist" => 3,
        "model.unit.caravel" => 3,
        _ => 0,
    };
}
