using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.World;

/// <summary>
/// Lost City Rumour placement (<c>86d3c9uex</c>, FreeCol <c>SimpleMapGenerator.makeLostCityRumours</c>): a target
/// number of rumour tiles are scattered on land at game start — clear of settlements, units and the player's
/// landing — from a dedicated RNG stream so the human's stream 0 stays byte-identical. The reward is rolled only
/// when a unit explores one (a later slice); this slice is placement + the per-tile flag + the save (v25).
/// </summary>
public class LostCityRumourTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    // The count formula our generator uses (mirrors LostCityRumourGenerator): width·height·45% / 35.
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
        Assert.Equal(26, SaveGame.CurrentVersion);
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

    // ---- Outcome resolution (86d3c9uhj, + Fountain of Youth 86d3c9ujx) ----
    //
    // The weighted table (FreeCol LostCityRumour.chooseType) at classic medium (good 48 / bad 23 / neutral 29),
    // good outcomes listed FoY-first as in FreeCol. For a LEARNABLE explorer the cumulative ranges are
    //   FoY [0,96) | Learn [96,1536) | TribalChief [1536,2976) | Colonist [2976,3936) |
    //   ExpeditionVanishes [3936,4036) | Nothing [4036,6936)   (total 6936).
    // A non-learnable explorer drops Learn and widens Chief: FoY [0,96) | Chief [96,2496) | Colonist [2496,3936) | …
    // A scripted weighted-pick roll lands a known outcome.

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

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(3936)); // 3936 → vanish

        Assert.Equal(Game.LostCityRumourType.ExpeditionVanishes, outcome);
        Assert.DoesNotContain(game.Units, u => u.Id == id);
        Assert.False(game.Map.HasRumour(tile));
    }

    [Theory]
    [InlineData(0, 40)]    // gold = random.Next(80)=0  + dx·5 = 40  (medium dx=8)
    [InlineData(79, 119)]  // gold = random.Next(80)=79 + dx·5 = 119
    public void Explore_TribalChief_GiftsGoldToTheOwner(int goldRoll, int expectedGift)
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int before = game.HumanPlayer.Gold;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(1536, goldRoll)); // 1536 → chief

        Assert.Equal(Game.LostCityRumourType.TribalChief, outcome);
        Assert.Equal(before + expectedGift, game.HumanPlayer.Gold);
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void Explore_Learn_UpgradesALearnableUnit_KeepingItsId()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour(); // a free colonist can learn
        int id = unit.Id;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(96)); // 96 → learn

        Assert.Equal(Game.LostCityRumourType.Learn, outcome);
        Unit learned = game.Units.Single(u => u.Id == id);
        Assert.Equal(SeasonedScout, learned.Type.Id); // free colonist → seasoned scout (model.unitChange.lostCity)
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void Explore_Colonist_MustersAFreeColonistOnTheTile()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int before = game.Units.Count;

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(2976)); // 2976 → colonist

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

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(4036)); // 4036 → nothing

        Assert.Equal(Game.LostCityRumourType.Nothing, outcome);
        Assert.Equal(unitsBefore, game.Units.Count);
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);
        Assert.False(game.Map.HasRumour(tile));
    }

    [Fact]
    public void Explore_ANonLearnableUnit_NeverLearns_TheLowRollGivesGoldInstead()
    {
        // An expert farmer has no model.unitChange.lostCity, so after FoY its good list is TRIBAL_CHIEF (weight 50)
        // then COLONIST: the roll 96 that is LEARN for a free colonist gives the chief's gift instead (allowLearn gate).
        (Game game, Unit unit, Position tile) = ExplorerOnRumour(ExpertFarmer);

        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(96, 0)); // 96 → chief (not learn)

        Assert.Equal(Game.LostCityRumourType.TribalChief, outcome);
    }

    [Fact]
    public void Explore_FountainOfYouth_LandsAnImmigrantBurstOnTheEuropeDock()
    {
        (Game game, Unit unit, Position tile) = ExplorerOnRumour();
        int before = game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope);

        // roll 0 → Fountain of Youth; then dx (=8) recruit draws (each value picks a recruitable type). Exactly
        // nine scripted values (1 type roll + 8 draws) pins dx=8: a wrong count would over/under-run the queue.
        Game.LostCityRumourType outcome = game.ExploreRumour(unit, tile, new ScriptedRandom(0, 0, 0, 0, 0, 0, 0, 0, 0));

        Assert.Equal(Game.LostCityRumourType.FountainOfYouth, outcome);
        int after = game.Units.Count(u => u.OwnerId == 0 && !u.IsNative && u.Location == UnitLocation.InEurope);
        Assert.Equal(before + 8, after); // dx = 8 immigrants at classic medium
        Assert.All(game.Units.Where(u => u.Location == UnitLocation.InEurope && u.OwnerId == 0),
            u => Assert.True(u.Type.RecruitProbability > 0)); // each is a recruitable type
        Assert.False(game.Map.HasRumour(tile));
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
}
