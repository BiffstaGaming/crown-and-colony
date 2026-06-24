using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// Isometric (diamond) tile-picking tests (86d3f675b). Two layers:
/// <list type="bullet">
///   <item>The screen↔tile transform round-trips and resolves edges/corners to the correct neighbour
///   (<see cref="MapView.TileAt(Vector2)"/> / <see cref="MapView.TileCentre"/>) — pure-math, no scene needed
///   beyond the Godot <see cref="Vector2"/>/<see cref="Mathf"/> types.</item>
///   <item>An end-to-end click-accuracy check: feeding the controller a real <see cref="InputEventMouseButton"/>
///   at a chosen viewport point (the event-position path the fix uses) selects the tile whose diamond contains
///   that point — verified at the centre and offset toward each of the four diamond edges, and under camera zoom.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MapPickingTests
{
    private const ulong Seed = 424242;

    // ----- Layer 1: the transform is a true inverse-isometric / point-in-diamond pick -----

    /// <summary>Every tile's centre round-trips: <c>TileAt(TileCentre(p)) == p</c> across a sample map.</summary>
    [TestCase]
    public void TileCentreRoundTripsForEveryTileOnASampleMap()
    {
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                var p = new Position(x, y);
                AssertThat(MapView.TileAt(MapView.TileCentre(p))).IsEqual(p);
            }
        }
    }

    /// <summary>
    /// A point nudged from a tile's centre toward each diamond edge/corner still resolves to that tile (inside),
    /// and a point nudged just past each edge/corner resolves to the expected diagonal neighbour. This is the
    /// behaviour that a rectangular bounding-box hit-test would get wrong near the diamond's slanted edges.
    /// </summary>
    [TestCase]
    public void PointsNearTheDiamondEdgesResolveToTheExpectedTile()
    {
        var tile = new Position(7, 7);
        Vector2 c = MapView.TileCentre(tile);

        // Diamond corners (local offsets): N (0,−32), E (+64,0), S (0,+32), W (−64,0). The four slanted edges
        // run between adjacent corners; crossing one steps to the diagonal neighbour in this rotated grid.
        // Just INSIDE each edge midpoint → still the tile itself.
        AssertThat(MapView.TileAt(c + new Vector2(31, 15))).IsEqual(tile);   // toward SE edge
        AssertThat(MapView.TileAt(c + new Vector2(-31, 15))).IsEqual(tile);  // toward SW edge
        AssertThat(MapView.TileAt(c + new Vector2(31, -15))).IsEqual(tile);  // toward NE edge
        AssertThat(MapView.TileAt(c + new Vector2(-31, -15))).IsEqual(tile); // toward NW edge

        // Just PAST each edge midpoint → the diagonal neighbour. (+x is the SE step, +y the SW step.)
        AssertThat(MapView.TileAt(c + new Vector2(33, 17))).IsEqual(new Position(8, 7));   // past SE
        AssertThat(MapView.TileAt(c + new Vector2(-33, 17))).IsEqual(new Position(7, 8));  // past SW
        AssertThat(MapView.TileAt(c + new Vector2(33, -17))).IsEqual(new Position(7, 6));  // past NE
        AssertThat(MapView.TileAt(c + new Vector2(-33, -17))).IsEqual(new Position(6, 7)); // past NW

        // Just PAST each corner → the orthogonal neighbour for that corner.
        AssertThat(MapView.TileAt(c + new Vector2(65, 0))).IsEqual(new Position(8, 6));  // past E corner (x+1,y−1)
        AssertThat(MapView.TileAt(c + new Vector2(0, 33))).IsEqual(new Position(8, 8));  // past S corner (x+1,y+1)
        AssertThat(MapView.TileAt(c + new Vector2(-65, 0))).IsEqual(new Position(6, 8)); // past W corner (x−1,y+1)
        AssertThat(MapView.TileAt(c + new Vector2(0, -33))).IsEqual(new Position(6, 6)); // past N corner (x−1,y−1)
    }

    // ----- Layer 2: clicking a viewport point selects the diamond it falls in (real event path) -----

    [TestCase(Timeout = 60000)]
    public async Task ClickingInsideATilesDiamond_SelectsThatTile_AtZoom1()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Pick an in-bounds tile away from the edges and centre the camera on it.
        var target = new Position(game.Map.Width / 2, game.Map.Height / 2);
        await CentreOn(runner, controller, target);

        // The diamond's centre and four interior edge-offsets must all pick the SAME tile.
        AssertPicks(controller, target, ViewportCentre(controller));
        AssertPicks(controller, target, ViewportCentre(controller) + new Vector2(30, 0));   // toward E
        AssertPicks(controller, target, ViewportCentre(controller) + new Vector2(-30, 0));  // toward W
        AssertPicks(controller, target, ViewportCentre(controller) + new Vector2(0, 15));   // toward S
        AssertPicks(controller, target, ViewportCentre(controller) + new Vector2(0, -15));  // toward N
        AssertPicks(controller, target, ViewportCentre(controller) + new Vector2(28, 13));  // toward SE edge
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickingNeighbouringDiamonds_SelectsEachNeighbour_AtZoom1()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        var target = new Position(game.Map.Width / 2, game.Map.Height / 2);
        await CentreOn(runner, controller, target);
        Vector2 centre = ViewportCentre(controller);

        // A point clearly inside each diagonal neighbour's diamond (one full diamond step from centre) picks it.
        AssertPicks(controller, new Position(target.X + 1, target.Y), centre + new Vector2(64, 32));   // SE
        AssertPicks(controller, new Position(target.X, target.Y + 1), centre + new Vector2(-64, 32));  // SW
        AssertPicks(controller, new Position(target.X, target.Y - 1), centre + new Vector2(64, -32));  // NE
        AssertPicks(controller, new Position(target.X - 1, target.Y), centre + new Vector2(-64, -32)); // NW
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickingIsAccurate_UnderCameraZoom()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        var target = new Position(game.Map.Width / 2, game.Map.Height / 2);
        await CentreOn(runner, controller, target);
        var camera = controller.GetNode<Camera2D>("Camera");
        camera.Zoom = new Vector2(2f, 2f); // zoom in: the picking must fold the camera zoom into the transform
        await runner.SimulateFrames(1);

        Vector2 centre = ViewportCentre(controller);
        // At 2× zoom a world offset of 30px toward E appears 60px from centre on screen.
        AssertPicks(controller, target, centre);
        AssertPicks(controller, target, centre + new Vector2(60, 0));   // toward E, scaled by zoom
        AssertPicks(controller, target, centre + new Vector2(0, -28));  // toward N, scaled by zoom
        // One full diamond step east is 128px on screen at 2× → the SE neighbour.
        AssertPicks(controller, new Position(target.X + 1, target.Y), centre + new Vector2(128, 64));
    }

    /// <summary>Drives the controller's real left-click handler at <paramref name="viewportPos"/> and asserts the picked (inspected) tile.</summary>
    private static void AssertPicks(GameController controller, Position expected, Vector2 viewportPos)
    {
        controller._UnhandledInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = viewportPos,
        });
        Position picked = InspectedTile(controller);
        AssertThat(picked)
            .OverrideFailureMessage($"click at viewport {viewportPos} picked {picked.X},{picked.Y}, expected {expected.X},{expected.Y}")
            .IsEqual(expected);
    }

    /// <summary>Centres the camera (zoom 1) on a tile so its diamond sits at screen-centre, like the InputTests ClickTile helper.</summary>
    private static async Task CentreOn(ISceneRunner runner, GameController controller, Position tile)
    {
        var camera = controller.GetNode<Camera2D>("Camera");
        camera.Zoom = Vector2.One;
        camera.Position = MapView.TileCentre(tile);
        await runner.SimulateFrames(1);
    }

    /// <summary>Viewport (screen) point at the centre of the view — where the camera-centred tile's diamond centre projects.</summary>
    private static Vector2 ViewportCentre(GameController controller) =>
        controller.GetViewport().GetVisibleRect().Size / 2f;

    private static Position InspectedTile(GameController controller) =>
        ((Position?)controller.GetType()
            .GetField("_inspectedTile", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller))!.Value;

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
