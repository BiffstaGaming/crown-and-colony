using System.IO;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the Setup-UI wave (stream S-E): the New-Game dialog's map-import row
/// (86d3fq1cg) and custom-difficulty editor (86d3fq0x7). Follows the MainMenuTests recipe: load the real main-menu
/// scene, add a <see cref="NewGameDialog"/>, drive the controls, and assert the forwarding statics — nulled before
/// AND after each test so a static never leaks between tests.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NewGameSetupUiTests
{
    private const string MenuScene = "res://scenes/MainMenu.tscn";

    /// <summary>Builds the menu + dialog and returns both (dialog fully _Ready).</summary>
    private static async Task<(ISceneRunner Runner, NewGameDialog Dialog)> OpenDialog()
    {
        ISceneRunner runner = ISceneRunner.Load(MenuScene);
        await runner.SimulateFrames(2);
        var menu = (Control)runner.Scene();
        var dialog = new NewGameDialog();
        menu.AddChild(dialog);
        await runner.SimulateFrames(1); // _Ready builds the controls
        return (runner, dialog);
    }

    private static T Find<T>(NewGameDialog dialog, string name) where T : class
    {
        var node = dialog.FindChild(name, recursive: true, owned: false) as T;
        AssertThat(node).OverrideFailureMessage($"missing control: {name}").IsNotNull();
        return node!;
    }

    /// <summary>A temp file holding a tiny valid 3×2 text map definition.</summary>
    private static string WriteTempMap(string extension, byte[]? bytes = null)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
        if (bytes is null)
        {
            File.WriteAllText(path, "3 2\nplains plains plains\nocean ocean ocean\n");
        }
        else
        {
            File.WriteAllBytes(path, bytes);
        }
        return path;
    }

    // ── Map import row (86d3fq1cg) ──────────────────────────────────────────────────────────────────────────────────

    [TestCase]
    public async Task MapDropdown_OffersTheImportRow_AfterRandomAndAmerica()
    {
        (ISceneRunner _, NewGameDialog dialog) = await OpenDialog();

        var mapOption = Find<OptionButton>(dialog, "MapOption");
        AssertThat(mapOption.ItemCount).IsEqual(3); // Random, America, Import map…
        AssertThat(mapOption.GetItemText(2)).Contains("Import");
        // The status line exists but stays hidden until an import is attempted (default layout unchanged).
        var status = Find<Label>(dialog, "ImportStatusLabel");
        AssertThat(status.Visible).IsFalse();
    }

    [TestCase]
    public async Task ImportingAValidFile_ShowsItsSummary_AndForwardsPendingImportedMap()
    {
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        NewGameDialog.PendingImportedMap = null; // clean slate (statics survive between tests)
        string path = WriteTempMap(".txt");
        try
        {
            MapSource? chosenMap = null;
            dialog.Open((_, _, _, map) => chosenMap = map);

            var mapOption = Find<OptionButton>(dialog, "MapOption");
            mapOption.Select(2); // the import row (Select does not emit ItemSelected → no native picker opens)
            dialog.OnImportFileSelected(path); // the validation path the picker's FileSelected drives
            await runner.SimulateFrames(1);

            // The status line shows the file + dimensions + settlement count, and the size/land dials disable
            // (an imported map sets its own dimensions, like the fixed America map).
            var status = Find<Label>(dialog, "ImportStatusLabel");
            AssertThat(status.Visible).IsTrue();
            AssertThat(status.Text).Contains(Path.GetFileName(path));
            AssertThat(status.Text).Contains("3×2");
            AssertThat(status.Text).Contains("0 settlements");
            AssertThat(Find<OptionButton>(dialog, "SizeOption").Disabled).IsTrue();
            AssertThat(Find<OptionButton>(dialog, "LandOption").Disabled).IsTrue();

            Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            // The import rides its static (consumed by the new-game host via Game.New's importOverride); the
            // underlying MapSource stays Random (the import choice is dialog-level, not an engine enum row).
            AssertThat(NewGameDialog.PendingImportedMap).IsNotNull();
            AssertThat(NewGameDialog.PendingImportedMap!.Map.Width).IsEqual(3);
            AssertThat(NewGameDialog.PendingImportedMap.Map.Height).IsEqual(2);
            AssertThat(chosenMap).IsEqual(MapSource.Random);
        }
        finally
        {
            File.Delete(path);
            NewGameDialog.PendingImportedMap = null; // tidy the static for the next test
        }
    }

    [TestCase]
    public async Task ImportingAColonizationMpFile_ValidatesThroughTheBinaryLoader()
    {
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        NewGameDialog.PendingImportedMap = null;
        // A synthesized 2×1 .MP: header {2,0,1,0,4,0} + plains, ocean. Never a real original-game file (licensing).
        string path = WriteTempMap(".MP", [2, 0, 1, 0, 4, 0, 2, 25]);
        try
        {
            dialog.Open((_, _, _, _) => { });
            dialog.OnImportFileSelected(path);
            await runner.SimulateFrames(1);

            AssertThat(Find<Label>(dialog, "ImportStatusLabel").Text).Contains("2×1");

            Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            AssertThat(NewGameDialog.PendingImportedMap).IsNotNull();
            AssertThat(NewGameDialog.PendingImportedMap!.Map.TerrainAt(new Position(0, 0)).Id)
                .IsEqual("model.tile.plains");
        }
        finally
        {
            File.Delete(path);
            NewGameDialog.PendingImportedMap = null;
        }
    }

    [TestCase]
    public async Task ImportingAnInvalidFile_ShowsTheError_RevertsToRandom_AndForwardsNoImport()
    {
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        NewGameDialog.PendingImportedMap = null;
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        File.WriteAllText(path, "not a map at all");
        try
        {
            dialog.Open((_, _, _, _) => { });

            var mapOption = Find<OptionButton>(dialog, "MapOption");
            mapOption.Select(2);
            dialog.OnImportFileSelected(path);
            await runner.SimulateFrames(1);

            // The parse error is surfaced and the dropdown reverts to Random (no dead import row left armed).
            var status = Find<Label>(dialog, "ImportStatusLabel");
            AssertThat(status.Visible).IsTrue();
            AssertThat(status.Text).Contains("header");
            AssertThat(mapOption.Selected).IsEqual(0);
            AssertThat(Find<OptionButton>(dialog, "SizeOption").Disabled).IsFalse(); // Random re-enables the dials

            Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            AssertThat(NewGameDialog.PendingImportedMap).IsNull(); // nothing armed → no override forwarded
        }
        finally
        {
            File.Delete(path);
            NewGameDialog.PendingImportedMap = null;
        }
    }

    [TestCase]
    public async Task DefaultStart_LeavesPendingImportedMapNull()
    {
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        // A stale static from an earlier (aborted) session must be overwritten with null on a default Start.
        NewGameDialog.PendingImportedMap = new MapImportResult(
            new GameMap(1, 1, [Ruleset.LoadClassic().Terrain("model.tile.ocean")]), []);
        dialog.Open((_, _, _, _) => { });

        Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(NewGameDialog.PendingImportedMap).IsNull();
        NewGameDialog.PendingImportedMap = null;
    }
}
