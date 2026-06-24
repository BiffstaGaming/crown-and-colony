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

    /// <summary>
    /// Resets the process-global Godot <see cref="Input"/> singleton before and after every case so the L3 suite is
    /// order-independent. Each case loads a fresh scene, but <see cref="Input"/> is a process singleton the scene
    /// runner mutates: <c>SetMousePos</c>/<c>SimulateMouseMove</c> leave the cursor parked at the previous case's last
    /// tile, and parsed button/key events can still sit in the buffer. A stale cursor makes the next case's relative
    /// mouse-move start from the wrong origin (so the click can land on the corner HUD instead of the target tile),
    /// which is the input-bleed flake (the failing set shifted run-to-run). We release the left mouse button and the
    /// keys these cases press, warp the cursor to the origin, then flush the buffer — so every case starts clean.
    /// </summary>
    [BeforeTest]
    public void ResetGlobalInputStateBeforeEachTest() => ResetGlobalInputState();

    [AfterTest]
    public void ResetGlobalInputStateAfterEachTest() => ResetGlobalInputState();

    private static void ResetGlobalInputState()
    {
        // Release the left mouse button in case a press is still flagged held on the global Input.
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        // Release every key these cases drive (W/G/N/B/D/Space/Enter/F1/F5/F9 + arrows + Ctrl), so no key stays flagged
        // pressed across cases — including the continuous-poll arrow pan keys CameraController reads in _Process.
        foreach (Key key in new[]
        {
            Key.W, Key.G, Key.N, Key.B, Key.D, Key.Space, Key.Enter, Key.F1, Key.F5, Key.F9,
            Key.Left, Key.Right, Key.Up, Key.Down, Key.Ctrl,
        })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, Pressed = false });
        }
        // Park the cursor at a neutral origin so the next case's first mouse-move starts from a known position.
        Input.WarpMouse(Vector2.Zero);
        // Drain anything queued above (and any leftover from the previous case) before the next case runs.
        Input.FlushBufferedEvents();
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickUnit_Selects_ThenClickAdjacentTile_Moves()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        Game game = GameOf(controller);
        Unit unit = TrimHumanRosterToOne(game); // a single human unit → unambiguous click/select/move
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
    public async Task SelectedUnitPanel_RoadButton_StartsBuildingARoad()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = game.Units[0];

        // The default human start is a plain free colonist; arm it as a pioneer (internal setters reached by
        // reflection, like the river overlay test) so it can build — a road applies on any land tile it stands on.
        typeof(Unit).GetProperty("RoleId")!.GetSetMethod(nonPublic: true)!.Invoke(unit, ["model.role.pioneer"]);
        typeof(Unit).GetProperty("RoleCount")!.GetSetMethod(nonPublic: true)!.Invoke(unit, [1]);

        await ClickTile(runner, controller, unit.Position); // select → the build-order buttons gate on the oracle
        var road = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/RoadButton");
        AssertThat(road.Disabled).IsFalse(); // a tooled pioneer on land can build a road

        road.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(unit.IsImproving).IsTrue();                            // the build order took
        AssertThat(unit.WorkImprovementId).IsEqual("model.improvement.road");
        AssertThat(unit.MovementLeft).IsEqual(0);                         // committed for the turn
        AssertThat(controller.GetNode<Label>("UI/SelectedUnitPanel/VBox/Label").Text).Contains("building road"); // shown
    }

    [TestCase(Timeout = 60000)]
    public async Task SelectedUnitPanel_SailToEuropeButton_ShownForShipsOnly_EnabledOnHighSeas_AndSails()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        var sail = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/SailToEuropeButton");

        // A land colonist: the ship-only order is hidden entirely.
        Unit land = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        await ClickTile(runner, controller, land.Position);
        AssertThat(sail.Visible).IsFalse();

        // The starting caravel sits on coastal water (not the high seas): the order shows but is disabled.
        Unit caravel = game.PlayerUnits.First(u => u.IsOnMap && u.Type.IsNaval);
        AssertThat(game.Map.TerrainAt(caravel.Position).Id).IsNotEqual("model.tile.highSeas"); // precondition for the case
        await ClickTile(runner, controller, caravel.Position);
        AssertThat(sail.Visible).IsTrue();
        AssertThat(sail.Disabled).IsTrue();

        // Put a ship on a high-seas tile (the map edge): the order enables, and pressing it sends the ship sailing.
        Position highSeas = game.Map.AllPositions().First(p =>
            game.Map.TerrainAt(p).Id == "model.tile.highSeas"
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        Unit ship = game.SpawnUnit(game.Ruleset.Unit("model.unit.caravel"), highSeas);
        await ClickTile(runner, controller, highSeas);
        AssertThat(sail.Visible).IsTrue();
        AssertThat(sail.Disabled).IsFalse();

        sail.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(ship.Location).IsEqual(UnitLocation.SailingToEurope); // the existing SailToEurope command fired
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
        Unit founder = TrimHumanRosterToOne(game); // a single founder so founding it leaves the human with no units

        await ClickTile(runner, controller, founder.Position); // select
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
    public async Task SelectingALoadedShip_AndClickingAnAdjacentEnemy_AmphibiouslyAssaultsIt()
    {
        // 86d3e4bmp: with the amphibiousMoves option on, selecting a carrier and clicking an adjacent enemy-held land
        // tile fires an amphibious assault straight off the ship (the armed passenger never disembarks). Staged through
        // the save layer (the presentation project can't set CarrierId / OwnerId / stance / the option directly): a
        // human caravel on water beside a land tile, a veteran soldier aboard it (full movement), and a rival colonist
        // on the land tile, the two at war. Restoring with WithAmphibiousMoves(true) turns the action on.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        // A water tile with two free land neighbours: one to assault, one only to keep the scan deterministic.
        Position water = game.Map.AllPositions().First(w => game.Map.TerrainAt(w).IsWater
            && !game.Units.Any(u => u.IsOnMap && u.Position == w)
            && w.Neighbours().Count(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
                && game.ColonyAt(n) is null && game.NativeSettlementAt(n) is null
                && !game.Units.Any(u => u.IsOnMap && u.Position == n)) >= 1);
        Position land = water.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.ColonyAt(n) is null && game.NativeSettlementAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));

        SaveGame save = SaveGame.From(game);
        SavedPlayer rival = save.Players!.First(p => !p.IsHuman && p.PlayerType == (int)PlayerType.Colonial);
        int shipId = game.Units.Max(u => u.Id) + 1;
        int marineId = shipId + 1;
        int defenderId = shipId + 2;
        int caravelMove = game.Ruleset.Unit("model.unit.caravel").Movement;
        int marineMove = game.Ruleset.Unit("model.unit.veteranSoldier").Movement;
        var staged = new List<SavedUnit>
        {
            new(shipId, "model.unit.caravel", water.X, water.Y, caravelMove, OwnerId: humanId),
            // The marine rides the ship (CarrierId = shipId): aboard, with full movement so it can assault.
            new(marineId, "model.unit.veteranSoldier", water.X, water.Y, marineMove,
                CarrierId: shipId, OwnerId: humanId, Role: "model.role.soldier", RoleCount: 1),
            new(defenderId, "model.unit.freeColonist", land.X, land.Y, 1, OwnerId: rival.PlayerId),
        };
        List<SavedPlayer> players = save.Players!.Select(p =>
            p.PlayerId == rival.PlayerId ? p with { Stances = WithWar(p.Stances, humanId) }
            : p.PlayerId == humanId ? p with { Stances = WithWar(p.Stances, rival.PlayerId) }
            : p).ToList();
        Game injected = (save with { Units = save.Units.Concat(staged).ToList(), Players = players })
            .Restore(game.Ruleset.WithAmphibiousMoves(true));
        SetGame(controller, injected);

        // Sanity: the marine is aboard the ship with movement, and the engine offers the amphibious assault.
        Unit marine = injected.Units.First(u => u.Id == marineId);
        AssertThat(marine.IsAboard).IsTrue();
        AssertThat(marine.MovementLeft).IsGreater(0);
        AssertThat(injected.CheckAttackAmphibious(marine, land).Allowed).IsTrue();

        // Select the ship, then click the adjacent enemy land tile → the marine assaults straight off the ship.
        await ClickTile(runner, controller, water);
        await ClickTile(runner, controller, land);

        // The assault resolved: the marine spent its turn (0 movement) and is STILL ABOARD (it never disembarked),
        // unless it was destroyed on the loss. A rejected action would have left it with full movement.
        Unit? after = injected.Units.FirstOrDefault(u => u.Id == marineId);
        AssertThat(after == null || (after.MovementLeft == 0 && after.IsAboard)).IsTrue();
        // And the marine is NOT standing on the land tile (an amphibious assault does not put it ashore).
        AssertThat(injected.Units.Any(u => u.Id == marineId && u.IsOnMap && u.Position == land)).IsFalse();
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
        DisbandAllHuman(game); // remove the rest of the starting roster so the human truly has no units
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
        DisbandAllHuman(game); // remove the rest of the starting roster so the human truly has no units
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

        // Pay tribute → the demanded payment leaves the human and the modal closes. A demand is for either a specific
        // goods (GoodsId set → comes out of the colony store) or gold (GoodsId null → comes out of the player's purse);
        // assert against whichever the seed produced, mirroring the engine's clamp-to-available (AcceptPendingDemand).
        int demanded = game.PendingDemand!.Amount;
        string? goodsId = game.PendingDemand!.GoodsId;
        int goodsBefore = goodsId is null ? 0 : colony.StoreOf(goodsId);
        int goldBefore = game.HumanPlayer.Gold;
        controller.GetNode<Button>("UI/NativeDemandPanel/VBox/Buttons/PayButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse();
        AssertThat(game.PendingDemand).IsNull();
        if (goodsId is null)
        {
            AssertThat(game.HumanPlayer.Gold).IsEqual(goldBefore - System.Math.Min(demanded, goldBefore)); // gold demand
        }
        else
        {
            AssertThat(colony.StoreOf(goodsId)).IsEqual(goodsBefore - System.Math.Min(demanded, goodsBefore)); // goods demand
        }
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
        TrimHumanRosterToOne(game); // start from a single human unit so "both render" means exactly two
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
        TrimHumanRosterToOne(game); // a single human unit so the count is exactly the human + the in-sight brave
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

    [TestCase(Timeout = 60000)]
    public async Task SelectedUnitPanel_DisbandButton_PromptsAndRemovesTheUnitOnConfirm()
    {
        // 86d3f0vgd: the Orders "Disband" button is gated on CheckDisband and removes the unit (via a ConfirmationDialog).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = TrimHumanRosterToOne(game); // a single unit so disbanding it leaves the human with none
        int unitId = unit.Id;

        await ClickTile(runner, controller, unit.Position); // select → panel + Disband button show
        var disband = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/DisbandButton");
        AssertThat(disband.Disabled).IsFalse(); // a plain land unit can be disbanded

        disband.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // A confirmation dialog is up; confirming it removes the unit.
        var dialog = controller.GetChildren().OfType<ConfirmationDialog>().First();
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);

        AssertThat(game.Units.Any(u => u.Id == unitId)).IsFalse();                       // gone for good
        AssertThat(controller.GetNode<PanelContainer>("UI/SelectedUnitPanel").Visible).IsFalse(); // selection cleared
    }

    [TestCase(Timeout = 60000)]
    public async Task PressingD_PromptsDisband_AndCancellingKeepsTheUnit()
    {
        // 86d3f0vgd: the D key opens the same confirmation; Cancel leaves the unit untouched.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = TrimHumanRosterToOne(game);
        int unitId = unit.Id;

        await ClickTile(runner, controller, unit.Position); // select
        runner.SimulateKeyPressed(Key.D);
        await runner.SimulateFrames(1);

        var dialog = controller.GetChildren().OfType<ConfirmationDialog>().First();
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
        await runner.SimulateFrames(2);

        AssertThat(game.Units.Any(u => u.Id == unitId)).IsTrue(); // still here — Cancel kept it
    }

    [TestCase(Timeout = 60000)]
    public async Task SelectedUnitPanel_CashInTreasureButton_AtAnOwnedColony_BanksGoldAndConsumesTheTrain()
    {
        // 86d3f62q1: the Orders "Cash in treasure" button is shown only for treasure trains, gated on
        // CheckCashInTreasureTrain, surfaces the net value in a confirmation, and on confirm banks the gold and the
        // train leaves the game. At a colony the King's transport cut applies (medium 60%); tax is zeroed (via the save
        // layer — the presentation project can't set TaxRate / TreasureAmount directly) to isolate the cut.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        // Found a colony with the human's founder; then stage (via the save layer) a human treasure train carrying
        // 1000 gold standing on that colony, with the human's tax zeroed.
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        SaveGame save = SaveGame.From(game);
        int trainId = game.Units.Max(u => u.Id) + 1;
        var train = new SavedUnit(trainId, "model.unit.treasureTrain", colony.Position.X, colony.Position.Y, 3,
            OwnerId: humanId, TreasureAmount: 1000);
        List<SavedPlayer> players = save.Players!
            .Select(p => p.PlayerId == humanId ? p with { Tax = 0 } : p).ToList();
        Game injected = (save with { Units = save.Units.Append(train).ToList(), Players = players }).Restore(game.Ruleset);
        SetGame(controller, injected);
        int goldBefore = injected.HumanPlayer.Gold;

        await ClickTile(runner, controller, colony.Position); // select the train (sits on the colony tile)
        var cashIn = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/CashInTreasureButton");
        AssertThat(cashIn.Visible).IsTrue();   // shown for a treasure train
        AssertThat(cashIn.Disabled).IsFalse(); // at an owned colony → cashable

        cashIn.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The confirmation surfaces the value (1000 − 60% King's cut, no tax → 400) before committing.
        var dialog = controller.GetChildren().OfType<ConfirmationDialog>().First();
        AssertThat(dialog.DialogText).Contains("400"); // net banked, shown to the player
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);

        AssertThat(injected.HumanPlayer.Gold).IsEqual(goldBefore + 400);             // banked the net
        AssertThat(injected.Units.Any(u => u.Id == trainId)).IsFalse();              // the train is consumed
        AssertThat(controller.GetNode<PanelContainer>("UI/SelectedUnitPanel").Visible).IsFalse(); // selection cleared
    }

    [TestCase(Timeout = 60000)]
    public async Task SelectedUnitPanel_CashInTreasureButton_AboardAGalleonInEurope_BanksFeeFree()
    {
        // 86d3f62q1: a treasure train carried home aboard a galleon docked in Europe cashes in fee-free (no King's
        // cut). Staged via the save layer (the presentation project can't set CarrierId / locations / tax directly): a
        // galleon and a treasure train both docked in Europe, the train aboard the galleon, tax zeroed. The button
        // enables and banks the full amount. The aboard train has no map tile, so the button is driven directly.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        SaveGame save = SaveGame.From(game);
        int galleonId = game.Units.Max(u => u.Id) + 1;
        int trainId = galleonId + 1;
        var staged = new List<SavedUnit>
        {
            // Both docked in Europe (Location 2 = InEurope); the train rides the galleon (CarrierId = galleonId).
            new(galleonId, "model.unit.galleon", 0, 0, 18, Location: 2, OwnerId: humanId),
            new(trainId, "model.unit.treasureTrain", 0, 0, 3, Location: 2, CarrierId: galleonId,
                OwnerId: humanId, TreasureAmount: 1000),
        };
        List<SavedPlayer> players = save.Players!
            .Select(p => p.PlayerId == humanId ? p with { Tax = 0 } : p).ToList(); // fee-free in Europe + no tax → full amount
        Game injected = (save with { Units = save.Units.Concat(staged).ToList(), Players = players }).Restore(game.Ruleset);
        SetGame(controller, injected);

        Unit train = injected.Units.First(u => u.Id == trainId);
        AssertThat(train.IsAboard).IsTrue();
        AssertThat(injected.CheckCashInTreasureTrain(train).Allowed).IsTrue(); // sanity: cashable in Europe
        int goldBefore = injected.HumanPlayer.Gold;

        // Select the aboard train directly (it has no map tile to click), then drive the order button.
        controller.GetType().GetField("_selectedUnit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, train);
        controller.GetType().GetMethod("RefreshView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);
        await runner.SimulateFrames(1);
        var cashIn = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/CashInTreasureButton");
        AssertThat(cashIn.Visible).IsTrue();
        AssertThat(cashIn.Disabled).IsFalse(); // aboard a galleon docked in Europe → cashable

        cashIn.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        var dialog = controller.GetChildren().OfType<ConfirmationDialog>().First();
        AssertThat(dialog.DialogText).Contains("1000"); // fee-free → full amount shown
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);

        AssertThat(injected.HumanPlayer.Gold).IsEqual(goldBefore + 1000);    // banked the full amount (no fee, no tax)
        AssertThat(injected.Units.Any(u => u.Id == trainId)).IsFalse();      // the train is consumed
    }

    [TestCase(Timeout = 60000)]
    public async Task PressingEnter_EndsTheTurn()
    {
        // 86d3f0vjg: Enter is the EndTurnButton path (advances the turn).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int before = game.Turn;

        runner.SimulateKeyPressed(Key.Enter);
        await runner.SimulateFrames(2);

        AssertThat(GameOf(controller).Turn).IsEqual(before + 1);
    }

    [TestCase(Timeout = 60000)]
    public async Task PressingSpace_SkipsTheUnit_SoTheWCycleDoesNotReOfferIt_UntilNextTurn()
    {
        // 86d3f0vuy: Space flags the active unit skipped-this-turn; the W-cycle passes it over until turn rollover.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        Unit first = game.NextUnitToMove(game.HumanPlayer)!;
        // Select the lowest-id unit, then skip it.
        await ClickTile(runner, controller, first.Position);
        runner.SimulateKeyPressed(Key.Space);
        await runner.SimulateFrames(1);

        // The skip set now holds the skipped unit; W no longer re-offers it (a different unit, or none).
        var skipped = (System.Collections.Generic.HashSet<int>)controller.GetType()
            .GetField("_skippedThisTurn", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        AssertThat(skipped.Contains(first.Id)).IsTrue();
        Unit? nextAfterSkip = game.NextUnitToMove(game.HumanPlayer, skipped);
        AssertThat(nextAfterSkip == null || nextAfterSkip.Id != first.Id).IsTrue();

        // Turn rollover clears the skip set so the unit is offered again.
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);
        AssertThat(skipped.Count).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task CtrlC_CentresTheCameraOnTheSelectedUnit()
    {
        // 86d3f0vqf: Ctrl+C recentres on the selected unit (reuses CenterCameraOnTile).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = game.PlayerUnits.First(u => u.IsOnMap);

        await ClickTile(runner, controller, unit.Position); // select it
        var camera = controller.GetNode<Camera2D>("Camera");
        camera.Position = Vector2.Zero; // move the camera away so the centring is observable

        runner.SimulateKeyPressed(Key.C, control: true);
        await runner.SimulateFrames(1);

        AssertThat(camera.Position).IsEqual(MapView.TileCentre(unit.Position));
    }

    [TestCase(Timeout = 60000)]
    public async Task F1_TogglesTheKeysLegend()
    {
        // 86d3f0vjg: F1 toggles the keys legend, built from the single authoritative key table.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        runner.SimulateKeyPressed(Key.F1);
        await runner.SimulateFrames(1);
        var legend = controller.GetNode<CanvasLayer>("UI").GetNodeOrNull<Label>("KeysLegend");
        AssertThat(legend).IsNotNull();
        AssertThat(legend!.Visible).IsTrue();
        AssertThat(legend.Text).Contains("End turn"); // a row generated from the authoritative table
        AssertThat(legend.Text).Contains("Disband");

        runner.SimulateKeyPressed(Key.F1);
        await runner.SimulateFrames(1);
        AssertThat(legend.Visible).IsFalse(); // toggled back off
    }

    [TestCase(Timeout = 60000)]
    public async Task RightClickTileMenu_ActivatesAUnitInAStack()
    {
        // 86d3f0vrz: right-click opens a menu listing each own unit on the tile; choosing one selects it (fixes the
        // stack-select bug where a left-click only picks the first). Drives the menu via the controller's handler.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Two human units on one tile (a stack).
        TrimHumanRosterToOne(game);
        Position pos = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Unit second = game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), pos);
        controller.GetType().GetField("_selectedUnit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, null);

        // Centre on the tile and park the cursor at screen-centre, then invoke the right-click handler (the screen-space
        // drag/release path is camera-owned). HandleRightClick reads _mapView.GetLocalMousePosition(); with the camera on
        // the tile, the cursor must sit at screen-centre to project onto it (the [BeforeTest] reset warps it to the origin,
        // which would project off-map and the handler would early-out without a menu). Mirrors ClickTile's centring.
        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(pos);
        runner.SetMousePos(controller.GetViewport().GetVisibleRect().Size / 2f);
        Input.FlushBufferedEvents(); // land the warp before HandleRightClick reads the cursor
        await runner.SimulateFrames(1);
        controller.GetType().GetMethod("HandleRightClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, null);
        await runner.SimulateFrames(1);

        var menu = controller.GetNode<CanvasLayer>("UI").GetChildren().OfType<PopupMenu>().First();
        // Entries: one per unit on the tile (two), a separator, "Centre here", "Go to here".
        int activateCount = 0;
        for (int i = 0; i < menu.ItemCount; i++)
        {
            if (menu.GetItemText(i).StartsWith("Activate"))
            {
                activateCount++;
            }
        }
        AssertThat(activateCount).IsEqual(2);

        // Fire the second unit's id → it becomes the selection.
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, second.Id);
        await runner.SimulateFrames(1);
        Unit? selected = (Unit?)controller.GetType()
            .GetField("_selectedUnit", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller);
        AssertThat(selected).IsNotNull();
        AssertThat(selected!.Id).IsEqual(second.Id);
    }

    [TestCase(Timeout = 60000)]
    public async Task RebindingEndTurnToAnotherKey_MakesTheNewKeyEndTheTurn_AndTheLegendUpdates()
    {
        // 86d3f0wjj: hotkeys are now named InputMap actions; a rebind applied to the InputMap is honoured by the
        // dispatch and reflected in the F1 legend. Rebind end_turn from Enter to T, then restore the default so the
        // process-global InputMap is left as the other cases expect (Enter ends the turn).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        var model = new GameLogic.App.KeyBindingsModel();
        var tKey = new GameLogic.App.KeyBindingsModel.KeyChord((long)Key.T, false);
        try
        {
            KeyBindingsService.Rebind(model, "end_turn", tKey); // Enter → T, live on the InputMap
            await runner.SimulateFrames(1);

            int before = GameOf(controller).Turn;
            runner.SimulateKeyPressed(Key.T); // the new End-Turn key
            await runner.SimulateFrames(2);
            AssertThat(GameOf(controller).Turn).IsEqual(before + 1); // the rebind took — T now ends the turn

            // The F1 legend, rebuilt on show, names the new key.
            runner.SimulateKeyPressed(Key.F1);
            await runner.SimulateFrames(1);
            var legend = controller.GetNode<CanvasLayer>("UI").GetNodeOrNull<Label>("KeysLegend");
            AssertThat(legend).IsNotNull();
            AssertThat(legend!.Text).Contains("T  —  End turn"); // the legend reflects the rebind
        }
        finally
        {
            KeyBindingsService.Rebind(model, "end_turn", GameLogic.App.KeyBindingsModel.DefaultFor("end_turn")); // restore Enter
            await runner.SimulateFrames(1);
        }
    }

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, game);

    /// <summary>Trims the human's starting roster (pioneer + soldier + caravel) to a single land founder and returns it — keeps these single-unit-era input tests unambiguous.</summary>
    private static Unit TrimHumanRosterToOne(Game game)
    {
        Unit keep = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval && u.Type.CanFoundColony);
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap && u != keep).ToList())
        {
            game.Disband(u);
        }
        return keep;
    }

    /// <summary>Disbands every on-map unit the human owns (for the "wiped out" defeat tests).</summary>
    private static void DisbandAllHuman(Game game)
    {
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
    }

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
        Input.FlushBufferedEvents(); // make the warp land before the press, so the click fires at this tile (not a stale pos)
        await runner.SimulateFrames(1);
        runner.SimulateMouseButtonPressed(MouseButton.Left); // press+release (one click) — never leaves the button held
        await runner.SimulateFrames(2);
    }

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
