using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
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
    public void ResetGlobalInputStateBeforeEachTest()
    {
        ResetGlobalInputState();
        // Move-slide animations are forced OFF for every case by default so marker placement is deterministic and
        // wall-clock-free (the L3 runner uses an x11 display, not headless, so a slide would otherwise spawn a transient
        // that takes real time). The one case that exercises the slide turns it back on locally. Reset the per-unit slide
        // memory too, so a marker reused at a position from a prior case can't be read as "this unit moved here".
        UnitMoveAnimation.Enabled = false;
        UnitMarker.ResetMoveMemory();
    }

    [AfterTest]
    public void ResetGlobalInputStateAfterEachTest()
    {
        ResetGlobalInputState();
        UnitMoveAnimation.Enabled = true; // restore the production default for anything outside this suite
        UnitMarker.ResetMoveMemory();
    }

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
    public async Task AfterAMove_TheUnitsMarkerSitsAtTheNewTilesPixelCentre_WithAnimationsInstant()
    {
        // 86d3fq26m: with the move-slide forced instant (BeforeTest sets UnitMoveAnimation.Enabled = false), a resolved
        // move must leave the unit's marker at the destination tile's pixel centre IMMEDIATELY — the cosmetic slide
        // never changes the final state, so a move looks identical whether or not the animation ran.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        Game game = GameOf(controller);
        Unit unit = TrimHumanRosterToOne(game);
        Position start = unit.Position;

        await ClickTile(runner, controller, start); // select
        Position target = start.Neighbours().First(n => game.CheckMove(unit, n).Allowed);
        await ClickTile(runner, controller, target); // move

        AssertThat(unit.Position).IsEqual(target); // the engine move resolved
        // Exactly one human marker, sitting on the destination's pixel centre (no in-flight slide left it short).
        var markers = controller.GetNode<Node2D>("MapView/UnitLayer").GetChildren()
            .OfType<UnitMarker>().Where(m => m.OwnerColor.A == 0f).ToList();
        AssertThat(markers.Count).IsEqual(1);
        AssertThat(markers[0].Position).IsEqual(MapView.TileCentre(target));
    }

    [TestCase(Timeout = 60000)]
    public async Task MovingAUnitsMarker_SpawnsAMoveSlide_ThatLeavesTheMarkerAtTheDestination()
    {
        // 86d3fq26m: drives the slide machinery directly (the integrator wires SyncUnitMarkers to pass the unit id;
        // until then the slide is dormant). A marker placed at tile A for unit #1, then re-placed at the adjacent tile B
        // for the SAME id, must (a) spawn exactly one UnitMoveAnimation under the unit layer — the visible step — and
        // (b) still leave the real marker at B's pixel centre, so the final state is unchanged by the decoration.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        var layer = controller.GetNode<Node2D>("MapView/UnitLayer");
        var mapView = controller.GetNode<Node2D>("MapView"); // the slide parents to the unit layer's parent (the durable MapView)
        foreach (Node child in layer.GetChildren())
        {
            child.QueueFree(); // a clean layer so the only nodes below are the ones this case adds
        }
        await runner.SimulateFrames(1);
        UnitMarker.ResetMoveMemory();
        UnitMoveAnimation.Enabled = true; // this is the one case that exercises the real slide

        var a = new Position(4, 4);
        var b = new Position(5, 4); // an adjacent tile

        // First placement at A: establishes the unit's last-known marker pixel (no slide on first appearance).
        var first = new UnitMarker { Position = MapView.TileCentre(a) };
        first.SetUnit("freeColonist", "default", unitId: 1);
        layer.AddChild(first);
        await runner.SimulateFrames(1);
        AssertThat(mapView.GetChildren().OfType<UnitMoveAnimation>().Count()).IsEqual(0); // first appearance never slides

        // The wholesale rebuild: free the old marker, add a fresh one for the SAME unit at B (what SyncUnitMarkers does).
        first.QueueFree();
        await runner.SimulateFrames(1);
        var moved = new UnitMarker { Position = MapView.TileCentre(b) };
        moved.SetUnit("freeColonist", "default", unitId: 1);
        layer.AddChild(moved);
        await runner.SimulateFrames(1);

        // A move-slide spawned (the visible step, on the durable MapView), and the real marker is already at the dest pixel.
        AssertThat(mapView.GetChildren().OfType<UnitMoveAnimation>().Count()).IsEqual(1);
        AssertThat(moved.Position).IsEqual(MapView.TileCentre(b));

        // Let the short slide run out (frames with a real per-frame delta so the tween advances in process time); it
        // frees itself and the marker remains exactly at B (final state unchanged).
        await runner.SimulateFrames(40, 20); // 40 × 20ms = 800ms > the slide's sub-0.5s duration
        AssertThat(mapView.GetChildren().OfType<UnitMoveAnimation>().Count()).IsEqual(0); // the transient cleaned itself up
        AssertThat(moved.Position).IsEqual(MapView.TileCentre(b));

        UnitMoveAnimation.Enabled = false; // restore the suite default for the remaining cases
    }

    [TestCase(Timeout = 60000)]
    public async Task ReplacingAMarkerFarFromItsLastTile_SnapsInsteadOfStreaking()
    {
        // 86d3fq26m (Wave 7 integration guard): a unit re-emerging from fog / a ship / Europe many tiles from where its
        // marker last sat must NOT slide (its LastTileCentre entry isn't pruned while off-map). With the slide forced ON,
        // re-placing the same unit id beyond MaxSlideTiles (10 king-steps) spawns ZERO UnitMoveAnimation — it snaps.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        var layer = controller.GetNode<Node2D>("MapView/UnitLayer");
        var mapView = controller.GetNode<Node2D>("MapView");
        foreach (Node child in layer.GetChildren())
        {
            child.QueueFree();
        }
        await runner.SimulateFrames(1);
        UnitMarker.ResetMoveMemory();
        UnitMoveAnimation.Enabled = true; // so the ONLY thing that can suppress the slide is the distance guard

        var near = new Position(4, 4);
        var far = new Position(4, 20); // 16 tiles away in Y — a reappearance, well beyond MaxSlideTiles (10)

        var first = new UnitMarker { Position = MapView.TileCentre(near) };
        first.SetUnit("freeColonist", "default", unitId: 7);
        layer.AddChild(first);
        await runner.SimulateFrames(1);
        first.QueueFree();
        await runner.SimulateFrames(1);

        var reappeared = new UnitMarker { Position = MapView.TileCentre(far) };
        reappeared.SetUnit("freeColonist", "default", unitId: 7);
        layer.AddChild(reappeared);
        await runner.SimulateFrames(1);

        AssertThat(mapView.GetChildren().OfType<UnitMoveAnimation>().Count()).IsEqual(0); // snapped, not streaked
        AssertThat(reappeared.Position).IsEqual(MapView.TileCentre(far));

        UnitMoveAnimation.Enabled = false; // restore the suite default for the remaining cases
    }

    [TestCase(Timeout = 60000)]
    public async Task ShowRoutePreview_DrawsTheWaypointLineForTheGivenRoute_AndClearsOnEmpty()
    {
        // 86d3fq1pe: drives the route-preview DRAWING API directly (the integrator wires GameController to feed it
        // _game.PreviewRoute; until then it's exercised here with a passed-in route). A route of three explored
        // neighbouring tiles must render the gold waypoint line/dots into the map layer; an empty route clears it.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        var mapView = controller.GetNode<MapView>("MapView");
        var origin = new Position(6, 6);
        // A short straight diagonal route (origin → waypoints); pixel centres are well clear of the corner HUD.
        IReadOnlyList<Position> route = [new Position(7, 6), new Position(8, 6), new Position(9, 6)];

        // Centre the camera on the middle of the route so the gold line lands on-screen for the render check.
        var camera = controller.GetNode<Camera2D>("Camera");
        camera.Position = MapView.TileCentre(new Position(8, 6));
        await runner.SimulateFrames(2);

        mapView.ShowRoutePreview(route, from: origin);
        await runner.SimulateFrames(2);

        // The overlay holds origin + the three waypoints, in order (the drawn tile sequence).
        AssertThat(mapView.PreviewedRouteTiles.Count).IsEqual(4);
        AssertThat(mapView.PreviewedRouteTiles[0]).IsEqual(origin);
        AssertThat(mapView.PreviewedRouteTiles[3]).IsEqual(route[2]);

        // RENDER-VERIFY: capture the viewport to a temp PNG, read it back, scan the route's on-screen span for the
        // gold path colour. Then DELETE the temp png — we never commit pngs.
        Image img = controller.GetViewport().GetTexture().GetImage();
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"routepreview_{System.Guid.NewGuid():N}.png");
        AssertThat(img.SavePng(tmp)).IsEqual(Error.Ok);
        try
        {
            using var loaded = Image.LoadFromFile(tmp);
            // Project a couple of the route's tile centres to screen space and scan a small box for goto gold.
            bool foundGold = false;
            foreach (Position tile in new[] { origin, route[0], route[1] })
            {
                Vector2 screen = mapView.GetGlobalTransformWithCanvas() * MapView.TileCentre(tile);
                int cx = Mathf.RoundToInt(screen.X), cy = Mathf.RoundToInt(screen.Y);
                for (int dy = -10; dy <= 10 && !foundGold; dy++)
                {
                    for (int dx = -10; dx <= 10 && !foundGold; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= loaded.GetWidth() || y >= loaded.GetHeight())
                        {
                            continue;
                        }
                        Color c = loaded.GetPixel(x, y);
                        if (c.R > 0.7f && c.G > 0.6f && c.B < 0.5f) // goto gold (0.96, 0.86, 0.20)
                        {
                            foundGold = true;
                        }
                    }
                }
            }
            AssertThat(foundGold).IsTrue(); // the gold waypoint line/dots rendered along the route
        }
        finally
        {
            if (System.IO.File.Exists(tmp))
            {
                System.IO.File.Delete(tmp);
            }
        }

        // An empty route clears the preview (the same call both shows and hides as the aimed tile changes).
        mapView.ShowRoutePreview([]);
        await runner.SimulateFrames(1);
        AssertThat(mapView.PreviewedRouteTiles.Count).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task PressingW_SelectsTheNextUnitNeedingOrders_AndCentresOnIt()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        // ResetGlobalInputState warps the cursor to the top-left corner (an edge); on CI that warp emits a motion event
        // that would edge-scroll this exact-centre camera assertion ~14px on the next simulated frame. Suppress edge-scroll
        // (the camera's documented test escape hatch, 86d3fq22p) so a parked test cursor can't drift the camera.
        controller.GetNode<CameraController>("Camera").EdgeScrollEnabled = false;
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        Unit expected = game.NextUnitToMove(game.HumanPlayer)!;
        AssertThat(expected).IsNotNull(); // a fresh game has a unit still needing orders

        runner.SimulateKeyPressed(Key.W); // cycle to the next unit needing orders
        await runner.SimulateFrames(1);

        // The camera centres on it and the selected-unit panel reflects it.
        AssertThat(controller.GetNode<Camera2D>("Camera").Position).IsEqual(MapView.TileCentre(expected.Position));
        AssertThat(controller.GetNode<PanelContainer>("UI/SelectedUnitPanel").Visible).IsTrue();
        AssertThat(controller.GetNode<Label>("UI/SelectedUnitPanel/VBox/Label").Text).Contains(Naming.Humanize(expected.Type.ShortName));
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
        AssertThat(label.Text).Contains(Naming.Humanize(unit.Type.ShortName)); // type (humanised, e.g. "Free Colonist")
        AssertThat(label.Text).Contains("moves");             // and its movement readout
    }

    [TestCase(Timeout = 60000)]
    public async Task DismissingTheAdvisor_KeepsItHiddenForThatUnitAcrossRefreshes_ButReturnsForAnother()
    {
        // Regression for 86d3jrzah: pressing the advisor's "Dismiss" must stick. Before the fix RefreshView (which
        // runs on every move/click/action) unconditionally re-Show()ed the card, so dismissal did nothing.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.GetNode<SettingsService>("/root/Settings").SetAdvisorHints(true); // the advisor is opt-in (off by default, 86d3jy0rn) — enable it for this test
        controller.StartNewGame(Seed);
        controller.GetNode<CameraController>("Camera").EdgeScrollEnabled = false; // parked test cursor must not drift the camera
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Two on-map human units that actually have advice (so the card shows), at distinct tiles.
        var advised = game.PlayerUnits.Where(u => u.IsOnMap && game.AdviseUnit(u).Count > 0).ToList();
        AssertThat(advised.Count >= 2).IsTrue(); // the classic start (ship + colonists) has several
        Unit unitA = advised[0];
        Unit unitB = advised.First(u => u.Position != unitA.Position);

        var advisor = controller.GetNode<AdvisorPanel>("UI/AdvisorPanel");

        await ClickTile(runner, controller, unitA.Position); // select A → the advisor card appears
        AssertThat(advisor.Visible).IsTrue();

        controller.GetNode<Button>("UI/AdvisorPanel/VBox/CloseButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(advisor.Visible).IsFalse(); // dismissed

        await ClickTile(runner, controller, unitA.Position); // re-select the SAME unit → RefreshView must NOT resurrect it
        AssertThat(advisor.Visible).IsFalse();

        await ClickTile(runner, controller, unitB.Position); // a DIFFERENT unit → fresh advice is welcome again
        AssertThat(advisor.Visible).IsTrue();
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
        // (No "hidden initially" pre-check: the readout is hover-driven and the suite parks the cursor at the screen
        // origin (0,0) between cases, which on some camera positions hovers an explored tile and shows the panel — an
        // input artefact, not the behaviour under test. This case asserts the click→readout contents, which is robust:
        // ClickTile centres the camera + cursor on the target tile, so the hovered tile equals the clicked one.)

        // Click the human's starting unit's tile: it's explored, so the readout names that tile's terrain and the
        // occupying unit (the same tile the click selects).
        Unit unit = game.PlayerUnits.First(u => u.IsOnMap);
        Position tile = unit.Position;
        await ClickTile(runner, controller, tile);

        AssertThat(panel.Visible).IsTrue();
        var label = controller.GetNode<Label>("UI/TileInfoPanel/VBox/Label");
        AssertThat(label.Text).Contains(Naming.Humanize(game.Map.TerrainAt(tile).ShortName)); // terrain (humanised, 86d3jy0rn)
        AssertThat(label.Text).Contains(Naming.Humanize(unit.Type.ShortName));                // occupant (humanised)
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
        AssertThat(controller.GetNode<Label>("UI/SelectedUnitPanel/VBox/Label").Text).Contains("building Road"); // shown (humanised improvement)
    }

    [TestCase(Timeout = 60000)]
    public async Task SelectedUnitPanel_SentryButton_SentriesTheUnit_AndClearOrdersWakesIt()
    {
        // 86d3f62r5 player-flow coverage: the Sentry + Clear-orders Order buttons were reachable but undriven by L3
        // (only Fortify + Road were). Press Sentry → the unit sentries; press Clear-orders → it wakes back to active.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = game.Units[0];

        await ClickTile(runner, controller, unit.Position); // select → the Order buttons show
        var sentry = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/SentryButton");
        var clear = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/ClearButton");
        AssertThat(sentry.Disabled).IsFalse(); // an active unit can sentry

        sentry.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(unit.Orders).IsEqual(UnitOrders.Sentry);  // the order took
        AssertThat(clear.Disabled).IsFalse();                // and Clear-orders can now wake it

        clear.EmitSignal(BaseButton.SignalName.Pressed);     // press Clear-orders (not just check Disabled)
        await runner.SimulateFrames(1);
        AssertThat(unit.Orders).IsEqual(UnitOrders.Active);  // woken back to active
    }

    [TestCase(Timeout = 60000)]
    public async Task EuropeButton_OpensTheEuropePanel()
    {
        // 86d3f62r5 player-flow coverage: drive the real UI/EuropeButton (not OpenEuropePanel() directly) → the Europe
        // screen opens. Closes the wiring-regression gap on the entry-point button (and exercises it with no panel open,
        // where the corner-HUD-hide fix 86d3fr6bc keeps it visible/clickable).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);

        var europePanel = controller.GetNode<PanelContainer>("UI/EuropePanel");
        AssertThat(europePanel.Visible).IsFalse(); // closed at game start

        controller.GetNode<Button>("UI/EuropeButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);
        AssertThat(europePanel.Visible).IsTrue(); // the button wired through to OpenEuropePanel
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

    /// <summary>An ocean tile (not yet the high seas) with an unoccupied high-seas neighbour — the crossing the prompt fires on.</summary>
    private static (Position ocean, Position highSeas) AnOceanTileBesideTheHighSeas(Game game) =>
        (from p in game.Map.AllPositions()
         where game.Map.TerrainAt(p).IsWater && game.Map.TerrainAt(p).Id != "model.tile.highSeas"
             && !game.Units.Any(u => u.IsOnMap && u.Position == p)
         from n in p.Neighbours()
         where game.Map.InBounds(n) && game.Map.TerrainAt(n).Id == "model.tile.highSeas"
             && !game.Units.Any(u => u.IsOnMap && u.Position == n)
         select (p, n)).First();

    [TestCase(Timeout = 60000)]
    public async Task MovingAShipOntoTheHighSeas_PromptsToSailToEurope_AndConfirmingSails()
    {
        // FreeCol moveHighSeas: a ship that CROSSES onto the high seas is offered the Europe passage (86d3fpzqp).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        (Position ocean, Position highSeas) = AnOceanTileBesideTheHighSeas(game);
        Unit ship = game.SpawnUnit(game.Ruleset.Unit("model.unit.caravel"), ocean);

        await ClickTile(runner, controller, ocean);    // select the ship
        await ClickTile(runner, controller, highSeas); // move it onto the high seas → the auto-prompt fires

        var dialog = controller.GetChildren().OfType<ConfirmationDialog>()
            .FirstOrDefault(d => d.Title.ToString() == "Sail to Europe");
        AssertThat(dialog).IsNotNull();
        AssertThat(ship.Position).IsEqual(highSeas);      // it crossed (a plain move, not yet sailing)
        AssertThat(ship.Location).IsEqual(UnitLocation.OnMap);

        dialog!.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);

        AssertThat(ship.Location).IsEqual(UnitLocation.SailingToEurope); // confirm forwarded to SailToEurope
        AssertThat(GodotObject.IsInstanceValid(dialog)).IsFalse();       // freed, no orphan
    }

    [TestCase(Timeout = 60000)]
    public async Task CancellingTheSailToEuropePrompt_LeavesTheShipOnTheHighSeas_AndFreesTheDialog()
    {
        // Cancel-path guard (Wave 8/9 discipline): Cancel / Escape / window-close raises Canceled (not Confirmed); the
        // ship stays put, the re-entrancy guard resets, and the node is freed (no stuck guard, no orphan).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        (Position ocean, Position highSeas) = AnOceanTileBesideTheHighSeas(game);
        Unit ship = game.SpawnUnit(game.Ruleset.Unit("model.unit.caravel"), ocean);

        await ClickTile(runner, controller, ocean);
        await ClickTile(runner, controller, highSeas);

        var dialog = controller.GetChildren().OfType<ConfirmationDialog>()
            .FirstOrDefault(d => d.Title.ToString() == "Sail to Europe");
        AssertThat(dialog).IsNotNull();

        dialog!.EmitSignal(AcceptDialog.SignalName.Canceled);
        await runner.SimulateFrames(2);

        AssertThat(ship.IsOnMap).IsTrue();                              // stayed on the map (did not sail)
        AssertThat(ship.Location).IsNotEqual(UnitLocation.SailingToEurope);
        AssertThat(GodotObject.IsInstanceValid(dialog)).IsFalse();      // freed, no orphan
        bool guard = (bool)controller.GetType()
            .GetField("_sailPromptDialogOpen", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        AssertThat(guard).IsFalse();                                    // re-entrancy guard reset
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
    public async Task ConfirmingAnAttack_PlaysACombatAnimation_WithoutBlockingTurnFlow()
    {
        // 86d3fpxj2: confirming an attack fires the non-blocking procedural CombatAnimation (a transient lunge node
        // parented to the unit layer) AND the turn proceeds immediately — the attacker has spent its turn (0 movement,
        // or it's gone) the same frame, i.e. nothing awaits the animation. The transient frees itself shortly after.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // An artillery (offence 7, no role needed) adjacent to a brave next to the human's start — the attack-L3 setup.
        string nation = game.Units.First(u => u.IsNative).OwnerNationId!;
        Position human = game.PlayerUnits.First(u => u.IsOnMap).Position;
        Position bravePos = human.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), bravePos, nation);
        Position artPos = bravePos.Neighbours().First(n => n != human && Free(game, n));
        Unit artillery = game.SpawnUnit(game.Ruleset.Unit("model.unit.artillery"), artPos);
        int artId = artillery.Id;

        await ClickTile(runner, controller, artPos);    // select the artillery
        await ClickTile(runner, controller, bravePos);  // open the pre-combat odds dialog
        controller.GetNode<Button>("UI/PreCombatPanel/VBox/Buttons/AttackButton")
            .EmitSignal(BaseButton.SignalName.Pressed); // confirm
        await runner.SimulateFrames(1);

        // The animation is playing (a CombatAnimation node sits on the durable MapView — it parents there, not the unit
        // layer, so the post-attack RefreshView's unit-layer wipe can't reap it mid-lunge) and the turn already advanced
        // for the attacker — it spent its turn or was destroyed (a blocking animation would have stalled here).
        var mapView = controller.GetNode<Node2D>("MapView");
        AssertThat(mapView.GetChildren().OfType<CombatAnimation>().Count()).IsGreater(0);
        Unit? attacker = game.Units.FirstOrDefault(u => u.Id == artId);
        AssertThat(attacker == null || attacker.MovementLeft == 0).IsTrue();

        // It cleans itself up: after the short (non-blocking) lunge plays out, no CombatAnimation remains. Frames carry a
        // real per-frame delta so the 0.36s tween advances in process time.
        await runner.SimulateFrames(40, 20); // 40 × 20ms = 800ms > CombatAnimation.DurationSeconds (0.36s)
        AssertThat(mapView.GetChildren().OfType<CombatAnimation>().Count()).IsEqual(0);
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
        // 86d3f62q1 / 86d3fb5mj: the Orders "Cash in treasure" button is shown only for treasure trains, gated on
        // CheckCashInTreasureTrain, surfaces the King's OFFER in a confirmation, and on confirm banks the net and the
        // train leaves the game. In the Col1 model the King's at-colony cut = the current tax rate; we stage tax = 25
        // (via the save layer — the presentation project can't set TaxRate / TreasureAmount directly) so the cut is a
        // real 250g and the net 750g, exercising the King's-offer wording.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        // Found a colony with the human's founder; then stage (via the save layer) a human treasure train carrying
        // 1000 gold standing on that colony, with the human's tax at 25% (the King's cut in the Col1 model).
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        SaveGame save = SaveGame.From(game);
        int trainId = game.Units.Max(u => u.Id) + 1;
        var train = new SavedUnit(trainId, "model.unit.treasureTrain", colony.Position.X, colony.Position.Y, 3,
            OwnerId: humanId, TreasureAmount: 1000);
        List<SavedPlayer> players = save.Players!
            .Select(p => p.PlayerId == humanId ? p with { Tax = 25 } : p).ToList();
        Game injected = (save with { Units = save.Units.Append(train).ToList(), Players = players }).Restore(game.Ruleset);
        SetGame(controller, injected);
        int goldBefore = injected.HumanPlayer.Gold;

        await ClickTile(runner, controller, colony.Position); // select the train (sits on the colony tile)
        var cashIn = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/CashInTreasureButton");
        AssertThat(cashIn.Visible).IsTrue();   // shown for a treasure train
        AssertThat(cashIn.Disabled).IsFalse(); // at an owned colony → cashable

        cashIn.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The confirmation frames the King's offer (take 25% / 250g; you keep 750g) before committing.
        var dialog = controller.GetChildren().OfType<ConfirmationDialog>().First();
        AssertThat(dialog.DialogText).Contains("25%"); // the King's cut = tax rate
        AssertThat(dialog.DialogText).Contains("750"); // net banked, shown to the player
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
        await runner.SimulateFrames(2);

        AssertThat(injected.HumanPlayer.Gold).IsEqual(goldBefore + 750);             // banked the net
        AssertThat(injected.Units.Any(u => u.Id == trainId)).IsFalse();              // the train is consumed
        AssertThat(controller.GetNode<PanelContainer>("UI/SelectedUnitPanel").Visible).IsFalse(); // selection cleared
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickOwnColony_WithASelectedAdjacentUnit_MovesItOntoTheColonyTile_WithoutJoiningOrOpeningThePanel()
    {
        // 86d3fzmove: clicking the player's own colony with a selected adjacent unit MOVES the unit onto the colony
        // tile (the playtest fix — no more being forced to use go-to). The unit then STANDS on the tile (it is still
        // on the map and the colony's population is unchanged — MoveUnit does not auto-join the work force), and the
        // colony panel does NOT open on the move. This is what lets a treasure train be walked onto a colony and
        // cashed in from there.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Found a colony with one founder, then spawn a fresh land unit on a free tile ADJACENT to that colony so it
        // is a legal one-tile move target. The spawned unit is the only other on-map human unit, so it is unambiguous.
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u); // clear any remaining starting units so the spawned mover is the only on-map human unit
        }
        Position adj = colony.Position.Neighbours().First(n => Free(game, n));
        Unit mover = game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), adj);
        int populationBefore = colony.Population;
        AssertThat(game.CheckMove(mover, colony.Position).Allowed).IsTrue(); // sanity: the colony tile is a legal move

        // Select the mover (click its tile), then click the colony tile: the unit walks onto the colony.
        await ClickTile(runner, controller, adj);
        await ClickTile(runner, controller, colony.Position);

        AssertThat(mover.Position).IsEqual(colony.Position); // moved onto the colony tile
        AssertThat(mover.IsOnMap).IsTrue();                  // still on the map — it did NOT vanish into the work force
        AssertThat(colony.Population).IsEqual(populationBefore); // not auto-joined — population unchanged
        AssertThat(controller.GetNode<PanelContainer>("UI/ColonyPanel").Visible).IsFalse(); // the move did not open the panel
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickOwnColony_WithNoMovableSelection_OpensTheColonyPanel()
    {
        // 86d3fzmove: the other half of the routing — with no movable selected unit (nothing selected here), clicking
        // the player's own colony opens the colony panel as before. (Once a unit has been walked onto the colony you
        // click again to manage it; this asserts the empty-handed click still reaches the panel.)
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Found a colony, then clear every on-map human unit so no unit sits on the colony tile and nothing is selected.
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        controller.GetType().GetField("_selectedUnit", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(controller, null);

        await ClickTile(runner, controller, colony.Position);

        AssertThat(controller.GetNode<PanelContainer>("UI/ColonyPanel").Visible).IsTrue(); // empty-handed click opens the panel
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
        // Suppress edge-scroll (86d3fq22p test escape hatch): the input-reset warps the cursor to the top-left corner,
        // which on CI would edge-scroll this exact-centre camera assertion off by ~14px on the next simulated frame.
        controller.GetNode<CameraController>("Camera").EdgeScrollEnabled = false;
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

        // Centre the camera on the tile, then invoke the right-click handler with the tile's on-screen point as the
        // event position. We PROJECT the tile's world centre through the very transform TileAtScreen inverts
        // (GetGlobalTransformWithCanvas — camera pan/zoom folded in), so the pick round-trips back to THIS exact tile no
        // matter the CI viewport size or any camera-limit clamp. The earlier `GetVisibleRect().Size / 2` assumed the
        // camera perfectly centred the tile; when the seed's start sits near a map edge the camera clamps to its limits,
        // the tile is no longer at viewport-centre, and the pick lands on the wrong (or out-of-bounds) tile → an empty
        // menu whose .First() throws — the intermittent "headless 0 tiles picked" flake (task_dd365b94).
        var mapView = controller.GetNode<MapView>("MapView");
        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(pos);
        await runner.SimulateFrames(1);
        Vector2 screenCentre = mapView.GetGlobalTransformWithCanvas() * MapView.TileCentre(pos);
        controller.GetType().GetMethod("HandleRightClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, [screenCentre]);
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

    // ── 86d3f62r5: player-flow coverage for reachable-but-undriven UI paths ──────────────────────────────────────────

    [TestCase(Timeout = 60000)]
    public async Task SelectedUnitPanel_PlowButton_StartsPlowingTheField()
    {
        // 86d3f62r5: the Plow order button was reachable but undriven by L3 (only Fortify + Road were). Arm a pioneer,
        // move it onto a plowable (non-forested, non-hill/mountain/arctic) land tile, press Plow → the build order takes.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = game.Units[0];

        // Arm the starting colonist as a tooled pioneer (internal setters via reflection, like the Road test), then
        // teleport it onto a plow-eligible tile (the plow scope excludes water/forest/hills/mountains/arctic).
        typeof(Unit).GetProperty("RoleId")!.GetSetMethod(nonPublic: true)!.Invoke(unit, ["model.role.pioneer"]);
        typeof(Unit).GetProperty("RoleCount")!.GetSetMethod(nonPublic: true)!.Invoke(unit, [1]);
        Position plowable = game.Map.AllPositions().First(p =>
            Free(game, p) && game.CheckBuildImprovement(SetUnitAt(unit, p), TileImprovementType.PlowId).Allowed);
        typeof(Unit).GetProperty("Position")!.GetSetMethod(nonPublic: true)!.Invoke(unit, [plowable]);

        await ClickTile(runner, controller, plowable); // select → the build-order buttons gate on the oracle
        var plow = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/PlowButton");
        AssertThat(plow.Disabled).IsFalse(); // a tooled pioneer on a plowable tile can plow

        plow.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(unit.IsImproving).IsTrue();                                    // the build order took
        AssertThat(unit.WorkImprovementId).IsEqual(TileImprovementType.PlowId);
        AssertThat(unit.MovementLeft).IsEqual(0);                                 // committed for the turn
    }

    [TestCase(Timeout = 60000)]
    public async Task SelectedUnitPanel_ClearForestButton_StartsClearingTheForest()
    {
        // 86d3f62r5: the Clear-forest order button was reachable but undriven by L3. Arm a pioneer, move it onto a
        // forested tile (the clear-forest scope requires isForested=true), press Clear-forest → the build order takes.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = game.Units[0];

        typeof(Unit).GetProperty("RoleId")!.GetSetMethod(nonPublic: true)!.Invoke(unit, ["model.role.pioneer"]);
        typeof(Unit).GetProperty("RoleCount")!.GetSetMethod(nonPublic: true)!.Invoke(unit, [1]);
        Position forested = game.Map.AllPositions().First(p =>
            Free(game, p) && game.Map.TerrainAt(p).IsForest
            && game.CheckBuildImprovement(SetUnitAt(unit, p), TileImprovementType.ClearForestId).Allowed);
        typeof(Unit).GetProperty("Position")!.GetSetMethod(nonPublic: true)!.Invoke(unit, [forested]);

        await ClickTile(runner, controller, forested); // select → the build-order buttons gate on the oracle
        var clearForest = controller.GetNode<Button>("UI/SelectedUnitPanel/VBox/Orders/ClearForestButton");
        AssertThat(clearForest.Disabled).IsFalse(); // a tooled pioneer on a forested tile can clear it

        clearForest.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(unit.IsImproving).IsTrue();                                          // the build order took
        AssertThat(unit.WorkImprovementId).IsEqual(TileImprovementType.ClearForestId);
        AssertThat(unit.MovementLeft).IsEqual(0);                                       // committed for the turn
    }

    [TestCase(Timeout = 60000)]
    public async Task RightClickTileMenu_CentreHere_CentresTheCameraOnThatTile()
    {
        // 86d3f62r5 regression: "Centre here" was mis-wired — `menu.AddItem("Centre here", CentreId)` with CentreId == -1,
        // but Godot 4.6's PopupMenu.AddItem treats id == -1 as the "auto-assign the item index" sentinel, so the item was
        // stored with id 0 and a real click missed the `case CentreId` arm (the camera never centred). Fixed to a non-(-1)
        // id; firing the real menu item must now centre the camera on the right-clicked tile.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        controller.GetNode<CameraController>("Camera").EdgeScrollEnabled = false;
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        // Right-click a specific explored tile: aim the camera at it so the screen-centre right-click maps there.
        Position target = game.Units[0].Position;
        var camera = controller.GetNode<Camera2D>("Camera");
        camera.Position = MapView.TileCentre(target);
        await runner.SimulateFrames(1);
        Vector2 screenCentre = controller.GetViewport().GetVisibleRect().Size / 2f;
        controller.GetType().GetMethod("HandleRightClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, [screenCentre]);
        await runner.SimulateFrames(1);

        // Move the camera away, then fire "Centre here" via its REAL menu id → it snaps back to the clicked tile's centre.
        camera.Position = MapView.TileCentre(target) + new Vector2(400, 400);
        await runner.SimulateFrames(1);
        var menu = controller.GetNode<CanvasLayer>("UI").GetChildren().OfType<PopupMenu>().First();
        int centreId = ItemIdWithText(menu, "Centre here"); // −10 after the fix (Godot preserves any non-(-1) id)
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, centreId);
        await runner.SimulateFrames(1);

        AssertThat(camera.Position).IsEqual(MapView.TileCentre(target)); // centred back on the right-clicked tile
    }

    [TestCase(Timeout = 60000)]
    public async Task RightClickTileMenu_GoToHere_SetsTheSelectedUnitsDestination()
    {
        // 86d3f62r5: the right-click menu's "Go to here" item was reachable but undriven. With a unit selected, open the
        // menu on a reachable explored tile and fire "Go to here" → the unit's standing destination is set (GotoId == -2).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        controller.GetNode<CameraController>("Camera").EdgeScrollEnabled = false;
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit unit = game.Units[0];
        Position origin = unit.Position;

        await ClickTile(runner, controller, unit.Position); // select the unit (the goto item acts on the selection)

        // A reachable explored neighbour is the destination; aim the right-click at it and fire "Go to here".
        Position dest = origin.Neighbours().First(n => game.CheckSetDestination(unit, n).Allowed);
        controller.GetNode<Camera2D>("Camera").Position = MapView.TileCentre(dest);
        await runner.SimulateFrames(1);
        Vector2 screenCentre = controller.GetViewport().GetVisibleRect().Size / 2f;
        controller.GetType().GetMethod("HandleRightClick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, [screenCentre]);
        await runner.SimulateFrames(1);

        var menu = controller.GetNode<CanvasLayer>("UI").GetChildren().OfType<PopupMenu>().First();
        int gotoId = ItemIdWithText(menu, "Go to here");
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, gotoId);
        await runner.SimulateFrames(1);

        AssertThat(unit.Destination.HasValue).IsTrue();
        AssertThat(unit.Destination!.Value).IsEqual(dest);       // the standing goto order was recorded
        AssertThat(unit.Position).IsEqual(origin);               // setting a destination doesn't move the unit
    }

    [TestCase(Timeout = 60000)]
    public async Task SettingAGotoDestination_ThenEndingTheTurn_WalksTheUnitTowardIt()
    {
        // 86d3f62r5: the full goto-walk player flow — arm goto (G), click a destination two tiles off, End Turn → the
        // per-turn ProcessGotos walks the unit toward the goal (it leaves its origin, closer to the destination).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        controller.GetNode<CameraController>("Camera").EdgeScrollEnabled = false;
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        // Use the human's caravel: on open ocean it always has room for a genuine multi-turn path, so the goto is stored
        // as a STANDING order (not consumed on the click). A land colonist can't be relied on here — the portrait-map
        // default (86d3jy0rn) can land the human on a 1–2 tile coastal patch with nowhere ≥2 tiles to walk. Disband the
        // other on-map units so the single walk is unambiguous.
        Unit unit = game.PlayerUnits.First(u => u.IsOnMap && u.Type.IsNaval);
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap && u != unit).ToList())
        {
            game.Disband(u);
        }
        Position origin = unit.Position;

        // The farthest reachable tile within a small radius — beyond one turn's sailing, so the goto is recorded (not
        // executed) and End Turn walks the ship PARTWAY toward it (closer, not arrived). Open water gives direct paths,
        // so stepping toward it reduces the Chebyshev distance. (Game.FindPath, internal, confirms reachability.)
        Position dest = game.Map.AllPositions()
            .Where(p => p != origin && Cheb(origin, p) <= 8 && game.CheckSetDestination(unit, p).Allowed
                && GamePath(game, unit, p).Count > 0)
            .OrderByDescending(p => GamePath(game, unit, p).Count)
            .First();

        // Arm goto via the real G key, then click the destination tile → SetSelectedDestination records the standing order.
        await ClickTile(runner, controller, origin);
        runner.SimulateKeyPressed(Key.G);
        await runner.SimulateFrames(1);
        await ClickTile(runner, controller, dest);
        AssertThat(unit.Destination!.Value).IsEqual(dest); // the goto order is set (not yet walked)
        AssertThat(unit.Position).IsEqual(origin);

        // End the turn via the button → ProcessGotos walks the unit toward the goal.
        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        AssertThat(unit.Position).IsNotEqual(origin);                            // it moved off its start tile
        AssertThat(Cheb(unit.Position, dest)).IsLess(Cheb(origin, dest));        // and closer to the destination
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickingARivalColony_WithASelectedUnit_AssaultsIt_ToCapture()
    {
        // 86d3f62r5: the player-initiated assault on a rival colony was reachable but undriven by L3. Stage an
        // undefended rival colony and a human artillery adjacent to it, at war; click the colony → the assault resolves
        // (the attacker spends its turn, and on a win the colony changes hands). Staged via the save layer (the
        // presentation project can't set Unit.OwnerId / Colony.OwnerId / stance directly).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        // Found a colony with a spawned rival colonist (so it belongs to the rival after we reassign it via the save
        // layer), then place a human artillery beside it and put the two at war.
        Position site = game.Map.AllPositions().First(p =>
            Free(game, p) && game.Map.TerrainAt(p).CanSettle && !game.Map.IsNativeOwned(p)
            && p.Neighbours().Any(n => Free(game, n))
            && p.Neighbours().All(n => !game.Map.InBounds(n) || game.ColonyAt(n) is null));
        Unit founder = game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), site);
        Colony colony = game.FoundColony(founder); // human-owned for now; reassigned to the rival below
        int colonyId = colony.Id;
        Position gunPos = site.Neighbours().First(n => Free(game, n));

        SaveGame save = SaveGame.From(game);
        SavedPlayer rival = save.Players!.First(p => !p.IsHuman && p.PlayerType == (int)PlayerType.Colonial);
        int gunId = game.Units.Max(u => u.Id) + 1;
        var staged = new SavedUnit(gunId, "model.unit.artillery", gunPos.X, gunPos.Y, 1, OwnerId: humanId);
        var colonies = save.Colonies!.Select(c => c.Id == colonyId ? c with { OwnerId = rival.PlayerId } : c).ToList();
        List<SavedPlayer> players = save.Players!.Select(p =>
            p.PlayerId == rival.PlayerId ? p with { Stances = WithWar(p.Stances, humanId) }
            : p.PlayerId == humanId ? p with { Stances = WithWar(p.Stances, rival.PlayerId) }
            : p).ToList();
        Game injected = (save with { Units = save.Units.Append(staged).ToList(), Colonies = colonies, Players = players })
            .Restore(game.Ruleset);
        SetGame(controller, injected);
        Colony rivalColony = injected.Colonies.First(c => c.Id == colonyId);

        Unit gun = injected.Units.First(u => u.Id == gunId);
        AssertThat(injected.CheckAttackColony(gun, rivalColony.Position).Allowed).IsTrue(); // sanity: the assault is legal

        // Select the artillery, then click the rival colony → the assault fires (no scout mission — artillery is no scout).
        await ClickTile(runner, controller, gunPos);
        await ClickTile(runner, controller, rivalColony.Position);
        await runner.SimulateFrames(2);

        // The assault resolved: the attacker spent its turn (0 movement, or it was destroyed). A rejected/blocked click
        // would have left it with full movement on its own tile.
        Unit? after = injected.Units.FirstOrDefault(u => u.Id == gunId);
        AssertThat(after == null || after.MovementLeft == 0).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    public async Task FoundingOnNativeOwnedLand_RaisesTheClaimPrompt_AndStealTakesTheLand()
    {
        // 86d3f62r5: the forced buy/steal/abandon land-claim prompt (founding on native-owned land) was reachable but
        // undriven by L3. Make the founder's tile native-owned, press B → the "Native land" ConfirmationDialog opens;
        // fire the "Steal" button → the colony is founded and the tile is taken (no gold spent).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit founder = TrimHumanRosterToOne(game);
        Position tile = founder.Position;

        // Make ONLY the founder's tile native-owned (clear its ring so the auto food-assign never claims another tile),
        // via the internal GameMap mutators reached by reflection (the presentation project can't call them directly).
        string nation = game.NativeSettlements.Select(s => s.NationTypeId).First();
        foreach (Position n in tile.Neighbours().Where(game.Map.InBounds))
        {
            InvokeMap(game, "ClearNativeOwner", n);
        }
        InvokeMap(game, "SetNativeOwner", tile, nation);
        AssertThat(game.RequiredLandClaim(tile).Required).IsTrue(); // sanity: founding here forces the claim

        await ClickTile(runner, controller, tile); // select the founder
        runner.SimulateKeyPressed(Key.B);           // found → the forced-claim prompt opens (not an immediate found)
        await runner.SimulateFrames(2);

        AssertThat(game.Colonies.Count).IsEqual(0); // nothing founded yet — the claim must be answered first
        var dialog = controller.GetChildren().OfType<ConfirmationDialog>().First(d => d.Title.ToString() == "Native land");
        AssertThat(dialog).IsNotNull();

        // Fire the "Steal" custom button (added via ConfirmationDialog.AddButton) → the colony is founded on the taken
        // tile, free of charge. The custom button is nested in the dialog's button row, so search all descendants.
        int goldBefore = game.HumanPlayer.Gold;
        Button steal = DescendantButtons(dialog).First(b => b.Text.ToString() == "Steal");
        steal.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        AssertThat(game.Colonies.Count).IsEqual(1);             // the colony was founded after the steal
        AssertThat(game.ColonyAt(tile)).IsNotNull();
        AssertThat(game.Map.IsNativeOwned(tile)).IsFalse();     // the land was taken
        AssertThat(game.HumanPlayer.Gold).IsEqual(goldBefore);  // stealing costs no gold
    }

    [TestCase(Timeout = 60000)]
    public async Task FoundingOnNativeOwnedLand_ThenAbandon_FoundsNothing_AndKeepsTheLandNative()
    {
        // 86d3f62r5 (the Abandon branch of the forced land-claim prompt): pressing B raises the prompt; choosing
        // Abandon (Cancel) founds nothing and leaves the land in native hands.
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Unit founder = TrimHumanRosterToOne(game);
        Position tile = founder.Position;

        string nation = game.NativeSettlements.Select(s => s.NationTypeId).First();
        foreach (Position n in tile.Neighbours().Where(game.Map.InBounds))
        {
            InvokeMap(game, "ClearNativeOwner", n);
        }
        InvokeMap(game, "SetNativeOwner", tile, nation);

        await ClickTile(runner, controller, tile);
        runner.SimulateKeyPressed(Key.B);
        await runner.SimulateFrames(2);

        var dialog = controller.GetChildren().OfType<ConfirmationDialog>().First(d => d.Title.ToString() == "Native land");
        dialog.EmitSignal(AcceptDialog.SignalName.Canceled); // "Abandon" is the dialog's Cancel action
        await runner.SimulateFrames(2);

        AssertThat(game.Colonies.Count).IsEqual(0);         // nothing founded
        AssertThat(game.Map.IsNativeOwned(tile)).IsTrue();  // still the natives'
    }

    [TestCase(Timeout = 60000)]
    public async Task NativeTributeDemand_Refuse_ClearsTheDemand_WithoutPaying()
    {
        // 86d3f62r5: the tribute-demand modal's "Refuse" branch was reachable but undriven by L3 (only Pay was). Same
        // setup as the pay test: garrison a stocked colony and enrage a brave beside it so End Turn raises a demand;
        // press Refuse → the demand clears and NOTHING is paid (gold and the colony store are untouched).
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);

        Colony founded = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!.Select(c => c.Id == founded.Id
            ? c with { Stores = new Dictionary<string, int> { ["model.goods.tobacco"] = 100 } }
            : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);

        game.SpawnUnit(game.Ruleset.Unit("model.unit.artillery"), colony.Position); // garrison → demands (not pillages)
        string nation = game.NativeSettlements.First().NationTypeId;
        Position adj = colony.Position.Neighbours().First(n => Free(game, n));
        game.SpawnUnit(game.Ruleset.Unit("model.unit.brave"), adj, nation);
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == nation))
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm);
        }

        controller.GetNode<Button>("UI/EndTurnButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(2);

        var panel = controller.GetNode<PanelContainer>("UI/NativeDemandPanel");
        AssertThat(panel.Visible).IsTrue();
        AssertThat(game.PendingDemand).IsNotNull();
        int goldBefore = game.HumanPlayer.Gold;
        int tobaccoBefore = colony.StoreOf("model.goods.tobacco");

        controller.GetNode<Button>("UI/NativeDemandPanel/VBox/Buttons/RefuseButton").EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(panel.Visible).IsFalse();                                         // the modal closed
        AssertThat(game.PendingDemand).IsNull();                                     // the demand was cleared
        AssertThat(game.HumanPlayer.Gold).IsEqual(goldBefore);                       // no gold paid
        AssertThat(colony.StoreOf("model.goods.tobacco")).IsEqual(tobaccoBefore);    // no goods paid
    }

    /// <summary>Teleports <paramref name="unit"/> to <paramref name="p"/> (internal Position setter, by reflection) and returns it — a fluent helper for the improvement-tile scans.</summary>
    private static Unit SetUnitAt(Unit unit, Position p)
    {
        typeof(Unit).GetProperty("Position")!.GetSetMethod(nonPublic: true)!.Invoke(unit, [p]);
        return unit;
    }

    /// <summary>The id of the first popup-menu item whose text equals <paramref name="text"/> (for firing "Centre here"/"Go to here").</summary>
    private static int ItemIdWithText(PopupMenu menu, string text)
    {
        for (int i = 0; i < menu.ItemCount; i++)
        {
            if (menu.GetItemText(i) == text)
            {
                return menu.GetItemId(i);
            }
        }
        throw new System.InvalidOperationException($"No popup-menu item with text '{text}'.");
    }

    /// <summary>Invokes an internal <see cref="GameMap"/> method (e.g. SetNativeOwner/ClearNativeOwner) by reflection — the presentation project can't call them directly.</summary>
    private static void InvokeMap(Game game, string method, params object[] args) =>
        typeof(GameMap).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game.Map, args);

    /// <summary>Chebyshev (king-move) distance between two tiles — the map's step metric (8-way movement).</summary>
    private static int Cheb(Position a, Position b) => System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    /// <summary>Every <see cref="Button"/> anywhere under <paramref name="root"/> (recursive, including internal children) — for finding a code-built dialog's custom buttons.</summary>
    private static IEnumerable<Button> DescendantButtons(Node root)
    {
        foreach (Node child in root.GetChildren(true))
        {
            if (child is Button b)
            {
                yield return b;
            }
            foreach (Button nested in DescendantButtons(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>The engine's own path from <paramref name="unit"/> to <paramref name="goal"/> (internal <c>Game.FindPath</c>), reached by reflection to confirm reachability in the goto-walk case.</summary>
    private static IReadOnlyList<Position> GamePath(Game game, Unit unit, Position goal) =>
        (IReadOnlyList<Position>)typeof(Game).GetMethod("FindPath", BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null, types: [typeof(Unit), typeof(Position)], modifiers: null)!.Invoke(game, [unit, goal])!;

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
