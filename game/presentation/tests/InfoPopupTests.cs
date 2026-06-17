using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md): the reusable info popup shows its configured text over a host screen, and
/// OK both emits <c>Closed</c> and (via the <c>Show</c> helper) frees the popup.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InfoPopupTests
{
    [TestCase]
    public async Task Show_DisplaysText_AndOkClosesAndFrees()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn"); // any Control host will do
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();

        var popup = InfoPopup.Show(host, "Saved", "Your game has been saved.");
        await runner.SimulateFrames(1);
        AssertThat(popup.GetNode<Label>("Panel/VBox/Title").Text).IsEqual("Saved");
        AssertThat(popup.GetNode<Label>("Panel/VBox/Message").Text).IsEqual("Your game has been saved.");

        bool closed = false;
        popup.Closed += () => closed = true;
        popup.GetNode<Button>("Panel/VBox/OkButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        AssertThat(closed).IsTrue();
        AssertThat(GodotObject.IsInstanceValid(popup)).IsFalse(); // Show() auto-frees on Closed
    }
}
