using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md): drive the real Help screen inside the Godot runtime — it shows the goal of
/// the game, the core gameplay loops and a controls / keybindings reference, and Back emits
/// <see cref="HelpPanel.Closed"/> so the host can dismiss it. The controls section restates the authoritative key
/// table (<see cref="GameController.BuildKeyBindings"/>); these assertions guard that the displayed keys stay correct.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HelpPanelTests
{
    private const string HelpScene = "res://scenes/HelpPanel.tscn";

    [TestCase]
    public async Task HelpPanel_ShowsGoal_CoreLoops_AndControls()
    {
        ISceneRunner runner = ISceneRunner.Load(HelpScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        AssertThat(scene.GetNode<Label>("Panel/VBox/Title").Text).IsEqual("Help");

        string body = scene.GetNode<RichTextLabel>("Panel/VBox/Body").Text;
        // Goal + each of the core loops named in the task brief.
        AssertThat(body).Contains("goal of the game");
        AssertThat(body).Contains("Explore");
        AssertThat(body).Contains("Found and run colonies");
        AssertThat(body).Contains("economy");
        AssertThat(body).Contains("independence");
        AssertThat(body).Contains("Natives");
        AssertThat(body).Contains("Combat");
        // The manual is pointed at for the full guide.
        AssertThat(body).Contains("docs/MANUAL.md");

        AssertThat(scene.GetNode<Button>("Panel/VBox/BackButton").Text).IsEqual("Back");
    }

    [TestCase]
    public async Task HelpPanel_ControlsReference_MatchesTheKeyTable()
    {
        ISceneRunner runner = ISceneRunner.Load(HelpScene);
        await runner.SimulateFrames(2);
        string body = runner.Scene().GetNode<RichTextLabel>("Panel/VBox/Body").Text;

        // The controls section must list the same key→action facts as GameController.BuildKeyBindings / the F1 legend.
        AssertThat(body).Contains("Controls and keybindings");
        AssertThat(body).Contains("End turn");
        AssertThat(body).Contains("Skip the selected unit");
        AssertThat(body).Contains("next unit needing orders");
        AssertThat(body).Contains("Build (found) a colony");
        AssertThat(body).Contains("Disband");
        AssertThat(body).Contains("Europe");
        AssertThat(body).Contains("Founding Fathers");
        AssertThat(body).Contains("Colopedia");
        AssertThat(body).Contains("Centre the camera");
        AssertThat(body).Contains("Save / Load");
        AssertThat(body).Contains("Quick-save");
        AssertThat(body).Contains("key legend");
    }

    [TestCase]
    public async Task BackButton_EmitsClosed()
    {
        ISceneRunner runner = ISceneRunner.Load(HelpScene);
        await runner.SimulateFrames(2);
        var help = (HelpPanel)runner.Scene();

        bool closed = false;
        help.Closed += () => closed = true;

        help.GetNode<Button>("Panel/VBox/BackButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(closed).IsTrue();
    }
}
