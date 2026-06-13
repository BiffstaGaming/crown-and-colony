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
        Unit unit = game.Units[0];

        // Only the starting colonist exists, so visibility is exactly its line of sight.
        Assert.Equal(Square(game, unit.Position, unit.Type.LineOfSight), game.CurrentlyVisible.ToHashSet());
        Assert.True(game.IsVisible(unit.Position));
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
        Colony colony = game.FoundColony(game.Units[0]); // founder leaves the map

        Assert.Empty(game.PlayerUnits); // braves (native) remain, but they don't lift the player's fog
        Assert.True(game.IsVisible(colony.Position));
        Assert.True(game.IsExplored(colony.Position));
        // No on-map units → visibility is exactly the colony's sight.
        Assert.Equal(Square(game, colony.Position, Game.ColonySightRadius), game.CurrentlyVisible.ToHashSet());
    }
}
