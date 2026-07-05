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
    // Most panels fill the game's 1024x600 base window cleanly. The two content-heavy screens (the colony's full
    // buildings grid, Europe's whole harbour) overflow it, so they capture at 1440x1080 (4:3) — the UI lays out
    // correctly there (verified) and both render clean (opaque parchment / sky backdrop). Centred modals look worse
    // enlarged (a small panel adrift in dead space), so they stay at 1024x600.
    private static readonly Vector2I CaptureSize = new(1024, 600);
    private static readonly Vector2I Big = new(1440, 1080);
    private const string OutDir = "C:/Users/Chris/Code/Colonization/docs/guide/img";

    private static bool Enabled => System.Environment.GetEnvironmentVariable("DOCS_CAPTURE") == "1";

    // ---- panel captures: open, let it settle, hide the floating map chrome LAST (a deferred RefreshView re-shows
    // the minimap/controls, so hiding before the settle frames doesn't stick), repaint once, then save. -----------
    // The minimap, its wood-framed backing panel (MiniMapBack), the +/-/N map controls, and the action-cluster
    // backing all float on the "UI" CanvasLayer above modal panels. Hide them all for a clean full-panel capture.
    private static readonly string[] MapChrome =
        ["UI/MiniMap", "UI/MiniMapBack", "UI/MapControls", "UI/ActionClusterBack"];

    private static async Task CapturePanel(ISceneRunner runner, GameController controller, string name)
    {
        await runner.SimulateFrames(4);
        foreach (string path in MapChrome)
            if (controller.GetNodeOrNull<CanvasItem>(path) is { } node) node.Visible = false;
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
        controller.GetWindow().Size = Big; // the whole colony (full buildings grid, worked tiles, idle colonists) fits at 1440x1080
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
        controller.GetWindow().Size = Big; // the whole harbour (recruit/train dock + full goods market) fits at 1440x1080
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

    [TestCase(Timeout = 120000)]
    public async Task Capture_ColopediaTabs()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        var panel = controller.GetNode<ColopediaPanel>("UI/ColopediaPanel");
        panel.Open(game);              // applies the opaque parchment frame (OpenTo alone leaves the map showing through)
        await runner.SimulateFrames(2);
        // Each reference tab, captured separately. An empty anchor is harmless (it just skips the row highlight).
        (ColopediaPanel.Category cat, string name)[] tabs =
        [
            (ColopediaPanel.Category.Terrain, "colopedia-terrain"),
            (ColopediaPanel.Category.Units, "colopedia-units"),
            (ColopediaPanel.Category.Buildings, "colopedia-buildings"),
            (ColopediaPanel.Category.Fathers, "colopedia-fathers"),
            (ColopediaPanel.Category.Nations, "colopedia-nations"),
            (ColopediaPanel.Category.Resources, "colopedia-resources"),
            (ColopediaPanel.Category.Concepts, "colopedia-concepts"),
        ];
        foreach ((ColopediaPanel.Category cat, string name) in tabs)
        {
            panel.OpenTo(game, cat, "");
            await CapturePanel(runner, controller, name);
        }
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_HighScores()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        controller.OpenHighScoresPanel();
        await CapturePanel(runner, controller, "high-scores");
    }

    [TestCase(Timeout = 60000)]
    public async Task Capture_MessageLog()
    {
        if (!Enabled) return;
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        GameController controller = LoadGame(runner);
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        controller.OpenMessageLogPanel();
        await CapturePanel(runner, controller, "message-log");
    }

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, game);
}
