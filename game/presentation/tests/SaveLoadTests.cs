using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the save/load feature: the slot dialog lists/chooses slots, the
/// controller's <c>SaveTo</c>/<c>LoadFrom</c> round-trip a game, and the main menu's pending-load boots a saved game.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SaveLoadTests
{
    private const string DialogScene = "res://scenes/SaveLoadDialog.tscn";
    private const string GameScene = "res://scenes/main.tscn";

    [TestCase]
    public async Task LoadMode_ListsFiveSlots()
    {
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        dialog.Open(SaveLoadDialog.Mode.Load, _ => { });
        await runner.SimulateFrames(1);

        AssertThat(dialog.GetNode<VBoxContainer>("Panel/VBox/Slots").GetChildCount()).IsEqual(5);
    }

    [TestCase]
    public async Task ChoosingAFilledSlot_InvokesCallback_AndCloses()
    {
        WriteSlot(2, "{}"); // any file makes the slot "filled" and choosable in load mode
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        string? chosen = null;
        bool closed = false;
        dialog.Closed += () => closed = true;
        dialog.Open(SaveLoadDialog.Mode.Load, p => chosen = p);
        await runner.SimulateFrames(1);

        var slot2 = (Button)dialog.GetNode<VBoxContainer>("Panel/VBox/Slots").GetChild(1);
        AssertThat(slot2.Disabled).IsFalse();
        slot2.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(chosen).IsEqual(SaveLoadDialog.SlotPath(2));
        AssertThat(closed).IsTrue();
    }

    [TestCase]
    public async Task BackButton_ClosesWithoutChoosing()
    {
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        bool closed = false;
        dialog.Closed += () => closed = true;
        dialog.Open(SaveLoadDialog.Mode.Save, _ => { });
        await runner.SimulateFrames(1);

        dialog.GetNode<Button>("Panel/VBox/BackButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(closed).IsTrue();
    }

    [TestCase]
    public async Task SaveTo_ThenLoadFrom_RestoresTheSavedTurn()
    {
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        var controller = (GameController)runner.Scene();
        await runner.SimulateFrames(2);
        controller.StartNewGame(424242);

        string path = SaveLoadDialog.SlotPath(4);
        controller.SaveTo(path); // save at turn 1
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(controller.GetNode<Label>("UI/StatusLabel").Text).Contains("Turn 2");

        controller.LoadFrom(path); // back to turn 1
        await runner.SimulateFrames(1);
        AssertThat(controller.GetNode<Label>("UI/StatusLabel").Text).Contains("Turn 1");
    }

    [TestCase]
    public async Task PendingLoadPath_BootsTheSavedGame_InsteadOfANewOne()
    {
        // Write a real save (turn 1, seed 424242) without a scene, then boot the game with the pending-load set.
        GameVariant variant = GameVariants.Default;
        Game game = Game.New(variant.LoadRuleset(), 424242);
        string path = SaveLoadDialog.SlotPath(5);
        WriteSlot(5, SaveGame.From(game, variant.Id).ToJson());
        GameController.PendingLoadPath = path;

        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);

        AssertThat(GameController.PendingLoadPath).IsNull(); // consumed
        AssertThat(runner.Scene().GetNode<Label>("UI/StatusLabel").Text).Contains("Turn 1");
    }

    private static void WriteSlot(int slot, string contents)
    {
        DirAccess.MakeDirRecursiveAbsolute(GameController.SavesDir);
        using var file = FileAccess.Open(SaveLoadDialog.SlotPath(slot), FileAccess.ModeFlags.Write);
        file.StoreString(contents);
    }
}
