using System.Linq;
using System.Threading.Tasks;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using CrownAndColony.Presentation;
using GdUnit4;
using static GdUnit4.Assertions;

namespace CrownAndColony.Presentation.Tests;

/// <summary>
/// L3 bridge test (86d3fy56v) for the New-Game <b>read → clear → thread</b> seam: the setup dials ride
/// <c>NewGameDialog.Pending*</c> statics across the menu→game scene change, and <c>GameController.NewGame()</c> must
/// (a) consume them into the started game and (b) clear every static so a later default new game can't inherit a stale
/// pick. <c>MainMenuTests</c> covers the dialog <em>write</em> side; this suite boots the real game scene and asserts
/// the <em>consume</em> side end-to-end through the new read-only <see cref="GameController.CurrentGame"/> accessor.
/// </summary>
/// <remarks>
/// All cross-scene statics are reset in ONE place (<see cref="ResetPendingStatics"/>) before and after every test —
/// when a new <c>Pending*</c> static is added (e.g. difficulty-option overrides or an imported map), extend that one
/// helper and, if the dial should be exercised here, the arrange block of the bridge test.
/// </remarks>
[TestSuite]
[RequireGodotRuntime]
public class NewGameBridgeTests
{
    private const string GameScene = "res://scenes/main.tscn";

    /// <summary>
    /// The single clean-slate list of every cross-scene new-game static — the GameController-owned picks and the
    /// NewGameDialog-owned setup dials. Statics survive between tests in the shared Godot process, so a stale value
    /// here would leak one test's picks into the next suite's "default" game.
    /// </summary>
    private static void ResetPendingStatics()
    {
        GameController.PendingLoadPath = null;
        GameController.PendingWorldSize = null;
        GameController.PendingLandMass = null;
        GameController.PendingDifficulty = null;
        GameController.PendingMapSource = null;
        GameController.PendingLandStyle = null;
        GameController.PendingNation = null;
        GameController.PendingVictoryConditions = null;
        GameController.PendingFogOfWar = null;
        GameController.PendingCustomIgnoreBoycott = null;
        GameController.PendingVariant = null;
        NewGameDialog.PendingRivalCount = null;
        NewGameDialog.PendingStartYear = null;
        NewGameDialog.PendingMapOptions = null;
        NewGameDialog.PendingRumourNumber = null;
        NewGameDialog.PendingNationalAdvantages = null;
        NewGameDialog.PendingDifficultyOverrides = null; // custom difficulty (86d3fq0x7)
        NewGameDialog.PendingImportedMap = null;         // imported scenario map (86d3fq1cg)
    }

    [BeforeTest]
    public void CleanSlateBefore() => ResetPendingStatics();

    [AfterTest]
    public void CleanSlateAfter() => ResetPendingStatics();

    [TestCase]
    public async Task NewGame_ConsumesTheSetupDialStatics_ClearsThem_AndThreadsThemIntoTheStartedGame()
    {
        // Arrange: the five setup dials a player could change on the New-Game dialog (non-default values that are
        // observable on the started game). The dialog write side is covered by MainMenuTests — here the statics are
        // set directly, exactly as the dialog's Start button leaves them.
        NewGameDialog.PendingRivalCount = 2;                                                    // classic default is 3
        NewGameDialog.PendingStartYear = 1600;                                                  // classic default is 1492
        NewGameDialog.PendingMapOptions = MapGenerationOptions.Classic with { MountainNumber = 12 }; // default 10
        NewGameDialog.PendingRumourNumber = 18;                                                 // "many"; default 35
        NewGameDialog.PendingNationalAdvantages = NationalAdvantages.None;                      // default Selectable

        // Act: booting the game scene runs GameController._Ready → NewGame(), the read-clear-thread seam under test.
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        // Assert (clear): every consumed dial static is nulled, so a later default new game can't inherit the picks.
        AssertThat(NewGameDialog.PendingRivalCount).IsNull();
        AssertThat(NewGameDialog.PendingStartYear).IsNull();
        AssertThat(NewGameDialog.PendingMapOptions).IsNull();
        AssertThat(NewGameDialog.PendingRumourNumber).IsNull();
        AssertThat(NewGameDialog.PendingNationalAdvantages).IsNull();

        // Assert (thread): the picks reached the running game — read through the public CurrentGame seam.
        Game? game = controller.CurrentGame;
        AssertThat(game).IsNotNull();
        AssertThat(game!.CurrentYear).IsEqual(1600); // start-year dial → Ruleset.WithStartingYear → turn 1 = 1600
        // Rival-count dial → Game.New(foreignPowerCount: 2) → the human + 2 rivals on the European side.
        AssertThat(game.Players.Count(p => p.PlayerType != PlayerType.Native)).IsEqual(3);
    }

    [TestCase]
    public async Task NewGame_WithNoPendingStatics_StartsTheClassicDefaultGame()
    {
        // The null path: no picks (the [BeforeTest] clean slate) → the classic defaults (start 1492, 3 rivals). Guards
        // the "absent dial is byte-identical" contract from the consume side.
        ISceneRunner runner = ISceneRunner.Load(GameScene);
        await runner.SimulateFrames(2);
        var controller = (GameController)runner.Scene();

        Game? game = controller.CurrentGame;
        AssertThat(game).IsNotNull();
        AssertThat(game!.CurrentYear).IsEqual(1492);
        AssertThat(game.Players.Count(p => p.PlayerType != PlayerType.Native)).IsEqual(4); // human + classic 3 rivals
    }
}
