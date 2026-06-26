using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// The New-Game setup dials threaded into <see cref="Game.New"/> (86d3fq1df / 86d3fq1fd / 86d3fq13u / 86d3fq18b /
/// 86d3fq1b8 / 86d3fq0za): configurable rival count, starting year, climate bands, map-gen counts, lost-city-rumour
/// number and the national-advantages toggle. Each test asserts the dial changes <see cref="Game.New"/>'s output as
/// expected and that the default (omitted) options stay byte-identical to the historical generator (ADR-009).
/// </summary>
public class NewGameSetupOptionsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    private static int RivalCount(Game game) =>
        game.Players.Count(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);

    private static int ElevationTiles(GameMap map) =>
        map.AllPositions().Count(p => map.TerrainAt(p).IsElevation);

    private static int RiverTiles(GameMap map) =>
        map.AllPositions().Count(map.HasRiver);

    // ---- Determinism: default options are byte-identical (ADR-009) ----

    [Fact]
    public void DefaultOptions_AreByteIdentical_AcrossAllNewDials()
    {
        // A Game.New with every new dial left at its default must produce the identical map (terrain, resources,
        // rivers) and the identical rival roster as a plain Game.New — the historical generator, unchanged.
        Game baseline = Game.New(Classic, Seed);
        Game withDefaults = Game.New(
            Classic, Seed,
            foreignPowerCount: null,
            mapOptions: MapGenerationOptions.Classic,
            rumourNumber: LostCityRumourGenerator.DefaultRumourNumber,
            nationalAdvantages: NationalAdvantages.Selectable);

        Assert.Equal(
            baseline.Map.AllPositions().Select(p => baseline.Map.TerrainAt(p).Id),
            withDefaults.Map.AllPositions().Select(p => withDefaults.Map.TerrainAt(p).Id));
        Assert.Equal(baseline.Map.Resources.Count, withDefaults.Map.Resources.Count);
        Assert.Equal(RiverTiles(baseline.Map), RiverTiles(withDefaults.Map));
        Assert.Equal(RivalCount(baseline), RivalCount(withDefaults));
        Assert.Equal(baseline.Map.Rumours.Count, withDefaults.Map.Rumours.Count);
    }

    [Fact]
    public void SameSeedSameOptions_IsByteIdentical()
    {
        var options = MapGenerationOptions.Classic with { MountainNumber = 6, RiverNumber = 25, TemperatureBias = 8 };
        Game a = Game.New(Classic, Seed, foreignPowerCount: 5, mapOptions: options, rumourNumber: 12);
        Game b = Game.New(Classic, Seed, foreignPowerCount: 5, mapOptions: options, rumourNumber: 12);

        Assert.Equal(
            a.Map.AllPositions().Select(p => a.Map.TerrainAt(p).Id),
            b.Map.AllPositions().Select(p => b.Map.TerrainAt(p).Id));
        Assert.Equal(a.Map.Resources, b.Map.Resources);
        Assert.Equal(RivalCount(a), RivalCount(b));
        Assert.Equal(a.Map.Rumours.OrderBy(p => p.Y).ThenBy(p => p.X),
                     b.Map.Rumours.OrderBy(p => p.Y).ThenBy(p => p.X));
    }

    // ---- Rival count (86d3fq1df) ----

    [Fact]
    public void DefaultRivalCount_IsThree()
    {
        Assert.Equal(3, RivalCount(Game.New(Classic, Seed)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public void ChosenRivalCount_LandsThatManyRivalPowers(int wanted)
    {
        Game game = Game.New(Classic, Seed, foreignPowerCount: wanted);
        Assert.Equal(wanted, RivalCount(game));
    }

    [Fact]
    public void RivalCount_IsClampedToTheRulesetsSelectableNations()
    {
        // The classic ruleset has 8 selectable non-REF European nations; asking for far more lands at most that many.
        int selectable = Classic.EuropeanNations.Count(n => n.Selectable && !n.IsRef);
        Game game = Game.New(Classic, Seed, foreignPowerCount: 99);
        Assert.Equal(selectable, RivalCount(game));
    }

    [Fact]
    public void RivalCount_ExcludesTheHumansOwnNation()
    {
        // When the human picks a nation, the rival pool excludes it: asking for "all" lands selectable-minus-one.
        string human = Classic.EuropeanNations.First(n => n.Selectable && !n.IsRef).Id;
        int selectable = Classic.EuropeanNations.Count(n => n.Selectable && !n.IsRef);
        Game game = Game.New(Classic, Seed, humanNationId: human, foreignPowerCount: 99);

        Assert.Equal(selectable - 1, RivalCount(game));
        Assert.DoesNotContain(game.Players.Where(p => !p.IsHuman), p => p.NationId == human);
    }

    // ---- Starting year (86d3fq1fd) — via Ruleset.WithStartingYear ----

    [Fact]
    public void DefaultStartingYear_IsClassic1492()
    {
        Assert.Equal(1492, Game.New(Classic, Seed).CurrentYear);
    }

    [Fact]
    public void ChosenStartingYear_ShowsOnTheCalendar()
    {
        Ruleset r = Ruleset.LoadClassic().WithStartingYear(1600);
        Game game = Game.New(r, Seed);
        Assert.Equal(1600, game.CurrentYear); // turn 1 now maps to the chosen year
    }

    [Fact]
    public void WithStartingYear_KeepsSeasonYearAboveTheStart()
    {
        // A start year on/after the classic season-split (1600) pushes the split year up so the one-turn-per-year era
        // is never empty (FreeCol keeps startingYear < seasonYear). Turn 1 still has no season.
        Ruleset r = Ruleset.LoadClassic().WithStartingYear(1700);
        Assert.True(r.Calendar.SeasonYear > r.Calendar.StartingYear);
        Assert.False(r.Calendar.HasSeason(1)); // turn 1 is still a bare-year era
    }

    [Fact]
    public void WithStartingYear_DefaultValue_IsByteIdenticalCalendar()
    {
        Ruleset r = Ruleset.LoadClassic().WithStartingYear(1492);
        Assert.Equal(Calendar.Classic.StartingYear, r.Calendar.StartingYear);
        Assert.Equal(Calendar.Classic.SeasonYear, r.Calendar.SeasonYear);
        Assert.Equal(Calendar.Classic.Seasons, r.Calendar.Seasons);
    }

    // ---- Map-gen counts (86d3fq18b) ----

    [Fact]
    public void MoreRivers_ProducesMoreRiverTiles_AtTheSameSeed()
    {
        int few = RiverTiles(Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { RiverNumber = 5 }).Map);
        int many = RiverTiles(Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { RiverNumber = 40 }).Map);
        Assert.True(many > few, $"expected more river tiles at riverNumber=40 ({many}) than at 5 ({few})");
    }

    [Fact]
    public void FewerMountains_ProducesFewerElevationTiles_AtTheSameSeed()
    {
        // mountainNumber is "one elevation per N land tiles", so a HIGHER number means FEWER elevation tiles.
        int dense = ElevationTiles(Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { MountainNumber = 5 }).Map);
        int sparse = ElevationTiles(Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { MountainNumber = 30 }).Map);
        Assert.True(dense > sparse, $"expected more elevation at mountainNumber=5 ({dense}) than at 30 ({sparse})");
    }

    [Fact]
    public void MoreBonusResources_ProducesMoreResources_AtTheSameSeed()
    {
        int few = Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { LandBonusChance = 0.02 }).Map.Resources.Count;
        int many = Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { LandBonusChance = 0.50 }).Map.Resources.Count;
        Assert.True(many > few, $"expected more resources at bonusChance=0.50 ({many}) than at 0.02 ({few})");
    }

    [Fact]
    public void MoreForest_ProducesMoreForestedTiles_AtTheSameSeed()
    {
        int ForestTiles(GameMap map) => map.AllPositions().Count(p => map.TerrainAt(p).IsForest);
        int few = ForestTiles(Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { ForestChance = 0.10 }).Map);
        int many = ForestTiles(Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { ForestChance = 0.85 }).Map);
        Assert.True(many > few, $"expected more forest at forestChance=0.85 ({many}) than at 0.10 ({few})");
    }

    // ---- Climate bands (86d3fq13u) ----

    [Fact]
    public void AWarmerClimate_ShiftsTheTerrainMix_AtTheSameSeed()
    {
        // A large positive temperature bias re-picks climate terrain from a hotter envelope, so the terrain mix differs
        // from the unbiased map (faithful to FreeCol's temperature band). The land footprint is the same (the bias is
        // RNG-free), so any difference is the climate re-pick, not a different continent.
        Game cool = Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { TemperatureBias = -15 });
        Game warm = Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { TemperatureBias = 15 });

        var coolTerrain = cool.Map.AllPositions().Select(p => cool.Map.TerrainAt(p).Id).ToList();
        var warmTerrain = warm.Map.AllPositions().Select(p => warm.Map.TerrainAt(p).Id).ToList();
        Assert.NotEqual(coolTerrain, warmTerrain); // the climate dial changed the terrain
    }

    [Fact]
    public void AHumidityBias_ShiftsTheTerrainMix_AtTheSameSeed()
    {
        Game dry = Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { HumidityBias = -40 });
        Game wet = Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { HumidityBias = 40 });

        var dryTerrain = dry.Map.AllPositions().Select(p => dry.Map.TerrainAt(p).Id).ToList();
        var wetTerrain = wet.Map.AllPositions().Select(p => wet.Map.TerrainAt(p).Id).ToList();
        Assert.NotEqual(dryTerrain, wetTerrain);
    }

    [Fact]
    public void ZeroClimateBias_IsByteIdenticalToTheDefault()
    {
        Game biased = Game.New(Classic, Seed, mapOptions: MapGenerationOptions.Classic with { TemperatureBias = 0, HumidityBias = 0 });
        Game baseline = Game.New(Classic, Seed);
        Assert.Equal(
            baseline.Map.AllPositions().Select(p => baseline.Map.TerrainAt(p).Id),
            biased.Map.AllPositions().Select(p => biased.Map.TerrainAt(p).Id));
    }

    // ---- Lost-city-rumour count (86d3fq1b8) ----

    [Fact]
    public void MoreRumours_ProducesMoreRumourTiles_AtTheSameSeed()
    {
        // rumourNumber is "one rumour per N land tiles", so a LOWER number means MORE rumours.
        int few = Game.New(Classic, Seed, rumourNumber: 80).Map.Rumours.Count;
        int many = Game.New(Classic, Seed, rumourNumber: 10).Map.Rumours.Count;
        Assert.True(many > few, $"expected more rumours at rumourNumber=10 ({many}) than at 80 ({few})");
    }

    [Fact]
    public void DefaultRumourNumber_MatchesTheClassicConstant()
    {
        Assert.Equal(35, LostCityRumourGenerator.DefaultRumourNumber);
        // The default Game.New rumour count equals an explicit-default call.
        Assert.Equal(
            Game.New(Classic, Seed).Map.Rumours.Count,
            Game.New(Classic, Seed, rumourNumber: LostCityRumourGenerator.DefaultRumourNumber).Map.Rumours.Count);
    }

    // ---- National advantages toggle (86d3fq0za) ----

    [Fact]
    public void NationalAdvantages_DefaultsToSelectable()
    {
        Assert.Equal(NationalAdvantages.Selectable, Game.New(Classic, Seed).NationalAdvantages);
    }

    [Fact]
    public void AdvantagesOff_SuppressesTheHumansNationModifiers()
    {
        // The English carry a -33% religious-unrest immigration bonus (a nation-type modifier). With advantages OFF the
        // human's immigration target equals the nation-less baseline; with advantages ON it is lower (the bonus applies).
        string english = "model.nation.english";
        int baseRequired = Game.New(Classic, Seed).ImmigrationRequired; // nation-less human (effective target)
        int withAdvantage = Game.New(Classic, Seed, humanNationId: english,
            nationalAdvantages: NationalAdvantages.Selectable).ImmigrationRequired;
        int advantagesOff = Game.New(Classic, Seed, humanNationId: english,
            nationalAdvantages: NationalAdvantages.None).ImmigrationRequired;

        Assert.True(withAdvantage < baseRequired, "English advantage should lower the immigration target");
        Assert.Equal(baseRequired, advantagesOff); // OFF → no advantage, back to the neutral baseline
    }

    [Fact]
    public void Fixed_AndSelectable_KeepTheAdvantage_OnlyNoneSuppressesIt()
    {
        // Fixed and Selectable are gameplay-identical for us (the player already picks the nation): both keep the
        // advantage; only None removes it.
        string english = "model.nation.english";
        int selectable = Game.New(Classic, Seed, humanNationId: english, nationalAdvantages: NationalAdvantages.Selectable).ImmigrationRequired;
        int @fixed = Game.New(Classic, Seed, humanNationId: english, nationalAdvantages: NationalAdvantages.Fixed).ImmigrationRequired;
        int none = Game.New(Classic, Seed, humanNationId: english, nationalAdvantages: NationalAdvantages.None).ImmigrationRequired;

        Assert.Equal(selectable, @fixed);
        Assert.True(none > selectable); // None loses the bonus → a higher target
    }
}
