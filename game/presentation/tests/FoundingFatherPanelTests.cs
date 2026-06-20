using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="FoundingFatherPanel"/>: it lists the offered fathers
/// and choosing one sets the human's recruitment target via <c>Game.ChooseFather</c>. Presentation-only (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FoundingFatherPanelTests
{
    [TestCase]
    public async Task FoundingFatherDialog_ListsOffers_AndChoosingSetsTheTarget()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();
        var game = GetGame(controller);

        // A fresh game offers a father per category; pick the first offer through the dialog.
        AssertThat(game.OfferedFathers.Count > 0).IsTrue();
        string firstOffer = game.OfferedFathers[0];
        string shortName = game.Ruleset.Father(firstOffer).ShortName;

        controller.OpenFoundingFatherPanel();
        await runner.SimulateFrames(1);
        var panel = controller.GetNode<PanelContainer>("UI/FoundingFatherPanel");
        AssertThat(panel.Visible).IsTrue();

        var choose = controller.GetNode<Button>($"UI/FoundingFatherPanel/VBox/Dynamic/Choose_{shortName}");
        choose.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.CurrentFather).IsEqual(firstOffer); // the engine now recruits the chosen father
    }

    private static CrownAndColony.GameLogic.GameSession.Game GetGame(GameController controller) =>
        (CrownAndColony.GameLogic.GameSession.Game)controller
            .GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
