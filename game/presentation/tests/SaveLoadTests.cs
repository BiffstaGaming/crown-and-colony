using System.Threading.Tasks;
using CrownAndColony.GameLogic.App;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the save/load feature: the slot dialog lists/chooses slots, deletes and
/// overwrite-confirms, surfaces the autosave entry, and the controller's <c>SaveTo</c>/<c>LoadFrom</c> round-trip a
/// game; the main menu's pending-load boots a saved game.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SaveLoadTests
{
    private const string DialogScene = "res://scenes/SaveLoadDialog.tscn";
    private const string GameScene = "res://scenes/main.tscn";

    // The first child of a slot row is the load/save button; the (optional) second is its Delete button.
    private static Button SlotButton(SaveLoadDialog dialog, int rowIndex) =>
        (Button)dialog.GetNode<VBoxContainer>("Panel/VBox/Slots").GetChild(rowIndex).GetChild(0);

    [TestCase]
    public async Task LoadMode_ListsFiveManualSlots_WhenNoAutosave()
    {
        ClearSaves();
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        dialog.Open(SaveLoadDialog.Mode.Load, _ => { });
        await runner.SimulateFrames(1);

        AssertThat(dialog.GetNode<VBoxContainer>("Panel/VBox/Slots").GetChildCount()).IsEqual(5);
    }

    [TestCase]
    public async Task Autosave_AppearsAsASixthEntry_WhenItExists()
    {
        ClearSaves();
        WriteFile(GameController.AutosavePath, "{}"); // an autosave on disk → a sixth (read-only) entry
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        dialog.Open(SaveLoadDialog.Mode.Load, _ => { });
        await runner.SimulateFrames(1);

        AssertThat(dialog.GetNode<VBoxContainer>("Panel/VBox/Slots").GetChildCount()).IsEqual(6);
        AssertThat(SlotButton(dialog, 5).Text).Contains("Autosave");
        AssertThat(SlotButton(dialog, 5).Disabled).IsFalse(); // loadable
    }

    [TestCase]
    public async Task SaveMode_CannotTargetTheAutosave()
    {
        ClearSaves();
        WriteFile(GameController.AutosavePath, "{}");
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        dialog.Open(SaveLoadDialog.Mode.Save, _ => { });
        await runner.SimulateFrames(1);

        // The autosave row exists but its button is disabled in Save mode — a manual save can never overwrite it.
        AssertThat(SlotButton(dialog, 5).Text).Contains("Autosave");
        AssertThat(SlotButton(dialog, 5).Disabled).IsTrue();
    }

    [TestCase]
    public async Task ChoosingAFilledSlot_InvokesCallback_AndCloses()
    {
        ClearSaves();
        WriteFile(SaveLoadDialog.SlotPath(2), "{}"); // any file makes the slot "filled" and choosable in load mode
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        string? chosen = null;
        bool closed = false;
        dialog.Closed += () => closed = true;
        dialog.Open(SaveLoadDialog.Mode.Load, p => chosen = p);
        await runner.SimulateFrames(1);

        Button slot2 = SlotButton(dialog, 1);
        AssertThat(slot2.Disabled).IsFalse();
        slot2.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(chosen).IsEqual(SaveLoadDialog.SlotPath(2));
        AssertThat(closed).IsTrue();
    }

    [TestCase]
    public async Task DeletingAFilledSlot_RemovesTheFile_AndRebuildsTheRow()
    {
        ClearSaves();
        WriteFile(SaveLoadDialog.SlotPath(3), "{}");
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        dialog.Open(SaveLoadDialog.Mode.Load, _ => { });
        await runner.SimulateFrames(1);

        // Row index 2 = slot 3; a filled, deletable row has a second child (the Delete button).
        Node row = dialog.GetNode<VBoxContainer>("Panel/VBox/Slots").GetChild(2);
        AssertThat(row.GetChildCount()).IsEqual(2);
        var delete = (Button)row.GetChild(1);
        delete.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The ConfirmationDialog popped up; confirm it.
        ConfirmDialogChild(dialog, confirm: true);
        await runner.SimulateFrames(1);

        AssertThat(FileAccess.FileExists(SaveLoadDialog.SlotPath(3))).IsFalse();
        // The rebuilt row is now empty (no Delete button).
        Node rebuilt = dialog.GetNode<VBoxContainer>("Panel/VBox/Slots").GetChild(2);
        AssertThat(rebuilt.GetChildCount()).IsEqual(1);
        AssertThat(((Button)rebuilt.GetChild(0)).Text).Contains("empty");
    }

    [TestCase]
    public async Task SavingOverAFilledSlot_PromptsOverwrite_CancelIsANoOp()
    {
        ClearSaves();
        WriteFile(SaveLoadDialog.SlotPath(1), "{}");
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        bool chosen = false;
        bool closed = false;
        dialog.Closed += () => closed = true;
        dialog.Open(SaveLoadDialog.Mode.Save, _ => chosen = true);
        await runner.SimulateFrames(1);

        SlotButton(dialog, 0).EmitSignal(BaseButton.SignalName.Pressed); // save over the filled slot 1
        await runner.SimulateFrames(1);

        // Cancel the overwrite confirmation → nothing is chosen, the dialog stays open.
        ConfirmDialogChild(dialog, confirm: false);
        await runner.SimulateFrames(1);

        AssertThat(chosen).IsFalse();
        AssertThat(closed).IsFalse();
    }

    [TestCase]
    public async Task SavingOverAFilledSlot_Confirm_ChoosesTheSlot()
    {
        ClearSaves();
        WriteFile(SaveLoadDialog.SlotPath(1), "{}");
        ISceneRunner runner = ISceneRunner.Load(DialogScene);
        await runner.SimulateFrames(1);
        var dialog = (SaveLoadDialog)runner.Scene();

        string? chosen = null;
        dialog.Open(SaveLoadDialog.Mode.Save, p => chosen = p);
        await runner.SimulateFrames(1);

        SlotButton(dialog, 0).EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        ConfirmDialogChild(dialog, confirm: true);
        await runner.SimulateFrames(1);

        AssertThat(chosen).IsEqual(SaveLoadDialog.SlotPath(1));
    }

    [TestCase]
    public async Task BackButton_ClosesWithoutChoosing()
    {
        ClearSaves();
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
    public async Task MessageLog_SurvivesSaveLoad_RoundTrip()
    {
        // Task 86d3e959t (save v59): the in-session message log is now persisted. Seed the controller's log with two
        // turns of categorized notices (the seam OnEndTurnPressed fills), save, then load — the loaded controller must
        // re-build the same per-turn log instead of starting empty (the pre-v59 behaviour).
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        var controller = (GameController)runner.Scene();
        await runner.SimulateFrames(2);
        controller.StartNewGame(424242);

        var log = MessageLogOf(controller);
        log.Clear();
        log.Add(new MessageLogPanel.Entry(1, new System.Collections.Generic.List<MessageLogPanel.LogMessage>
        {
            new(MessageCategory.Combat, "A privateer sank your Caravel at (4,5)!"),
            new(MessageCategory.Economy, "Your custom house in Jamestown sold 100 sugar for 250 gold."),
        }));
        log.Add(new MessageLogPanel.Entry(2, new System.Collections.Generic.List<MessageLogPanel.LogMessage>
        {
            new(MessageCategory.Monarch, "The Crown lowered your tax rate to 18%."),
        }));

        string path = SaveLoadDialog.SlotPath(3);
        controller.SaveTo(path);
        controller.LoadFrom(path);
        await runner.SimulateFrames(1);

        var restored = MessageLogOf(controller);
        AssertThat(restored.Count).IsEqual(2);                 // two turns re-grouped
        AssertThat(restored[0].Turn).IsEqual(1);
        AssertThat(restored[0].Messages.Count).IsEqual(2);     // both turn-1 notices kept, in order
        AssertThat(restored[0].Messages[0].Category).IsEqual(MessageCategory.Combat);
        AssertThat(restored[0].Messages[1].Category).IsEqual(MessageCategory.Economy);
        AssertThat(restored[1].Turn).IsEqual(2);
        AssertThat(restored[1].Messages[0].Text).Contains("tax rate");
    }

    private static System.Collections.Generic.List<MessageLogPanel.Entry> MessageLogOf(GameController controller) =>
        (System.Collections.Generic.List<MessageLogPanel.Entry>)controller.GetType()
            .GetField("_messageLog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(controller)!;

    [TestCase]
    public async Task EndingATurn_WritesTheAutosave()
    {
        ClearSaves();
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        var controller = (GameController)runner.Scene();
        await runner.SimulateFrames(2);
        controller.StartNewGame(424242);

        AssertThat(FileAccess.FileExists(GameController.AutosavePath)).IsFalse();
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The default autosave period is 1, so a turn end writes the autosave (the /root/Settings autoload supplies it).
        AssertThat(FileAccess.FileExists(GameController.AutosavePath)).IsTrue();
    }

    [TestCase]
    public async Task PendingLoadPath_BootsTheSavedGame_InsteadOfANewOne()
    {
        // Write a real save (turn 1, seed 424242) without a scene, then boot the game with the pending-load set.
        GameVariant variant = GameVariants.Default;
        Game game = Game.New(variant.LoadRuleset(), 424242);
        string path = SaveLoadDialog.SlotPath(5);
        WriteFile(path, SaveGame.From(game, variant.Id).ToJson());
        GameController.PendingLoadPath = path;

        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);

        AssertThat(GameController.PendingLoadPath).IsNull(); // consumed
        AssertThat(runner.Scene().GetNode<Label>("UI/StatusLabel").Text).Contains("Turn 1");
    }

    // Finds the first ConfirmationDialog child of the save dialog and emits its confirm/cancel signal.
    private static void ConfirmDialogChild(SaveLoadDialog dialog, bool confirm)
    {
        foreach (Node child in dialog.GetChildren())
        {
            if (child is ConfirmationDialog cd)
            {
                cd.EmitSignal(confirm ? AcceptDialog.SignalName.Confirmed : ConfirmationDialog.SignalName.Canceled);
                return;
            }
        }
    }

    private static void WriteFile(string path, string contents)
    {
        DirAccess.MakeDirRecursiveAbsolute(GameController.SavesDir);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(contents);
    }

    // Removes all manual slots + the autosave so each test starts from a known empty saves directory.
    private static void ClearSaves()
    {
        DirAccess.MakeDirRecursiveAbsolute(GameController.SavesDir);
        for (int i = 1; i <= 5; i++)
        {
            if (FileAccess.FileExists(SaveLoadDialog.SlotPath(i)))
            {
                DirAccess.RemoveAbsolute(SaveLoadDialog.SlotPath(i));
            }
        }
        if (FileAccess.FileExists(GameController.AutosavePath))
        {
            DirAccess.RemoveAbsolute(GameController.AutosavePath);
        }
    }
}
