using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Units;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L4 visual goldens for the in-game management screens — the <see cref="ColonyPanel"/> and the
/// <see cref="EuropePanel"/> (docs/TESTING.md). Both are driven to a fully deterministic state (seed 424242, plus a
/// fixed injected Europe save) and the whole 1024×600 viewport is diffed against a committed golden via the shared
/// <see cref="GoldenAssert"/>, exactly like the menu goldens (<see cref="MenuGoldenTests"/>). The same looser text
/// tolerance absorbs cross-platform font antialiasing (the Cardo UI font is bundled, so glyphs are otherwise stable).
/// Regenerate intentionally with the env var <c>GOLDEN_UPDATE=1</c> and commit the new PNGs.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UiPanelGoldenTests
{
    private const ulong GoldenSeed = 424242;
    private static readonly Vector2I CaptureSize = new(1024, 600);
    /// <summary>Looser fraction than the map goldens — text-heavy panels accumulate more cross-platform font-antialiasing noise (matches <see cref="MenuGoldenTests"/>).</summary>
    private const double TextTolerance = 0.02;

    [TestCase(Timeout = 60000)]
    public async Task ColonyPanel_FreshColony_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.GetWindow().Size = CaptureSize;
        controller.StartNewGame(GoldenSeed);
        await runner.SimulateFrames(2);

        // Found a colony where the starting unit stands (the L3 colony tests' deterministic fixture) and open its
        // management screen. The colony panel paints an opaque parchment background over the whole viewport, so a
        // full-frame capture is stable regardless of the map drawn behind the UI layer.
        Game game = GameOf(controller);
        Colony colony = game.FoundColony(game.Units[0]);
        controller.OpenColonyPanel(colony);
        await runner.SimulateFrames(4); // let the deferred rebuild + layout settle

        // L4 golden (86d3f69e9): the reworked colony panel (production overview + per-slot worker portraits) diffed
        // against the committed Linux-CI baseline, regenerated via the GOLDEN_UPDATE=1 workflow_dispatch on the CI runner.
        GoldenAssert.Assert("colony-panel-seed424242", controller.GetViewport().GetTexture().GetImage(), TextTolerance);
    }

    [TestCase(Timeout = 60000)]
    public async Task EuropePanel_DockAndShip_MatchesGolden()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.GetWindow().Size = CaptureSize;
        controller.StartNewGame(GoldenSeed); // the deterministic map drawn (dimmed) behind the centred dialog
        await runner.SimulateFrames(2);

        // Inject a fixed Europe state so every dynamic section of the spatial harbour layout renders deterministically:
        // a treasury, two ships in port (one carrying a goods stack, one with two stacks — so the hold-slot boxes show
        // goods icons + amounts) and two colonists on the dock (the portrait chips). Mirrors the L3 EuropePanel fixture
        // (built through the public save layer), so the golden never depends on having played turns to reach Europe.
        Game game = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 2,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean"],
            Units =
            [
                // A caravel carrying a goods stack (so a hold slot shows the goods icon + amount); a galleon with two
                // goods stacks; two colonists on the dock (portrait chips); so the cards/holds/icons are all visible.
                new SavedUnit(1, "model.unit.caravel", 1, 0, 12, (int)UnitLocation.InEurope, 0,
                    new System.Collections.Generic.Dictionary<string, int> { ["model.goods.sugar"] = 100 }),
                new SavedUnit(3, "model.unit.galleon", 1, 0, 12, (int)UnitLocation.InEurope, 0,
                    new System.Collections.Generic.Dictionary<string, int> { ["model.goods.tobacco"] = 100, ["model.goods.ore"] = 50 }),
                new SavedUnit(2, "model.unit.freeColonist", 0, 0, 3, (int)UnitLocation.InEurope),
                new SavedUnit(4, "model.unit.expertOreMiner", 0, 0, 3, (int)UnitLocation.InEurope),
                // Two in-transit ships so the harbour's in-transit lanes render: one crossing towards Europe ("expected
                // soon"), one bound for the New World — exercising the ShipsSailing* oracles in the layout.
                new SavedUnit(5, "model.unit.caravel", 0, 0, 12, (int)UnitLocation.SailingToEurope, 2),
                new SavedUnit(6, "model.unit.galleon", 0, 0, 12, (int)UnitLocation.SailingToNewWorld, 1),
            ],
            Explored = [],
            Gold = 5000,
        }.Restore(GameLogic.Specification.Ruleset.LoadClassic());
        SetGame(controller, game);

        controller.OpenEuropePanel();
        await runner.SimulateFrames(4);

        // L4 golden (86d3f69e9): the reworked zoned-harbour Europe panel (header + recruit/train/purchase + goods market
        // + ships-in-port hold-slot cards + sail + docks) diffed against the committed Linux-CI baseline, regenerated via
        // the GOLDEN_UPDATE=1 workflow_dispatch on the CI runner.
        GoldenAssert.Assert("europe-panel", controller.GetViewport().GetTexture().GetImage(), TextTolerance);
    }

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, game);
}
