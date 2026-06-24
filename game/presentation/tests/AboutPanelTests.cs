using System.Threading.Tasks;
using CrownAndColony.GameLogic.App;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md): drive the real About screen inside the Godot runtime — it shows the game
/// name and the compiled-in version (from <see cref="AppInfo"/>) plus the GPL v2 / repo / manual text, and Back emits
/// <see cref="AboutPanel.Closed"/> so the host can dismiss it.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AboutPanelTests
{
    private const string AboutScene = "res://scenes/AboutPanel.tscn";

    [TestCase]
    public async Task AboutPanel_ShowsNameVersionAndLicenseText()
    {
        ISceneRunner runner = ISceneRunner.Load(AboutScene);
        await runner.SimulateFrames(2);
        var scene = runner.Scene();

        AssertThat(scene.GetNode<Label>("Panel/VBox/Title").Text).IsEqual(AppInfo.Name);
        AssertThat(scene.GetNode<Label>("Panel/VBox/Version").Text).Contains(AppInfo.Version);

        string body = scene.GetNode<RichTextLabel>("Panel/VBox/Body").Text;
        AssertThat(body).Contains("GPL v2");
        AssertThat(body).Contains("LICENSE");
        AssertThat(body).Contains("CREDITS.md");
        AssertThat(body).Contains(AppInfo.RepositoryUrl);
        AssertThat(body).Contains("docs/MANUAL.md");
        AssertThat(scene.GetNode<Button>("Panel/VBox/BackButton").Text).IsEqual("Back");
    }

    [TestCase]
    public async Task BackButton_EmitsClosed()
    {
        ISceneRunner runner = ISceneRunner.Load(AboutScene);
        await runner.SimulateFrames(2);
        var about = (AboutPanel)runner.Scene();

        bool closed = false;
        about.Closed += () => closed = true;

        about.GetNode<Button>("Panel/VBox/BackButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(closed).IsTrue();
    }
}
