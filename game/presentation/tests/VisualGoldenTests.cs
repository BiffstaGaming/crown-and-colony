using System.Linq;
using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L4 visual regression (docs/TESTING.md): renders deterministic game states and
/// diffs the viewport against committed golden PNGs.
///
/// - Goldens live in <c>game/tests/visual/goldens/</c> (a <c>.gdignore</c> dir —
///   read via absolute path, invisible to Godot's importer).
/// - Regenerate intentionally with the env var <c>GOLDEN_UPDATE=1</c>; new goldens
///   are reviewed in the commit diff like any change.
/// - On mismatch the actual and per-pixel diff images are written to
///   <c>game/TestResults/visual/</c> (uploaded as CI artifacts).
/// - The UI layer is hidden during capture: goldens cover the map view only,
///   keeping them stable across platforms' font rendering.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class VisualGoldenTests
{
    private const ulong GoldenSeed = 424242;
    private static readonly Vector2I CaptureSize = new(1024, 600);

    [TestCase(Timeout = 60000)]
    public async Task MapView_SeededWorld_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();

        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false;
        controller.StartNewGame(GoldenSeed);
        await runner.SimulateFrames(5); // let resize + draw settle

        Image actual = controller.GetViewport().GetTexture().GetImage();
        GoldenAssert.Assert("map-seed424242", actual);
    }

    [TestCase(Timeout = 60000)]
    public async Task MapView_AfterColonyFounded_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();

        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false;
        controller.StartNewGame(GoldenSeed);
        await runner.SimulateFrames(2);

        // Deterministic action: found a colony where the starting unit stands,
        // then refresh the view via the End Turn button (public signal path).
        var game = GetGame(controller);
        game.FoundColony(game.Units[0]);
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(3);

        Image actual = controller.GetViewport().GetTexture().GetImage();
        GoldenAssert.Assert("colony-seed424242", actual);
    }

    [TestCase(Timeout = 60000)]
    public async Task MapView_NativeSettlement_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();

        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false;
        controller.StartNewGame(GoldenSeed);
        await runner.SimulateFrames(2);

        // Reveal one (deterministically chosen) native settlement by exploring its
        // tile, refresh via the public End-Turn path, then frame it with the camera.
        var game = GetGame(controller);
        var settlement = game.NativeSettlements
            .OrderBy(s => s.Position.Y)
            .ThenBy(s => s.Position.X)
            .First();
        game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), settlement.Position);
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(settlement.Position);
        await runner.SimulateFrames(3);

        Image actual = controller.GetViewport().GetTexture().GetImage();
        GoldenAssert.Assert("native-settlement-seed424242", actual);
    }

    [TestCase(Timeout = 60000)]
    public async Task MapView_RememberedFog_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();

        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false;
        controller.StartNewGame(GoldenSeed);
        await runner.SimulateFrames(2);

        // Step the colonist one tile onto a land neighbour, then refresh: the tiles it
        // leaves behind become explored-but-not-visible and should render dimmed.
        var game = GetGame(controller);
        var unit = game.Units[0];
        var origin = unit.Position;
        var dest = origin.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && game.CheckMove(unit, n).Allowed);
        game.MoveUnit(unit, dest);
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(origin);
        await runner.SimulateFrames(3);

        Image actual = controller.GetViewport().GetTexture().GetImage();
        GoldenAssert.Assert("remembered-fog-seed424242", actual);
    }

    [TestCase(Timeout = 60000)]
    public async Task MapView_RenderedUnits_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();

        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false;
        controller.StartNewGame(GoldenSeed);
        await runner.SimulateFrames(2);

        // The 1c-1 rendering: a second own unit (no ring) and an in-sight native brave (owner ring) beside the
        // start. Refresh via a click (not End Turn — that would run the native AI and move the brave).
        var game = GetGame(controller);
        var humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        var free = humanPos.Neighbours().Where(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n)).Take(2).ToList();
        game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), free[0]);
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), free[1], game.NativeSettlements.First().NationTypeId);

        // Refresh deterministically via F5 (QuickSave → RefreshView): no End Turn (which would run the native AI
        // and move the brave) and no screen-space click (whose tile mapping can lag the camera by a frame).
        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(humanPos);
        runner.SimulateKeyPressed(Key.F5);
        await runner.SimulateFrames(3);

        Image actual = controller.GetViewport().GetTexture().GetImage();
        GoldenAssert.Assert("rendered-units-seed424242", actual);
    }

    [TestCase(Timeout = 60000)]
    public async Task MapView_RumourMarker_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();

        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false;
        controller.StartNewGame(GoldenSeed);
        await runner.SimulateFrames(2);

        // Place a rumour on an explored, unit-free, colony-free tile beside the start unit so the marker draws
        // unobstructed, then centre on it. AddRumour is internal — reach it by reflection from the view assembly.
        var game = GetGame(controller);
        var unitPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        var rumourPos = unitPos.Neighbours().First(n =>
            game.IsExplored(n) && !game.Units.Any(u => u.IsOnMap && u.Position == n) && game.ColonyAt(n) is null);
        typeof(CrownAndColony.GameLogic.World.GameMap)
            .GetMethod("AddRumour", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(game.Map, [rumourPos]);

        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(rumourPos);
        runner.SimulateKeyPressed(Key.F5); // QuickSave → RefreshView, redrawing the rumour layer (no AI/turn advance)
        await runner.SimulateFrames(3);

        Image actual = controller.GetViewport().GetTexture().GetImage();
        GoldenAssert.Assert("rumour-marker-seed424242", actual);
    }

    [TestCase(Timeout = 60000)]
    public async Task MapView_River_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();

        controller.GetWindow().Size = CaptureSize;
        controller.GetNode<CanvasLayer>("UI").Visible = false;
        controller.StartNewGame(GoldenSeed);
        await runner.SimulateFrames(2);

        // Stamp a winding river course on explored land tiles around the start unit so the golden reliably captures
        // the overlay's connected-course rendering (the generated rivers fall inland, off the start view). A winding
        // line (not a star) reads like a real river and exercises the connectivity + derived small/large style.
        // SetImprovement is internal — reach it by reflection from the view assembly (ADR-006: a pure render of the
        // improvement layer, the same path generation/load use).
        var game = GetGame(controller);
        var unitPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        var river = CrownAndColony.GameLogic.World.Improvements.TileImprovementType.ClassicRiver();
        var setImprovement = typeof(CrownAndColony.GameLogic.World.GameMap)
            .GetMethod("SetImprovement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        // A deterministic winding course (E, SE, E, SE, …) from the start tile — laid only on explored land tiles.
        var steps = new (int dx, int dy)[] { (1, 0), (1, 1), (1, 0), (0, 1), (1, 0) };
        var cursor = unitPos;
        var course = new System.Collections.Generic.List<CrownAndColony.GameLogic.World.Position> { unitPos };
        foreach (var (dx, dy) in steps)
        {
            var next = new CrownAndColony.GameLogic.World.Position(cursor.X + dx, cursor.Y + dy);
            if (game.Map.InBounds(next) && game.IsExplored(next) && !game.Map.TerrainAt(next).IsWater)
            {
                course.Add(next);
                cursor = next;
            }
        }
        foreach (var p in course)
        {
            setImprovement.Invoke(game.Map, [p, river]);
        }

        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(unitPos);
        runner.SimulateKeyPressed(Key.F5); // QuickSave → RefreshView, redrawing the river layer (no AI/turn advance)
        await runner.SimulateFrames(3);

        Image actual = controller.GetViewport().GetTexture().GetImage();
        GoldenAssert.Assert("river-seed424242", actual);
    }

    [TestCase(Timeout = 60000)]
    public async Task MiniMap_SeededWorld_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();

        controller.GetWindow().Size = CaptureSize;
        controller.StartNewGame(GoldenSeed); // UI stays visible: the minimap draws an opaque backdrop
        await runner.SimulateFrames(5);

        // Crop the full-screen capture to the minimap's own rect — self-contained (opaque), so independent of
        // the map view rendered behind the rest of the UI. Pure flat-colour cells + dots → cross-platform stable.
        var mini = controller.GetNode<MiniMap>("UI/MiniMap");
        Rect2 rect = mini.GetGlobalRect();
        Image actual = controller.GetViewport().GetTexture().GetImage()
            .GetRegion(new Rect2I((Vector2I)rect.Position, (Vector2I)rect.Size));
        GoldenAssert.Assert("minimap-seed424242", actual);
    }

    private static CrownAndColony.GameLogic.GameSession.Game GetGame(GameController controller) =>
        (CrownAndColony.GameLogic.GameSession.Game)controller
            .GetType()
            .GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(controller)!;

}
