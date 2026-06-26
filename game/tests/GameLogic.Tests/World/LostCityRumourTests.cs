using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// Lost City Rumour placement (<c>86d3c9uex</c>, FreeCol <c>SimpleMapGenerator.makeLostCityRumours</c>): a target
/// number of rumour tiles are scattered on land at game start â€” clear of settlements, units and the player's
/// landing â€” from a dedicated RNG stream so the human's stream 0 stays byte-identical. The reward is rolled only
/// when a unit explores one (a later slice); this slice is placement + the per-tile flag + the save (v25).
/// </summary>
public class LostCityRumourTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    // The count formula our generator uses (mirrors LostCityRumourGenerator): widthÂ·heightÂ·45% / 35.
    private static int Target(int w, int h) => w * h * 45 / 100 / 35;

    // ---- Placement ----

    [Fact]
    public void ANewGame_ScattersRumoursOnLand_AtTheTargetCount()
    {
        Game game = Game.New(Classic, Seed);
        var rumours = game.Map.Rumours;

        Assert.NotEmpty(rumours);
        Assert.True(rumours.Count <= Target(game.Map.Width, game.Map.Height));
        Assert.All(rumours, p => Assert.False(game.Map.TerrainAt(p).IsWater)); // never on water
        Assert.Equal(rumours.Count, rumours.Distinct().Count());              // no tile twice
    }

    [Fact]
    public void Placement_IsDeterministicForASeed()
    {
        var a = Game.New(Classic, Seed).Map.Rumours.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        var b = Game.New(Classic, Seed).Map.Rumours.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Rumours_AvoidThePolarRows_TheStartArea_AndOccupiedTiles()
    {
        Game game = Game.New(Classic, Seed);
        Position start = game.PlayerUnits.First().Position; // the lone starting colonist sits on the start tile
        var startArea = start.Neighbours().Append(start).ToHashSet();
        var occupied = game.Units.Where(u => u.IsOnMap).Select(u => u.Position)
            .Concat(game.NativeSettlements.Select(s => s.Position)).ToHashSet();

        Assert.All(game.Map.Rumours, p =>
        {
            Assert.True(p.Y > 2 && p.Y < game.Map.Height - 3, $"rumour on polar row at {p}");
            Assert.DoesNotContain(p, startArea);
            Assert.DoesNotContain(p, occupied);
        });
    }

    [Fact]
    public void Generator_ToleratesAnOverConstrainedMap_WithoutThrowing()
    {
        // A tiny map whose eligible region is tighter than the target: Place returns what fits, never throws.
        var map = new GameMap(6, 12, [.. Enumerable.Repeat(Classic.Terrain("model.tile.plains"), 72)]);
        var placed = LostCityRumourGenerator.Place(map, new System.Collections.Generic.HashSet<Position>(),
            new CrownAndColony.GameLogic.Randomness.Pcg32Random(Seed));
        Assert.True(placed.Count <= Target(6, 12) + 1); // bounded; many polar rows on a 12-tall map
        Assert.All(placed, p => Assert.False(map.TerrainAt(p).IsWater));
    }

    // ---- GameMap model ----

    [Fact]
    public void GameMap_TracksAndRemovesRumours()
    {
        var p = new Position(2, 2);
        var map = new GameMap(5, 5, [.. Enumerable.Repeat(Classic.Terrain("model.tile.plains"), 25)],
            rumours: [p]);

        Assert.True(map.HasRumour(p));
        Assert.Contains(p, map.Rumours);
        Assert.False(map.HasRumour(new Position(0, 0)));

        map.RemoveRumour(p);
        Assert.False(map.HasRumour(p));
        Assert.Empty(map.Rumours);
    }

    // ---- Persistence (v25, additive) ----

    [Fact]
    public void Rumours_RoundTripThroughSave()
    {
        Game game = Game.New(Classic, Seed);
        var before = game.Map.Rumours.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(before, restored.Map.Rumours.OrderBy(p => p.Y).ThenBy(p => p.X).ToList());
        Assert.Equal(63, SaveGame.CurrentVersion);
    }

    [Fact]
    public void ARumourFreeGame_OmitsTheToken_AndOldSavesLoadWithNone()
    {
        // A constructed game with no rumours serializes with no Rumours token (byte-stable vs v24); a v24-style
        // JSON (no Rumours key) loads under v25 with an empty rumour set (back-compat).
        var save = new SaveGame
        {
            Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
            MapWidth = 1, MapHeight = 1, Terrain = ["model.tile.plains"], Units = [], Explored = [],
        };
        string json = save.ToJson();
        Assert.DoesNotContain("Rumours", json);

        Game loaded = SaveGame.FromJson(json).Restore(Classic);
        Assert.Empty(loaded.Map.Rumours);
    }

    // ---- Outcome resolution (86d3c9uhj, + Fountain of Youth 86d3c9ujx, + treasure finds 86d3c9t1e) ----
    //
    // The weighted table (FreeCol LostCityRumour.chooseType) at classic medium (good 48 / bad 23 / neutral 29),
    // good outcomes listed FoY-first as in FreeCol. For a LEARNABLE explorer the cumulative ranges are
    //   FoY [0,96) | Learn [96,1536) | TribalChief [1536,2976) | Colonist [2976,3936) | Ruins [3936,4224) |
    //   Cibola [4224,4416) | ExpeditionVanishes [4416,4516) | Nothing [4516,7416)   (total 7416).
    // A non-learnable explorer drops Learn, widens Chief: FoY [0,96) | Chief [96,2496) | Colonist [2496,3936) |
    //   Ruins [3936,4224) | â€¦ (same treasure/bad/neutral tail). A scripted weighted-pick roll lands a known outcome.

    /// <summary>Deterministic RNG returning a scripted sequence of Next(..) values (weighted-pick roll, then gold).</summary>
    private sealed class ScriptedRandom(params int[] values) : IGameRandom
    {
        private readonly Queue<int> _values = new(values);
        public int Next(int maxExclusive) => _values.Dequeue();
        public int Next(int minInclusive, int maxExclusive) => _values.Dequeue();
        public double NextDouble() => 0;
        public RandomState SaveState() => new(0, 0);
    }

    private const string FreeColonist = "model.unit.freeColonist";
    private const string ExpertFarmer = "model.unit.expertFarmer";
    private const string SeasonedScout = "model.unit.seasonedScout";

    /// <summary>Spawns a human-owned land unit on a rumour-bearing land tile and returns (game, unit, tile).</summary>
    private static (Game Game, Unit Unit, Position Tile) ExplorerOnRumour(string unitTypeId = FreeColonist)
    {
        Game game = Game.New(Classic, Seed);
        Position tile = game.PlayerUnits.First().Position; // a land tile (the start tile is rumour-free, so we mark it)
        Unit unit = game.SpawnUnit(Classic.Unit(unitTypeId), tile);
        game.Map.AddRumour(tile);
        return (game, unit, tile);
    }

    [Fact]
    public void Explore_ExpeditionVanishes_RemovesTheUnit_AndConsumesTheRumour()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int id = unit.Id;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(4800)); // 4800 â†’ vanish (MOUNDS now holds [4416,4800))

        Assert.Equal(Game.LostCityRumourType.ExpeditionVanishes, outcome);
        Assert.DoesNotContain(game.Units, u => u.Id == id);
        Assert.False(game.Map.HasRumour(tile));
    }

    [Theory]
    [InlineData(0, 40)]    // gold = random.Next(80)=0  + dxÂ·5 = 40  (medium dx=8)
    [InlineData(79, 119)]  // gold = random.Next(80)=79 + dxÂ·5 = 119
    public void Explore_TribalChief_GiftsGoldToTheOwner(int goldRoll, int expectedGift)
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int before = game.HumanPlayer.Gold;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(1536, goldRoll)); // 1536 â†’ chief

        Assert.Equal(Game.LostCityRumourType.TribalChief, outcome);
        Assert.Equal(before + expectedGift, game.HumanPlayer.Gold);
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void Explore_Learn_UpgradesALearnableUnit_KeepingItsId()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour(); // a free colonist can learn
        int id = unit.Id;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(96)); // 96 â†’ learn

        Assert.Equal(Game.LostCityRumourType.Learn, outcome);
        Unit learned = game.Units.Single(u => u.Id == id);
        Assert.Equal(SeasonedScout, learned.Type.Id); // free colonist â†’ seasoned scout (model.unitChange.lostCity)
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void Explore_Colonist_MustersAFreeColonistOnTheTile()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int before = game.Units.Count;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(2976)); // 2976 â†’ colonist

        Assert.Equal(Game.LostCityRumourType.Colonist, outcome);
        Assert.Equal(before + 1, game.Units.Count);
        // A found free colonist, human-owned, standing on the rumour tile.
        Assert.True(game.Units.Count(u =>
            u.IsOnMap && u.Position == tile && u.OwnerId == 0 && !u.IsNative && u.Type.Id == FreeColonist) >= 2);
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void Explore_Nothing_LeavesUnitsAndGoldUntouched_ButConsumesTheRumour()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int unitsBefore = game.Units.Count;
        int goldBefore = game.HumanPlayer.Gold;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(4900)); // 4900 â†’ nothing (vanish now [4800,4900))

        Assert.Equal(Game.LostCityRumourType.Nothing, outcome);
        Assert.Equal(unitsBefore, game.Units.Count);
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);
        Assert.False(game.Map.HasRumour(tile));
    }

    // ---- Player-facing outcome notices (86d3c9umy) ----
    // A human exploring a non-mounds rumour resolves it inside the move with no return value the UI reads, so the
    // game records a transient RumourNotice (mirroring the AI-phase combat/raid notices) the presentation drains.

    [Theory]
    [InlineData(1536, "treasure")]   // TribalChief
    [InlineData(96, "scout")]        // Learn (free colonist learns)
    [InlineData(2976, "colonists")]  // Colonist
    [InlineData(4900, "nothing")]    // Nothing
    public void Explore_ByAHuman_RecordsAPlayerFacingNotice_ForTheTile(int roll, string expectedFragment)
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();

        game.ExploreRumour(unit, tile, new ScriptedRandom(roll, 0));

        RumourNotice notice = Assert.Single(game.RumourNotices);
        Assert.Equal(tile, notice.Position);
        Assert.Contains(expectedFragment, notice.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TakeRumourNotices_DrainsAndClearsTheCollectedNotices()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        game.ExploreRumour(unit, tile, new ScriptedRandom(1536, 0)); // a chief gift â†’ one notice

        var drained = game.TakeRumourNotices();

        Assert.Single(drained);
        Assert.Empty(game.RumourNotices); // drained: the UI reads each outcome once
    }

    [Fact]
    public void Explore_ByAForeignPower_RecordsNoHumanNotice()
    {
        // Only the human's rumour outcomes surface in the player-facing list â€” a foreign power has no UI, so its
        // explore (on its own stream) must never leak into the human's notices.
        Game game = Game.New(Classic, Seed);
        Player foreign = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Position tile = game.PlayerUnits.First().Position;
        Unit unit = game.SpawnUnit(Classic.Unit(FreeColonist), tile, foreign.PlayerId);
        game.Map.AddRumour(tile);

        game.ExploreRumour(unit, tile, new ScriptedRandom(1536, 0)); // chief gift to the foreign power

        Assert.Empty(game.RumourNotices);
    }

    [Fact]
    public void Explore_StrangeMounds_RecordsNoImmediateNotice_ItsOutcomeComesViaThePrompt()
    {
        // MOUNDS pauses for an investigate/decline choice; ExploreRumour returns the sentinel without resolving, so
        // no immediate notice is recorded â€” the description comes via ResolvePendingMounds.
        (Game game, Unit unit, Position tile, _) = MoundsExplorer();

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(4416)); // 4416 â†’ MOUNDS (native table)

        Assert.Equal(Game.LostCityRumourType.Mounds, outcome);
        Assert.Empty(game.RumourNotices);
    }

    [Fact]
    public void Explore_ANonLearnableUnit_NeverLearns_TheLowRollGivesGoldInstead()
    {
        // An expert farmer has no model.unitChange.lostCity, so after FoY its good list is TRIBAL_CHIEF (weight 50)
        // then COLONIST: the roll 96 that is LEARN for a free colonist gives the chief's gift instead (allowLearn gate).
        (Game game, Unit unit, Position tile) = ExplorerOnRumour(ExpertFarmer);

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(96, 0)); // 96 â†’ chief (not learn)

        Assert.Equal(Game.LostCityRumourType.TribalChief, outcome);
    }

    [Fact]
    public void Explore_FountainOfYouth_LandsAnImmigrantBurstOnTheEuropeDock()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int before = game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope);

        // roll 0 â†’ Fountain of Youth; then dx (=8) recruit draws (each value picks a recruitable type). Exactly
        // nine scripted values (1 type roll + 8 draws) pins dx=8: a wrong count would over/under-run the queue.
        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(0, 0, 0, 0, 0, 0, 0, 0, 0));

        Assert.Equal(Game.LostCityRumourType.FountainOfYouth, outcome);
        int after = game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope);
        Assert.Equal(before + 8, after); // dx = 8 immigrants at classic medium
        Assert.All(game.Units.Where(u => u.Location == UnitLocation.InEurope && u.OwnerId == 0),
            u => Assert.True(u.Type.RecruitProbability > 0)); // each is a recruitable type
        Assert.False(game.Map.HasRumour(tile));
    }

    // ---- Fountain of Youth â†’ recruit-choice routing (86d3c9ujx) ----

    private const string Brewster = "model.foundingFather.williamBrewster";

    /// <summary>A game whose human Congress holds the given fathers, with a free colonist on a rumour tile.</summary>
    private static (Game Game, Unit Unit, Position Tile) ExplorerWithCongress(params string[] fathers)
    {
        SaveGame save = SaveGame.From(Game.New(Classic, Seed));
        Game game = (save with { Players = save.Players!.Select(p => p with { Congress = p.IsHuman ? fathers : p.Congress }).ToList() })
            .Restore(Classic);
        Position tile = game.PlayerUnits.First().Position;
        Unit unit = game.SpawnUnit(Classic.Unit(FreeColonist), tile);
        game.Map.AddRumour(tile);
        return (game, unit, tile);
    }

    [Fact]
    public void Explore_FountainOfYouth_WithBrewster_RoutesToTheRecruitChoice_DrawingNoBurstRng()
    {
        // A select-recruit human (William Brewster): the FoY burst arms a pending recruit choice of dx picks instead
        // of generating the immigrants directly. A single scripted value (the FoY type roll, 0) is enough â€” the burst
        // makes NO recruit draws here (each pick draws later, on the player's own stream), so an empty queue afterward
        // proves no RNG was spent on the burst.
        (Game game, Unit unit, Position tile) = ExplorerWithCongress(Brewster);
        int europeBefore = game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope);

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(0)); // only the FoY type roll

        Assert.Equal(Game.LostCityRumourType.FountainOfYouth, outcome);
        Assert.NotNull(game.PendingEmigration);
        Assert.True(game.PendingEmigration!.IsFountainOfYouth);
        Assert.Equal(8, game.PendingEmigration.Remaining);                 // dx picks owed
        Assert.Equal(game.RecruitDock, game.PendingEmigration.RecruitTypeIds); // pick from the live dock
        Assert.Equal(europeBefore, game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope)); // none generated yet
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void FountainOfYouth_ChooseEmigrant_LandsDxImmigrants_WithoutConsumingImmigration()
    {
        (Game game, Unit unit, Position tile) = ExplorerWithCongress(Brewster);
        game.ExploreRumour(unit, tile, new ScriptedRandom(0)); // arm the dx-pick FoY choice
        int immigrationBefore = game.HumanPlayer.Immigration;
        int requiredBefore = game.HumanPlayer.ImmigrationRequired;
        int europeBefore = game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope);

        int picks = 0;
        while (game.PendingEmigration is { IsFountainOfYouth: true })
        {
            Assert.NotNull(game.ChooseEmigrant(0)); // always take dock slot 0
            picks++;
        }

        Assert.Equal(8, picks);                                            // exactly dx picks
        Assert.Null(game.PendingEmigration);                               // the burst cleared
        Assert.Equal(europeBefore + 8, game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope));
        Assert.Equal(immigrationBefore, game.HumanPlayer.Immigration);     // FoY consumes no immigration (FreeCol MigrationType.FOUNTAIN)
        Assert.Equal(requiredBefore, game.HumanPlayer.ImmigrationRequired); // and never raises the target
        Assert.Equal(Game.RecruitSlots, game.RecruitDock.Count);           // the dock stayed full (refilled each pick)
    }

    [Fact]
    public void Explore_FountainOfYouth_WithoutBrewster_StillGeneratesDirectly_ByteIdenticalPath()
    {
        // A human WITHOUT Brewster keeps the historical direct path: dx recruit draws off the passed stream, no pause.
        // (Guards the byte-identity requirement for every non-choosing player.)
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int europeBefore = game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope);

        game.ExploreRumour(unit, tile, new ScriptedRandom(0, 0, 0, 0, 0, 0, 0, 0, 0)); // FoY + dx direct draws

        Assert.Null(game.PendingEmigration); // no choice â€” generated directly
        Assert.Equal(europeBefore + 8, game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope));
    }

    [Fact]
    public void Explore_FountainOfYouth_WithNoEurope_GeneratesNothing_AndNoticesTheGate()
    {
        // A human whose Europe is closed (empty recruit dock â€” a rebel, or a minimal ruleset) gets the noEurope gate:
        // no recruits, no pause, and a player-facing "no Europe" notice instead of the generic success line.
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        game.HumanPlayer.RecruitDockList.Clear(); // simulate a closed Europe (as DeclareIndependence does)
        int unitsBefore = game.Units.Count;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(0)); // FoY; no burst draws

        Assert.Equal(Game.LostCityRumourType.FountainOfYouth, outcome);
        Assert.Null(game.PendingEmigration);                  // no choice armed
        Assert.Equal(unitsBefore, game.Units.Count);          // no immigrants generated
        RumourNotice notice = Assert.Single(game.RumourNotices);
        Assert.Contains("no Europe", notice.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    private const string TreasureTrain = "model.unit.treasureTrain";

    [Fact]
    public void Explore_Ruins_SmallFind_PaysGold_NoTrain()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int goldBefore = game.HumanPlayer.Gold;

        // 3936 â†’ Ruins; amount roll 0 â†’ 0Ã—300 + 50 = 50 (< 500) â†’ straight gold.
        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(3936, 0));

        Assert.Equal(Game.LostCityRumourType.Ruins, outcome);
        Assert.Equal(goldBefore + 50, game.HumanPlayer.Gold);
        Assert.DoesNotContain(game.Units, u => u.Type.Id == TreasureTrain); // small find â†’ gold, no train
    }

    [Fact]
    public void Explore_Ruins_RichFind_SpawnsATreasureTrain()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int goldBefore = game.HumanPlayer.Gold;

        // 3936 â†’ Ruins; amount roll 5 â†’ 5Ã—300 + 50 = 1550 (â‰¥ 500) â†’ a treasure train.
        game.ExploreRumour(unit, tile, new ScriptedRandom(3936, 5));

        Assert.Equal(goldBefore, game.HumanPlayer.Gold); // no instant gold â€” it's a train
        Unit train = game.Units.Single(u => u.Type.Id == TreasureTrain);
        Assert.Equal(tile, train.Position);
        Assert.Equal(0, train.OwnerId);
        Assert.Equal(1550, train.TreasureAmount);
    }

    [Fact]
    public void Explore_Cibola_SpawnsABigTreasureTrain()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();

        // 4224 â†’ Cibola; amount roll 0 â†’ 0 + dxÃ—300 = 2400.
        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(4224, 0));

        Assert.Equal(Game.LostCityRumourType.Cibola, outcome);
        Unit train = game.Units.Single(u => u.Type.Id == TreasureTrain);
        Assert.Equal(2400, train.TreasureAmount);

        // The find is recorded in the human's History (FreeCol CITY_OF_GOLD) with score 0 â€” its value rides the
        // treasure (â†’ gold summand once cashed in), so it adds no direct points but survives in the report.
        HistoryEvent e = Assert.Single(game.History, h => h.Kind == HistoryEventKind.CityOfGold);
        Assert.Equal(0, e.Score);
        Assert.Contains("2400", e.Description);
    }

    [Fact]
    public void Explore_Cibola_ByAForeignPower_RecordsNoEntryInTheHumansHistory()
    {
        Game game = Game.New(Classic, Seed);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Position tile = game.Map.AllPositions().First(p => !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        game.Map.AddRumour(tile);
        Unit explorer = game.SpawnUnit(Classic.Unit(FreeColonist), tile, power.PlayerId);

        game.ExploreRumour(explorer, tile, new ScriptedRandom(4224, 0)); // a foreign power finds Cibola

        // The history log is the human's only â€” a foreign power's find earns no entry (mirrors every RecordHistory).
        Assert.DoesNotContain(game.History, h => h.Kind == HistoryEventKind.CityOfGold);
    }

    // ---- Seven Cities of Gold — the finite Cibola list (86d3fpxrc, FreeCol NameCache.getNextCityOfCibola) ----
    //
    // A new game holds CibolaCityCount (7) undiscovered cities. While any remain, a CIBOLA rumour spawns the big
    // treasure train and consumes one; once all seven are found, a further CIBOLA roll degrades to an ordinary RUINS
    // find (FreeCol ServerUnit's CIBOLA → "Fall through, found all the cities" → case RUINS).

    /// <summary>Resolves a CIBOLA rumour for a fresh free colonist on an empty land tile, returning the (game, outcome). 4224 → Cibola; then the treasure/ruins amount roll.</summary>
    private static (Game Game, Game.LostCityRumourType Outcome) ResolveOneCibola(Game game, int amountRoll)
    {
        Position tile = game.Map.AllPositions().First(p => !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p)
            && !game.Map.HasRumour(p));
        Unit unit = game.SpawnUnit(Classic.Unit(FreeColonist), tile);
        game.Map.AddRumour(tile);
        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(4224, amountRoll));
        return (game, outcome);
    }

    [Fact]
    public void AFreshGame_HoldsAllSevenCitiesOfGold()
    {
        Assert.Equal(7, Game.CibolaCityCount);
        Assert.Equal(7, Game.New(Classic, Seed).CitiesOfCibolaRemaining);
    }

    [Fact]
    public void EachCibolaFind_DecrementsTheCount_AndSpawnsTheBigTreasure()
    {
        Game game = Game.New(Classic, Seed);
        for (int found = 1; found <= Game.CibolaCityCount; found++)
        {
            (_, Game.LostCityRumourType outcome) = ResolveOneCibola(game, amountRoll: 0); // 0 → dx·300 = 2400 treasure
            Assert.Equal(Game.LostCityRumourType.Cibola, outcome);
            Assert.Equal(Game.CibolaCityCount - found, game.CitiesOfCibolaRemaining); // counted down by one each find
        }
        // Seven treasure trains spawned (one per city of gold), each carrying the big 2400 find.
        Assert.Equal(7, game.Units.Count(u => u.Type.Id == TreasureTrain && u.TreasureAmount == 2400));
    }

    [Fact]
    public void TheEighthCibola_AfterTheListIsExhausted_DegradesToAnOrdinaryRuinsFind()
    {
        Game game = Game.New(Classic, Seed);
        for (int i = 0; i < Game.CibolaCityCount; i++)
        {
            ResolveOneCibola(game, amountRoll: 0); // exhaust all seven cities (each a 2400 treasure)
        }
        Assert.Equal(0, game.CitiesOfCibolaRemaining);
        int trainsBefore = game.Units.Count(u => u.Type.Id == TreasureTrain);
        int goldBefore = game.HumanPlayer.Gold;

        // The 8th CIBOLA roll: the list is empty, so it degrades to RUINS. Amount roll 0 → 0×300 + 50 = 50 gold (a small
        // find — straight gold, no train). The outcome label stays Cibola (the roll drawn), but it resolves as ruins.
        (_, Game.LostCityRumourType outcome) = ResolveOneCibola(game, amountRoll: 0);

        Assert.Equal(Game.LostCityRumourType.Cibola, outcome);            // the rumour rolled was still Cibola…
        Assert.Equal(goldBefore + 50, game.HumanPlayer.Gold);            // …but it paid a small ruins find in gold
        Assert.Equal(trainsBefore, game.Units.Count(u => u.Type.Id == TreasureTrain)); // no new treasure train
        Assert.Equal(0, game.CitiesOfCibolaRemaining);                   // still exhausted (never decrements below 0)
    }

    [Fact]
    public void AnExhaustedCibola_CanStillSpawnATreasureTrain_OnALargeRuinsRoll()
    {
        // Degrading to RUINS uses the ruins amount formula: a large roll still becomes a treasure train (the ruins ≥ 500 branch).
        Game game = Game.New(Classic, Seed);
        for (int i = 0; i < Game.CibolaCityCount; i++)
        {
            ResolveOneCibola(game, amountRoll: 0);
        }
        int trainsBefore = game.Units.Count(u => u.Type.Id == TreasureTrain);

        // 8th Cibola, exhausted → ruins; amount roll 5 → 5×300 + 50 = 1550 (≥ 500) → a treasure train (a ruins-sized one).
        ResolveOneCibola(game, amountRoll: 5);

        Unit train = Assert.Single(game.Units, u => u.Type.Id == TreasureTrain && u.TreasureAmount == 1550);
        Assert.Equal(1550, train.TreasureAmount);
        Assert.Equal(trainsBefore + 1, game.Units.Count(u => u.Type.Id == TreasureTrain));
    }

    // ---- Persistence (v63, additive omit-when-default) ----

    [Fact]
    public void CibolaCount_RoundTripsThroughSave()
    {
        Game game = Game.New(Classic, Seed);
        ResolveOneCibola(game, amountRoll: 0); // find one city → 6 remain
        Assert.Equal(6, game.CitiesOfCibolaRemaining);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(6, restored.CitiesOfCibolaRemaining); // the persisted counter survives reload
        Assert.Equal(63, SaveGame.CurrentVersion);
    }

    [Fact]
    public void AFreshGame_OmitsTheCibolaToken_AndOldSavesLoadWithTheClassicCount()
    {
        // A game where no Cibola has been found writes no CitiesOfCibolaRemaining token (byte-stable vs v62); a pre-v63
        // save (no token) loads under v63 with the full classic count of seven (back-compat).
        Game fresh = Game.New(Classic, Seed);
        string json = SaveGame.From(fresh).ToJson();
        Assert.DoesNotContain("CitiesOfCibolaRemaining", json);

        // Down-version load: a v62-style save (the token absent) restores with all seven cities still undiscovered.
        SaveGame asV62 = SaveGame.FromJson(json) with { Version = 62, CitiesOfCibolaRemaining = null };
        Game loaded = SaveGame.FromJson(asV62.ToJson()).Restore(Classic);
        Assert.Equal(Game.CibolaCityCount, loaded.CitiesOfCibolaRemaining); // the classic count defaulted
    }

    [Fact]
    public void TheExhaustedCount_SurvivesReload_AndKeepsDegradingToRuins()
    {
        // Find all seven, save, reload: the reloaded game still degrades a Cibola to ruins (the counter persisted at 0).
        Game game = Game.New(Classic, Seed);
        for (int i = 0; i < Game.CibolaCityCount; i++)
        {
            ResolveOneCibola(game, amountRoll: 0);
        }
        Game reloaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.Equal(0, reloaded.CitiesOfCibolaRemaining);

        int goldBefore = reloaded.HumanPlayer.Gold;
        ResolveOneCibola(reloaded, amountRoll: 0); // a Cibola in the reloaded game still degrades to a small ruins find
        Assert.Equal(goldBefore + 50, reloaded.HumanPlayer.Gold); // 50 gold, no train — proving the persisted 0 still degrades
    }

    // ---- Scout + de Soto modifiers (86d3c9uhj) ----

    private const string DeSoto = "model.foundingFather.hernandoDeSoto";

    private static Game ElectDeSoto()
    {
        SaveGame save = SaveGame.From(Game.New(Classic, Seed));
        return (save with { Players = save.Players!.Select(p => p with { Congress = new[] { DeSoto } }).ToList() })
            .Restore(Classic);
    }

    [Fact]
    public void SeasonedScout_NeverVanishes_WhereAFreeColonistWould()
    {
        // The exact same draw (4800, the vanish slot off native land) that vanishes a free colonist cannot vanish a
        // seasoned scout: the expert scout removes EXPEDITION_VANISHES from the table, and off native land (no burial)
        // that leaves no bad outcome at all.
        (Game free, Unit colonist, Position t1) = ExplorerOnRumour();
        Assert.Equal(Game.LostCityRumourType.ExpeditionVanishes, free.ExploreRumour(colonist, t1, new ScriptedRandom(4800)));

        (Game scoutGame, Unit scout, Position t2) = ExplorerOnRumour(SeasonedScout);
        int scoutId = scout.Id;
        Game.LostCityRumourType outcome = scoutGame.ExploreRumour(scout, t2, new ScriptedRandom(4800));

        Assert.NotEqual(Game.LostCityRumourType.ExpeditionVanishes, outcome);
        Assert.Contains(scoutGame.Units, u => u.Id == scoutId); // the scout survived
    }

    [Fact]
    public void DeSoto_ForcesAGoodOutcome_WhereARegularColonistWouldGetNothing()
    {
        // Draw 5000 lands a regular colonist in NOTHING off native land; Hernando de Soto makes the whole table good,
        // so the same draw lands a tribal-chief gift instead.
        (Game regular, Unit plain, Position rt) = ExplorerOnRumour();
        Assert.Equal(Game.LostCityRumourType.Nothing, regular.ExploreRumour(plain, rt, new ScriptedRandom(5000)));

        Game game = ElectDeSoto();
        Position tile = game.PlayerUnits.First().Position;
        Unit unit = game.SpawnUnit(Classic.Unit(FreeColonist), tile);
        game.Map.AddRumour(tile);

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(5000, 0));
        Assert.Equal(Game.LostCityRumourType.TribalChief, outcome);
    }

    // ---- Explore trigger (move / disembark) ----

    [Fact]
    public void MovingALandUnitOntoARumour_ExploresIt()
    {
        Game game = Game.New(Classic, Seed);
        Unit unit = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        Position to = unit.Position.Neighbours().First(n => game.CheckMove(unit, n).Allowed); // a legal adjacent land tile
        game.Map.AddRumour(to);

        game.MoveUnit(unit, to);

        Assert.False(game.Map.HasRumour(to)); // investigated on arrival (outcome drawn from the human's stream 0)
    }

    [Fact]
    public void AnAmphibiousLanding_ExploresARumourToo()
    {
        Game game = Game.New(Classic, Seed);
        // A water tile beside an unoccupied land tile, away from the start so nothing else stands there.
        var occupied = game.Units.Where(u => u.IsOnMap).Select(u => u.Position)
            .Concat(game.NativeSettlements.Select(s => s.Position)).ToHashSet();
        (Position water, Position land) = (from p in game.Map.AllPositions()
            where game.Map.TerrainAt(p).IsWater
            from n in p.Neighbours()
            where game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && !occupied.Contains(n)
            select (p, n)).First();

        Unit ship = game.SpawnUnit(Classic.Unit("model.unit.caravel"), water);
        Unit passenger = game.SpawnUnit(Classic.Unit(FreeColonist), land);
        game.Board(passenger, ship);
        game.Map.AddRumour(land); // the (now-empty) landing tile carries a rumour

        game.Disembark(passenger, land);

        Assert.False(game.Map.HasRumour(land)); // the amphibious landing investigated it
    }

    [Fact]
    public void ANativeBraveSteppingOntoARumour_DoesNotExploreIt()
    {
        Game game = Game.New(Classic, Seed);
        // A native nation type id to own the brave (FreeCol: only Europeans explore rumours).
        string nation = game.NativeSettlements.First().NationTypeId;
        var occupied = game.Units.Where(u => u.IsOnMap).Select(u => u.Position)
            .Concat(game.NativeSettlements.Select(s => s.Position)).ToHashSet();
        // Any pair of adjacent unoccupied land tiles, clear of settlements (so the brave's move is legal).
        (Position from, Position to) = (from p in game.Map.AllPositions()
            where !game.Map.TerrainAt(p).IsWater && !occupied.Contains(p) && game.ColonyAt(p) is null
            from n in p.Neighbours()
            where game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && !occupied.Contains(n) && game.ColonyAt(n) is null
            select (p, n)).First();
        Unit brave = game.SpawnUnit(Classic.Unit("model.unit.brave"), from, nation);
        game.Map.AddRumour(to);

        game.MoveUnit(brave, to);

        Assert.True(game.Map.HasRumour(to)); // a brave leaves the rumour untouched
    }

    // ---- Strange mounds + burial ground (86d3c9umy) ----
    //
    // On NATIVE-OWNED land the table also offers MOUNDS (weight 8) and a BURIAL_GROUND/EXPEDITION_VANISHES bad pair
    // (normalised to 25/75, the same 100-weight footprint the lone vanish takes off native land). For a learnable
    // free colonist on a native tile the cumulative ranges are: FoY [0,96) | Learn [96,1536) | Chief [1536,2976) |
    // Colonist [2976,3936) | Ruins [3936,4224) | Cibola [4224,4416) | MOUNDS [4416,4800) | Burial [4800,4825) |
    // Vanish [4825,4900) | Nothing [4900,7800). MOUNDS pauses for an investigate/decline choice; investigate runs
    // FreeCol's degradation loop. (Off native land the table is byte-identical to before â€” the conditional-add guard,
    // proved by the non-native boundary tests above still passing with roll 4416 == Vanish.)

    /// <summary>A human free colonist on a rumour tile the natives own (so MOUNDS/BURIAL_GROUND are possible); returns the owning nation id.</summary>
    private static (Game Game, Unit Unit, Position Tile, string Nation) MoundsExplorer(string unitTypeId = FreeColonist)
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour(unitTypeId);
        string nation = game.NativeSettlements.First().NationTypeId;
        game.Map.SetNativeOwner(tile, nation);
        return (game, unit, tile, nation);
    }

    [Fact]
    public void Explore_OnNativeLand_RollingMounds_Pauses_LeavingTheRumourForADecision()
    {
        (Game game, Unit unit, Position tile, _) = MoundsExplorer();
        int unitsBefore = game.Units.Count;
        int goldBefore = game.HumanPlayer.Gold;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(4416)); // 4416 â†’ MOUNDS (native table)

        Assert.Equal(Game.LostCityRumourType.Mounds, outcome);
        Assert.True(game.Map.HasRumour(tile));        // NOT consumed â€” it awaits investigate/decline
        Assert.Equal(unitsBefore, game.Units.Count);  // no effect on the peek
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);
    }

    [Fact]
    public void DeclineMounds_RemovesTheRumour_WithNoEffect()
    {
        (Game game, Unit unit, Position tile, _) = MoundsExplorer();
        game.ExploreRumour(unit, tile, new ScriptedRandom(4416)); // reveal the mounds
        int unitsBefore = game.Units.Count;
        int goldBefore = game.HumanPlayer.Gold;

        game.DeclineMounds(tile); // takes no IGameRandom â€” structurally cannot draw

        Assert.False(game.Map.HasRumour(tile));
        Assert.Equal(unitsBefore, game.Units.Count);
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);
    }

    [Fact]
    public void ResolvePendingMounds_Decline_RemovesTheRumour_AndDescribesIt()
    {
        // The presentation oracle behind the strange-mounds decision panel. _pendingMounds is transient UI state
        // with no public setter (a human only reaches it by rolling MOUNDS on its own stream), so inject it directly.
        (Game game, Unit unit, Position tile, _) = MoundsExplorer();
        SetPendingMounds(game, unit, tile);

        string outcome = game.ResolvePendingMounds(investigate: false);

        Assert.Contains("undisturbed", outcome);
        Assert.Null(game.PendingMounds);        // resolved
        Assert.False(game.Map.HasRumour(tile)); // declined â†’ the rumour is consumed
    }

    [Fact]
    public void ResolvePendingMounds_Investigate_ResolvesTheRumour_AndDescribesTheOutcome()
    {
        (Game game, Unit unit, Position tile, _) = MoundsExplorer();
        SetPendingMounds(game, unit, tile);

        string outcome = game.ResolvePendingMounds(investigate: true);

        Assert.False(string.IsNullOrEmpty(outcome)); // a human-readable result line
        Assert.Null(game.PendingMounds);             // resolved
        Assert.False(game.Map.HasRumour(tile));      // investigated â†’ consumed
    }

    [Fact]
    public void ResolvePendingMounds_WithNothingPending_ReturnsEmpty()
    {
        (Game game, _, _, _) = MoundsExplorer();
        Assert.Equal("", game.ResolvePendingMounds(investigate: true));
    }

    private static void SetPendingMounds(Game game, Unit unit, Position tile) =>
        typeof(Game).GetField("_pendingMounds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(game, new Game.PendingMoundsDecision(unit.Id, tile));

    [Fact]
    public void InvestigateMounds_AcceptsTribalChief_ResolvingAndConsumingTheRumour()
    {
        (Game game, Unit unit, Position tile, _) = MoundsExplorer();
        int goldBefore = game.HumanPlayer.Gold;

        // loop draw 1536 â†’ Chief (accepted at once); then the chief gold roll (0 â†’ dxÂ·5 = 40). Exactly two draws.
        Game.LostCityRumourType outcome = game.InvestigateMounds(unit, tile, new ScriptedRandom(1536, 0));

        Assert.Equal(Game.LostCityRumourType.TribalChief, outcome);
        Assert.Equal(goldBefore + 40, game.HumanPlayer.Gold);
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void InvestigateMounds_RejectsTheFirstNothing_AcceptsTheSecond()
    {
        (Game game, Unit unit, Position tile, _) = MoundsExplorer();

        // 4900 â†’ Nothing (first, rerolled), 4900 â†’ Nothing (second, accepted). Exactly two draws â€” a third would
        // empty the queue and throw, pinning the sticky-second-NOTHING rule.
        Game.LostCityRumourType outcome = game.InvestigateMounds(unit, tile, new ScriptedRandom(4900, 4900));

        Assert.Equal(Game.LostCityRumourType.Nothing, outcome);
        Assert.False(game.Map.HasRumour(tile));
    }

    [Theory]
    [InlineData(0)]   // the ruins+burial roll < badPercent (FreeCol's fall-through branch)â€¦
    [InlineData(99)]  // â€¦and >= badPercent (the plain-ruins branch) â€” both still resolve as RUINS
    public void InvestigateMounds_Ruins_ConsumesTheBurialRoll_ButStaysRuins(int burialRoll)
    {
        (Game game, Unit unit, Position tile, string nation) = MoundsExplorer();
        int goldBefore = game.HumanPlayer.Gold;

        // 3936 â†’ Ruins; then the "ruins+burial" roll (consumed, never changes the outcome); then amount 0 â†’ 50 gold.
        Game.LostCityRumourType outcome = game.InvestigateMounds(unit, tile, new ScriptedRandom(3936, burialRoll, 0));

        Assert.Equal(Game.LostCityRumourType.Ruins, outcome);           // never BurialGround (FreeCol quirk)
        Assert.Equal(goldBefore + 50, game.HumanPlayer.Gold);          // a small ruins â†’ straight gold
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.NotEqual(NativeSettlement.MaxAlarm, s.Alarm));  // no burial-ground anger
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void InvestigateMounds_BurialGround_RaisesTheOwningNationToMaxAlarm_NoGoldNoUnit()
    {
        (Game game, Unit unit, Position tile, string nation) = MoundsExplorer();
        int unitsBefore = game.Units.Count;
        int goldBefore = game.HumanPlayer.Gold;

        Game.LostCityRumourType outcome = game.InvestigateMounds(unit, tile, new ScriptedRandom(4800)); // 4800 â†’ Burial

        Assert.Equal(Game.LostCityRumourType.BurialGround, outcome);
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(NativeSettlement.MaxAlarm, s.Alarm)); // the desecrated nation turns hateful
        Assert.Equal(unitsBefore, game.Units.Count);               // no unit lost
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);           // no gold
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void InvestigateMounds_RerollsAnUnacceptableOutcome_ThenAccepts()
    {
        (Game game, Unit unit, Position tile, _) = MoundsExplorer();
        int goldBefore = game.HumanPlayer.Gold;

        // 4224 â†’ Cibola (a default outcome mounds can't yield â†’ rerolled); 1536 â†’ Chief; gold roll. Three draws.
        Game.LostCityRumourType outcome = game.InvestigateMounds(unit, tile, new ScriptedRandom(4224, 1536, 0));

        Assert.Equal(Game.LostCityRumourType.TribalChief, outcome);
        Assert.Equal(goldBefore + 40, game.HumanPlayer.Gold);
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void Explore_OnNativeLand_CanRollBurialGroundDirectly_AndResolvesItAtOnce()
    {
        (Game game, Unit unit, Position tile, string nation) = MoundsExplorer();

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(4800)); // 4800 â†’ Burial (direct, not via mounds)

        Assert.Equal(Game.LostCityRumourType.BurialGround, outcome);
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation),
            s => Assert.Equal(NativeSettlement.MaxAlarm, s.Alarm));
        Assert.False(game.Map.HasRumour(tile)); // a direct (non-mounds) outcome is consumed immediately
    }

    [Fact]
    public void Explore_MoundsOrBurial_OnNonNativeLand_DegradeToNothing()
    {
        // MOUNDS is on the table unconditionally (FreeCol), but off native-owned land a MOUNDS/BURIAL roll degrades to
        // NOTHING at resolve â€” so the explorer never actually faces strange mounds or a burial ground in the wilderness
        // (no prompt, no harm), while its weight stays in the denominator. Roll 4416 lands in the MOUNDS slot off native land.
        (Game game, Unit unit, Position tile) = ExplorerOnRumour(); // NOT native-owned
        int id = unit.Id;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(4416));

        Assert.Equal(Game.LostCityRumourType.Nothing, outcome); // degraded â€” never MOUNDS/BURIAL off native land
        Assert.Contains(game.Units, u => u.Id == id);           // not lost (it resolves as a harmless nothing)
        Assert.False(game.Map.HasRumour(tile));                 // rumour consumed
    }

    [Fact]
    public void PendingMoundsWrappers_AreNoOps_WhenNothingIsPending()
    {
        Game game = Game.New(Classic, Seed);
        Assert.Null(game.PendingMounds);
        Assert.Equal(Game.LostCityRumourType.Nothing, game.InvestigatePendingMounds());
        game.DeclinePendingMounds(); // no throw, no state
        Assert.Null(game.PendingMounds);
    }

    [Fact]
    public void MovingHumanUnitOntoNativeOwnedRumour_EitherResolvesItOrRaisesAMoundsPrompt()
    {
        Game game = Game.New(Classic, Seed);
        Unit unit = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        Position to = unit.Position.Neighbours().First(n => game.CheckMove(unit, n).Allowed);
        string nation = game.NativeSettlements.First().NationTypeId;
        game.Map.AddRumour(to);
        game.Map.SetNativeOwner(to, nation);

        game.MoveUnit(unit, to); // explored on the human's stream 0

        // Whatever the roll, the wiring holds: a human's MOUNDS pauses for a decision; anything else resolves at once.
        if (game.PendingMounds is { } pending)
        {
            Assert.Equal(to, pending.Tile);
            Assert.True(game.Map.HasRumour(to));   // left in place, awaiting the choice
            game.DeclinePendingMounds();
            Assert.False(game.Map.HasRumour(to));  // declining consumes it
        }
        else
        {
            Assert.False(game.Map.HasRumour(to));  // a non-mounds outcome was resolved + consumed on arrival
        }
    }

    [Fact]
    public void InvestigateMounds_OnAnAlreadyConsumedTile_IsANoOp_NoRewardNoDraw()
    {
        // Guards the stale-tile double-reward: if the rumour was consumed (e.g. by another unit) before the decision
        // was answered, investigating grants nothing and draws no RNG (an empty ScriptedRandom would throw on a draw).
        (Game game, Unit unit, Position tile, _) = MoundsExplorer();
        game.Map.RemoveRumour(tile);
        int goldBefore = game.HumanPlayer.Gold;
        int unitsBefore = game.Units.Count;

        Game.LostCityRumourType outcome = game.InvestigateMounds(unit, tile, new ScriptedRandom());

        Assert.Equal(Game.LostCityRumourType.Nothing, outcome);
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);
        Assert.Equal(unitsBefore, game.Units.Count);
    }

    /// <summary>Searches seeds for a fresh game where the human's first move onto a native-owned rumour rolls MOUNDS (deterministic).</summary>
    private static (Game Game, Unit Unit, Position Tile)? FindHumanMoundsPrompt(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        string nation = game.NativeSettlements.First().NationTypeId;
        Unit unit = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        Position? dest = null;
        foreach (Position n in unit.Position.Neighbours())
        {
            if (game.CheckMove(unit, n).Allowed) { dest = n; break; }
        }
        if (dest is not { } to)
        {
            return null;
        }
        game.Map.AddRumour(to);
        game.Map.SetNativeOwner(to, nation);
        game.MoveUnit(unit, to);
        return game.PendingMounds is not null ? (game, unit, to) : null;
    }

    [Fact]
    public void HumanPendingMounds_BlocksFurtherExploration_AndIsAutoDeclinedAtEndTurn()
    {
        (Game Game, Unit Unit, Position Tile)? found = null;
        for (ulong s = 1; s <= 400 && found is null; s++)
        {
            found = FindHumanMoundsPrompt(s);
        }
        Assert.NotNull(found); // a MOUNDS roll exists well within the window (~5% of native-tile rolls)
        (Game game, Unit unit, Position tile) = found!.Value;

        Assert.Equal(tile, game.PendingMounds!.Tile);
        Assert.True(game.Map.HasRumour(tile)); // left in place, awaiting the choice

        // Re-entry guard: while the decision is outstanding, another human unit stepping onto a fresh native rumour
        // does NOT explore it (no re-roll, no second pending).
        string nation = game.NativeSettlements.First().NationTypeId;
        if (game.PlayerUnits.FirstOrDefault(u => u.IsOnMap && !u.Type.IsNaval && u.Id != unit.Id) is { } other)
        {
            Position? dest2 = null;
            foreach (Position n in other.Position.Neighbours())
            {
                if (game.CheckMove(other, n).Allowed) { dest2 = n; break; }
            }
            if (dest2 is { } to2 && !game.Map.HasRumour(to2))
            {
                game.Map.AddRumour(to2);
                game.Map.SetNativeOwner(to2, nation);
                game.MoveUnit(other, to2);
                Assert.True(game.Map.HasRumour(to2));         // blocked â€” the pending decision gates exploration
                Assert.Equal(tile, game.PendingMounds!.Tile); // still the original prompt, not overwritten
            }
        }

        // EndTurn auto-declines an unanswered prompt: it clears, and the rumour is consumed (FreeCol session timeout).
        game.EndTurn();
        Assert.Null(game.PendingMounds);
        Assert.False(game.Map.HasRumour(tile));
    }
}
