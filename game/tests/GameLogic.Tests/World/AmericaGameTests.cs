using System.IO;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// New games started on FreeCol's fixed America map (<see cref="MapSource.America"/>): the loaded terrain becomes the
/// world, our generators lay rivers/resources/regions on top, and a full game can be played on it. The default map
/// source stays the procedural New World, unchanged byte-for-byte.
/// </summary>
public class AmericaGameTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void America_NewGame_BuildsTheFixedAmericaMap_IgnoringWorldSizeArgs()
    {
        // Pass deliberately wrong world-size args: on a fixed map they must be ignored (the loaded grid sets the size).
        Game game = Game.New(Classic, seed: 42, mapWidth: 99, mapHeight: 7, mapSource: MapSource.America);

        Assert.Equal(40, game.Map.Width);
        Assert.Equal(180, game.Map.Height);
    }

    [Fact]
    public void America_NewGame_LaysRiversResourcesAndRegionsOnTheLoadedTerrain()
    {
        // FixedMap loads terrain only; Game.New runs the shared decorate pass (MapGenerator.DecorateFixedMap), so the
        // played map carries the river, bonus-resource and region layers a scenario file doesn't include.
        Game game = Game.New(Classic, seed: 42, mapSource: MapSource.America);

        Assert.Contains(game.Map.AllPositions(), p => game.Map.HasRiver(p));
        Assert.NotEmpty(game.Map.Resources);
        Assert.True(game.Map.Regions.Count > 1, "expected the terrain partitioned into multiple named regions");
    }

    [Fact]
    public void America_NewGame_PlacesTheHumanWithStartingUnits_OnCoastalLand()
    {
        Game game = Game.New(Classic, seed: 42, mapSource: MapSource.America);

        Assert.True(game.HumanPlayer.IsHuman);
        Assert.NotEmpty(game.PlayerUnits);

        // The land start is settleable land with a water berth for the caravel (FreeCol's coastal arrival).
        Unit landUnit = game.PlayerUnits.First(u => u.IsOnMap);
        Position start = landUnit.Position;
        Assert.False(game.Map.TerrainAt(start).IsWater);
        Assert.True(game.Map.TerrainAt(start).CanSettle);
        Assert.Contains(start.Neighbours(), n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater);
    }

    [Fact]
    public void America_NewGame_FoundsAColonyAndRunsTurns_InvariantsHold()
    {
        Game game = Game.New(Classic, seed: 42, mapSource: MapSource.America);

        for (int turn = 0; turn < 20; turn++)
        {
            // From turn 5, found a colony with the first on-map unit that can; otherwise shuffle it onto open land.
            Unit? unit = game.PlayerUnits.FirstOrDefault(u => u.IsOnMap);
            if (unit is not null)
            {
                if (turn >= 5 && game.CheckFoundColony(unit).Allowed)
                {
                    game.FoundColony(unit);
                }
                else
                {
                    Position? next = unit.Position.Neighbours()
                        .Where(n => game.CheckMove(unit, n).Allowed)
                        .Cast<Position?>()
                        .FirstOrDefault();
                    if (next is not null)
                    {
                        game.MoveUnit(unit, next.Value);
                    }
                }
            }

            int turnBefore = game.Turn;
            game.EndTurn();
            Assert.Equal(turnBefore + 1, game.Turn); // the world always advances on the America map too
        }

        Assert.True(game.Colonies.Count >= 1, "the human could not found a colony on the America map in 20 turns");
        Assert.All(game.Colonies, c => Assert.True(c.Population >= 1));
        Assert.All(game.Players, p => Assert.True(p.Gold >= 0));
        Assert.All(game.Explored, p => Assert.True(game.Map.InBounds(p)));
    }

    [Fact]
    public void America_NewGame_IsDeterministic_SameSeedSameSave()
    {
        string a = SaveGame.From(Game.New(Classic, seed: 7, mapSource: MapSource.America)).ToJson();
        string b = SaveGame.From(Game.New(Classic, seed: 7, mapSource: MapSource.America)).ToJson();
        Assert.Equal(a, b);
    }

    // A small scenario map that declares a [settlements] section: two apache settlements in the east column, far from
    // the ocean-fed west coast where the human lands. Used to drive Game.New's imported-settlement install path.
    private const string SettlementsScenario = """
        8 8
        ocean ocean plains plains plains plains plains plains
        ocean ocean plains plains plains plains plains plains
        ocean ocean plains plains plains plains plains plains
        ocean ocean plains plains plains plains plains plains
        ocean ocean plains plains plains plains plains plains
        ocean ocean plains plains plains plains plains plains
        ocean ocean plains plains plains plains plains plains
        ocean ocean plains plains plains plains plains plains
        [settlements]
        7 1 apache camp capital 5 expertOreMiner
        7 6 apache village regular 3
        """;

    [Fact]
    public void NewGame_FromImportedMapDeclaringSettlements_InstallsThoseExactSettlements_NotGeneratorPlaced()
    {
        // A scenario map that declares [settlements] installs those exact settlements (the scenario author placed the
        // natives) and skips the procedural generator entirely. Drive it through Game.New's real install path via the
        // test-only importOverride seam.
        MapImportResult scenario = MapImporter.Import(new StringReader(SettlementsScenario), Classic, "settlements-scenario");
        Game game = Game.New(Classic, seed: 13, importOverride: scenario);

        // Exactly the two declared settlements, at their declared tiles, with the declared nation/type/capital/size/skill.
        Assert.Equal(2, game.NativeSettlements.Count);

        NativeSettlement capital = game.NativeSettlementAt(new Position(7, 1))!;
        Assert.NotNull(capital);
        Assert.Equal("model.nationType.apache", capital.NationTypeId);
        Assert.Equal("model.settlement.camp", capital.SettlementTypeId);
        Assert.True(capital.IsCapital);
        Assert.Equal(5, capital.Size);
        Assert.Equal("model.unit.expertOreMiner", capital.LearnableSkill);

        NativeSettlement village = game.NativeSettlementAt(new Position(7, 6))!;
        Assert.NotNull(village);
        Assert.False(village.IsCapital);
        Assert.Equal(3, village.Size);
        Assert.Null(village.LearnableSkill);

        // The installed settlements were finished as the generator finishes its own: each carries wanted goods, and
        // each claims the land in its radius (so the imported natives play like generated ones).
        Assert.All(game.NativeSettlements, s => Assert.NotEmpty(s.WantedGoods));
        Assert.Contains(game.Map.AllPositions(), p => game.Map.NativeOwnerOf(p) is not null);

        // The apache nation became a Native player (its settlements reference it), exactly as a generated game would.
        Assert.Contains(game.Players, p => p.PlayerType == PlayerType.Native);
    }

    // A scenario map that fixes the human's landing tile + the REF entry tile ([starts]) and declares a per-tile region
    // layer ([regions]). The human is fixed inland at (5,4) — NOT the coastal tile the heuristic would pick — so the test
    // can tell the fixed start was honoured; the REF enters at the west-coast water tile (0,4).
    private const string StartsAndRegionsScenario = """
        8 8
        ocean plains plains plains plains plains plains plains
        ocean plains plains plains plains plains plains plains
        ocean plains plains plains plains plains plains plains
        ocean plains plains plains plains plains plains plains
        ocean plains plains plains plains plains plains plains
        ocean plains plains plains plains plains plains plains
        ocean plains plains plains plains plains plains plains
        ocean plains plains plains plains plains plains plains
        [starts]
        human 5 4
        ref 0 4
        """;

    [Fact]
    public void NewGame_FromImportedMapDeclaringStarts_FixesTheHumanAndRefEntryTiles()
    {
        // The [starts] section fixes both the human's landing and the REF's entry tile; Game.New must honour them in
        // place of its coastal-start heuristic / nearest-water REF tile.
        MapImportResult scenario =
            MapImporter.Import(new StringReader(StartsAndRegionsScenario), Classic, "starts-scenario");
        Game game = Game.New(Classic, seed: 21, importOverride: scenario);

        // The human's land units sit on the fixed start (and its free land neighbours), never the coastal heuristic tile.
        Unit landUnit = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        var startArea = new System.Collections.Generic.HashSet<Position>(
            new Position(5, 4).Neighbours()) { new(5, 4) };
        Assert.Contains(landUnit.Position, startArea);

        // The REF entry tile is the declared (0,4), not the nearest-water fallback.
        Assert.Equal(new Position(0, 4), game.RefEntryTile);
    }

    // A 2×2 scenario whose [regions] layer tags every tile, so we can prove the imported ids survive into the played
    // game rather than being re-derived by the generator's RegionGenerator.Assign.
    private const string RegionLayerScenario = """
        2 2
        plains plains
        ocean ocean
        [regions]
        region 0 Land 777
        region 1 Ocean 0 model.region.atlantic
        0 0 0
        1 0 0
        0 1 1
        1 1 1
        """;

    [Fact]
    public void NewGame_FromImportedMapDeclaringRegions_KeepsTheImportedRegionLayer_NotAReDerivedOne()
    {
        MapImportResult scenario =
            MapImporter.Import(new StringReader(RegionLayerScenario), Classic, "regions-scenario");
        Game game = Game.New(Classic, seed: 21, importOverride: scenario);

        // Exactly the two imported regions (a re-derivation would always create the 8 fixed regions first), with the
        // imported score, and every tile carries the imported id.
        Assert.Equal(2, game.Map.Regions.Count);
        Assert.Equal(777, game.Map.Regions[0].ScoreValue);
        Assert.Equal(0, game.Map.RegionIdAt(new Position(0, 0)));
        Assert.Equal(1, game.Map.RegionIdAt(new Position(0, 1)));
    }

    [Fact]
    public void America_NewGame_GeneratesNativeSettlements_BecauseTheFileDeclaresNone()
    {
        // america.txt declares no [settlements] section, so Game.New falls back to the procedural generator — the
        // America game keeps generator-placed natives (byte-identical to before this wiring).
        Assert.Empty(FixedMap.ImportAmerica(Classic).Settlements); // the file itself declares no settlements
        Game game = Game.New(Classic, seed: 42, mapSource: MapSource.America);
        Assert.NotEmpty(game.NativeSettlements); // …yet the new game has natives — placed by the generator
    }

    [Fact]
    public void DefaultSource_IsTheRandomNewWorld_NotAmerica()
    {
        // Omitting mapSource yields the historical default game: the 36×24 procedural world, byte-identical to an
        // explicit Random request (the America branch never runs for the default).
        Game defaulted = Game.New(Classic, seed: 7);
        Assert.Equal(36, defaulted.Map.Width);
        Assert.Equal(24, defaulted.Map.Height);

        Assert.Equal(
            SaveGame.From(defaulted).ToJson(),
            SaveGame.From(Game.New(Classic, seed: 7, mapSource: MapSource.Random)).ToJson());
    }
}
