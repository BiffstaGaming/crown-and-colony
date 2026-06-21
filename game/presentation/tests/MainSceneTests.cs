using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md): drive the real main scene inside the
/// Godot runtime and assert the UI wiring reaches the game logic.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MainSceneTests
{
    [TestCase]
    public async Task MainScene_Loads_AndShowsTurn1()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var label = runner.Scene().GetNode<Label>("UI/StatusLabel");
        AssertThat(label.Text).Contains("Turn 1");
    }

    [TestCase]
    public async Task EndTurnButton_AdvancesTurn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var button = runner.Scene().GetNode<Button>("UI/EndTurnButton");
        button.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var label = runner.Scene().GetNode<Label>("UI/StatusLabel");
        AssertThat(label.Text).Contains("Turn 2");
    }

    [TestCase]
    public async Task CalendarHud_ShowsTheDate_AndAdvancesWithTheTurn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var calendar = runner.Scene().GetNode<Label>("UI/CalendarPanel/CalendarLabel");
        // Turn 1 is 1492 in the classic calendar (a bare year — seasons begin at 1600), shown in the
        // dedicated HUD readout, not just the dev status string.
        AssertThat(calendar.Text).IsEqual("1492");

        runner.Scene().GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(calendar.Text).IsEqual("1493"); // the readout follows Game.CalendarLabel as turns pass
    }

    [TestCase]
    public async Task ColonyPanel_OpensWithColonyDetails_AndCloses()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Found a colony through the game API, then open its panel.
        var game = (CrownAndColony.GameLogic.GameSession.Game)controller
            .GetType()
            .GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(controller)!;
        var colony = game.FoundColony(game.Units[0]);
        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/ColonyPanel/VBox/ColonyTitle").Text)
            .IsEqual(colony.Name);
        AssertThat(controller.GetNode<Label>("UI/ColonyPanel/VBox/ColonyInfo").Text)
            .Contains("Population: 1");

        controller.GetNode<Button>("UI/ColonyPanel/VBox/CloseButton")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase]
    public async Task UnitMarker_SitsOnUnitTile()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var controller = (GameController)runner.Scene();
        var layer = controller.GetNode<Node2D>("MapView/UnitLayer");
        int count = layer.GetChildCount();
        AssertThat(count).IsEqual(3); // turn 1: the human's starting roster (pioneer + soldier + caravel), no rivals/braves in sight

        // Every marker must sit at the centre of some diamond: isometric centres land on multiples of half the tile width/height.
        for (int i = 0; i < count; i++)
        {
            Vector2 pos = ((UnitMarker)layer.GetChild(i)).Position;
            AssertThat(pos.X % (MapView.TileW / 2f)).IsEqual(0f);
            AssertThat(pos.Y % (MapView.TileH / 2f)).IsEqual(0f);
        }
    }
}
