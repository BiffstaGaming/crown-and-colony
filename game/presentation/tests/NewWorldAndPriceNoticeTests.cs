using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for Parity Wave 8 Stream A: (1) the <b>name-the-new-world</b> dialog appears on
/// first landfall and stores the typed name (`86d3fq1fn`); (2) a <b>price-change</b> notice renders in the turn-message
/// log after End Turn (`86d3fpz0p`). Presentation-only over the GameLogic oracles (ADR-006).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NewWorldAndPriceNoticeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Colonist = "model.unit.freeColonist";
    private const string Sugar = "model.goods.sugar";

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, game);

    private static void InvokePrivate(GameController controller, string method) =>
        controller.GetType()
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);

    /// <summary>A 4×1 coast row (ocean then plains) with a human colonist on the ocean tile, all explored.</summary>
    private static Game CoastGame() => new SaveGame
    {
        Turn = 1, RandomStateValue = 1, RandomIncrement = 1,
        MapWidth = 4, MapHeight = 1,
        Terrain = ["model.tile.ocean", "model.tile.plains", "model.tile.plains", "model.tile.plains"],
        Units = [new SavedUnit(1, Colonist, 0, 0, 12)],
        Explored = [0, 1, 2, 3],
    }.Restore(Classic);

    // ── Name-the-new-world dialog ──────────────────────────────────────────────────────────────────────────────

    [TestCase]
    public async Task FirstLandfall_OpensTheNamingDialog_AndStoresTheTypedName()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Inject a coast game and step the colonist ashore so the engine flags the naming prompt as pending.
        Game game = CoastGame();
        SetGame(controller, game);
        game.MoveUnit(game.Units.First(u => u.Id == 1), new Position(1, 0));
        AssertThat(game.NewWorldNamePending).IsTrue(); // sanity: the engine owes the prompt

        // Drive the controller's prompt hook (the same call the move handlers make).
        InvokePrivate(controller, "MaybePromptNewWorldName");
        await runner.SimulateFrames(2);

        // The code-built dialog is on screen, pre-filled with the engine's default name.
        var dialog = controller.GetChildren().OfType<AcceptDialog>().FirstOrDefault(d => d.Title.ToString() == "The New World");
        AssertThat(dialog).IsNotNull();
        AssertThat(dialog!.Visible).IsTrue();
        var field = dialog.FindChild("NewWorldNameField", recursive: true, owned: false) as LineEdit;
        AssertThat(field).IsNotNull();
        AssertThat(field!.Text).IsEqual(Game.DefaultNewWorldName);

        // Type a name and confirm → the engine records it and the prompt closes.
        field.Text = "Columbia";
        dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);

        AssertThat(game.NewWorldName).IsEqual("Columbia");
        AssertThat(game.NewWorldNamePending).IsFalse();
    }

    [TestCase]
    public async Task NamingDialog_DoesNotOpen_WhenNoLandfallIsPending()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // A fresh game (no landfall yet) → the hook is a no-op, no dialog.
        SetGame(controller, CoastGame());
        InvokePrivate(controller, "MaybePromptNewWorldName");
        await runner.SimulateFrames(1);

        AssertThat(controller.GetChildren().OfType<AcceptDialog>().Any(d => d.Title.ToString() == "The New World")).IsFalse();
    }

    // ── Price-change notice in the turn-message log ────────────────────────────────────────────────────────────

    [TestCase(Timeout = 60000)]
    public async Task PriceChange_RendersInTheTurnMessageLog_AfterEndTurn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Flood the human's Europe market with sugar so its price falls this turn (a real trade move; the engine
        // re-derives the price-change notice at End Turn). Market.Sell is the same call SellColonyGoods makes.
        game.Market.Sell(Sugar, 3000, game.TaxRate);

        // End the turn via the button (the notice path lives in OnEndTurnPressed, not Game.EndTurn).
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        // The price move surfaces as a row in the dismissible TurnMessagePanel.
        AssertThat(controller.GetNode<PanelContainer>("UI/TurnMessagePanel").Visible).IsTrue();
        var dynamic = controller.GetNode<VBoxContainer>("UI/TurnMessagePanel/VBox/Scroll/Dynamic");
        string rows = string.Join("\n", dynamic.GetChildren().OfType<Label>().Select(l => l.Text)).ToLower();
        AssertThat(rows).Contains("price of");
        AssertThat(rows).Contains("sugar");
    }
}
