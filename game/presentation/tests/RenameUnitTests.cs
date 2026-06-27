using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for Parity Wave 9 Stream A — <b>custom unit naming</b> (`86d3drmzu`): the
/// rename action opens a code-built dialog pre-filled with the selected unit's current name, Confirm forwards the typed
/// text to <see cref="Game.NameUnit"/> (reflected in the HUD readout), and <b>Cancel/Escape leaves the name unchanged
/// and frees the dialog</b> (the Wave 8 cancel-path regression guard). The right-click context menu surfaces the
/// rename entry for the selected unit. Presentation-only over the GameLogic command (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RenameUnitTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Colonist = "model.unit.freeColonist";

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, game);

    private static void SetSelectedUnit(GameController controller, Unit? unit) =>
        controller.GetType()
            .GetField("_selectedUnit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, unit);

    private static void InvokePrivate(GameController controller, string method) =>
        controller.GetType()
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);

    /// <summary>A 3×1 plains row with a single human colonist on the middle tile, all explored.</summary>
    private static Game OneUnitGame() => new SaveGame
    {
        Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
        MapWidth = 3, MapHeight = 1,
        Terrain = ["model.tile.plains", "model.tile.plains", "model.tile.plains"],
        Units = [new SavedUnit(1, Colonist, 1, 0, 3)],
        Explored = [0, 1, 2],
    }.Restore(Classic);

    private static AcceptDialog? RenameDialog(GameController controller) =>
        controller.GetChildren().OfType<AcceptDialog>().FirstOrDefault(d => d.Title.ToString() == "Rename unit");

    // ── Rename: open pre-filled, confirm stores the name ─────────────────────────────────────────────────────────

    [TestCase]
    public async Task RenamingASelectedUnit_OpensPrefilled_AndConfirmStoresTheName()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game game = OneUnitGame();
        SetGame(controller, game);
        Unit unit = game.Units.First(u => u.Id == 1);
        game.NameUnit(unit, "Endeavour"); // give it an existing name so we can prove the field is pre-filled
        SetSelectedUnit(controller, unit);

        InvokePrivate(controller, "RenameSelectedUnit");
        await runner.SimulateFrames(2);

        // The code-built dialog is on screen, pre-filled with the unit's current custom name.
        AcceptDialog? dialog = RenameDialog(controller);
        AssertThat(dialog).IsNotNull();
        AssertThat(dialog!.Visible).IsTrue();
        var field = dialog.FindChild("RenameUnitField", recursive: true, owned: false) as LineEdit;
        AssertThat(field).IsNotNull();
        AssertThat(field!.Text).IsEqual("Endeavour");

        // Type a new name and confirm → Game.NameUnit stores it (trimmed) and the prompt closes (no orphan).
        field.Text = "  Resolution  "; // surrounding whitespace proves GameLogic trims it
        dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);

        AssertThat(unit.Name).IsEqual("Resolution");
        AssertThat(GodotObject.IsInstanceValid(dialog)).IsFalse();

        // The HUD selected-unit readout now leads with the custom name.
        AssertThat(controller.GetNode<Label>("UI/SelectedUnitPanel/VBox/Label").Text).Contains("Resolution");
    }

    [TestCase]
    public async Task RenamingAnUnnamedUnit_OpensBlank_AndAClearedNameRevertsToTheType()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game game = OneUnitGame();
        SetGame(controller, game);
        Unit unit = game.Units.First(u => u.Id == 1);
        game.NameUnit(unit, "Discovery");
        SetSelectedUnit(controller, unit);

        InvokePrivate(controller, "RenameSelectedUnit");
        await runner.SimulateFrames(2);
        AcceptDialog dialog = RenameDialog(controller)!;
        var field = dialog.FindChild("RenameUnitField", recursive: true, owned: false) as LineEdit;

        // A blank/whitespace name clears the custom name back to the generic type name (rule lives in Game.NameUnit).
        field!.Text = "   ";
        dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);

        AssertThat(unit.Name).IsNull();
    }

    // ── Cancel / Escape path (Wave 8 regression guard) ───────────────────────────────────────────────────────────

    [TestCase]
    public async Task CancellingTheRenameDialog_LeavesTheNameUnchanged_AndFreesTheDialog()
    {
        // Regression guard: a code-built AcceptDialog raises Canceled (not Confirmed) on Escape / the window-close X.
        // The Canceled handler must free the node and reset the re-entrancy guard, leaving the unit's name unchanged —
        // otherwise the guard stuck open (the prompt never re-fired) and the dialog leaked as an orphan.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game game = OneUnitGame();
        SetGame(controller, game);
        Unit unit = game.Units.First(u => u.Id == 1);
        game.NameUnit(unit, "Beagle");
        SetSelectedUnit(controller, unit);

        InvokePrivate(controller, "RenameSelectedUnit");
        await runner.SimulateFrames(2);
        AcceptDialog dialog = RenameDialog(controller)!;
        var field = dialog.FindChild("RenameUnitField", recursive: true, owned: false) as LineEdit;
        field!.Text = "Victory"; // typed but NOT confirmed — Cancel must discard it

        // Dismiss (Escape / titlebar X both raise Canceled) instead of confirming.
        dialog.EmitSignal(AcceptDialog.SignalName.Canceled);
        await runner.SimulateFrames(2);

        // The name is unchanged, the dialog was freed (no orphan), and the guard reset so a re-open is a clean no-op.
        AssertThat(unit.Name).IsEqual("Beagle");
        AssertThat(GodotObject.IsInstanceValid(dialog)).IsFalse();

        bool guardOpen = (bool)controller.GetType()
            .GetField("_renameUnitDialogOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        AssertThat(guardOpen).IsFalse();

        // A subsequent rename attempt opens a fresh dialog (guard was reset, not stuck).
        InvokePrivate(controller, "RenameSelectedUnit");
        await runner.SimulateFrames(2);
        AssertThat(RenameDialog(controller)).IsNotNull();
    }

    [TestCase]
    public async Task RenameSelectedUnit_IsANoOp_WithNoSelection()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        SetGame(controller, OneUnitGame());
        SetSelectedUnit(controller, null);

        InvokePrivate(controller, "RenameSelectedUnit");
        await runner.SimulateFrames(1);

        AssertThat(RenameDialog(controller)).IsNull(); // no selection → no dialog
    }

    // ── Right-click context-menu surface ─────────────────────────────────────────────────────────────────────────

    [TestCase(Timeout = 60000)]
    public async Task RightClickMenu_OffersRename_ForTheSelectedUnitOnTheTile()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game game = OneUnitGame();
        SetGame(controller, game);
        Unit unit = game.Units.First(u => u.Id == 1);
        SetSelectedUnit(controller, unit);

        // Centre the camera on the unit's tile and right-click it with the screen-centre as the event position.
        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(unit.Position);
        await runner.SimulateFrames(1);
        Vector2 screenCentre = controller.GetViewport().GetVisibleRect().Size / 2f;
        controller.GetType().GetMethod("HandleRightClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, [screenCentre]);
        await runner.SimulateFrames(1);

        var menu = controller.GetNode<CanvasLayer>("UI").GetChildren().OfType<PopupMenu>().First();
        bool hasRename = false;
        int renameId = 0;
        for (int i = 0; i < menu.ItemCount; i++)
        {
            if (menu.GetItemText(i).StartsWith("Rename"))
            {
                hasRename = true;
                renameId = menu.GetItemId(i);
            }
        }
        AssertThat(hasRename).IsTrue();

        // Firing the rename entry opens the rename dialog.
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, renameId);
        await runner.SimulateFrames(2);
        AssertThat(RenameDialog(controller)).IsNotNull();
    }

    // ── Render-verify the dialog actually paints ─────────────────────────────────────────────────────────────────

    [TestCase]
    public async Task RenameDialog_RendersOnScreen_WithItsField()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game game = OneUnitGame();
        SetGame(controller, game);
        Unit unit = game.Units.First(u => u.Id == 1);
        SetSelectedUnit(controller, unit);

        InvokePrivate(controller, "RenameSelectedUnit");
        await runner.SimulateFrames(3);

        AcceptDialog dialog = RenameDialog(controller)!;
        var field = dialog.FindChild("RenameUnitField", recursive: true, owned: false) as LineEdit;
        AssertThat(field).IsNotNull();

        // RENDER-VERIFY: capture the viewport to a temp PNG, confirm the field occupies a non-empty on-screen rect
        // (i.e. the modal actually laid out and painted), then DELETE the temp png — we never commit pngs.
        Image img = controller.GetViewport().GetTexture().GetImage();
        string tmp = Path.Combine(Path.GetTempPath(), $"renameunit_{System.Guid.NewGuid():N}.png");
        AssertThat(img.SavePng(tmp)).IsEqual(Error.Ok);
        try
        {
            using var loaded = Image.LoadFromFile(tmp);
            AssertThat(loaded.GetWidth()).IsGreater(0);
            AssertThat(loaded.GetHeight()).IsGreater(0);
            Rect2 rect = field!.GetGlobalRect();
            AssertThat(rect.Size.X).IsGreater(0f); // the field has a real on-screen footprint
            AssertThat(rect.Size.Y).IsGreater(0f);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }
}
