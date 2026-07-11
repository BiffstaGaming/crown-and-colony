using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="PioneerAttainedPanel"/> (WS2.7): the Historical-Figure-attained
/// celebration modal. Two paths: the panel renders a figure's name/category/perk on demand, and — driven through a live
/// controller — a new Congress member fires the modal automatically (Australia-only). Presentation-only (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PioneerAttainedPanelTests
{
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();
    private const string Macarthur = "model.foundingFather.elizabethMacarthur";

    [TestCase]
    public async Task Open_ShowsTheFigureNameJoiningBody_CategoryAndPerk()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/MainMenu.tscn");
        await runner.SimulateFrames(2);
        var host = (Control)runner.Scene();
        var panel = new PioneerAttainedPanel();
        host.AddChild(panel); // runs _Ready, building the shell
        await runner.SimulateFrames(1);

        Game game = Game.New(Australia, 0xA05UL, mapSource: MapSource.Australia);
        panel.Open(game, Macarthur, "Federation Convention", GameVariants.Australia.DisplayOverrides);
        await runner.SimulateFrames(2); // let the deferred Recenter + layout settle before the size assertion

        AssertThat(panel.Visible).IsTrue();
        string title = panel.GetNode<Label>("VBox/PioneerTitle").Text;
        AssertThat(title).Contains("Elizabeth Macarthur");     // the figure's name
        AssertThat(title).Contains("Federation Convention");   // the variant's electing body (not "Continental Congress")
        AssertThat(panel.GetNode<Label>("VBox/PioneerCategory").Text).Contains("Industry"); // Trade → "Industry & Commerce"
        AssertThat(panel.GetNode<Label>("VBox/PerkChip/PioneerPerk").Text).IsNotEmpty();     // a plain-English perk summary

        // Regression guard for the sizing bug: an autowrap label with no pinned width once reported its min-height as if it
        // were one character wide, ballooning the modal to the full viewport with a slab of dead parchment below the button.
        // The card must hug its content — a few hundred px tall, never the whole screen.
        AssertThat(panel.Size.Y).IsLess(460f);

        panel.QueueFree();
    }

    [TestCase]
    public async Task ElectingAFigure_UnderAustralia_FiresTheCelebration_ThroughTheController()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Start a real Australia game (VictoryFederation on) through the production NewGame() path.
        GameController.PendingVariant = GameVariants.Australia;
        GameController.PendingMapSource = MapSource.Australia;
        typeof(GameController).GetMethod("NewGame", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
        await runner.SimulateFrames(2);

        // The celebration is hidden until a figure joins.
        var panel = controller.GetNode<PioneerAttainedPanel>("UI/PioneerAttainedPanel");
        AssertThat(panel.Visible).IsFalse();

        // Elect a Pioneer (add to the human's Congress via the internal list) and drive a refresh — the controller detects
        // the new Congress member and opens the celebration.
        Game game = GameOf(controller);
        var congress = (List<string>)typeof(Player)
            .GetProperty("CongressList", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(game.HumanPlayer)!;
        congress.Add(Macarthur);
        typeof(GameController).GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(controller, null);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsTrue();
        AssertThat(panel.GetNode<Label>("VBox/PioneerTitle").Text).Contains("Federation Convention");
    }

    private static Game GameOf(GameController controller) =>
        (Game)typeof(GameController).GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
}
