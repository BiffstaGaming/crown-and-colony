using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 input tests (docs/TESTING.md): simulated mouse/keyboard driving the real
/// scene — closes the "click-to-move and hotkeys untested" debt from the
/// units-movement and save-load system docs.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InputTests
{
    private const ulong Seed = 424242;

    [TestCase(Timeout = 60000)]
    public async Task ClickUnit_Selects_ThenClickAdjacentTile_Moves()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        Game game = GameOf(controller);
        Unit unit = game.Units[0];
        Position start = unit.Position;

        // Click the unit's tile: it becomes selected (exactly one marker in the unit layer is selected).
        await ClickTile(runner, controller, start);
        int selected = controller.GetNode<Node2D>("MapView/UnitLayer").GetChildren().OfType<UnitMarker>().Count(m => m.Selected);
        AssertThat(selected).IsEqual(1);

        // Click a legal neighbouring tile: the unit walks there.
        Position target = start.Neighbours().First(n => game.CheckMove(unit, n).Allowed);
        await ClickTile(runner, controller, target);
        AssertThat(unit.Position).IsEqual(target);
    }

    [TestCase(Timeout = 60000)]
    public async Task PressingW_SelectsTheNextUnitNeedingOrders_AndCentresOnIt()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        Unit expected = game.NextUnitToMove(game.HumanPlayer)!;
        AssertThat(expected).IsNotNull(); // a fresh game has a unit still needing orders

        runner.SimulateKeyPressed(Key.W); // cycle to the next unit needing orders
        await runner.SimulateFrames(1);

        // The camera centres on it and the selected-unit panel reflects it.
        AssertThat(controller.GetNode<Camera2D>("Camera").Position).IsEqual(MapView.TileCentre(expected.Position));
        AssertThat(controller.GetNode<PanelContainer>("UI/SelectedUnitPanel").Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/SelectedUnitPanel/VBox/Label").Text).Contains(expected.Type.ShortName);
    }

    [TestCase(Timeout = 60000)]
    public async Task SelectingAUnit_ShowsTheSelectedUnitInfoPanel()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = game.Units[0];

        var panel = controller.GetNode<PanelContainer>("UI/SelectedUnitPanel");
        AssertThat(panel.Visible).IsFalse(); // nothing selected at game start

        await ClickTile(runner, controller, unit.Position); // select the unit

        AssertThat(panel.Visible).IsTrue();
        var label = controller.GetNode<Label>("UI/SelectedUnitPanel/VBox/Label");
        AssertThat(label.Text).Contains(unit.Type.ShortName); // type
        AssertThat(label.Text).Contains("moves");             // and its movement readout
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickingATile_ShowsTheTileInfoReadout_WithTerrainAndOccupant()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        var panel = controller.GetNode<PanelContainer>("UI/TileInfoPanel");
        AssertThat(panel.Visible).IsFalse(); // empty/hidden until a tile is clicked (so the visual goldens are unaffected)

        // Click the human's starting unit's tile: it's explored, so the readout names that tile's terrain and the
        // occupying unit (the same tile the click selects).
        Unit unit = game.PlayerUnits.First(u => u.IsOnMap);
        Position tile = unit.Position;
        await ClickTile(runner, controller, tile);

        AssertThat(panel.Visible).IsTrue();
        var label = controller.GetNode<Label>("UI/TileInfoPanel/VBox/Label");
        AssertThat(label.Text).Contains(game.Map.TerrainAt(tile).ShortName); // terrain
        AssertThat(label.Text).Contains(unit.Type.ShortName);                // occupant
    }

    [TestCase(Timeout = 60000)]
    public async Task SelectedUnitPanel_FortifyButton_FortifiesTheUnit()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = game.Units[0];

        await ClickTile(runner, controller, unit.Position); // select → panel + order buttons show
        var fortify = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/FortifyButton");
        AssertThat(fortify.Disabled).IsFalse(); // an active unit can fortify

        fortify.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(unit.Orders).IsEqual(UnitOrders.Fortifying);                 // the order took
        AssertThat(controller.GetNode<Label>("UI/SelectedUnitPanel/VBox/Label").Text).Contains("fortifying"); // shown
        AssertThat(fortify.Disabled).IsTrue();                                  // can't re-fortify
        AssertThat(controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/ClearButton").Disabled).IsFalse(); // can wake
    }

    [TestCase(Timeout = 60000)]
    public async Task GotoMode_Arms_AndSetsTheSelectedUnitDestination_AndDrawsTheMarker()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        Game game = GameOf(controller);
        Unit unit = game.Units[0];
        Position origin = unit.Position;

        // Select the unit, then arm goto-target mode with the real G key.
        await ClickTile(runner, controller, origin);
        runner.SimulateKeyPressed(Key.G);
        await runner.SimulateFrames(1);
        bool armed = (bool)controller.GetType().GetField("_gotoMode", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        AssertThat(armed).IsTrue(); // G armed goto-target mode for the selected unit

        // The armed click forwards to SetSelectedDestination (its public seam). A reachable explored neighbour:
        Position dest = origin.Neighbours().First(n => game.CheckSetDestination(unit, n).Allowed);
        bool set = controller.SetSelectedDestination(dest);

        AssertThat(set).IsTrue();
        AssertThat(unit.Destination.HasValue).IsTrue();
        AssertThat(unit.Destination!.Value).IsEqual(dest);    // standing goto order recorded (ProcessGotos walks it)
        AssertThat(unit.Position).IsEqual(origin);            // setting a destination doesn't move the unit
        AssertThat(controller.GetNode<GotoMarker>("MapView/GotoMarker").Visible).IsTrue(); // destination marker shown
    }

    [TestCase(Timeout = 60000)]
    public async Task PressingN_StartsAFreshGame()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game before = GameOf(controller);
        before.EndTurn();
        AssertThat(before.Turn).IsEqual(2);

        runner.SimulateKeyPressed(Key.N);
        await runner.SimulateFrames(2);

        Game after = GameOf(controller);
        AssertThat(ReferenceEquals(before, after)).IsFalse();
        AssertThat(after.Turn).IsEqual(1);
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickingAnAdjacentShip_Boards_AndClickingLand_Disembarks()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Place a colonist on a coast and a ship on the water beside it (both unoccupied, deterministic scan).
        Position landPos = game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater
            && !game.Units.Any(u => u.IsOnMap && u.Position == p)
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater
                && !game.Units.Any(u => u.IsOnMap && u.Position == n)));
        Position seaPos = landPos.Neighbours().First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
        Unit colonist = game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), landPos);
        Unit ship = game.SpawnUnit(game.Ruleset.Unit("model.unit.caravel"), seaPos);

        // Select the colonist, click the adjacent ship → it boards.
        await ClickTile(runner, controller, landPos);
        await ClickTile(runner, controller, seaPos);
        AssertThat(colonist.IsAboard).IsTrue();
        AssertThat(colonist.CarrierId!.Value).IsEqual(ship.Id);

        // Select the ship, click the adjacent land → the passenger goes ashore.
        await ClickTile(runner, controller, seaPos);
        await ClickTile(runner, controller, landPos);
        AssertThat(colonist.IsAboard).IsFalse();
        AssertThat(colonist.Position).IsEqual(landPos);
    }

    [TestCase(Timeout = 60000)]
    public async Task PressingB_WithSelectedUnit_FoundsColony()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        await ClickTile(runner, controller, game.Units[0].Position); // select
        runner.SimulateKeyPressed(Key.B);
        await runner.SimulateFrames(2);

        AssertThat(game.Colonies.Count).IsEqual(1);
        AssertThat(game.PlayerUnits.Count()).IsEqual(0); // founder settled (native braves remain)
        // No unit marker remains: the human has no on-map unit, and no rival/brave is in sight near the new colony.
        AssertThat(controller.GetNode<Node2D>("MapView/UnitLayer").GetChildCount()).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task QuickSaveF5_ThenF9_RestoresTheTurn()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        runner.SimulateKeyPressed(Key.F5); // save at turn 1
        await runner.SimulateFrames(2);
        GameOf(controller).EndTurn();
        GameOf(controller).EndTurn();
        AssertThat(GameOf(controller).Turn).IsEqual(3);

        runner.SimulateKeyPressed(Key.F9); // load
        await runner.SimulateFrames(2);
        AssertThat(GameOf(controller).Turn).IsEqual(1);
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickingAnEnemy_WithSelectedUnit_Attacks()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Plant a native brave next to the human's start (on-screen for the click) and a human artillery
        // (offence 7, no role needed) adjacent to it, then click the brave to attack.
        string nation = game.Units.First(u => u.IsNative).OwnerNationId!;
        Position human = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position bravePos = human.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), bravePos, nation);
        Position artPos = bravePos.Neighbours().First(n => n != human && Free(game, n));
        Unit artillery = game.SpawnUnit(game.Ruleset.Unit("model.unit.artillery"), artPos);
        int artId = artillery.Id;

        await ClickTile(runner, controller, artPos);    // select the artillery
        await ClickTile(runner, controller, bravePos);  // click the brave → open the pre-combat odds dialog

        // The odds dialog is up and the attack has NOT been rolled yet: the artillery still has its full
        // movement (CombatOddsAgainst is side-effect-free; nothing is committed until Attack is pressed).
        var panel = controller.GetNode<PanelContainer>("UI/PreCombatPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(panel.GetNode<Label>("VBox/Info").Text).Contains("Chance to win");
        AssertThat(game.Units.First(u => u.Id == artId).MovementLeft).IsGreater(0);

        controller.GetNode<Button>("UI/PreCombatPanel/VBox/Buttons/AttackButton")
            .EmitSignal(BaseButton.SignalName.Pressed); // confirm the attack
        await runner.SimulateFrames(2);

        // The attack resolved: the dialog closed and the attacker spent its turn — it's gone (slain/demoted-away)
        // or present with 0 movement. A rejected move would have left it on its tile with full movement.
        AssertThat(panel.Visible).IsFalse();
        Unit? after = game.Units.FirstOrDefault(u => u.Id == artId);
        AssertThat(after == null || after.MovementLeft == 0).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    public async Task CancellingThePreCombatDialog_LeavesTheAttackerUntouched()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        string nation = game.Units.First(u => u.IsNative).OwnerNationId!;
        Position human = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position bravePos = human.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), bravePos, nation);
        Position artPos = bravePos.Neighbours().First(n => n != human && Free(game, n));
        Unit artillery = game.SpawnUnit(game.Ruleset.Unit("model.unit.artillery"), artPos);
        int artId = artillery.Id;
        int bravesBefore = game.Units.Count(u => u.IsNative);

        await ClickTile(runner, controller, artPos);    // select the artillery
        await ClickTile(runner, controller, bravePos);  // open the dialog

        controller.GetNode<Button>("UI/PreCombatPanel/VBox/Buttons/CancelButton")
            .EmitSignal(BaseButton.SignalName.Pressed); // back out
        await runner.SimulateFrames(2);

        // Nothing happened: dialog closed, attacker keeps its full movement, every brave still alive.
        AssertThat(controller.GetNode<PanelContainer>("UI/PreCombatPanel").Visible).IsFalse();
        Unit after = game.Units.First(u => u.Id == artId);
        AssertThat(after.MovementLeft).IsGreater(0);
        AssertThat(game.Units.Count(u => u.IsNative)).IsEqual(bravesBefore);
    }

    [TestCase(Timeout = 60000)]
    public async Task NativeRaid_DuringEndTurn_ShowsANoticeInTheStatusBar()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Plant a brave adjacent to the human's starting unit and enrage every native nation, so when the turn
        // ends the brave raids the human — exercising the AI-combat → CombatNotice → status-bar feedback path.
        Position humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position bravePos = humanPos.Neighbours().First(n => Free(game, n));
        string nation = game.NativeSettlements.First().NationTypeId;
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), bravePos, nation);
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm);
        }

        // End the turn via the button (the notice path lives in OnEndTurnPressed, not Game.EndTurn).
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        // The raid surfaces as a row in the dismissible TurnMessagePanel (not the status bar any more).
        AssertThat(controller.GetNode<PanelContainer>("UI/TurnMessagePanel").Visible).IsTrue();
        AssertThat(MessageRows(controller).ToLower()).Contains("raid"); // "raided" (native won) or "fought off … raid"
    }

    /// <summary>The concatenated text of every <c>Message_*</c> row in the turn-message panel (helper for the L3 notice tests).</summary>
    private static string MessageRows(GameController controller)
    {
        var dynamic = controller.GetNode<VBoxContainer>("UI/TurnMessagePanel/VBox/Scroll/Dynamic");
        return string.Join("\n", dynamic.GetChildren().OfType<Label>().Select(l => l.Text));
    }

    /// <summary>Every one-line outcome the engine can describe for a resolved (non-mounds) Lost City Rumour (mirrors Game.DescribeMoundsOutcome).</summary>
    private static readonly string[] RumourOutcomeMessages =
    [
        "The expedition vanishes without a trace!",
        "Tribal chiefs share their treasure with you!",
        "Your explorer learns the ways of a seasoned scout!",
        "A band of colonists joins your expedition!",
        "A Fountain of Youth! Settlers flock to your docks.",
        "You uncover ancient ruins — treasure!",
        "You have found one of the Seven Cities of Cibola — a vast treasure!",
        "You find nothing of note.",
    ];

    [TestCase(Timeout = 60000)]
    public async Task ExploringARumour_BySteppingOntoIt_ShowsTheOutcomeInTheStatusBar()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Plant a rumour on a free, non-native tile adjacent to the human's first land unit (AddRumour is internal to
        // GameLogic, so inject it through the save layer like the colony-capture test), then step a unit onto it: any
        // roll on non-native land resolves immediately (MOUNDS/BURIAL degrade to NOTHING — no pause), recording exactly
        // one RumourNotice the controller drains into the status bar. Exercises the explore → RumourNotice → drain path.
        Unit unit0 = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        Position to = unit0.Position.Neighbours()
            .First(n => Free(game, n) && !game.Map.IsNativeOwned(n) && game.CheckMove(unit0, n).Allowed);
        int unitId = unit0.Id;

        SaveGame save = SaveGame.From(game);
        Game injected = (save with { Rumours = (save.Rumours ?? []).Append(to.Y * game.Map.Width + to.X).ToList() })
            .Restore(game.Ruleset);
        SetGame(controller, injected);
        AssertThat(injected.Map.HasRumour(to)).IsTrue(); // the injected rumour round-tripped

        // Resolve the rumour on the engine (the move records exactly one RumourNotice for the human — no pause off
        // native land), then trigger one controller refresh by selecting a unit; the refresh drains the notice into
        // the status bar. Splitting the engine move from the UI refresh keeps the assertion off click-frame timing.
        injected.MoveUnit(injected.Units.First(u => u.Id == unitId), to);
        AssertThat(injected.Map.HasRumour(to)).IsFalse();    // a non-native rumour is consumed on arrival (never a pause)
        AssertThat(injected.PendingMounds).IsNull();         // and never raises a mounds prompt off native land
        RumourNotice recorded = injected.RumourNotices.Count == 1
            ? injected.RumourNotices[0]
            : default; // (asserted below — the engine records exactly one outcome for the human)
        AssertThat(injected.RumourNotices.Count)
            .OverrideFailureMessage($"expected one recorded rumour notice, got {injected.RumourNotices.Count}")
            .IsEqual(1);
        AssertThat(RumourOutcomeMessages).Contains(recorded.Message); // it's a known outcome description

        // One controller refresh drains the recorded notice into the status bar.
        controller.GetType().GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);
        await runner.SimulateFrames(1);
        string status = controller.GetNode<Label>("UI/StatusLabel").Text;
        AssertThat(status)
            .OverrideFailureMessage($"recorded=[{recorded.Message}] remaining={injected.RumourNotices.Count} status=[{status}]")
            .Contains(recorded.Message); // the resolved outcome surfaced in the status bar
    }

    [TestCase(Timeout = 60000)]
    public async Task ForeignPowerCapturesUndefendedColony_DuringEndTurn_ShowsALossNotice()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // The human founds an undefended colony; inject a foreign power's artillery beside it and put the two at
        // war — all via the save layer (the presentation project can't set Unit.OwnerId or stance directly). On
        // End Turn the at-war power captures the colony, exercising the AI-capture → ColonyLossNotice → status-bar
        // path. (Three attackers make the capture robust against the seed's combat rolls — any one win suffices.)
        Unit founder = game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony);
        Colony colony = game.FoundColony(founder);
        int humanId = game.HumanPlayer.PlayerId;

        SaveGame save = SaveGame.From(game);
        SavedPlayer rival = save.Players!.First(p => !p.IsHuman && p.PlayerType == (int)PlayerType.Colonial);
        int nextId = game.Units.Max(u => u.Id) + 1;
        List<SavedUnit> attackers = colony.Position.Neighbours()
            .Where(n => Free(game, n))
            .Take(3)
            .Select((n, i) => new SavedUnit(nextId + i, "model.unit.artillery", n.X, n.Y, 1, OwnerId: rival.PlayerId))
            .ToList();

        // War is symmetric: record it on both the rival and the human.
        List<SavedPlayer> players = save.Players!.Select(p =>
            p.PlayerId == rival.PlayerId ? p with { Stances = WithWar(p.Stances, humanId) }
            : p.PlayerId == humanId ? p with { Stances = WithWar(p.Stances, rival.PlayerId) }
            : p).ToList();
        Game injected = (save with { Units = save.Units.Concat(attackers).ToList(), Players = players }).Restore(game.Ruleset);
        SetGame(controller, injected);

        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        // The capture surfaces as a row in the dismissible TurnMessagePanel.
        AssertThat(controller.GetNode<PanelContainer>("UI/TurnMessagePanel").Visible).IsTrue();
        AssertThat(MessageRows(controller).ToLower()).Contains("captured your colony");
    }

    private static IReadOnlyDictionary<int, Stance> WithWar(IReadOnlyDictionary<int, Stance>? existing, int other)
    {
        var d = existing is null ? new Dictionary<int, Stance>() : new Dictionary<int, Stance>(existing);
        d[other] = Stance.War;
        return d;
    }

    [TestCase(Timeout = 60000)]
    public async Task WhenTheHumanIsWipedOut_EndTurn_ShowsDefeat()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Found a colony with the human's only unit (so 0 human units, 1 colony), then hand that colony to a rival
        // via the save layer — leaving the human with no colonies and no units: IsHumanDefeated. End Turn must
        // surface the defeat as a turn-message row.
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        SaveGame save = SaveGame.From(game);
        SavedPlayer rival = save.Players!.First(p => !p.IsHuman && p.PlayerType == (int)PlayerType.Colonial);
        var colonies = save.Colonies!.Select(c => c.Id == colony.Id ? c with { OwnerId = rival.PlayerId } : c).ToList();
        Game injected = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, injected);
        AssertThat(injected.IsHumanDefeated).IsTrue(); // sanity: wiped out

        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        // The defeat surfaces as a turn-message row (after the loss notice that caused it).
        AssertThat(controller.GetNode<PanelContainer>("UI/TurnMessagePanel").Visible).IsTrue();
        AssertThat(MessageRows(controller).ToLower()).Contains("defeated");
    }

    [TestCase(Timeout = 60000)]
    public async Task TurnMessagePanel_AfterARaid_ListsTheNotice_AndOkDismissesIt()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Same setup as the native-raid notice test: a brave adjacent to the human's unit + every native enraged,
        // so the AI phase raids the human and produces a notice row in the panel.
        Position humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position bravePos = humanPos.Neighbours().First(n => Free(game, n));
        string nation = game.NativeSettlements.First().NationTypeId;
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), bravePos, nation);
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm);
        }

        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        var panel = controller.GetNode<PanelContainer>("UI/TurnMessagePanel");
        AssertThat(panel.Visible).IsTrue();
        // At least one named message row rendered, and a first row exists (Message_0).
        var dynamic = controller.GetNode<VBoxContainer>("UI/TurnMessagePanel/VBox/Scroll/Dynamic");
        AssertThat(dynamic.GetNodeOrNull("Message_0")).IsNotNull();

        // OK dismisses the panel.
        controller.GetNode<Button>("UI/TurnMessagePanel/VBox/OkButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(panel.Visible).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    public async Task WhenTheHumanIsWipedOut_TheGameOverScreenShows_AndEndTurnIsDisabled()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Wipe the human out (same setup as the defeat-notice test above): found a colony with the only unit, then
        // hand that colony to a rival via the save layer → no human colonies, no human units = IsHumanDefeated.
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        SaveGame save = SaveGame.From(game);
        SavedPlayer rival = save.Players!.First(p => !p.IsHuman && p.PlayerType == (int)PlayerType.Colonial);
        var colonies = save.Colonies!.Select(c => c.Id == colony.Id ? c with { OwnerId = rival.PlayerId } : c).ToList();
        Game injected = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, injected);

        // End Turn → RefreshView reflects the defeat: the game-over overlay shows and End Turn is disabled + relabelled.
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        AssertThat(controller.GetNode<Control>("UI/GameOverScreen").Visible).IsTrue();
        Button endTurn = controller.GetNode<Button>("UI/EndTurnButton");
        AssertThat(endTurn.Disabled).IsTrue();
        AssertThat(endTurn.Text.ToLower()).Contains("game over");

        // "New Game" clears the defeat: a fresh, non-defeated game, overlay hidden, End Turn live again.
        controller.GetNode<Button>("UI/GameOverScreen/Panel/VBox/NewGameButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        AssertThat(GameOf(controller).IsHumanDefeated).IsFalse();
        AssertThat(controller.GetNode<Control>("UI/GameOverScreen").Visible).IsFalse();
        AssertThat(controller.GetNode<Button>("UI/EndTurnButton").Disabled).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    public async Task NativeTributeDemand_DuringEndTurn_OpensThePanel_AndPayingTransfers()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Found a colony and stock it with tobacco via the save layer (Colony.AddGoods is internal to GameLogic);
        // then, on the restored game, garrison it (so a brave can't pillage → it demands instead) and plant an
        // enraged brave beside it (SpawnUnit / ChangeNativeAlarm are public).
        Colony founded = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!.Select(c => c.Id == founded.Id
            ? c with { Stores = new Dictionary<string, int> { ["model.goods.tobacco"] = 100 } }
            : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);

        game.SpawnUnit(game.Ruleset.Unit("model.unit.artillery"), colony.Position); // garrison → not pillageable
        string nation = game.NativeSettlements.First().NationTypeId;
        Position adj = colony.Position.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), adj, nation);
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == nation))
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // Hateful → it demands
        }

        // End Turn → the brave raises a tribute demand → the modal opens.
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        var panel = controller.GetNode<PanelContainer>("UI/NativeDemandPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(game.PendingDemand).IsNotNull();

        // Pay tribute → the demanded goods leave the colony and the modal closes.
        int demanded = game.PendingDemand!.Amount;
        string goodsId = game.PendingDemand!.GoodsId!;
        int before = colony.StoreOf(goodsId);
        controller.GetNode<Button>("UI/NativeDemandPanel/VBox/Buttons/PayButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse();
        AssertThat(game.PendingDemand).IsNull();
        AssertThat(colony.StoreOf(goodsId)).IsEqual(before - demanded);
    }

    [TestCase(Timeout = 60000)]
    public async Task MultipleOwnUnits_AllRender()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Spawn a second human-owned unit next to the first, then refresh: the unit layer draws both (the old
        // single-marker HUD only ever showed the first).
        Position humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position spot = humanPos.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), spot);

        await ClickTile(runner, controller, humanPos); // forces a RefreshView (and selects the first unit)

        AssertThat(controller.GetNode<Node2D>("MapView/UnitLayer").GetChildCount()).IsEqual(2);
    }

    [TestCase(Timeout = 60000)]
    public async Task NonHumanUnit_RendersOnlyWhenInSight_WithAnOwnerRing()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // A brave adjacent to the human is in sight → drawn (with a non-transparent owner ring); the braves that
        // spawned far away near their settlements are out of sight → not drawn. So exactly two markers: the
        // human's own (no ring) and the one visible brave (ring).
        Position humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        string nation = game.NativeSettlements.First().NationTypeId;
        Position nearTile = humanPos.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), nearTile, nation);

        await ClickTile(runner, controller, humanPos); // refresh

        var markers = controller.GetNode<Node2D>("MapView/UnitLayer").GetChildren().OfType<UnitMarker>().ToList();
        AssertThat(markers.Count).IsEqual(2); // the human + the in-sight brave; far braves are not drawn
        AssertThat(markers.Any(m => m.Position == MapView.TileCentre(nearTile) && m.OwnerColor.A > 0f)).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    public async Task ForeignUnit_RendersWithItsNationColour()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Inject a foreign colonial unit next to the human via the save layer (the presentation project can't
        // set Unit.OwnerId directly), reload it into the controller, refresh: it must render with its NATION's
        // colour ring — exercising OwnerColorOf → NormalizeHex → Color.FromString, which the brave tests skip.
        Position humanPos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position spot = humanPos.Neighbours().First(n => Free(game, n));
        SaveGame save = SaveGame.From(game);
        var rival = save.Players!.First(p => !p.IsHuman && p.PlayerType == (int)PlayerType.Colonial);
        int uid = game.Units.Max(u => u.Id) + 1;
        var foreignUnit = new SavedUnit(uid, "model.unit.freeColonist", spot.X, spot.Y, 0, OwnerId: rival.PlayerId);
        Game injected = (save with { Units = save.Units.Append(foreignUnit).ToList() }).Restore(game.Ruleset);
        SetGame(controller, injected);

        runner.SimulateKeyPressed(Key.F5); // refresh (no turn advance, no AI movement)
        await runner.SimulateFrames(2);

        UnitMarker marker = controller.GetNode<Node2D>("MapView/UnitLayer").GetChildren().OfType<UnitMarker>()
            .First(m => m.Position == MapView.TileCentre(spot));
        string nationId = injected.Players.First(p => p.PlayerId == rival.PlayerId).NationId!;
        string hex = injected.Ruleset.EuropeanNations.First(n => n.Id == nationId).Color!;
        string bare = hex.Length >= 2 && hex[0] == '0' && (hex[1] == 'x' || hex[1] == 'X') ? hex[2..] : hex.TrimStart('#');
        Color expected = Color.FromString("#" + bare, Colors.Magenta);
        AssertThat(marker.OwnerColor).IsEqual(expected); // the nation colour — not the fallback red, not the native constant
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickingANativeSettlement_OpensInteractionPanel_AndSpeakWithChiefVisits()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // A native settlement with a free adjacent land tile; drop a free colonist beside it (spawning a
        // human unit reveals the settlement tile, so the click below opens the panel rather than trying to move).
        NativeSettlement settlement = game.NativeSettlements.First(s => s.Position.Neighbours().Any(n => Free(game, n)));
        Position adj = settlement.Position.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), adj);

        await ClickTile(runner, controller, adj);                 // select the colonist
        await ClickTile(runner, controller, settlement.Position); // click the settlement → open the panel

        var panel = controller.GetNode<PanelContainer>("UI/NativeSettlementPanel");
        AssertThat(panel.Visible).IsTrue();

        // Speak with the chief: the settlement is marked visited, and the action re-gates away on rebuild.
        var speak = panel.FindChild("Speak", recursive: true, owned: false) as Button;
        AssertThat(speak).IsNotNull();
        speak!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(settlement.HasBeenVisited).IsTrue();
        // The panel rebuilt to reflect the visit (the same rebuild re-gates Speak away).
        AssertThat(panel.GetNode<Label>("VBox/NativeInfo").Text.ToLower()).Contains("spoken");
    }

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, game);

    private static bool Free(Game game, Position n) =>
        game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
        && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
        && !game.Units.Any(u => u.IsOnMap && u.Position == n);

    /// <summary>
    /// Left-clicks the window position corresponding to a map tile (camera-aware, zoom 1). Centres the camera on
    /// the tile first so the click lands at screen-centre — clear of the corner HUD overlays (the minimap, the
    /// turn controls), which otherwise consume a click that happens to project onto them.
    /// </summary>
    private static async Task ClickTile(ISceneRunner runner, GameController controller, Position tile)
    {
        var camera = controller.GetNode<Camera2D>("Camera");
        camera.Position = MapView.TileCentre(tile);
        Vector2 viewportSize = controller.GetViewport().GetVisibleRect().Size;
        Vector2 screen = MapView.TileCentre(tile) - camera.Position + viewportSize / 2f;

        runner.SetMousePos(screen);
        await runner.SimulateFrames(1);
        runner.SimulateMouseButtonPressed(MouseButton.Left);
        await runner.SimulateFrames(2);
    }

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
