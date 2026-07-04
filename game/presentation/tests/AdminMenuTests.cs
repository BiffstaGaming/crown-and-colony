using System.Linq;
using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 coverage for the hidden Admin / cheat menu (86d3jypd1): the backtick key opens a code box; the correct code
/// ("eldorado", case-insensitive) unlocks + opens the Admin menu for the session; a wrong code doesn't unlock; and the
/// "Show all map" toggle reveals the whole map (presentation-only) and un-reveals it again. Drives the real dialogs and
/// asserts through observable state (the dialogs' titles + <see cref="MapView.ExploredTileCount"/>).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AdminMenuTests
{
    private static async Task<(ISceneRunner Runner, GameController Controller)> StartGame()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        return (runner, controller);
    }

    private static AcceptDialog? DialogTitled(GameController c, string title) =>
        c.GetChildren().OfType<AcceptDialog>().FirstOrDefault(d => d.Title == title);

    private static async Task<AcceptDialog> UnlockAndOpen(ISceneRunner runner, GameController controller)
    {
        runner.SimulateKeyPressed(Key.Quoteleft);
        await runner.SimulateFrames(1);
        var prompt = DialogTitled(controller, "Enter code");
        AssertThat(prompt).IsNotNull();
        (prompt!.FindChild("CodeField", recursive: true, owned: false) as LineEdit)!.Text = "eldorado";
        prompt.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await runner.SimulateFrames(1);
        var menu = DialogTitled(controller, "Admin");
        AssertThat(menu).IsNotNull();
        return menu!;
    }

    [TestCase]
    public async Task Backtick_WhileLocked_OpensTheCodeBox()
    {
        (ISceneRunner runner, GameController controller) = await StartGame();
        runner.SimulateKeyPressed(Key.Quoteleft);
        await runner.SimulateFrames(1);
        AssertThat(DialogTitled(controller, "Enter code")).IsNotNull();
        AssertThat(DialogTitled(controller, "Admin")).IsNull(); // not straight to the menu while still locked
    }

    [TestCase]
    public async Task CorrectCode_UnlocksAndOpensTheAdminMenu()
    {
        (ISceneRunner runner, GameController controller) = await StartGame();
        await UnlockAndOpen(runner, controller);
        AssertThat(DialogTitled(controller, "Admin")!.FindChild("ShowAllMapToggle", true, false)).IsNotNull();
    }

    [TestCase]
    public async Task WrongCode_DoesNotUnlock_AndReopensTheCodeBox()
    {
        (ISceneRunner runner, GameController controller) = await StartGame();
        runner.SimulateKeyPressed(Key.Quoteleft);
        await runner.SimulateFrames(1);
        var prompt = DialogTitled(controller, "Enter code")!;
        (prompt.FindChild("CodeField", true, false) as LineEdit)!.Text = "nope";
        prompt.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);
        AssertThat(DialogTitled(controller, "Admin")).IsNull(); // wrong code → no menu

        // Still locked: the key opens the code box again, not the menu.
        runner.SimulateKeyPressed(Key.Quoteleft);
        await runner.SimulateFrames(1);
        AssertThat(DialogTitled(controller, "Enter code")).IsNotNull();
        AssertThat(DialogTitled(controller, "Admin")).IsNull();
    }

    [TestCase]
    public async Task ShowAllMap_RevealsTheWholeMap_AndTogglesBack()
    {
        (ISceneRunner runner, GameController controller) = await StartGame();
        var mapView = controller.GetNode<MapView>("MapView");
        int allTiles = controller.CurrentGame!.Map.Width * controller.CurrentGame.Map.Height;
        int fogged = mapView.ExploredTileCount;
        AssertThat(fogged).IsLess(allTiles); // fog on at the start — only tiles near the starting units are explored

        var menu = await UnlockAndOpen(runner, controller);
        var toggle = menu.FindChild("ShowAllMapToggle", true, false) as CheckButton;
        AssertThat(toggle).IsNotNull();

        toggle!.EmitSignal(BaseButton.SignalName.Toggled, true); // flip "Show all map" on
        await runner.SimulateFrames(1);
        AssertThat(mapView.ExploredTileCount).IsEqual(allTiles); // the whole map is now drawn

        toggle.EmitSignal(BaseButton.SignalName.Toggled, false); // flip it back off
        await runner.SimulateFrames(1);
        AssertThat(mapView.ExploredTileCount).IsEqual(fogged); // fog restored
    }

    [TestCase]
    public async Task Backtick_WhenAlreadyUnlocked_OpensTheMenuDirectly()
    {
        (ISceneRunner runner, GameController controller) = await StartGame();
        var menu = await UnlockAndOpen(runner, controller);
        menu.EmitSignal(AcceptDialog.SignalName.Confirmed); // Close
        await runner.SimulateFrames(2);

        runner.SimulateKeyPressed(Key.Quoteleft);
        await runner.SimulateFrames(1);
        AssertThat(DialogTitled(controller, "Enter code")).IsNull(); // no code box the second time
        AssertThat(DialogTitled(controller, "Admin")).IsNotNull();   // straight to the menu
    }
}
