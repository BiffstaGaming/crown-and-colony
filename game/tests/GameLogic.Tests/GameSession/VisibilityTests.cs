using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Explored-vs-visible fog of war: <see cref="Game.CurrentlyVisible"/> is what the
/// player can see right now (an on-map unit or colony in range); <see cref="Game.Explored"/>
/// is everything ever seen. Visible is always a subset of explored.
/// </summary>
public class VisibilityTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    /// <summary>In-bounds tiles within a square (Chebyshev) radius of a centre.</summary>
    private static HashSet<Position> Square(Game game, Position centre, int radius)
    {
        var set = new HashSet<Position>();
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                var p = new Position(centre.X + dx, centre.Y + dy);
                if (game.Map.InBounds(p))
                {
                    set.Add(p);
                }
            }
        }
        return set;
    }

    [Fact]
    public void NewGame_VisibleEqualsTheStartingUnitsSight()
    {
        Game game = Game.New(Classic, Seed);

        // Visibility is exactly the union of the starting roster's lines of sight (pioneer + soldier + caravel).
        var expected = game.PlayerUnits.SelectMany(u => Square(game, u.Position, u.Type.LineOfSight)).ToHashSet();
        Assert.Equal(expected, game.CurrentlyVisible.ToHashSet());
        foreach (Unit u in game.PlayerUnits)
        {
            Assert.True(game.IsVisible(u.Position));
        }
    }

    [Fact]
    public void Visible_IsAlwaysASubsetOfExplored()
    {
        Game game = Game.New(Classic, Seed);
        Assert.Subset(game.Explored.ToHashSet(), game.CurrentlyVisible.ToHashSet());
    }

    [Fact]
    public void MovingOn_RemembersButNoLongerSeesWhereYouCameFrom()
    {
        Game game = Game.New(Classic, Seed);
        Unit unit = game.Units[0];
        Position origin = unit.Position;

        // The start always has a land neighbour to step onto.
        Position dest = origin.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.CheckMove(unit, n).Allowed);
        game.MoveUnit(unit, dest);

        Assert.Subset(game.Explored.ToHashSet(), game.CurrentlyVisible.ToHashSet());

        // The tile on the far side of our origin is two tiles from us now: still
        // explored (we saw it), no longer visible (out of sight, no colony there).
        var behind = new Position(origin.X * 2 - dest.X, origin.Y * 2 - dest.Y);
        if (game.Map.InBounds(behind))
        {
            Assert.True(game.IsExplored(behind));
            Assert.False(game.IsVisible(behind));
            Assert.DoesNotContain(behind, game.CurrentlyVisible);
        }
    }

    [Fact]
    public void Colony_KeepsItsSurroundingsVisibleAfterTheFounderIsConsumed()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.Type.CanFoundColony && game.CheckFoundColony(u).Allowed));
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u); // disband the rest of the roster so only the colony lifts the player's fog
        }

        Assert.Empty(game.PlayerUnits); // braves (native) remain, but they don't lift the player's fog
        Assert.True(game.IsVisible(colony.Position));
        Assert.True(game.IsExplored(colony.Position));
        // No on-map units → visibility is exactly the colony's sight (classic visible-radius 2, a 5×5 ring).
        Assert.Equal(2, game.ColonySightRadius); // FreeCol classic colony visible-radius
        Assert.Equal(Square(game, colony.Position, game.ColonySightRadius), game.CurrentlyVisible.ToHashSet());
    }

    // -------- model.option.fogOfWar (86d3dzdw3) --------
    // FreeCol Player.getVisibleTileSet: fog ON → visible is the union of current lines of sight (explored-but-unseen
    // tiles are re-hidden); fog OFF → visible is *all* explored tiles (everywhere ever seen stays visible).

    /// <summary>Chebyshev "tile p within radius of centre" — mirrors Game.InSight (which is private).</summary>
    private static bool Within(Position centre, Position p, int radius) =>
        Math.Abs(centre.X - p.X) <= radius && Math.Abs(centre.Y - p.Y) <= radius;

    /// <summary>The classic spec parses fog of war ON, and a fog-off override flips just that one option.</summary>
    [Fact]
    public void FogOfWar_ClassicDefaultsOn_OverrideFlipsIt()
    {
        Assert.True(Ruleset.LoadClassic().GameOptions.FogOfWar); // classic default (byte-identical game)
        Assert.False(Ruleset.LoadClassic().WithFogOfWar(false).GameOptions.FogOfWar);
        Assert.True(Ruleset.LoadClassic().WithFogOfWar(true).GameOptions.FogOfWar);
    }

    /// <summary>
    /// Fog OFF: a tile the player explored stays visible after the only unit that saw it walks two tiles away — there is
    /// no remembered-but-hidden state, every explored tile is in sight (FreeCol's no-fog branch).
    /// </summary>
    [Fact]
    public void FogOff_KeepsAnExploredTileVisibleAfterTheUnitLeaves()
    {
        Game game = Game.New(Ruleset.LoadClassic().WithFogOfWar(false), Seed);
        // Reduce the roster to a single land unit so exactly one mover lifts the fog (others would keep tiles in sight).
        Unit mover = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap && u != mover).ToList())
        {
            game.Disband(u);
        }
        Position origin = mover.Position;

        // The tile two steps "behind" the destination: explored from the start, but it will fall out of current sight
        // once the mover steps away (a radius-1 colonist).
        Position dest = origin.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.CheckMove(mover, n).Allowed);
        var behind = new Position(origin.X * 2 - dest.X, origin.Y * 2 - dest.Y);
        Assert.True(game.Map.InBounds(behind));
        Assert.True(game.IsExplored(behind));

        game.MoveUnit(mover, dest);

        Assert.False(Within(dest, behind, mover.Type.LineOfSight)); // it really is out of the mover's line of sight now
        Assert.True(game.IsExplored(behind));
        Assert.True(game.IsVisible(behind));                         // …but fog is off, so it stays visible
        Assert.Contains(behind, game.CurrentlyVisible);
        // With fog off the visible set is exactly the explored set.
        Assert.Equal(game.Explored.ToHashSet(), game.CurrentlyVisible.ToHashSet());
    }

    /// <summary>
    /// Fog ON (the classic default): the same explored tile is re-hidden once the unit walks away — visibility is only
    /// the current line of sight. This pins the byte-identical default behaviour against the fog-off case above.
    /// </summary>
    [Fact]
    public void FogOn_ReHidesAnExploredTileAfterTheUnitLeaves()
    {
        Game game = Game.New(Classic, Seed); // classic = fog on
        Unit mover = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap && u != mover).ToList())
        {
            game.Disband(u);
        }
        Position origin = mover.Position;

        Position dest = origin.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.CheckMove(mover, n).Allowed);
        var behind = new Position(origin.X * 2 - dest.X, origin.Y * 2 - dest.Y);
        Assert.True(game.Map.InBounds(behind));

        game.MoveUnit(mover, dest);

        Assert.True(game.IsExplored(behind));        // still remembered…
        Assert.False(game.IsVisible(behind));        // …but re-hidden (fog on)
        Assert.DoesNotContain(behind, game.CurrentlyVisible);
    }
}
