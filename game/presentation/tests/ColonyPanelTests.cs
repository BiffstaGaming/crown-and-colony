using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 tests for the interactive colony screen: staffing buildings, releasing
/// field workers, and choosing construction — all via the real UI controls.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ColonyPanelTests
{
    private const string Carpenter = "model.building.carpenterHouse";

    [TestCase(Timeout = 60000)]
    public async Task StaffButton_AssignsIdleColonist_ToTheWorkshop()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();

        // The founder starts in the fields: release via the panel's button.
        var release = FindButton(controller, "Release_");
        if (release is not null)
        {
            release.EmitSignal(BaseButton.SignalName.Pressed);
            await runner.SimulateFrames(1);
        }
        AssertThat(colony.IdleColonists).IsEqual(1);

        var staff = FindButton(controller, "Staff_carpenterHouse");
        AssertThat(staff).IsNotNull();
        staff!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(colony.BuildingWorkers[Carpenter]).IsEqual(1);
        AssertThat(colony.IdleColonists).IsEqual(0);

        // The rebuilt panel now offers an Unstaff button; using it frees the colonist.
        var unstaff = FindButton(controller, "Unstaff_carpenterHouse");
        AssertThat(unstaff).IsNotNull();
        unstaff!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colony.IdleColonists).IsEqual(1);
    }

    [TestCase(Timeout = 60000)]
    public async Task ConstructionDropdown_SetsTheBuildTarget_AndStopClearsIt()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();

        var options = controller.GetNode<PanelContainer>("UI/ColonyPanel")
            .FindChild("BuildOptions", recursive: true, owned: false) as OptionButton;
        AssertThat(options).IsNotNull();
        AssertThat(options!.ItemCount > 1).IsTrue();

        options.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        await runner.SimulateFrames(1);

        AssertThat(colony.CurrentBuild).IsNotNull();
        var buildables = game.Buildables(colony).ToList(); // after SetBuild the same item is no longer buildable… recompute via stop
        var stop = FindButton(controller, "StopBuild");
        AssertThat(stop).IsNotNull();
        stop!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colony.CurrentBuild).IsNull();
    }

    [TestCase(Timeout = 60000)]
    public async Task BuildMenu_OffersUnits_EnqueuesAndRemovesAWagonTrain()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();

        // The picker lists buildings first, then units — a unit (the wagon train) is genuinely offered.
        int buildings = game.Buildables(colony).Count();
        var units = game.BuildableUnits(colony).ToList();
        int wagonAt = units.FindIndex(u => u.Id == "model.unit.wagonTrain");
        AssertThat(wagonAt >= 0).IsTrue();

        var options = controller.GetNode<PanelContainer>("UI/ColonyPanel")
            .FindChild("BuildOptions", recursive: true, owned: false) as OptionButton;
        AssertThat(options).IsNotNull();
        options!.EmitSignal(OptionButton.SignalName.ItemSelected, (long)(1 + buildings + wagonAt));
        await runner.SimulateFrames(1);
        AssertThat(colony.BuildQueue.Contains("model.unit.wagonTrain")).IsTrue(); // a unit is queued via the UI

        // Remove the queued item via its row's ✕ button.
        var remove = FindButton(controller, "RemoveBuild_0");
        AssertThat(remove).IsNotNull();
        remove!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colony.BuildQueue.Count).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task BuildQueue_ReordersWithTheUpButton()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();

        // Two queued buildables (a building then a unit); re-open so both rows render.
        game.EnqueueBuild(colony, "model.building.warehouse");
        game.EnqueueBuild(colony, "model.unit.wagonTrain");
        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);
        AssertThat(colony.CurrentBuild).IsEqual("model.building.warehouse");

        var up = FindButton(controller, "Up_1"); // pull the second item to the front
        AssertThat(up).IsNotNull();
        up!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colony.CurrentBuild).IsEqual("model.unit.wagonTrain");
    }

    [TestCase(Timeout = 60000)]
    public async Task TileWorkPicker_AssignsAColonist_ToANonFoodTileGood()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();

        // The founder starts farming food — release it via the panel so there's an idle colonist (and the
        // per-tile work pickers appear).
        FindButton(controller, "Release_")!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colony.IdleColonists).IsEqual(1);

        // Find a free surrounding tile that can produce a NON-FOOD good (lumber / ore / cotton / furs / …).
        Position tile = colony.Position.Neighbours()
            .First(n => game.TileWorkOptions(n).Any(o => !game.Ruleset.Goods(o.GoodsId).IsFood));
        var opts = game.TileWorkOptions(tile);
        int optionIndex = opts.ToList().FindIndex(o => !game.Ruleset.Goods(o.GoodsId).IsFood);
        string goodsId = opts[optionIndex].GoodsId;

        // Drive that tile's picker → select the non-food good (item 0 is the "Work…" placeholder).
        var picker = controller.GetNode<PanelContainer>("UI/ColonyPanel")
            .FindChild($"Work_{tile.X}_{tile.Y}", recursive: true, owned: false) as OptionButton;
        AssertThat(picker).IsNotNull();
        picker!.EmitSignal(OptionButton.SignalName.ItemSelected, (long)(optionIndex + 1));
        await runner.SimulateFrames(1);

        // The idle colonist now works that tile producing the chosen non-food good.
        AssertThat(colony.TileWorkers.ContainsKey(tile)).IsTrue();
        AssertThat(colony.TileWorkers[tile]).IsEqual(goodsId);
        AssertThat(colony.IdleColonists).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task JoinButton_AddsAnAdjacentColonist_ToThePopulation()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();

        // A free colonist standing beside the colony, then re-open the panel so it lists the joinable unit.
        Position adj = FreeNeighbour(game, colony);
        Unit joiner = game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), adj);
        int joinerId = joiner.Id;
        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);

        var join = FindButton(controller, $"Join_{joinerId}");
        AssertThat(join).IsNotNull();
        join!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(colony.Population).IsEqual(2);
        AssertThat(game.Units.Any(u => u.Id == joinerId)).IsFalse(); // the colonist joined → off the map
    }

    [TestCase(Timeout = 60000)]
    public async Task LeaveButton_DetachesAFreeColonist_OntoTheMap()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();

        // Grow to population 2 (join a colonist) so one may leave; re-open so the Leave button renders.
        game.JoinColony(game.SpawnUnit(game.Ruleset.Unit("model.unit.freeColonist"), FreeNeighbour(game, colony)), colony);
        AssertThat(colony.Population).IsEqual(2);
        int onMapBefore = game.Units.Count(u => u.IsOnMap);
        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);

        var leave = FindButton(controller, "LeaveColony");
        AssertThat(leave).IsNotNull();
        leave!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(colony.Population).IsEqual(1);
        AssertThat(game.Units.Count(u => u.IsOnMap)).IsEqual(onMapBefore + 1); // a free colonist appeared on the map
        AssertThat(game.Units.Any(u => u.IsOnMap && u.Position == colony.Position && u.Type.Id == "model.unit.freeColonist")).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    public async Task ArmButton_EquipsAColonistAsASoldier_UsingTheColonysMuskets()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony founded = game.FoundColony(game.Units[0]);

        // Stock 50 muskets and stand a free colonist in the colony, via the save layer (Colony.AddGoods + Unit.OwnerId
        // are internal to GameLogic), then reload into the controller.
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!.Select(c => c.Id == founded.Id
            ? c with { Stores = new Dictionary<string, int> { ["model.goods.muskets"] = 50 } } : c).ToList();
        int uid = game.Units.Max(u => u.Id) + 1;
        var colonist = new SavedUnit(uid, "model.unit.freeColonist", founded.Position.X, founded.Position.Y, 0, OwnerId: 0);
        game = (save with { Colonies = colonies, Units = save.Units.Append(colonist).ToList() }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);

        var arm = FindButton(controller, $"Equip_{uid}_soldier");
        AssertThat(arm).IsNotNull();
        arm!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        AssertThat(game.Units.First(u => u.Id == uid).RoleId).IsEqual("model.role.soldier"); // armed
        AssertThat(colony.StoreOf("model.goods.muskets")).IsEqual(0);                          // 50 muskets consumed
    }

    [TestCase(Timeout = 60000)]
    public async Task ColonyScreen_DrawsTheSurroundingTilesAsArt_AndTheBuildingsAsAGrid()
    {
        (_, GameController controller, _, Colony colony) = await OpenPanel();
        PanelContainer panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");

        // The surrounding tiles are drawn as FreeCol terrain diamonds (real art, not text) in the isometric view.
        var tilesView = panel.FindChild("TilesView", recursive: true, owned: false) as Control;
        AssertThat(tilesView).IsNotNull();
        int terrainArt = tilesView!.FindChildren("*", recursive: true, owned: false)
            .OfType<TextureRect>().Count(t => t.Texture is not null);
        AssertThat(terrainArt >= 9).IsTrue(); // at least the 3×3 ring of terrain tiles is rendered as textures

        // The colony's buildings render as a 4-wide grid of building-image cells.
        var buildings = panel.FindChild("BuildingsGrid", recursive: true, owned: false) as GridContainer;
        AssertThat(buildings).IsNotNull();
        AssertThat(buildings!.Columns).IsEqual(4);
        AssertThat(buildings.GetChildCount()).IsEqual(colony.Buildings.Count);
    }

    [TestCase(Timeout = 60000)]
    public async Task ClickingAWorkedTileThenAFreeTile_MovesTheColonist()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();
        PanelContainer panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");

        // The founder is auto-assigned to a food tile. Click it to pick the colonist up, then click a free,
        // producible neighbour to drop it there (click-to-move).
        Position from = colony.TileWorkers.Keys.First();
        // A free, producible neighbour the colony can actually work — excludes sea tiles, which need Docks (a fresh
        // colony has none), so the click-to-move lands on workable land.
        Position to = colony.Position.Neighbours().First(n =>
            !colony.TileWorkers.ContainsKey(n) && game.ColonyCanWorkTile(colony, n) && game.TileWorkOptions(n).Count > 0);

        TileButton(panel, from).EmitSignal(BaseButton.SignalName.Pressed); // pick up
        await runner.SimulateFrames(1);
        TileButton(panel, to).EmitSignal(BaseButton.SignalName.Pressed);   // drop → move
        await runner.SimulateFrames(1);

        AssertThat(colony.TileWorkers.ContainsKey(from)).IsFalse(); // left the old tile
        AssertThat(colony.TileWorkers.ContainsKey(to)).IsTrue();    // now works the new one
        AssertThat(colony.IdleColonists).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task SonsOfLibertyBar_ShowsRebelsRoyalistsAndBonus_FromColonyLiberty()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony founded = game.FoundColony(game.Units[0]);

        // Inject a known liberty + population via the save layer (Colony.Liberty is internal to GameLogic):
        // pop 5 + liberty 600 → SoL 60% → 3 rebels, 2 royalists, +1 production bonus.
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!
            .Select(c => c.Id == founded.Id ? c with { Population = 5, Liberty = 600 } : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);
        PanelContainer panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");

        AssertThat(LabelText(panel, "RebelCount")).IsEqual("Rebels: 3");
        AssertThat(LabelText(panel, "RebelPercent")).IsEqual("60%");
        AssertThat(LabelText(panel, "PopulationCount")).IsEqual("Population: 5");
        AssertThat(LabelText(panel, "ProductionBonus")).IsEqual("Bonus: +1");
        AssertThat(LabelText(panel, "RoyalistCount")).IsEqual("Royalists: 2");
        AssertThat(LabelText(panel, "RoyalistPercent")).IsEqual("40%");
    }

    [TestCase(Timeout = 60000)]
    public async Task TileBadges_ShowTheSonsOfLibertyBoostedYield()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony founded = game.FoundColony(game.Units[0]);
        Position worked = founded.TileWorkers.Keys.First();
        int boosted = game.TileYield(worked, founded.TileWorkers[worked]) + 2; // base + the +2 SoL bonus

        // Full Sons of Liberty (liberty = 200·pop → SoL 100 → +2) via the save layer (Colony.Liberty is internal).
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!
            .Select(c => c.Id == founded.Id ? c with { Liberty = Colony.LibertyPerRebel * c.Population } : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);
        AssertThat(colony.ProductionBonus).IsEqual(2);

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);
        var tilesView = controller.GetNode<PanelContainer>("UI/ColonyPanel")
            .FindChild("TilesView", recursive: true, owned: false) as Control;
        bool badgeShowsBoosted = tilesView!.FindChildren("*", recursive: true, owned: false)
            .OfType<Label>().Any(l => l.Text.EndsWith($" {boosted}")); // the worked-tile badge shows the effective yield
        AssertThat(badgeShowsBoosted).IsTrue();
    }

    [TestCase(Timeout = 60000)]
    public async Task CargoSection_LoadsAndUnloadsGoods_AndSafelyRefusesAFullHoldOrEmptyWarehouse()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony founded = game.FoundColony(game.Units[0]);

        // Stock the colony with 250 ore and stand a player-owned wagon train (a 2-hold land carrier — no coastal
        // water tile needed for a deterministic test) on the colony tile, via the save layer (Colony.AddGoods +
        // Unit.OwnerId are internal to GameLogic), then reload into the controller. A wagon train exercises the same
        // IsCarrier load/unload path a ship does.
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!.Select(c => c.Id == founded.Id
            ? c with { Stores = new Dictionary<string, int> { ["model.goods.ore"] = 250 } } : c).ToList();
        int wagonId = game.Units.Max(u => u.Id) + 1;
        var wagon = new SavedUnit(wagonId, "model.unit.wagonTrain", founded.Position.X, founded.Position.Y, 0, OwnerId: 0);
        game = (save with { Colonies = colonies, Units = save.Units.Append(wagon).ToList() }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);
        Unit carrier = game.Units.First(u => u.Id == wagonId);

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);

        // Load a lot (100) of ore via the colony screen's per-carrier picker (item 0 is the "Load goods…" placeholder;
        // ore is the only stored good, so item 1).
        var loadPicker = controller.GetNode<PanelContainer>("UI/ColonyPanel")
            .FindChild($"Load_{wagonId}", recursive: true, owned: false) as OptionButton;
        AssertThat(loadPicker).IsNotNull();
        loadPicker!.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        await runner.SimulateFrames(1);
        AssertThat(carrier.CargoOf("model.goods.ore")).IsEqual(100);  // a lot is now aboard
        AssertThat(colony.StoreOf("model.goods.ore")).IsEqual(150);   // and gone from the warehouse

        // Unload it back via the cargo row's Unload button → it returns to the warehouse.
        var unload = FindButton(controller, $"Unload_{wagonId}_ore");
        AssertThat(unload).IsNotNull();
        unload!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(carrier.CargoOf("model.goods.ore")).IsEqual(0);    // hold emptied
        AssertThat(colony.StoreOf("model.goods.ore")).IsEqual(250);   // warehouse whole again

        // Fill the wagon's 2 holds (200 ore) directly through the thin command, then attempt a third lot: the engine's
        // hold-full guard refuses it and the command swallows the InvalidMoveException (no throw to the UI).
        controller.LoadColonyCargo(carrier, colony, "model.goods.ore", 100);
        controller.LoadColonyCargo(carrier, colony, "model.goods.ore", 100);
        await runner.SimulateFrames(1);
        AssertThat(carrier.CargoOf("model.goods.ore")).IsEqual(200);  // both holds full
        AssertThat(colony.StoreOf("model.goods.ore")).IsEqual(50);
        controller.LoadColonyCargo(carrier, colony, "model.goods.ore", 100); // would need a third hold — refused, not thrown
        await runner.SimulateFrames(1);
        AssertThat(carrier.CargoOf("model.goods.ore")).IsEqual(200);  // unchanged — the guard held
        AssertThat(colony.StoreOf("model.goods.ore")).IsEqual(50);

        // Out-of-stock: unload everything, then try to load more ore than the warehouse holds — also safely refused.
        controller.UnloadColonyCargo(carrier, colony, "model.goods.ore", 200);
        await runner.SimulateFrames(1);
        AssertThat(colony.StoreOf("model.goods.ore")).IsEqual(250);
        controller.LoadColonyCargo(carrier, colony, "model.goods.ore", 999); // more than is stored — refused, not thrown
        await runner.SimulateFrames(1);
        AssertThat(carrier.CargoOf("model.goods.ore")).IsEqual(0);    // nothing loaded
        AssertThat(colony.StoreOf("model.goods.ore")).IsEqual(250);   // warehouse untouched
    }

    [TestCase(Timeout = 60000)]
    public async Task CustomHouseExportSection_TogglesAGood_SetsRetain_AndEndTurnAutoSells()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony founded = game.FoundColony(game.Units[0]);

        // Give the colony a custom house + 250 ore in store, via the save layer (Colony.AddBuilding/AddGoods are
        // internal to GameLogic), then reload into the controller — exactly the seeding the cargo/arm tests use.
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!.Select(c => c.Id == founded.Id
            ? c with
            {
                Stores = new Dictionary<string, int> { ["model.goods.ore"] = 250 },
                Buildings = c.Buildings!.Append("model.building.customHouse").ToList(),
            }
            : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);
        AssertThat(game.ColonyHasCustomHouse(colony)).IsTrue();

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);
        PanelContainer panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");

        // Set the retain level to 50 first (so the auto-sell ships the surplus 250 − 50 = 200), then flag ore Exported.
        var retain = panel.FindChild("Retain_ore", recursive: true, owned: false) as SpinBox;
        AssertThat(retain).IsNotNull();
        retain!.EmitSignal(Range.SignalName.ValueChanged, 50.0);
        await runner.SimulateFrames(1);
        AssertThat(colony.ExportOf("model.goods.ore").ExportLevel).IsEqual(50);

        var toggle = panel.FindChild("Export_ore", recursive: true, owned: false) as CheckBox;
        AssertThat(toggle).IsNotNull();
        toggle!.EmitSignal(BaseButton.SignalName.Toggled, true);
        await runner.SimulateFrames(1);

        // The panel wired the toggle straight through to the engine: ore is now flagged for custom-house export.
        AssertThat(colony.ExportOf("model.goods.ore").Exported).IsTrue();
        AssertThat(colony.ExportOf("model.goods.ore").ExportLevel).IsEqual(50);

        // End Turn: the custom house auto-sells the surplus over the retain level (250 → 50) for gold.
        int goldBefore = game.Gold;
        game.EndTurn();
        AssertThat(colony.StoreOf("model.goods.ore")).IsEqual(50); // sold down to the retain level
        AssertThat(game.Gold > goldBefore).IsTrue();               // the sale credited the treasury

        // Un-ticking the good turns the export back off (and, at the default level, forgets the entry).
        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);
        panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");
        var toggleOff = panel.FindChild("Export_ore", recursive: true, owned: false) as CheckBox;
        toggleOff!.EmitSignal(BaseButton.SignalName.Toggled, false);
        await runner.SimulateFrames(1);
        AssertThat(colony.ExportOf("model.goods.ore").Exported).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    public async Task CustomHouseExportSection_IsHidden_WithoutACustomHouse()
    {
        (_, GameController controller, Game game, Colony colony) = await OpenPanel();
        AssertThat(game.ColonyHasCustomHouse(colony)).IsFalse(); // a fresh colony has no custom house

        // No export controls render for a colony that can't auto-sell — the section is gated on the custom house.
        var toggle = controller.GetNode<PanelContainer>("UI/ColonyPanel")
            .FindChild("Export_ore", recursive: true, owned: false) as CheckBox;
        AssertThat(toggle).IsNull();
    }

    [TestCase(Timeout = 60000)]
    public async Task ExpertOnATile_RendersItsExpertSprite_NotTheFreeColonist()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony founded = game.FoundColony(game.Units[0]);

        // Retype the founder's auto-assigned tile worker into an expert lumberjack via the save layer (the worker-type
        // overlay is internal to GameLogic), then reload — the colony screen must draw THAT type's sprite (86d3f674f).
        Position worked = founded.TileWorkers.Keys.First();
        string good = founded.TileWorkers[worked];
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!.Select(c => c.Id == founded.Id
            ? c with { Workers = new[] { new SavedWorker(worked.X, worked.Y, good, "model.unit.expertLumberJack") } }
            : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);
        AssertThat(colony.WorkerTypeAt(worked)).IsEqual("model.unit.expertLumberJack");

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);
        var tilesView = controller.GetNode<PanelContainer>("UI/ColonyPanel")
            .FindChild("TilesView", recursive: true, owned: false) as Control;
        AssertThat(tilesView).IsNotNull();

        // The expert-lumberjack sprite is rendered on a worked tile (not the free-colonist sprite).
        Texture2D? expert = UnitMarker.ResolveTexture("expertLumberJack", UnitSpriteCatalog.DefaultRole)
            ?? ColonyArt.UnitIcon("expertLumberJack");
        Texture2D? free = UnitMarker.ResolveTexture("freeColonist", UnitSpriteCatalog.DefaultRole)
            ?? ColonyArt.UnitIcon("freeColonist");
        AssertThat(expert).IsNotNull();
        var tileTextures = tilesView!.FindChildren("*", recursive: true, owned: false)
            .OfType<TextureRect>().Select(t => t.Texture).ToList();
        AssertThat(tileTextures.Contains(expert!)).IsTrue();   // the expert's own art is drawn
        AssertThat(tileTextures.Contains(free!)).IsFalse();    // and NOT the hardcoded free-colonist sprite
    }

    [TestCase(Timeout = 60000)]
    public async Task BuildingCell_RendersItsWorkerUnits_IncludingAnExpertSprite()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony founded = game.FoundColony(game.Units[0]);

        // Staff the carpenter's house with a master carpenter via the save layer (set the building worker count + its
        // non-free occupant type), then reload — the building cell must draw that expert's per-slot portrait (86d3f6754).
        const string Carp = "model.building.carpenterHouse";
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!.Select(c => c.Id == founded.Id
            ? c with
            {
                Population = 2, // a tile founder + the building worker
                BuildingWorkers = new Dictionary<string, int> { [Carp] = 1 },
                BuildingWorkerTypes = new Dictionary<string, IReadOnlyList<string>> { [Carp] = new[] { "model.unit.masterCarpenter" } },
            }
            : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);
        AssertThat(colony.BuildingOccupants(Carp).Contains("model.unit.masterCarpenter")).IsTrue();

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);
        PanelContainer panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");

        // A clickable per-slot worker portrait exists for the building, drawing the master-carpenter sprite.
        var portrait = panel.FindChild($"Worker_carpenterHouse_0", recursive: true, owned: false) as Button;
        AssertThat(portrait).IsNotNull();
        Texture2D? master = UnitMarker.ResolveTexture("masterCarpenter", UnitSpriteCatalog.DefaultRole)
            ?? ColonyArt.UnitIcon("masterCarpenter");
        AssertThat(master).IsNotNull();
        bool drawsExpert = portrait!.FindChildren("*", recursive: true, owned: false)
            .OfType<TextureRect>().Any(t => t.Texture == master);
        AssertThat(drawsExpert).IsTrue();

        // Clicking the portrait removes the building worker (the FreeCol click-to-remove gesture).
        portrait.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colony.BuildingWorkers.GetValueOrDefault(Carp)).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task ProductionOverview_RendersPerGoodRows_FromTheSummaryOracle()
    {
        (_, GameController controller, Game game, Colony colony) = await OpenPanel();
        PanelContainer panel = controller.GetNode<PanelContainer>("UI/ColonyPanel");

        var overview = panel.FindChild("ProductionOverview", recursive: true, owned: false) as Control;
        AssertThat(overview).IsNotNull();

        // Every good the oracle reports as touched gets a named Production_{good} row in the overview.
        var summary = game.ColonyProductionSummary(colony);
        foreach (var kv in summary)
        {
            if (kv.Value.Produced == 0 && kv.Value.Consumed == 0)
            {
                continue;
            }
            string shortName = kv.Key[(kv.Key.LastIndexOf('.') + 1)..];
            var row = overview!.FindChild($"Production_{shortName}", recursive: true, owned: false) as Control;
            AssertThat(row).IsNotNull();
        }

        // Food is always shown (the colonists eat it) and bells (the town hall makes them) — the two staples.
        AssertThat(overview!.FindChild("Production_food", recursive: true, owned: false)).IsNotNull();
        AssertThat(overview.FindChild("Production_bells", recursive: true, owned: false)).IsNotNull();
    }

    // ── Phase 2: drag-and-drop worker assignment ───────────────────────────────────────────────────────────────
    // L3 drives the drag CALLBACKS directly (Godot can't synthesise a real mouse-drag headlessly — same limitation the
    // EuropePanel drag tests work around): get an idle chip's payload via source._GetDragData(pos), assert the target's
    // target._CanDropData(pos, data), call target._DropData(pos, data), then assert the ENGINE state changed. A position
    // of Vector2.Zero is fine — the colony sources/targets ignore the position (whole-control hit). The existing
    // click-to-assign tests above stay green (the drag is additive).

    [TestCase(Timeout = 60000)]
    public async Task DragIdleColonistOntoFreeTile_AssignsTheWorker()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();

        // Free the founder so there's an idle colonist (and an idle chip appears).
        FindButton(controller, "Release_")!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colony.IdleColonists).IsEqual(1);

        // A free, producible neighbour the colony can actually work (excludes sea tiles — a fresh colony has no docks).
        Position tile = colony.Position.Neighbours().First(n =>
            !colony.TileWorkers.ContainsKey(n) && game.ColonyCanWorkTile(colony, n) && game.TileWorkOptions(n).Count > 0);

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "IdleColonist_0")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, $"TileDrop_{tile.X}_{tile.Y}")!;
        AssertThat(source).IsNotNull();
        AssertThat(target).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsTrue(); // a free, workable tile accepts the worker
        target._DropData(Vector2.Zero, data);
        await runner.SimulateFrames(1);

        AssertThat(colony.TileWorkers.ContainsKey(tile)).IsTrue(); // the colony now works that tile
        AssertThat(colony.IdleColonists).IsEqual(0);               // the idle colonist took the job
    }

    [TestCase(Timeout = 60000)]
    public async Task DragIdleColonistOntoAlreadyWorkedTile_IsRefused()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony founded = game.FoundColony(game.Units[0]);

        // Grow to population 2 with only the founder's tile worker assigned (the save layer keeps the second colonist
        // idle — JoinColony would auto-seat it), so there is BOTH an idle chip AND an already-worked tile to refuse.
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!
            .Select(c => c.Id == founded.Id ? c with { Population = 2 } : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);
        AssertThat(colony.IdleColonists).IsEqual(1);
        Position worked = colony.TileWorkers.Keys.First(); // the founder's auto-assigned (already worked) tile

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "IdleColonist_0")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, $"TileDrop_{worked.X}_{worked.Y}")!;
        AssertThat(source).IsNotNull();
        AssertThat(target).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsFalse(); // an already-worked tile is refused

        target._DropData(Vector2.Zero, data); // even forced, the re-check no-ops
        await runner.SimulateFrames(1);
        AssertThat(colony.IdleColonists).IsEqual(1); // unchanged — no second worker seated on the worked tile
    }

    [TestCase(Timeout = 60000)]
    public async Task DragIdleColonistOntoBuilding_StaffsIt()
    {
        (ISceneRunner runner, GameController controller, Game game, Colony colony) = await OpenPanel();

        // Free the founder so there's an idle colonist (and an idle chip appears).
        FindButton(controller, "Release_")!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
        AssertThat(colony.IdleColonists).IsEqual(1);
        AssertThat(colony.BuildingWorkers.GetValueOrDefault(Carpenter)).IsEqual(0);

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "IdleColonist_0")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, "BuildingDrop_carpenterHouse")!;
        AssertThat(source).IsNotNull();
        AssertThat(target).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsTrue(); // a building with a free workplace accepts the worker
        target._DropData(Vector2.Zero, data);
        await runner.SimulateFrames(1);

        AssertThat(colony.BuildingWorkers[Carpenter]).IsEqual(1); // staffed via the drop
        AssertThat(colony.BuildingOccupants(Carpenter).Count).IsEqual(1);
        AssertThat(colony.IdleColonists).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task DragIdleColonistOntoFullBuilding_IsRefused()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        Colony founded = game.FoundColony(game.Units[0]);

        // Fully staff the carpenter's house to its every workplace and add a spare idle colonist, via the save layer,
        // then reload — a chip exists but the full building must refuse the drop.
        int workplaces = game.Ruleset.Building(Carpenter).Workplaces;
        SaveGame save = SaveGame.From(game);
        var colonies = save.Colonies!.Select(c => c.Id == founded.Id
            ? c with
            {
                Population = 1 + workplaces + 1, // the founder's tile worker + a full building + one idle
                BuildingWorkers = new Dictionary<string, int> { [Carpenter] = workplaces },
            }
            : c).ToList();
        game = (save with { Colonies = colonies }).Restore(game.Ruleset);
        SetGame(controller, game);
        Colony colony = game.Colonies.First(c => c.Id == founded.Id);
        AssertThat(colony.IdleColonists).IsEqual(1);
        AssertThat(game.CheckAssignBuildingWork(colony, Carpenter).Allowed).IsFalse(); // already full

        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);

        EuropeDragSource source = FindControl<EuropeDragSource>(controller, "IdleColonist_0")!;
        EuropeDropTarget target = FindControl<EuropeDropTarget>(controller, "BuildingDrop_carpenterHouse")!;
        AssertThat(source).IsNotNull();
        AssertThat(target).IsNotNull();

        Variant data = source._GetDragData(Vector2.Zero);
        AssertThat(target._CanDropData(Vector2.Zero, data)).IsFalse(); // full building — refused

        target._DropData(Vector2.Zero, data); // forced drop no-ops
        await runner.SimulateFrames(1);
        AssertThat(colony.BuildingWorkers[Carpenter]).IsEqual(workplaces); // unchanged — the guard held
        AssertThat(colony.IdleColonists).IsEqual(1);
    }

    private static string LabelText(PanelContainer panel, string name) =>
        ((Label)panel.FindChild(name, recursive: true, owned: false)).Text;

    private static Button TileButton(PanelContainer panel, Position tile) =>
        (Button)panel.FindChild($"Tile_{tile.X}_{tile.Y}", recursive: true, owned: false);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, game);

    private static Position FreeNeighbour(Game game, Colony colony) =>
        colony.Position.Neighbours().First(n =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.ColonyAt(n) is null && game.NativeSettlementAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));

    private static async Task<(ISceneRunner, GameController, Game, Colony)> OpenPanel()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(424242);
        await runner.SimulateFrames(2);

        var game = (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
        Colony colony = game.FoundColony(game.Units[0]);
        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(1);
        return (runner, controller, game, colony);
    }

    private static Button? FindButton(GameController controller, string namePrefix) =>
        controller.GetNode<PanelContainer>("UI/ColonyPanel")
            .FindChildren("*", recursive: true, owned: false)
            .OfType<Button>()
            .FirstOrDefault(b => b.Name.ToString().StartsWith(namePrefix));

    /// <summary>Finds the first drag-source / drop-target control of type <typeparamref name="T"/> whose name starts with <paramref name="namePrefix"/> (Phase 2 drag-drop).</summary>
    private static T? FindControl<T>(GameController controller, string namePrefix) where T : Node =>
        controller.GetNode<PanelContainer>("UI/ColonyPanel")
            .FindChildren("*", recursive: true, owned: false)
            .OfType<T>()
            .FirstOrDefault(c => c.Name.ToString().StartsWith(namePrefix));
}
