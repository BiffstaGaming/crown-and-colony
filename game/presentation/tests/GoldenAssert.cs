using System;
using System.IO;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// Shared L4 golden-image comparison (docs/TESTING.md): diffs a captured viewport against a committed golden PNG with
/// a per-channel + fraction tolerance (GPU/font rasterisation noise), writes actual/diff artifacts on mismatch, and
/// regenerates the golden when <c>GOLDEN_UPDATE=1</c>. Used by the map goldens (<see cref="VisualGoldenTests"/>) and
/// the UI/menu goldens (<see cref="MenuGoldenTests"/>).
/// </summary>
public static class GoldenAssert
{
    /// <summary>Per-channel delta below this is noise (GPU/font rasterisation differences).</summary>
    public const int ChannelTolerance = 8;

    /// <summary>Default fraction of pixels allowed to exceed the channel tolerance before it counts as a regression.</summary>
    public const double DefaultMaxDifferingFraction = 0.005;

    private static string ProjectDir => ProjectSettings.GlobalizePath("res://");

    /// <summary>
    /// Asserts <paramref name="actual"/> matches the committed golden <paramref name="name"/>. Text-heavy UI frames may
    /// pass a looser <paramref name="maxDifferingFraction"/> to absorb cross-platform font antialiasing.
    /// </summary>
    public static void Assert(string name, Image actual, double maxDifferingFraction = DefaultMaxDifferingFraction)
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
        if (fraction > maxDifferingFraction)
        {
            SaveFailureArtifacts(resultsDir, name, actual, diff);
        }
        AssertThat(fraction)
            .OverrideFailureMessage(
                $"Visual golden '{name}': {differing} pixels ({fraction:P2}) differ beyond tolerance " +
                $"(allowed {maxDifferingFraction:P1}). Artifacts in TestResults/visual/. " +
                "If the change is intentional, regenerate with GOLDEN_UPDATE=1 and commit the new golden.")
            .IsLessEqual(maxDifferingFraction);
    }

    private static void SaveFailureArtifacts(string resultsDir, string name, Image actual, Image? diff)
    {
        Directory.CreateDirectory(resultsDir);
        actual.SavePng(Path.Combine(resultsDir, $"{name}.actual.png"));
        diff?.SavePng(Path.Combine(resultsDir, $"{name}.diff.png"));
    }
}
