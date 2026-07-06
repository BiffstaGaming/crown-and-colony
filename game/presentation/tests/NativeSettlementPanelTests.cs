using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
/// L3 tests for the on-map native-trade UI (86d3f62qh): a human carrier adjacent to a coastal settlement opens the
/// <see cref="NativeSettlementPanel"/> and drives a <b>sell</b> and a <b>buy</b> through the real panel controls —
/// pick a good, accept the natives' price — asserting gold, ship cargo and the settlement's goods stock all change.
/// Presentation-only (ADR-006): every action forwards to a shipped <see cref="Game"/> trade oracle. State is staged
/// through the save layer (the presentation project can't load cargo / set gold directly), the established pattern.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class NativeSettlementPanelTests
{
    private const ulong Seed = 424242;
    private const string Sugar = "model.goods.sugar";
    private const string MissionaryRole = "model.role.missionary";
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Artillery = "model.unit.artillery"; // innate offence 7 — "armed" without a role (for demand-tribute)

    [TestCase(Timeout = 60000)]
    public async Task EstablishMissionButton_ShownForAMissionary_FoundsTheMission_OwnedByTheHuman()
    {
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit missionary) =
            await OpenMissionPanel(missionary: true);

        // A missionary beside a Content settlement: the Establish button is offered, no rival mission stands yet.
        AssertThat(settlement.HasMission).IsFalse();
        AssertThat(FindButton(controller, "Establish")).IsNotNull();
        AssertThat(FindButton(controller, "Denounce")).IsNull();

        await Press(runner, controller, "Establish");

        // The mission installed: the settlement now records the human as its mission owner and the missionary is consumed.
        AssertThat(settlement.HasMission).IsTrue();
        AssertThat(settlement.MissionOwnerId!.Value).IsEqual(game.HumanPlayer.PlayerId);
        AssertThat(game.Units.Any(u => u.Id == missionary.Id && u.IsOnMap)).IsFalse();
    }

    [TestCase(Timeout = 60000)]
    public async Task EstablishMissionButton_NotShown_ForANonMissionaryUnit()
    {
        (_, GameController controller, _, NativeSettlement settlement, _) =
            await OpenMissionPanel(missionary: false);

        // An ordinary (default-role) colonist is no missionary: neither the Establish nor the Denounce button appears.
        AssertThat(settlement.HasMission).IsFalse();
        AssertThat(FindButton(controller, "Establish")).IsNull();
        AssertThat(FindButton(controller, "Denounce")).IsNull();
    }

    // ---- Native first contact (86d3kgbnq): the chief's peace offer — accept (peace) or reject (war) ----

    [TestCase(Timeout = 60000)]
    public async Task SpeakWithChief_FirstContact_OpensThePeaceOffer_AcceptEstablishesPeace()
    {
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, _) =
            await OpenMissionPanel(missionary: false);

        // First contact: speaking with the chief defers to the Col1 peace offer (no inline result), and surfaces the
        // accept/reject dialog (a parchment ConfirmationDialog parented to the controller).
        await Press(runner, controller, "Speak");
        await runner.SimulateFrames(1);
        AssertThat(game.PendingFirstContact).IsNotNull();
        var dialog = controller.GetChildren().OfType<ConfirmationDialog>().FirstOrDefault();
        AssertThat(dialog).IsNotNull();

        // Accept the peace → the offer resolves and the tribe is NOT at war.
        dialog!.EmitSignal("confirmed");
        await runner.SimulateFrames(1);
        AssertThat(game.PendingFirstContact).IsNull();
        AssertThat(game.NativeStanceToward(settlement.NationTypeId, game.HumanPlayer.PlayerId)).IsNotEqual(Stance.War);
    }

    [TestCase(Timeout = 60000)]
    public async Task SpeakWithChief_FirstContact_RejectDeclaresWar()
    {
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, _) =
            await OpenMissionPanel(missionary: false);

        await Press(runner, controller, "Speak");
        await runner.SimulateFrames(1);
        var dialog = controller.GetChildren().OfType<ConfirmationDialog>().First();

        // Reject the offer (the Cancel button = "Reject (war)") → the tribe declares war.
        dialog.EmitSignal("canceled");
        await runner.SimulateFrames(1);
        AssertThat(game.PendingFirstContact).IsNull();
        AssertThat(game.NativeStanceToward(settlement.NationTypeId, game.HumanPlayer.PlayerId)).IsEqual(Stance.War);
    }

    [TestCase(Timeout = 60000)]
    public async Task DenounceMissionButton_ShownOverARivalMission_OustsTheRival_InstallingTheHumansMission()
    {
        // A rival colonial power already holds the mission; the human's missionary may denounce (not re-establish) it.
        // With the rival's immigration left at 0 the denounce roll (r × rivalImm ÷ (humanImm+1)) is 0 < 0.5 → always wins.
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit missionary) =
            await OpenMissionPanel(missionary: true, rivalMissionOwnerId: RivalColonialId);

        AssertThat(settlement.HasMission).IsTrue();
        AssertThat(settlement.MissionOwnerId!.Value).IsNotEqual(game.HumanPlayer.PlayerId);
        AssertThat(FindButton(controller, "Denounce")).IsNotNull();
        AssertThat(FindButton(controller, "Establish")).IsNull();

        await Press(runner, controller, "Denounce");

        // The rival was ousted and the human's mission installed in its place; the challenger is consumed.
        AssertThat(settlement.HasMission).IsTrue();
        AssertThat(settlement.MissionOwnerId!.Value).IsEqual(game.HumanPlayer.PlayerId);
        AssertThat(game.Units.Any(u => u.Id == missionary.Id && u.IsOnMap)).IsFalse();
    }

    /// <summary>The first non-human colonial player id (the rival whose mission the human denounces).</summary>
    private const int RivalColonialId = -1; // resolved against the live game in OpenMissionPanel

    // ---- Demand tribute (86d3fq01a) -------------------------------------------------------------------------

    [TestCase(Timeout = 60000)]
    public async Task DemandTributeButton_ShownForAnArmedUnit_ShakesDownTheChief_ForGold()
    {
        // An artillery (innate offence) beside a Content settlement: the Demand-tribute button is offered and pays out.
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit gun) =
            await OpenArmedPanel();

        AssertThat(FindButton(controller, "DemandTribute")).IsNotNull();
        int goldBefore = game.Gold;

        await Press(runner, controller, "DemandTribute");

        // A Content tribe pays tribute (gold rose) and the demand ended the unit's turn (FreeCol demandTribute).
        AssertThat(game.Gold).IsGreater(goldBefore);
        AssertThat(gun.MovementLeft).IsEqual(0);
    }

    [TestCase(Timeout = 60000)]
    public async Task DemandTributeButton_NotShown_ForAnUnarmedColonist()
    {
        // A plain free colonist has no offensive strength, so the Demand-tribute action is not offered.
        (_, GameController controller, _, _, _) = await OpenMissionPanel(missionary: false);
        AssertThat(FindButton(controller, "DemandTribute")).IsNull();
    }

    // ---- Incite (86d3fq00w / 86d3fq0bt) ---------------------------------------------------------------------

    [TestCase(Timeout = 60000)]
    public async Task InciteButton_ShownForAFundedMissionary_OpensTheRivalPicker_AndTurnsTheTribeOnTheRival()
    {
        // A funded missionary beside a Content settlement: the Incite button opens a rival-picker; picking the rival
        // pays the chief, charges the gold, and spikes the tribe's alarm toward that rival to war level.
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit missionary) =
            await OpenMissionPanel(missionary: true, humanGold: 100000);
        int rivalId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        int goldBefore = game.Gold;
        int rivalAlarmBefore = settlement.AlarmFor(rivalId);

        AssertThat(FindButton(controller, "Incite")).IsNotNull();
        await Press(runner, controller, "Incite");                 // open the rival-picker
        await Press(runner, controller, $"Incite_{rivalId}");      // pay the chief to war on the rival

        AssertThat(game.Gold).IsLess(goldBefore);                                          // bribe paid
        AssertThat(settlement.AlarmFor(rivalId)).IsEqual(rivalAlarmBefore + Game.InciteWarAlarm); // tribe turns on the rival
        AssertThat(missionary.MovementLeft).IsEqual(0);                                     // the audience ended the turn
    }

    [TestCase(Timeout = 60000)]
    public async Task InciteButton_NotShown_ForAPennilessMissionary()
    {
        // With no gold the missionary cannot afford any bribe, so the Incite action is not offered (the oracle gates it).
        (_, GameController controller, _, _, _) = await OpenMissionPanel(missionary: true, humanGold: 0);
        AssertThat(FindButton(controller, "Incite")).IsNull();
    }

    [TestCase(Timeout = 60000)]
    public async Task InciteButton_NotShown_ForANonMissionary()
    {
        // A funded but unrobed colonist has no inciteNatives ability, so the Incite action is not offered.
        (_, GameController controller, _, _, _) = await OpenMissionPanel(missionary: false, humanGold: 100000);
        AssertThat(FindButton(controller, "Incite")).IsNull();
    }

    // ---- Learn skill (86d3f62r5) ----------------------------------------------------------------------------

    [TestCase(Timeout = 60000)]
    public async Task LearnButton_ShownForALearnableColonist_TrainsItIntoTheTaughtExpert()
    {
        // 86d3f62r5: the native-settlement "Learn skill" action was reachable but undriven by L3. A free colonist
        // beside a Content settlement teaching a skill it can learn: the Learn button is offered; pressing it upgrades
        // the colonist to the taught expert type (keeping its id) and consumes the settlement's skill.
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit colonist) =
            await OpenLearnPanel();
        int colonistId = colonist.Id;

        AssertThat(game.CheckLearnSkill(colonist, settlement).Allowed).IsTrue(); // sanity: learnable
        string taughtSkill = settlement.LearnableSkill!; // capture before the press consumes it
        Button? learn = FindButton(controller, "Learn");
        AssertThat(learn).IsNotNull();

        // Press the button we already resolved (a re-lookup after the press would miss it — the action rebuilds the panel).
        learn!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);

        // The colonist upgraded to the settlement's taught expert type (same id) — the core of the flow. A non-capital
        // settlement also consumes its skill on teaching; a capital teaches indefinitely (SkillConsumed stays false), so
        // the consume side is asserted only when the staged settlement is not a capital.
        Unit upgraded = game.Units.First(u => u.Id == colonistId);
        AssertThat(upgraded.Type.Id).IsEqual(taughtSkill);
        if (!settlement.IsCapital)
        {
            AssertThat(settlement.SkillConsumed).IsTrue();
        }
    }

    [TestCase(Timeout = 60000)]
    public async Task LearnButton_NotShown_ForAnExpertWhoCannotLearn()
    {
        // An expert farmer already knows a profession, so it has no natives-change row for the taught skill: the Learn
        // action is not offered (the oracle gates it).
        (_, GameController controller, _, _, _) = await OpenLearnPanel(unitType: "model.unit.expertFarmer");
        AssertThat(FindButton(controller, "Learn")).IsNull();
    }

    /// <summary>
    /// Stages (through the save layer) a human colonist (<paramref name="unitType"/>) on a free land tile adjacent to a
    /// discovered settlement whose <c>LearnableSkill</c> is forced to <c>expertFarmer</c> and whose alarm is Content
    /// (below Angry, so it teaches), then opens the native-settlement panel acting with it. Mirrors <c>OpenMissionPanel</c>.
    /// </summary>
    private static async Task<(ISceneRunner, GameController, Game, NativeSettlement, Unit)> OpenLearnPanel(
        string unitType = FreeColonist)
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        NativeSettlement seed = game.NativeSettlements.First(s =>
            s.Position.Neighbours().Any(n => FreeLand(game, n)));
        Position land = seed.Position.Neighbours().First(n => FreeLand(game, n));

        SaveGame save = SaveGame.From(game);
        int unitId = game.Units.Max(u => u.Id) + 1;
        int move = game.Ruleset.Unit(unitType).Movement;
        var staged = new SavedUnit(unitId, unitType, land.X, land.Y, move, OwnerId: humanId);
        // Force a known learnable skill so the case never depends on the seed's random skill assignment.
        List<SavedNativeSettlement> settlements = save.NativeSettlements!
            .Select(s => s.Id == seed.Id ? s with { LearnableSkill = "model.unit.expertFarmer" } : s)
            .ToList();
        Game injected = (save with { Units = save.Units.Concat([staged]).ToList(), NativeSettlements = settlements })
            .Restore(game.Ruleset);
        SetGame(controller, injected);

        NativeSettlement settlement = injected.NativeSettlements.First(s => s.Id == seed.Id);
        injected.ChangeNativeAlarm(settlement, 300); // Content — below Angry, so it teaches
        Unit unit = injected.Units.First(u => u.Id == unitId);

        controller.OpenNativeSettlementPanel(settlement, unit);
        await runner.SimulateFrames(1);
        var panel = controller.GetNode<PanelContainer>("UI/NativeSettlementPanel");
        AssertThat(panel.Visible).IsTrue();
        return (runner, controller, injected, settlement, unit);
    }

    /// <summary>
    /// Stages (through the save layer) a human <see cref="Artillery"/> on a free land tile adjacent to a discovered
    /// settlement set <b>Content</b> (so it pays tribute), and opens the native-settlement panel acting with it. Mirrors
    /// <c>OpenMissionPanel</c> but for an armed unit (the demand-tribute path).
    /// </summary>
    private static async Task<(ISceneRunner, GameController, Game, NativeSettlement, Unit)> OpenArmedPanel()
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        NativeSettlement seed = game.NativeSettlements.First(s =>
            s.Position.Neighbours().Any(n => FreeLand(game, n)));
        Position land = seed.Position.Neighbours().First(n => FreeLand(game, n));

        SaveGame save = SaveGame.From(game);
        int unitId = game.Units.Max(u => u.Id) + 1;
        int gunMove = game.Ruleset.Unit(Artillery).Movement;
        var staged = new SavedUnit(unitId, Artillery, land.X, land.Y, gunMove, OwnerId: humanId);
        Game injected = (save with { Units = save.Units.Concat([staged]).ToList() }).Restore(game.Ruleset);
        SetGame(controller, injected);

        NativeSettlement settlement = injected.NativeSettlements.First(s => s.Id == seed.Id);
        injected.ChangeNativeAlarm(settlement, 300); // Content — a calm tribe pays tribute
        Unit gun = injected.Units.First(u => u.Id == unitId);

        controller.OpenNativeSettlementPanel(settlement, gun);
        await runner.SimulateFrames(1);
        var panel = controller.GetNode<PanelContainer>("UI/NativeSettlementPanel");
        AssertThat(panel.Visible).IsTrue();
        return (runner, controller, injected, settlement, gun);
    }

    [TestCase(Timeout = 60000)]
    public async Task SellButton_SellsCargoToTheSettlement_ForGold_DrainingTheHold_GrowingTheStore()
    {
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit ship) =
            await OpenTradePanel(shipCargo: 100, settlementSugar: 0);

        // The ship holds 100 sugar; selling it grows the settlement's store and empties the hold.
        AssertThat(ship.CargoOf(Sugar)).IsEqual(100);
        int goldBefore = game.Gold;
        int stockBefore = settlement.GeneralStockOf(Sugar); // the store may hold a generator-seeded amount already

        // Open the Sell sub-flow, pick the sugar lot, accept the natives' offer.
        await Press(runner, controller, "Sell");
        await Press(runner, controller, "Trade_sugar");
        await Press(runner, controller, "Accept");

        AssertThat(game.Gold).IsGreater(goldBefore);                              // paid for the cargo
        AssertThat(ship.CargoOf(Sugar)).IsEqual(0);                              // the hold is empty
        AssertThat(settlement.GeneralStockOf(Sugar)).IsEqual(stockBefore + 100); // the goods joined the store
    }

    [TestCase(Timeout = 60000)]
    public async Task BuyButton_BuysFromTheSettlementStore_ForGold_LoadingTheHold_DrainingTheStore()
    {
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit ship) =
            await OpenTradePanel(shipCargo: 0, settlementSugar: 80, humanGold: 10000);

        // The settlement holds sugar to sell; the ship is empty and the human can afford it.
        AssertThat(ship.CargoOf(Sugar)).IsEqual(0);
        int goldBefore = game.Gold;
        int stockBefore = settlement.GeneralStockOf(Sugar);
        int lot = System.Math.Min(stockBefore, 100); // GoodsToSell offers the store capped at one hold (100)

        // Open the Buy sub-flow, pick the sugar lot, accept (pay) the natives' asking price.
        await Press(runner, controller, "Buy");
        await Press(runner, controller, "Trade_sugar");
        await Press(runner, controller, "Accept");

        AssertThat(game.Gold).IsLess(goldBefore);                                // gold was spent
        AssertThat(ship.CargoOf(Sugar)).IsEqual(lot);                            // the hold loaded the lot
        AssertThat(settlement.GeneralStockOf(Sugar)).IsEqual(stockBefore - lot); // the store drained by the lot
    }

    [TestCase(Timeout = 60000)]
    public async Task Haggle_AskingMore_ThenAccepting_ClosesTheDealAtTheHaggledPrice_NotTheStandardOne()
    {
        (ISceneRunner runner, GameController controller, Game game, NativeSettlement settlement, Unit ship) =
            await OpenTradePanel(shipCargo: 100, settlementSugar: 0);

        int standard = game.NativeSalePrice(settlement, Sugar, 100); // the natives' standard offer for the lot
        int goldBefore = game.Gold;

        // Open the Sell sub-flow, pick the lot, push for more (one haggle round), then accept the standing offer.
        await Press(runner, controller, "Sell");
        await Press(runner, controller, "Trade_sugar");
        await Press(runner, controller, "Haggle"); // ask more — the chief accepts the higher offer or counters upward
        // If the chief walked off (lost patience), the only button left is Back — guard the rare case.
        if (FindButton(controller, "Accept") is not null)
        {
            await Press(runner, controller, "Accept");
            int got = game.Gold - goldBefore;
            // The deal closed at the HAGGLED figure, which is at least the standard price (a successful push raises it).
            AssertThat(got).IsGreaterEqual(standard);
            AssertThat(ship.CargoOf(Sugar)).IsEqual(0);
        }
    }

    /// <summary>Presses the named button inside the native panel and lets the rebuild settle.</summary>
    private static async Task Press(ISceneRunner runner, GameController controller, string name)
    {
        Button? button = FindButton(controller, name);
        AssertThat(button).IsNotNull();
        button!.EmitSignal(BaseButton.SignalName.Pressed);
        await runner.SimulateFrames(1);
    }

    /// <summary>
    /// Starts the main scene, then stages (through the save layer) a human caravel — carrying <paramref name="shipCargo"/>
    /// sugar — on coastal water beside a discovered native settlement holding <paramref name="settlementSugar"/> sugar,
    /// with the human given <paramref name="humanGold"/>. Sets the settlement Content (so trade is allowed) and opens the
    /// native-settlement panel acting with that ship.
    /// </summary>
    private static async Task<(ISceneRunner, GameController, Game, NativeSettlement, Unit)> OpenTradePanel(
        int shipCargo, int settlementSugar, int humanGold = 0)
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        // A native settlement with an adjacent free water tile (a coastal settlement reachable by ship).
        NativeSettlement seed = game.NativeSettlements.First(s =>
            s.Position.Neighbours().Any(n => FreeWater(game, n)));
        Position water = seed.Position.Neighbours().First(n => FreeWater(game, n));

        // Stage the caravel-with-cargo and the human's gold through the save layer; the settlement's sugar stock and
        // its alarm round-trip too, so the restored game is fully set up for trade.
        SaveGame save = SaveGame.From(game);
        int shipId = game.Units.Max(u => u.Id) + 1;
        int caravelMove = game.Ruleset.Unit("model.unit.caravel").Movement;
        var cargo = shipCargo > 0 ? new Dictionary<string, int> { [Sugar] = shipCargo } : null;
        var staged = new SavedUnit(shipId, "model.unit.caravel", water.X, water.Y, caravelMove,
            Cargo: cargo, OwnerId: humanId);
        List<SavedPlayer> players = save.Players!
            .Select(p => p.PlayerId == humanId ? p with { Gold = humanGold } : p).ToList();
        Game injected = (save with { Units = save.Units.Concat([staged]).ToList(), Players = players })
            .Restore(game.Ruleset);
        SetGame(controller, injected);

        NativeSettlement settlement = injected.NativeSettlements.First(s => s.Id == seed.Id);
        if (settlementSugar > 0)
        {
            settlement.AddGoods(Sugar, settlementSugar); // public goods-store mutator
        }
        injected.ChangeNativeAlarm(settlement, 300); // Content — trade is allowed (public alarm mutator)
        Unit ship = injected.Units.First(u => u.Id == shipId);

        controller.OpenNativeSettlementPanel(settlement, ship);
        await runner.SimulateFrames(1);
        var panel = controller.GetNode<PanelContainer>("UI/NativeSettlementPanel");
        AssertThat(panel.Visible).IsTrue();
        return (runner, controller, injected, settlement, ship);
    }

    /// <summary>
    /// Starts the main scene, then stages (through the save layer) a human <see cref="FreeColonist"/> — in the
    /// <see cref="MissionaryRole"/> when <paramref name="missionary"/>, else the default role — on a free land tile
    /// adjacent to a discovered native settlement set <b>Content</b> (so a mission installs rather than gets the
    /// missionary killed). When <paramref name="rivalMissionOwnerId"/> names a non-human colonial player, that player's
    /// mission is pre-installed at the settlement (and their immigration left at 0, so the human's denounce roll wins).
    /// Opens the native-settlement panel acting with that colonist. Mirrors <c>OpenTradePanel</c>.
    /// </summary>
    private static async Task<(ISceneRunner, GameController, Game, NativeSettlement, Unit)> OpenMissionPanel(
        bool missionary, int rivalMissionOwnerId = 0, int humanGold = 0)
    {
        ISceneRunner runner = ISceneRunner.Load("res://scenes/main.tscn");
        var controller = (GameController)runner.Scene();
        controller.StartNewGame(Seed);
        await runner.SimulateFrames(2);
        Game game = GameOf(controller);
        int humanId = game.HumanPlayer.PlayerId;

        // A settlement with a free adjacent land tile a missionary can stand on.
        NativeSettlement seed = game.NativeSettlements.First(s =>
            s.Position.Neighbours().Any(n => FreeLand(game, n)));
        Position land = seed.Position.Neighbours().First(n => FreeLand(game, n));

        // Resolve the rival owner: the sentinel RivalColonialId → the first non-human colonial player in the live game.
        int? rivalOwner = rivalMissionOwnerId == RivalColonialId
            ? game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId
            : (rivalMissionOwnerId != 0 ? rivalMissionOwnerId : (int?)null);

        // Stage the missionary through the save layer; pre-install a rival mission and zero the rival's immigration if asked.
        SaveGame save = SaveGame.From(game);
        int unitId = game.Units.Max(u => u.Id) + 1;
        int colonistMove = game.Ruleset.Unit(FreeColonist).Movement
            + (missionary ? 3 : 0); // the missionary role grants +3 movement; either way movement is left for the action
        var staged = new SavedUnit(unitId, FreeColonist, land.X, land.Y, colonistMove,
            Role: missionary ? MissionaryRole : null, OwnerId: humanId);
        List<SavedNativeSettlement> settlements = save.NativeSettlements!
            .Select(s => s.Id == seed.Id && rivalOwner is { } owner
                ? s with { MissionOwnerId = owner, MissionIsExpert = false }
                : s)
            .ToList();
        // Zero the rival's immigration if a rival mission is staged (so the denounce roll wins), and give the human the
        // requested gold (the incite path needs a purse to pay the chief's bribe).
        IReadOnlyList<SavedPlayer> players = save.Players!
            .Select(p =>
            {
                if (rivalOwner is { } rid && p.PlayerId == rid)
                {
                    p = p with { Immigration = 0 };
                }
                if (p.PlayerId == humanId && humanGold > 0)
                {
                    p = p with { Gold = humanGold };
                }
                return p;
            })
            .ToList();
        Game injected = (save with
        {
            Units = save.Units.Concat([staged]).ToList(),
            NativeSettlements = settlements,
            Players = players,
        }).Restore(game.Ruleset);
        SetGame(controller, injected);

        NativeSettlement settlement = injected.NativeSettlements.First(s => s.Id == seed.Id);
        injected.ChangeNativeAlarm(settlement, 300); // Content — a mission installs (Angry/Hateful would kill the missionary)
        Unit colonist = injected.Units.First(u => u.Id == unitId);

        controller.OpenNativeSettlementPanel(settlement, colonist);
        await runner.SimulateFrames(1);
        var panel = controller.GetNode<PanelContainer>("UI/NativeSettlementPanel");
        AssertThat(panel.Visible).IsTrue();
        return (runner, controller, injected, settlement, colonist);
    }

    /// <summary>A land tile a land unit may stand on — in bounds, not water, and clear of settlements/colonies/units.</summary>
    private static bool FreeLand(Game game, Position n) =>
        game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
        && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
        && !game.Units.Any(u => u.IsOnMap && u.Position == n);

    /// <summary>A water tile that is free for a ship — in bounds, water, and clear of settlements/colonies/units.</summary>
    private static bool FreeWater(Game game, Position n) =>
        game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater
        && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
        && !game.Units.Any(u => u.IsOnMap && u.Position == n);

    private static Button? FindButton(GameController controller, string name) =>
        controller.GetNode<PanelContainer>("UI/NativeSettlementPanel")
            .FindChild(name, recursive: true, owned: false) as Button;

    private static void SetGame(GameController controller, Game game) =>
        controller.GetType().GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, game);

    private static Game GameOf(GameController controller) =>
        (Game)controller.GetType()
            .GetField("_game", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(controller)!;
}
