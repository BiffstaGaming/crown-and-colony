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
/// L3 interaction tests (docs/TESTING.md) for the on-map river overlay (`86d3b3qdx`): the river improvement layer is
/// drawn as a blue course over the terrain and is fog-gated — a river draws on an explored tile but not on an
/// unexplored one. Presentation-only (ADR-006): the test stamps rivers on the existing improvement layer and reads
/// back what the <see cref="RiverOverlay"/> renders.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RiverOverlayTests
{
    private static readonly Vector2I CaptureSize = new(800, 600);

    // The overlay's water blue (RiverOverlay.Water) — what a drawn course looks like on screen.
    private static readonly Color RiverWater = new(0.20f, 0.42f, 0.72f);

    [TestCase(Timeout = 60000)]
    public async Task RiverOverlay_DrawsOnExploredRiverTile_NotOnUnexploredOne()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false; // keep the capture to the map + overlay only
        Game game = GameOf(controller);

        // A land river tile that is explored, and another that is hidden by fog. Stamp the river improvement on both.
        Position explored = game.Map.AllPositions()
            .First(p => game.IsExplored(p) && !game.Map.TerrainAt(p).IsWater);
        Position hidden = game.Map.AllPositions()
            .First(p => !game.IsExplored(p) && !game.Map.TerrainAt(p).IsWater);
        SetRiver(game.Map, explored);
        SetRiver(game.Map, hidden);

        // Explored river tile: the overlay draws a course → river-blue pixels near its screen centre.
        AssertThat(await RiverDrawnAt(runner, controller, explored)).IsTrue();
        // Unexplored river tile: fog-gated → no course drawn.
        AssertThat(await RiverDrawnAt(runner, controller, hidden)).IsFalse();
    }

    /// <summary>
    /// Centres the camera on <paramref name="tile"/>, refreshes the view, and reports whether the river overlay drew
    /// any water-blue pixel in a small window around the tile's screen centre.
    /// </summary>
    private static async Task<bool> RiverDrawnAt(ISceneRunner runner, GameController controller, Position tile)
    {
        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(tile);
        Refresh(controller);
        await runner.SimulateFrames(3);

        Image img = controller.GetViewport().GetTexture().GetImage();
        var centre = new Vector2I(img.GetWidth() / 2, img.GetHeight() / 2);
        const int window = 24; // the course passes through the diamond centre; sample a small box around it
        for (int dy = -window; dy <= window; dy++)
        {
            for (int dx = -window; dx <= window; dx++)
            {
                int x = centre.X + dx, y = centre.Y + dy;
                if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight())
                {
                    continue;
                }
                if (IsRiverWater(img.GetPixel(x, y)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>True when a pixel matches the river water colour within a small tolerance (anti-aliasing/blend slack).</summary>
    private static bool IsRiverWater(Color c) =>
        Mathf.Abs(c.R - RiverWater.R) < 0.10f
        && Mathf.Abs(c.G - RiverWater.G) < 0.10f
        && Mathf.Abs(c.B - RiverWater.B) < 0.10f;

    private static void SetRiver(GameMap map, Position p) =>
        typeof(GameMap).GetMethod("SetImprovement", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(map, [p, TileImprovementType.ClassicRiver()]);

    private static void Refresh(GameController controller) =>
        typeof(GameController).GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
