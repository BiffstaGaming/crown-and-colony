using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
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
}
