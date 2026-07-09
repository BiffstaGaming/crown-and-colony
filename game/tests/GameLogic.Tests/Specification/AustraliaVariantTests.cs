using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
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

        // Two turns per year begin at the 1851 gold rush (the doc-03 Gold-Rush era boundary), not 1830 — the busy
        // Gold-Rush → Federation half of the campaign gets finer granularity while the sparse founding era stays one
        // deliberate turn per year (spec fix 2026-07-09; the comment said "gold rush" but the value read 1830).
        Assert.Equal(1851, Australia.Calendar.SeasonYear);
        // The Pioneer age boundaries are real campaign-era lines from doc 03: 1830 (Separate Colonies) and 1889
        // (Federation Movement) — so the Democracy & Federation Pioneers become common exactly as the campaign opens.
        Assert.Equal(1, Australia.AgeForYear(1788)); // founding era
        Assert.Equal(2, Australia.AgeForYear(1830)); // growth & gold begins
        Assert.Equal(2, Australia.AgeForYear(1888)); // still age 2 the year before Federation
        Assert.Equal(3, Australia.AgeForYear(1889)); // Federation Movement opens age 3
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
    public void AustraliaRuleset_HasADefaultColonyNameBucket_SydneyFirst()
    {
        // The variant default bucket (model.nation.default) is what the NATION-LESS human founds by — Sydney first
        // (86d3-P8 fix 1). Without it the human would fall back to the hard-coded American static list.
        Assert.NotEmpty(Australia.DefaultColonyNames);
        Assert.Equal("Sydney", Australia.DefaultColonyNames[0]);
        Assert.DoesNotContain("Jamestown", Australia.DefaultColonyNames);
        foreach (string town in new[] { "Melbourne", "Adelaide", "Brisbane", "Perth", "Hobart", "Parramatta" })
        {
            Assert.Contains(town, Australia.DefaultColonyNames);
        }
    }

    [Fact]
    public void ClassicRuleset_HasNoDefaultColonyNameBucket_SoItsFallbackIsByteIdentical()
    {
        // Classic ships no model.nation.default bucket, so DefaultColonyNames is empty and Game.ColonyNamesFor keeps
        // the hard-coded American fallback (Jamestown/Plymouth/…) — classic colony names are unchanged (ADR-018).
        Assert.Empty(Ruleset.LoadClassic().DefaultColonyNames);
    }

    [Fact]
    public void AustraliaGame_FoundsSydney_AsTheHumansFirstColony()
    {
        // End-to-end: the nation-less human's first colony reads its name from the variant default bucket, so an
        // Australian game founds "Sydney" (the First Fleet), not "Jamestown" (86d3-P8 fix 1).
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia);

        // Walk the on-map colonist onto open land and found — mirrors AmericaGameTests' founding loop.
        Colony? first = null;
        for (int turn = 0; turn < 20 && first is null; turn++)
        {
            Unit? unit = game.PlayerUnits.FirstOrDefault(u => u.IsOnMap && !u.Type.IsNaval);
            if (unit is not null)
            {
                if (game.CheckFoundColony(unit).Allowed)
                {
                    first = game.FoundColony(unit);
                    break;
                }
                Position? next = unit.Position.Neighbours()
                    .Where(n => game.CheckMove(unit, n).Allowed)
                    .Cast<Position?>()
                    .FirstOrDefault();
                if (next is not null)
                {
                    game.MoveUnit(unit, next.Value);
                }
            }
            game.EndTurn();
        }

        Assert.NotNull(first);
        Assert.Equal("Sydney", first!.Name);
    }

    // --- First Nations naming pass (86d3-P8 fix 2) -----------------------------------

    [Fact]
    public void AustraliaNatives_UseFirstNationsPeoples_NotTheClassicTribes()
    {
        // The classic native nations were renamed to real Australian First Nations peoples (a naming pass only —
        // the display name derives from the id suffix, so renaming the id renames the tribe). Eora (Sydney/coastal
        // NSW) is the anchor; none of the classic New-World tribe ids survive.
        var ids = Australia.NativeNationTypes.Select(n => n.Id).ToHashSet();
        Assert.Equal(8, Australia.NativeNationTypes.Count);
        foreach (string id in new[]
                 {
                     "model.nationType.eora", "model.nationType.kulin",
                     "model.nationType.arrernte", "model.nationType.yawuru", "model.nationType.larrakia",
                     "model.nationType.yolngu", "model.nationType.noongar", "model.nationType.wangkatja",
                 })
        {
            Assert.Contains(id, ids);
        }

        // The classic tribe ids are gone from the Australia spec.
        foreach (string classicId in new[]
                 {
                     "model.nationType.apache", "model.nationType.sioux", "model.nationType.tupi",
                     "model.nationType.arawak", "model.nationType.cherokee", "model.nationType.iroquois",
                     "model.nationType.inca", "model.nationType.aztec",
                 })
        {
            Assert.DoesNotContain(classicId, ids);
        }

        // The display name resolves to the new people (id suffix → capitalised).
        Assert.Equal("eora", Australia.NativeNation("model.nationType.eora").ShortName);
    }

    [Fact]
    public void AustraliaNatives_NoFirstNationsPeopleIsRenderedAsACityEmpire()
    {
        // The classic aztec/inca used the advanced-empire "city" settlement template; when renamed they were
        // re-based to the `village` template so no First Nations people is a walled city empire (docs 15/16/19).
        // Every native nation now founds camps or villages — never a city settlement.
        foreach (NativeNationType nation in Australia.NativeNationTypes)
        {
            Assert.DoesNotContain("city", nation.SettlementTypeId);
            Assert.DoesNotContain("inca", nation.SettlementTypeId);
            Assert.DoesNotContain("aztec", nation.SettlementTypeId);
        }

        // The Noongar (ex-inca) and Wangkatja (ex-aztec) both found ordinary villages now.
        Assert.Equal("model.settlement.village", Australia.NativeNation("model.nationType.noongar").SettlementTypeId);
        Assert.Equal("model.settlement.village", Australia.NativeNation("model.nationType.wangkatja").SettlementTypeId);
    }

    [Fact]
    public void AustraliaNatives_NoFirstNationsPeopleIsInnatelyWarlike()
    {
        // doc 15 §5: First Nations resistance must be *contextual* — a response to land pressure and broken
        // agreements — never an innate "warlike" trait. The classic reskin left three peoples (Eora/Arrernte/
        // Wangkatja) at aggression="high" inherited from the Aztec/warlike source types; that innate-high framing
        // is neutralised (spec fix 2026-07-09). The holistic contextual-resistance system is deferred to 4b/ADR-022.
        foreach (NativeNationType nation in Australia.NativeNationTypes)
        {
            Assert.NotEqual(NativeAggression.High, nation.Aggression);
        }

        // The three formerly-"high" peoples are now Average — no First Nations people is modelled as inherently
        // more hostile than another.
        foreach (string id in new[] { "model.nationType.eora", "model.nationType.arrernte", "model.nationType.wangkatja" })
        {
            Assert.Equal(NativeAggression.Average, Australia.NativeNation(id).Aggression);
        }
    }

    [Fact]
    public void HistoricalEvents_CarryAuthoredPopupText_ForTheDilemmas()
    {
        // WS1.1b: the multi-option dilemmas carry authored name/prompt/labels so the event popup renders real prose,
        // not humanized ids. (Unauthored events would fall back to the humanized id — but all 31 are now authored.)
        EventDef eureka = Australia.HistoricalEvent("event.eurekaStockade")!;
        Assert.Equal("The Eureka Stockade", eureka.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(eureka.Prompt));
        Assert.Equal("Concede reform", eureka.Option("reform")!.DisplayLabel);
        Assert.Equal("Send in the troops", eureka.Option("suppress")!.DisplayLabel);

        // The adversarial verify pass corrected the gold-immigration prompt for accuracy (the *gold colonies'*
        // population tripled, not the whole continent's) — the corrected text is what shipped.
        Assert.Contains("gold colonies", Australia.HistoricalEvent("event.goldImmigrationSurge")!.Prompt);
    }

    [Fact]
    public void AustraliaCatalog_CarriesTheSetupEventAndBatchOne_WhileClassicHasNone()
    {
        // Classic defines zero historical events → the event runtime is a strict no-op and classic replays
        // byte-identically (86d3mmbfn/86d3mmb3r; ADR-023). Only the Australia variant carries a catalogue.
        Assert.Empty(Ruleset.LoadClassic().HistoricalEvents);
        Assert.True(Australia.HistoricalEvents.Count >= 10, "the 1788-1830 batch + setup event should be present");

        // The forced setup event fires once at scenario start (the First Fleet landing).
        EventDef sydney = Australia.HistoricalEvent("event.sydneyCoveEstablished")!;
        Assert.NotNull(sydney);
        Assert.Equal(EventTrigger.ScenarioStart, sydney.Trigger);
        Assert.True(sydney.OneShot);

        // A representative batch event — the merino-sheep wool boost, era-gated to the early colony, applying a
        // timed modifier to the wool good (id `cotton`, displayed as "Wool" via the reskin's DisplayOverrides).
        EventDef merino = Australia.HistoricalEvent("event.merinoSheep")!;
        Assert.NotNull(merino);
        Assert.Equal(1797, merino.EarliestYear);
        Assert.Contains(
            merino.Options.SelectMany(o => o.Effects),
            e => e.Kind == EventEffectKind.TimedModifier && e.TargetId == "model.goods.cotton");
    }

    [Fact]
    public void AustraliaCatalog_CarriesBatchesTwoAndThree_ExpansionGoldAndFederation()
    {
        // Batches 2 (1830-1872: expansion & gold) and 3 (1872-1901: infrastructure & Federation) are authored as
        // data appended to the batch-1 catalogue (86d3mmbg9). A clean parse is proven by the ruleset loading at all —
        // a malformed id/attribute makes the whole Australia ruleset fail to load and every Australia test go red — so
        // reaching this assertion means all ~30 event-defs parsed. Here we spot-check the marquee events of each batch.

        // The catalogue grew well past batch 1's 11 (setup + 10). Batch 2 (~9) + batch 3 (~12) take it to ~30.
        Assert.True(
            Australia.HistoricalEvents.Count >= 28,
            $"expected batches 1-3 (~30 events), got {Australia.HistoricalEvents.Count}");

        // Batch 2 marquee: the 1851 gold rush — a one-shot bonanza that fills the treasury, draws immigrants, and
        // lifts the gold good (id `silver`, displayed as "Gold"). Year-gated to 1851.
        EventDef goldRush = Australia.HistoricalEvent("event.goldRush")!;
        Assert.NotNull(goldRush);
        Assert.True(goldRush.OneShot);
        Assert.Equal(1851, goldRush.EarliestYear);
        var goldRushEffects = goldRush.Options.SelectMany(o => o.Effects).ToList();
        Assert.Contains(goldRushEffects, e => e.Kind == EventEffectKind.GrantGold && e.Value > 0);
        Assert.Contains(goldRushEffects, e => e.Kind == EventEffectKind.GrantUnit);

        // Batch 2 also carries the Eureka Stockade as a genuine dilemma (reform vs. suppress) — reform grants a large
        // civic-voice (liberty) gain.
        EventDef eureka = Australia.HistoricalEvent("event.eurekaStockade")!;
        Assert.NotNull(eureka);
        Assert.True(eureka.Options.Count >= 2, "Eureka is a dilemma with a reform option and a suppress option");
        Assert.Contains(
            eureka.Options.SelectMany(o => o.Effects),
            e => e.Kind == EventEffectKind.GrantLiberty && e.Value > 0);

        // Batch 3 marquee: a Federation-convention event and a Federation-referendum event, both year-gated into the
        // 1897-1901 Federation window and granting civic voice (liberty) toward the constitutional settlement.
        EventDef convention = Australia.HistoricalEvent("event.federationConvention")!;
        Assert.NotNull(convention);
        Assert.True(convention.EarliestYear >= 1893, "the Federation conventions are a Federation-era event");
        Assert.Contains(
            convention.Options.SelectMany(o => o.Effects),
            e => e.Kind == EventEffectKind.GrantLiberty && e.Value > 0);

        EventDef referendum = Australia.HistoricalEvent("event.federationReferendum")!;
        Assert.NotNull(referendum);
        Assert.Equal(1898, referendum.EarliestYear);

        // Batch 3 also carries the Overland Telegraph (1872) and Broken Hill (1883) as one-shot milestones.
        Assert.NotNull(Australia.HistoricalEvent("event.overlandTelegraph"));
        Assert.True(Australia.HistoricalEvent("event.overlandTelegraph")!.OneShot);
        Assert.NotNull(Australia.HistoricalEvent("event.brokenHill"));

        // The two settlement-gated colony effects (inland-exploration reveal, payable-field ore find) carry a
        // settlements>=1 requires gate so they can never fire before the first colony exists.
        foreach (string gatedId in new[] { "event.inlandExploration", "event.payableField" })
        {
            EventDef gated = Australia.HistoricalEvent(gatedId)!;
            Assert.NotNull(gated);
            Assert.NotEmpty(gated.Requirements); // a <requires> settlements>=1 limit is present
        }

        // No event in the catalogue references a goods or unit id that the Australia ruleset does not define — an
        // invalid id would otherwise surface only at runtime. Ruleset.Goods/Unit THROW KeyNotFoundException on an
        // unknown id, so a bad reference here fails this test loudly. (revealMap/movementBonus carry no goods/unit id.)
        foreach (EventEffect effect in Australia.HistoricalEvents.SelectMany(ev => ev.Options).SelectMany(o => o.Effects))
        {
            if (effect.Kind is EventEffectKind.TimedModifier or EventEffectKind.GrantGoods
                && effect.TargetId is { } target && target.StartsWith("model.goods."))
            {
                Assert.NotNull(Australia.Goods(target));
            }
            if (effect.Kind == EventEffectKind.GrantUnit && effect.TargetId is { } unitId)
            {
                Assert.NotNull(Australia.Unit(unitId));
            }
        }
    }

    [Fact]
    public void AustraliaVictory_IsOnlyByReferendum_NotTheClassicWarOrLastPowerWins()
    {
        // The Australian Federation is won SOLELY by the referendum → Commonwealth proclamation (Chris 2026-07-09):
        // the classic War-of-Independence / last-power-standing wins are off, and Game.Winner makes the Federation
        // victory exclusive whenever it is enabled.
        Assert.True(GameVariants.Australia.ReferendumVictoryOnly);
        Assert.False(GameVariants.ClassicAmerica.ReferendumVictoryOnly);

        // The ruleset options agree: only the Federation victory is on.
        Assert.True(Australia.VictoryFederation);
        Assert.False(Australia.VictoryDefeatRef);
        Assert.False(Australia.VictoryDefeatEuropeans);

        // With 0 rival powers the human is the only European from turn 1 — under the classic "defeat all Europeans"
        // condition that would be an instant win. Here a fresh (pre-Commonwealth) Australia game has NO winner: you
        // must federate. (Classic, which ships that condition on, is unaffected — Game.Winner's Federation branch is
        // skipped when VictoryFederation is off.)
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia, foreignPowerCount: 0);
        Assert.Null(game.Winner);
        game.EndTurn();
        Assert.Null(game.Winner);
    }

    [Fact]
    public void AustraliaVariant_FixesItsWorld_TheAustraliaMap_AndZeroRivalPowers()
    {
        // The Australian Federation is fixed to the authored Australia continent and to a colonial contest of ONE —
        // historically the continent was British-settled alone (the Dutch/French charted the coast but founded no
        // colonies), so there are 0 rival European powers (Chris 2026-07-09). The New-Game dialog reads these to
        // default + lock the map and rival-power pickers. Classic fixes neither (the player chooses freely).
        Assert.Equal(MapSource.Australia, GameVariants.Australia.ForcedMapSource);
        Assert.Equal(0, GameVariants.Australia.DefaultForeignPowerCount);
        Assert.Null(GameVariants.ClassicAmerica.ForcedMapSource);
        Assert.Null(GameVariants.ClassicAmerica.DefaultForeignPowerCount);
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
