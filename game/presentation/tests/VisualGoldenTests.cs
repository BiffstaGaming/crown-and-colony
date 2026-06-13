using System;
using System.IO;
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

    // Per-channel delta below this is noise (GPU rasterization differences);
    // and up to 0.5% of pixels may exceed it before we call it a regression.
    private const int ChannelTolerance = 8;
    private const double MaxDifferingFraction = 0.005;

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
        AssertGolden("map-seed424242", actual);
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
        AssertGolden("colony-seed424242", actual);
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
        AssertGolden("native-settlement-seed424242", actual);
    }

    private static CrownAndColony.GameLogic.GameSession.Game GetGame(GameController controller) =>
        (CrownAndColony.GameLogic.GameSession.Game)controller
            .GetType()
            .GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(controller)!;

    private static string ProjectDir => ProjectSettings.GlobalizePath("res://");

    private static void AssertGolden(string name, Image actual)
    {
        string goldenPath = Path.Combine(ProjectDir, "tests", "visual", "goldens", $"{name}.png");
        string resultsDir = Path.Combine(ProjectDir, "TestResults", "visual");

        if (System.Environment.GetEnvironmentVariable("GOLDEN_UPDATE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            actual.SavePng(goldenPath);
            GD.Print($"[golden] regenerated {name} ({actual.GetWidth()}x{actual.GetHeight()})");
            return;
        }

        AssertThat(File.Exists(goldenPath))
            .OverrideFailureMessage($"Golden '{name}' missing — run once with GOLDEN_UPDATE=1 to create it.")
            .IsTrue();

        var golden = new Image();
        golden.Load(goldenPath);

        if (golden.GetWidth() != actual.GetWidth() || golden.GetHeight() != actual.GetHeight())
        {
            SaveFailureArtifacts(resultsDir, name, actual, null);
            AssertThat(false)
                .OverrideFailureMessage(
                    $"Golden '{name}' size {golden.GetWidth()}x{golden.GetHeight()} != actual " +
                    $"{actual.GetWidth()}x{actual.GetHeight()} — capture setup changed?")
                .IsTrue();
            return;
        }

        golden.Convert(Image.Format.Rgba8);
        actual.Convert(Image.Format.Rgba8);
        byte[] expected = golden.GetData();
        byte[] got = actual.GetData();

        int differing = 0;
        var diff = Image.CreateEmpty(golden.GetWidth(), golden.GetHeight(), false, Image.Format.Rgba8);
        for (int i = 0; i < expected.Length; i += 4)
        {
            bool pixelDiffers =
                Math.Abs(expected[i] - got[i]) > ChannelTolerance ||
                Math.Abs(expected[i + 1] - got[i + 1]) > ChannelTolerance ||
                Math.Abs(expected[i + 2] - got[i + 2]) > ChannelTolerance;
            if (pixelDiffers)
            {
                differing++;
                int p = i / 4;
                diff.SetPixel(p % golden.GetWidth(), p / golden.GetWidth(), Colors.Red);
            }
        }

        double fraction = differing / (double)(expected.Length / 4);
        if (fraction > MaxDifferingFraction)
        {
            SaveFailureArtifacts(resultsDir, name, actual, diff);
        }
        AssertThat(fraction)
            .OverrideFailureMessage(
                $"Visual golden '{name}': {differing} pixels ({fraction:P2}) differ beyond tolerance " +
                $"(allowed {MaxDifferingFraction:P1}). Artifacts in TestResults/visual/. " +
                "If the change is intentional, regenerate with GOLDEN_UPDATE=1 and commit the new golden.")
            .IsLessEqual(MaxDifferingFraction);
    }

    private static void SaveFailureArtifacts(string resultsDir, string name, Image actual, Image? diff)
    {
        Directory.CreateDirectory(resultsDir);
        actual.SavePng(Path.Combine(resultsDir, $"{name}.actual.png"));
        diff?.SavePng(Path.Combine(resultsDir, $"{name}.diff.png"));
    }
}
