using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.Presentation;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 interaction tests (docs/TESTING.md) for the <see cref="DeclarationPanel"/> (`86d3c9xht`): the
/// Declaration-of-Independence signing screen. When the human is eligible the panel shows the consequences and, on
/// confirm, forwards <see cref="Game.DeclareIndependence(Player, string?)"/> and shows the signed declaration; when ineligible it shows
/// the <see cref="Game.CheckDeclareIndependence"/> gate's reason. Presentation-only (ADR-006) — the panel reads oracles
/// and forwards the one command. The eligible state is built through the public save layer (founding a coastal colony,
/// then rewriting its banked liberty so national Sons-of-Liberty hits 100%), so the test needs no internal access.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DeclarationPanelTests
{
    [TestCase]
    public async Task Eligible_ShowsConsequences_Confirm_DeclaresAndSigns_ThenCloses()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Make the human rebellion-ready through the public save layer: found a coastal colony, then force its banked
        // liberty so national SoL reaches 100% (mirrors the GameLogic RebellionReady helper, via Save/Restore).
        SetGame(controller, RebellionReady(GetGame(controller)));
        Game game = GetGame(controller);
        AssertThat(game.CheckDeclareIndependence(game.HumanPlayer).Allowed).IsTrue();

        controller.OpenDeclarationPanel();
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/DeclarationPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/DeclarationPanel/VBox/DeclarationTitle").Text).Contains("Independence");

        var dynamic = controller.GetNode<VBoxContainer>("UI/DeclarationPanel/VBox/Dynamic");
        // The confirm phase lists the consequences (Europe closes, the REF comes, the muster) + the confirm button.
        AssertThat(dynamic.GetNodeOrNull("ConsequenceEurope")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("ConsequenceRef")).IsNotNull();
        AssertThat(dynamic.GetNodeOrNull("ConsequenceMuster")).IsNotNull();
        var confirm = dynamic.GetNodeOrNull<Button>("ConfirmButton");
        AssertThat(confirm).IsNotNull();

        // Confirm → the panel forwards DeclareIndependence (the human becomes a Rebel) and shows the signed text.
        confirm!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.HumanPlayer.PlayerType).IsEqual(PlayerType.Rebel); // the command was forwarded
        AssertThat(dynamic.GetNodeOrNull("Resolution")).IsNotNull();       // the signed declaration's flavour text
        var signedClose = dynamic.GetNodeOrNull<Button>("SignedCloseButton");
        AssertThat(signedClose).IsNotNull();

        signedClose!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase]
    public async Task Ineligible_ShowsGateReason_AndClosesWithoutDeclaring()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // A fresh turn-1 game: no colony, 0% rebel sentiment → the gate is closed. The HUD button stays hidden, and the
        // panel (driven directly) explains why instead of declaring.
        Game game = GetGame(controller);
        AssertThat(game.CheckDeclareIndependence(game.HumanPlayer).Allowed).IsFalse();
        AssertThat(controller.GetNode<Button>("UI/IndependenceButton").Visible).IsFalse();

        controller.OpenDeclarationPanel();
        await runner.SimulateFrames(1);

        var panel = controller.GetNode<PanelContainer>("UI/DeclarationPanel");
        AssertThat(panel.Visible).IsTrue();
        var dynamic = controller.GetNode<VBoxContainer>("UI/DeclarationPanel/VBox/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("GateReason")).IsNotNull();   // the oracle's reason, not the consequences
        AssertThat(dynamic.GetNodeOrNull("ConfirmButton")).IsNull();   // no way to declare while ineligible

        controller.GetNode<Button>("UI/DeclarationPanel/VBox/Dynamic/DismissButton")
            .EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
        AssertThat(game.HumanPlayer.PlayerType).IsEqual(PlayerType.Colonial); // nothing was declared
    }

    /// <summary>
    /// Returns the game made rebellion-ready: a coastal colony founded for the human, with its banked liberty forced
    /// (through the public save layer) so national Sons-of-Liberty is 100% and <c>CheckDeclareIndependence</c> allows.
    /// </summary>
    private static Game RebellionReady(Game game)
    {
        // Found the human's starting unit as a colony on a coastal, *settle-able* land tile beside water that isn't
        // adjacent to another colony (the scene's map terrain differs from the L1 helper's, so pre-filter on the same
        // terms CheckFoundColony gates on — settle-able terrain + colony spacing — to skip mountains/forbidden tiles).
        // Also require an UNCLAIMED tile (RequiredLandClaim.Required == false): founding on native-owned land throws
        // LandClaimRequiredException (the C3 forced-claim gate, Game.FoundColony) unless an explicit claim choice is
        // passed, so excluding native-owned sites keeps the no-argument FoundColony below safe regardless of which tile
        // the scene's generated map offers first.
        Position coastal = game.Map.AllPositions().First(p =>
            game.Map.TerrainAt(p).CanSettle
            && !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null
            && game.NativeSettlementAt(p) is null
            && !game.RequiredLandClaim(p).Required
            && p.Neighbours().All(n => !game.Map.InBounds(n) || game.ColonyAt(n) is null)
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater));
        Unit colonist = game.SpawnUnit(game.Ruleset.Unit(Game.StartingUnitTypeId), coastal);
        Colony colony = game.FoundColony(colonist);

        // Rewrite the colony's banked liberty in the save so national SoL = 100% (LibertyPerRebel · population). The
        // Liberty setter is internal, so we go through the public Save/Restore round-trip — the same seam the
        // VictoryPanel test uses to manufacture a winning state.
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!
            .Select(c => c.Id == colony.Id ? c with { Liberty = Colony.LibertyPerRebel * c.Population } : c)
            .ToList();
        return (save with { Colonies = colonies }).Restore(game.Ruleset);
    }

    private static Game GetGame(GameController controller) =>
        (Game)controller
            .GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, game);
}
