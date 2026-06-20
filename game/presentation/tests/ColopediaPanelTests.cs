using System.Threading.Tasks;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="ColopediaPanel"/> (Goods category): the Colopedia
/// button opens it, it lists a row per ruleset goods type with the good's facts (a tradeable good shows its market
/// price), and Close hides it. Presentation-only (ADR-006) over <c>Game.Ruleset</c>/<c>Game.Market</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ColopediaPanelTests
{
    [TestCase]
    public async Task ColopediaButton_OpensTheGoodsReference_ListingEachGood_AndCloses()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        controller.GetNode<Button>("UI/ColopediaButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/ColopediaPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/ColopediaPanel/VBox/ColopediaTitle").Text).Contains("Goods");

        // Tobacco is always in the classic ruleset and is market-tradeable → its row renders, named by the good,
        // showing the readable name plus its sell/buy market price.
        var dynamic = controller.GetNode<VBoxContainer>("UI/ColopediaPanel/VBox/Scroll/Dynamic");
        var tobacco = dynamic.GetNodeOrNull<Label>("Goods_tobacco");
        AssertThat(tobacco).IsNotNull();
        AssertThat(tobacco!.Text).Contains("Tobacco");
        AssertThat(tobacco.Text).Contains("sell");
        AssertThat(tobacco.Text).Contains("buy");

        controller.GetNode<Button>("UI/ColopediaPanel/VBox/CloseButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }
}
