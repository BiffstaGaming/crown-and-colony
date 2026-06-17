using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
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
        Assert.Equal(25, SaveGame.CurrentVersion);
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
}
