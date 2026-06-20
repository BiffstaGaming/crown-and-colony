using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the on-map <b>pioneer-built improvement</b> overlay (`86d3dqr62`):
/// the road/plow improvement layer is drawn over the terrain and is fog-gated — an improvement draws on an explored
/// tile but not on an unexplored one. Presentation-only (ADR-006): the test stamps improvements on the existing
/// improvement layer and reads back what the <see cref="ImprovementOverlay"/> renders. Mirrors
/// <see cref="RiverOverlayTests"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ImprovementOverlayTests
{
    private static readonly Vector2I CaptureSize = new(800, 600);

    // The overlay's road brown (ImprovementOverlay.Road) — what a drawn road track looks like on screen.
    private static readonly Color RoadBrown = new(0.55f, 0.40f, 0.22f);

    // The overlay's plow furrow brown (ImprovementOverlay.Furrow) — what a drawn furrow looks like on screen.
    private static readonly Color FurrowBrown = new(0.42f, 0.28f, 0.16f);

    [TestCase(Timeout = 60000)]
    public async Task ImprovementOverlay_DrawsRoadOnExploredTile_NotOnUnexploredOne()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false; // keep the capture to the map + overlay only
        Game game = GameOf(controller);

        // A land tile that is explored, and another hidden by fog. Stamp a road on both.
        Position explored = game.Map.AllPositions()
            .First(p => game.IsExplored(p) && !game.Map.TerrainAt(p).IsWater);
        Position hidden = game.Map.AllPositions()
            .First(p => !game.IsExplored(p) && !game.Map.TerrainAt(p).IsWater);
        SetImprovement(game.Map, explored, Road());
        SetImprovement(game.Map, hidden, Road());

        // Explored road tile: the overlay draws a hub/track → road-brown pixels near its screen centre.
        AssertThat(await DrawnAt(runner, controller, explored, RoadBrown)).IsTrue();
        // Unexplored road tile: fog-gated → nothing drawn.
        AssertThat(await DrawnAt(runner, controller, hidden, RoadBrown)).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    public async Task ImprovementOverlay_DrawsPlowOnExploredTile_NotOnUnexploredOne()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false;
        Game game = GameOf(controller);

        Position explored = game.Map.AllPositions()
            .First(p => game.IsExplored(p) && !game.Map.TerrainAt(p).IsWater);
        Position hidden = game.Map.AllPositions()
            .First(p => !game.IsExplored(p) && !game.Map.TerrainAt(p).IsWater);
        SetImprovement(game.Map, explored, Plow());
        SetImprovement(game.Map, hidden, Plow());

        // Explored plowed tile: the overlay draws furrows → furrow-brown pixels near its screen centre.
        AssertThat(await DrawnAt(runner, controller, explored, FurrowBrown)).IsTrue();
        // Unexplored plowed tile: fog-gated → nothing drawn.
        AssertThat(await DrawnAt(runner, controller, hidden, FurrowBrown)).IsFalse();
    }

    /// <summary>
    /// Centres the camera on <paramref name="tile"/>, refreshes the view, and reports whether the improvement overlay
    /// drew any pixel matching <paramref name="target"/> in a small window around the tile's screen centre.
    /// </summary>
    private static async Task<bool> DrawnAt(ISceneRunner runner, GameController controller, Position tile, Color target)
    {
        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(tile);
        Refresh(controller);
        await runner.SimulateFrames(3);

        Image img = controller.GetViewport().GetTexture().GetImage();
        var centre = new Vector2I(img.GetWidth() / 2, img.GetHeight() / 2);
        const int window = 24; // the track/furrows pass through the diamond centre; sample a small box around it
        for (int dy = -window; dy <= window; dy++)
        {
            for (int dx = -window; dx <= window; dx++)
            {
                int x = centre.X + dx, y = centre.Y + dy;
                if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight())
                {
                    continue;
                }
                if (Matches(img.GetPixel(x, y), target))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>True when a pixel matches a target colour within a small tolerance (anti-aliasing/blend slack).</summary>
    private static bool Matches(Color c, Color target) =>
        Mathf.Abs(c.R - target.R) < 0.08f
        && Mathf.Abs(c.G - target.G) < 0.08f
        && Mathf.Abs(c.B - target.B) < 0.08f;

    // A minimal road/plow improvement instance — only the Id matters to the overlay (it reads HasImprovement by id).
    private static TileImprovementType Road() =>
        TileImprovementType.FromModifiers(TileImprovementType.RoadId, 1, 0, 0, []);

    private static TileImprovementType Plow() =>
        TileImprovementType.FromModifiers(TileImprovementType.PlowId, 1, 0, 0, []);

    private static void SetImprovement(GameMap map, Position p, TileImprovementType improvement) =>
        typeof(GameMap).GetMethod("AddImprovement", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(map, [p, improvement]);

    private static void Refresh(GameController controller) =>
        typeof(GameController).GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
