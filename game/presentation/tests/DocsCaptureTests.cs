using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Units;
using GdUnit4;
using Godot;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// Documentation screenshot capture — NOT a golden/regression suite. Each case drives a seeded, deterministic
/// game to a target screen at the 1024×600 base resolution and writes a clean PNG to <see cref="OutDir"/> for the
/// player Handbook (docs/guide/img). Gated behind <c>DOCS_CAPTURE=1</c> so a normal test run skips them (they write
/// files, not assertions). Run with:
///   $env:DOCS_CAPTURE='1'; dotnet test game/CrownAndColony.csproj --settings game/gdunit.runsettings --filter FullyQualifiedName~DocsCaptureTests
/// The GdUnit host is windowed-GL locally, so real pixels are captured; ignore the leak-at-exit non-zero exit.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DocsCaptureTests
{
    private const ulong Seed = 424242;
    private static readonly Vector2I CaptureSize = new(1024, 600);
    private const string OutDir = "C:/Users/Chris/Code/Colonization/docs/guide/img";

    private static bool Enabled => System.Environment.GetEnvironmentVariable("DOCS_CAPTURE") == "1";

    // ---- panel captures: open, let it settle, hide the floating map chrome LAST (a deferred RefreshView re-shows
    // the minimap/controls, so hiding before the settle frames doesn't stick), repaint once, then save. -----------
    private static async Task CapturePanel(ISceneRunner runner, GameController controller, string name)
    {
        await runner.SimulateFrames(4);
        controller.GetNode<CanvasItem>("UI/MiniMap").Visible = false;
        if (controller.GetNodeOrNull<CanvasItem>("UI/MapControls") is { } mc) mc.Visible = false;
        await runner.SimulateFrames(1);
        Save(controller, name);
    }

    private static void Save(GameController controller, string name)
    {
        Image img = controller.GetViewport().GetTexture().GetImage();
        Error err = img.SavePng($"{OutDir}/{name}.png");
        GD.Print($"[DOCS_CAPTURE] {name}.png -> {err} ({img.GetWidth()}x{img.GetHeight()})");
    }

    private static GameController LoadGame(ISceneRunner runner)
    {
        var controller = (GameController)runner.Scene();
        controller.GetWindow().Size = CaptureSize;
        return controller;
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_WorldMapAndHud()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        // Found the starting colony so the shot shows a real colony, explored terrain around it and the full HUD.
        // World map keeps its chrome (minimap/controls) — they are part of the HUD we are illustrating.
        Game game = GameOf(controller);
        game.FoundColony(game.Units[0]);
        await runner.SimulateFrames(4);
        Save(controller, "world-map-hud");
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_ColonyScreen()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony colony = game.FoundColony(game.Units[0]);
        controller.OpenColonyPanel(colony);
        await CapturePanel(runner, controller, "colony-screen");
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_EuropeScreen()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 2,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean"],
            Units =
            [
                new SavedUnit(1, "model.unit.caravel", 1, 0, 12, (int)UnitLocation.InEurope, 0,
                    new System.Collections.Generic.Dictionary<string, int> { ["model.goods.sugar"] = 100 }),
                new SavedUnit(3, "model.unit.galleon", 1, 0, 12, (int)UnitLocation.InEurope, 0,
                    new System.Collections.Generic.Dictionary<string, int> { ["model.goods.tobacco"] = 100, ["model.goods.ore"] = 50 }),
                new SavedUnit(2, "model.unit.freeColonist", 0, 0, 3, (int)UnitLocation.InEurope),
                new SavedUnit(4, "model.unit.expertOreMiner", 0, 0, 3, (int)UnitLocation.InEurope),
                new SavedUnit(5, "model.unit.caravel", 0, 0, 12, (int)UnitLocation.SailingToEurope, 2),
                new SavedUnit(6, "model.unit.galleon", 0, 0, 12, (int)UnitLocation.SailingToNewWorld, 1),
            ],
            Explored = [],
            Gold = 5000,
        }.Restore(GameLogic.Specification.Ruleset.LoadClassic());
        SetGame(controller, game);
        controller.OpenEuropePanel();
        await CapturePanel(runner, controller, "europe-screen");
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_FoundingFathers()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        controller.OpenFoundingFatherPanel();
        await CapturePanel(runner, controller, "founding-fathers");
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_Colopedia()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        controller.OpenColopediaPanel();
        await CapturePanel(runner, controller, "colopedia");
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_ColonyReport()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        game.FoundColony(game.Units[0]);
        controller.OpenColonyReportPanel();
        await CapturePanel(runner, controller, "colony-report");
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_NativeSettlement()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        if (game.NativeSettlements.Count == 0) return;
        controller.OpenNativeSettlementPanel(game.NativeSettlements[0], null);
        await CapturePanel(runner, controller, "native-settlement");
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_TradeRoutes()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        game.FoundColony(game.Units[0]);
        controller.OpenTradeRoutePanel();
        await CapturePanel(runner, controller, "trade-routes");
    }

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, game);
}
