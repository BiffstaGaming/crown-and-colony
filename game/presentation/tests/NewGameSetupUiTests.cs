using System.IO;
using System.Linq;
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

    // ── Landmass style forwarding (86d3f62r5) ───────────────────────────────────────────────────────────────────────

    [TestCase]
    public async Task LandStyleDropdown_OffersTheThreeStyles_AndStartForwardsThePickedStyle()
    {
        // 86d3f62r5: the landmass-style dropdown was reachable but undriven by L3. Selecting a non-default style and
        // pressing Start forwards it onto GameController.PendingLandStyle (consumed by the new-game host into Game.New).
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        GameController.PendingLandStyle = null; // clean slate (statics survive between tests)
        try
        {
            var landStyle = Find<OptionButton>(dialog, "LandStyleOption");
            AssertThat(landStyle.ItemCount).IsEqual(WorldSizeOptions.LandStyles.Count); // Continent / Archipelago / Islands
            AssertThat(landStyle.Selected).IsEqual(WorldSizeOptions.DefaultLandStyleIndex); // Continent — the shipped default

            dialog.Open((_, _, _, _) => { });

            // Pick a NON-default style (the first that differs from the default index) and Start.
            int pick = Enumerable.Range(0, WorldSizeOptions.LandStyles.Count)
                .First(i => i != WorldSizeOptions.DefaultLandStyleIndex);
            landStyle.Select(pick);
            Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            // The picked style rides the static into the new-game host (ADR-006 — the style shapes only the random map).
            AssertThat(GameController.PendingLandStyle).IsNotNull();
            AssertThat(GameController.PendingLandStyle!.Style).IsEqual(WorldSizeOptions.LandStyles[pick].Style);
        }
        finally
        {
            GameController.PendingLandStyle = null; // tidy the static for the next test
        }
    }

    [TestCase]
    public async Task DefaultStart_ForwardsTheDefaultLandStyle()
    {
        // Left untouched, Start forwards the shipped-default (Continent) style — the byte-identical historical map.
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        GameController.PendingLandStyle = null;
        try
        {
            dialog.Open((_, _, _, _) => { });
            Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            AssertThat(GameController.PendingLandStyle).IsNotNull();
            AssertThat(GameController.PendingLandStyle!.Style).IsEqual(WorldSizeOptions.DefaultLandStyle.Style); // Continent
        }
        finally
        {
            GameController.PendingLandStyle = null;
        }
    }

    // ── Custom difficulty (86d3fq0x7) ───────────────────────────────────────────────────────────────────────────────

    /// <summary>The difficulty dropdown's "Custom…" row index (after the five real levels).</summary>
    private static int CustomDifficultyIndex => DifficultyLevels.All.Count;

    [TestCase]
    public async Task DifficultyDropdown_OffersTheCustomRow_AfterTheFiveLevels()
    {
        (ISceneRunner _, NewGameDialog dialog) = await OpenDialog();

        var difficultyOption = Find<OptionButton>(dialog, "DifficultyOption");
        AssertThat(difficultyOption.ItemCount).IsEqual(DifficultyLevels.All.Count + 1);
        AssertThat(difficultyOption.GetItemText(CustomDifficultyIndex)).Contains("Custom");
        AssertThat(difficultyOption.Selected).IsEqual(DifficultyLevels.DefaultIndex); // Conquistador — the default
        // The editor overlay exists (as a hidden child) with one control per schema field.
        var editor = dialog.FindChild("DifficultyEditor", recursive: true, owned: false) as DifficultyEditor;
        AssertThat(editor).IsNotNull();
        AssertThat(editor!.Visible).IsFalse();
        AssertThat(editor.FindChild("FoundingFatherFactorField", recursive: true, owned: false)).IsNotNull();
        AssertThat(editor.FindChild("RefSizeSoldiersField", recursive: true, owned: false)).IsNotNull();
        AssertThat(editor.FindChild("ExpertStartingUnitsField", recursive: true, owned: false)).IsNotNull();
    }

    [TestCase]
    public async Task PickingCustom_EditingAField_AndStarting_ForwardsTheOverrides_OnTheBaseLevel()
    {
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        NewGameDialog.PendingDifficultyOverrides = null; // clean slate (statics survive between tests)
        try
        {
            DifficultyLevel? chosenDifficulty = null;
            dialog.Open((_, _, difficulty, _) => chosenDifficulty = difficulty);

            // Select() does not emit ItemSelected — emit it explicitly (the MainMenuTests recipe) to open the editor.
            var difficultyOption = Find<OptionButton>(dialog, "DifficultyOption");
            difficultyOption.Select(CustomDifficultyIndex);
            difficultyOption.EmitSignal(OptionButton.SignalName.ItemSelected, CustomDifficultyIndex);
            await runner.SimulateFrames(1);

            var editor = (DifficultyEditor)dialog.FindChild("DifficultyEditor", recursive: true, owned: false)!;
            AssertThat(editor.Visible).IsTrue();

            // The fields seed from the base level's parsed values (medium: foundingFatherFactor 40); edit one.
            var factor = (LineEdit)editor.FindChild("FoundingFatherFactorField", recursive: true, owned: false)!;
            AssertThat(factor.Text).IsEqual("40");
            factor.Text = "77";
            ((Button)editor.FindChild("ApplyButton", recursive: true, owned: false)!)
                .EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);
            AssertThat(editor.Visible).IsFalse(); // a valid OK closes the editor; the dropdown stays on Custom…

            Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            // The onStart callback receives the BASE level (medium); the edits ride the overrides static.
            AssertThat(chosenDifficulty).IsNotNull();
            AssertThat(chosenDifficulty!.Id).IsEqual(DifficultyLevels.DefaultId);
            AssertThat(NewGameDialog.PendingDifficultyOverrides).IsNotNull();
            AssertThat(NewGameDialog.PendingDifficultyOverrides!.FoundingFatherFactor).IsEqual(77);
            // The unedited fields keep the base level's values (the seed was medium's parsed options).
            AssertThat(NewGameDialog.PendingDifficultyOverrides.Government.Bad).IsEqual(6);
        }
        finally
        {
            NewGameDialog.PendingDifficultyOverrides = null; // tidy the static for the next test
        }
    }

    [TestCase]
    public async Task CancellingTheEditor_RevertsTheDropdown_AndForwardsNoOverrides()
    {
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        NewGameDialog.PendingDifficultyOverrides = null;
        try
        {
            DifficultyLevel? chosenDifficulty = null;
            dialog.Open((_, _, difficulty, _) => chosenDifficulty = difficulty);

            // Sit on a real non-default level first (Viceroy, index 4) so the revert target is observable.
            var difficultyOption = Find<OptionButton>(dialog, "DifficultyOption");
            difficultyOption.Select(4);
            difficultyOption.EmitSignal(OptionButton.SignalName.ItemSelected, 4);
            difficultyOption.Select(CustomDifficultyIndex);
            difficultyOption.EmitSignal(OptionButton.SignalName.ItemSelected, CustomDifficultyIndex);
            await runner.SimulateFrames(1);

            var editor = (DifficultyEditor)dialog.FindChild("DifficultyEditor", recursive: true, owned: false)!;
            AssertThat(editor.Visible).IsTrue();
            // The seed is the CURRENT base level's values (veryHard: foundingFatherFactor 56), not always medium's.
            AssertThat(((LineEdit)editor.FindChild("FoundingFatherFactorField", recursive: true, owned: false)!).Text)
                .IsEqual("56");

            ((Button)editor.FindChild("CancelButton", recursive: true, owned: false)!)
                .EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            AssertThat(editor.Visible).IsFalse();
            AssertThat(difficultyOption.Selected).IsEqual(4); // reverted to the base level

            Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            AssertThat(chosenDifficulty!.Id).IsEqual(DifficultyLevels.All[4].Id);
            AssertThat(NewGameDialog.PendingDifficultyOverrides).IsNull(); // a cancel arms nothing
        }
        finally
        {
            NewGameDialog.PendingDifficultyOverrides = null;
        }
    }

    [TestCase]
    public async Task AnInvalidFieldValue_ShowsAnInlineError_AndKeepsTheEditorOpen()
    {
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        NewGameDialog.PendingDifficultyOverrides = null;
        try
        {
            dialog.Open((_, _, _, _) => { });

            var difficultyOption = Find<OptionButton>(dialog, "DifficultyOption");
            difficultyOption.Select(CustomDifficultyIndex);
            difficultyOption.EmitSignal(OptionButton.SignalName.ItemSelected, CustomDifficultyIndex);
            await runner.SimulateFrames(1);

            var editor = (DifficultyEditor)dialog.FindChild("DifficultyEditor", recursive: true, owned: false)!;
            var factor = (LineEdit)editor.FindChild("FoundingFatherFactorField", recursive: true, owned: false)!;
            factor.Text = "not a number";
            ((Button)editor.FindChild("ApplyButton", recursive: true, owned: false)!)
                .EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);

            // The editor stays open with an inline error naming the bad field; nothing was applied.
            AssertThat(editor.Visible).IsTrue();
            var error = (Label)editor.FindChild("ErrorLabel", recursive: true, owned: false)!;
            AssertThat(error.Visible).IsTrue();
            AssertThat(error.Text).Contains("Founding-father factor");

            // Correcting the value lets OK through.
            factor.Text = "41";
            ((Button)editor.FindChild("ApplyButton", recursive: true, owned: false)!)
                .EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);
            AssertThat(editor.Visible).IsFalse();

            Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);
            AssertThat(NewGameDialog.PendingDifficultyOverrides).IsNotNull();
            AssertThat(NewGameDialog.PendingDifficultyOverrides!.FoundingFatherFactor).IsEqual(41);
        }
        finally
        {
            NewGameDialog.PendingDifficultyOverrides = null;
        }
    }

    [TestCase]
    public async Task DefaultStart_LeavesPendingDifficultyOverridesNull()
    {
        (ISceneRunner runner, NewGameDialog dialog) = await OpenDialog();
        // A stale static from an earlier (aborted) session must be overwritten with null on a default Start.
        NewGameDialog.PendingDifficultyOverrides = DifficultyOptions.ClassicMedium;
        dialog.Open((_, _, _, _) => { });

        Find<Button>(dialog, "StartButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(NewGameDialog.PendingDifficultyOverrides).IsNull();
        NewGameDialog.PendingDifficultyOverrides = null;
    }
}
