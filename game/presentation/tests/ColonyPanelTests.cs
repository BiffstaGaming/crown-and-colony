using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
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
