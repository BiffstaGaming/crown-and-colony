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
    public async Task UnitMarker_SitsOnUnitTile()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);

        var controller = (GameController)runner.Scene();
        var marker = controller.GetNode<UnitMarker>("MapView/UnitMarker");

        // The marker must sit at the centre of some on-map tile (the unit's).
        Vector2 pos = marker.Position;
        AssertThat(pos.X % MapView.TileSize).IsEqual(MapView.TileSize / 2f);
        AssertThat(pos.Y % MapView.TileSize).IsEqual(MapView.TileSize / 2f);
    }
}
