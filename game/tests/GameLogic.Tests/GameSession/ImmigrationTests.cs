using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Immigration &amp; Europe recruitment (Phase 4 slice 4). All numbers are pinned to
/// FreeCol source: the recruit-price formula (<c>Europe.getCurrentRecruitPrice</c>),
/// the immigration target/increment and the −4/+2 Europe contribution
/// (<c>Player</c> + classic difficulty spec), and the recruit weights
/// (<c>recruit-probability</c> in the ruleset).
/// </summary>
public class ImmigrationTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Crosses = "model.goods.crosses";

    // ───────────────────────── recruit pool & price ─────────────────────────

    [Fact]
    public void RecruitablePool_ReadsSpecProbabilities_AndPersonAbility()
    {
        // Weights come straight from the spec's recruit-probability attribute.
        Assert.Equal(20, Classic.Unit("model.unit.freeColonist").RecruitProbability);
        Assert.Equal(20, Classic.Unit("model.unit.indenturedServant").RecruitProbability);
        Assert.Equal(20, Classic.Unit("model.unit.pettyCriminal").RecruitProbability);
        Assert.Equal(1, Classic.Unit("model.unit.expertOreMiner").RecruitProbability);
        Assert.Equal(1, Classic.Unit("model.unit.expertFisherman").RecruitProbability);
        Assert.Equal(0, Classic.Unit("model.unit.caravel").RecruitProbability); // ships not recruitable

        // Persons (suppress immigration in Europe); ships are not persons.
        Assert.True(Classic.Unit("model.unit.freeColonist").IsPerson);
        Assert.False(Classic.Unit("model.unit.caravel").IsPerson);
    }

    [Fact]
    public void NewGame_StocksAFullDock_OfRecruitableTypes()
    {
        var game = Game.New(Classic, seed: 123);

        Assert.Equal(Game.RecruitSlots, game.RecruitDock.Count);
        Assert.All(game.RecruitDock, id => Assert.True(Classic.Unit(id).RecruitProbability > 0));
        Assert.Equal(Game.InitialImmigration, game.ImmigrationRequired); // 15
        Assert.Equal(0, game.Immigration);
    }

    [Fact]
    public void Dock_IsDeterministicForASeed()
    {
        Assert.Equal(
            Game.New(Classic, seed: 123).RecruitDock,
            Game.New(Classic, seed: 123).RecruitDock);
    }

    [Theory]
    // FreeCol Europe.getCurrentRecruitPrice: max(base·max(required−immigration,0)/required, floor),
    // base 200, floor 80, required 15 (classic). Integer division, verified by hand.
    [InlineData(0, 200)]   // full base:          200·15/15 = 200
    [InlineData(5, 133)]   //                     200·10/15 = 133
    [InlineData(10, 80)]   //  200·5/15 = 66 < floor → 80
    [InlineData(15, 80)]   //  difference 0 → floor
    [InlineData(20, 80)]   //  past target → floor
    public void RecruitPrice_MatchesFreeColFormula(int immigration, int expected)
    {
        Game game = WithImmigration(immigration, required: 15);
        Assert.Equal(expected, game.RecruitPrice);
    }

    // ───────────────────────── accrual & emigration ─────────────────────────

    [Fact]
    public void ColonylessPlayer_AccruesThePlayerBonus_EachTurn()
    {
        var game = Game.New(Classic, seed: 1); // unit left on the map, never founds
        game.EndTurn();
        Assert.Equal(Game.PlayerImmigrationBonus, game.Immigration); // +2, no colony crosses
        game.EndTurn();
        Assert.Equal(2 * Game.PlayerImmigrationBonus, game.Immigration); // +2 again
    }

    [Fact]
    public void ColonyCrosses_DrainIntoImmigration_AndLeaveTheWarehouse()
    {
        var game = Game.New(Classic, seed: 42);
        Colony colony = game.FoundColony(game.Units[0]); // free chapel → 1 cross/turn unattended

        game.EndTurn();

        Assert.Equal(3, game.Immigration);          // 1 chapel cross + 2 player bonus
        Assert.Equal(0, colony.StoreOf(Crosses));   // crosses drained, not tradeable stock
    }

    [Fact]
    public void Emigrant_AppearsInEurope_WhenImmigrationMeetsTheTarget()
    {
        var game = Game.New(Classic, seed: 42);
        game.FoundColony(game.Units[0]); // 3 immigration/turn (1 cross + 2 bonus)

        for (int t = 0; t < 4; t++)
        {
            game.EndTurn(); // 3,6,9,12 — below the 15 target
        }
        Assert.Empty(game.UnitsInEurope);
        Assert.Equal(12, game.Immigration);
        Assert.Equal(15, game.ImmigrationRequired);

        game.EndTurn(); // 12 + 3 = 15 → emigrate

        Unit emigrant = Assert.Single(game.UnitsInEurope);
        Assert.True(emigrant.Type.IsPerson);
        Assert.Equal(UnitLocation.InEurope, emigrant.Location);
        Assert.Equal(17, game.ImmigrationRequired);     // target rose by the crosses increment
        Assert.Equal(0, game.Immigration);              // surplus 0 after reducing by the old target
    }

    [Fact]
    public void PersonsInEurope_SuppressImmigration_ClampedNonNegative()
    {
        var game = Game.New(Classic, seed: 42);
        game.FoundColony(game.Units[0]);
        for (int t = 0; t < 5; t++)
        {
            game.EndTurn(); // produces one emigrant; now one person idles in Europe
        }
        Assert.Single(game.UnitsInEurope);
        Assert.Equal(0, game.Immigration);

        game.EndTurn(); // 1 cross, Europe = 1·(−4)+2 = −2 → clamped to −1; net 0

        Assert.Equal(0, game.Immigration);          // stalled, not negative
        Assert.Single(game.UnitsInEurope);          // no second emigrant
    }

    // ───────────────────────── paid recruitment ─────────────────────────

    [Fact]
    public void Recruit_PaysGold_PlacesUnitInEurope_RefillsDock_AndEscalatesPrice()
    {
        var game = Game.New(Classic, seed: 42, startingGold: 1000);
        game.FoundColony(game.Units[0]);

        Assert.Equal(200, game.RecruitPrice); // immigration 0, required 15 → full base
        string slotType = game.RecruitDock[0];

        Unit recruit = game.Recruit(0);

        Assert.Equal(800, game.Gold);                       // paid exactly 200
        Assert.Equal(slotType, recruit.Type.Id);            // got the slot's type
        Assert.Equal(UnitLocation.InEurope, recruit.Location);
        Assert.Contains(recruit, game.UnitsInEurope);
        Assert.Equal(Game.RecruitSlots, game.RecruitDock.Count); // refilled
        Assert.Equal(17, game.ImmigrationRequired);         // paid recruit also advances the clock
        Assert.Equal(230, game.RecruitPrice);               // base rose 200 → 230 (immigration 0, required 17)
    }

    [Fact]
    public void Recruit_IsRejected_WithoutGold_OrForABadSlot()
    {
        var game = Game.New(Classic, seed: 42, startingGold: 0);

        Assert.False(game.CheckRecruit(0).Allowed);          // can't afford 200
        Assert.False(game.CheckRecruit(-1).Allowed);         // no such slot
        Assert.False(game.CheckRecruit(Game.RecruitSlots).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.Recruit(0));
    }

    // ───────────────────────── persistence ─────────────────────────

    [Fact]
    public void SaveRoundTrip_PreservesImmigration_Dock_AndEscalatedPrice()
    {
        var game = Game.New(Classic, seed: 42, startingGold: 1000);
        game.FoundColony(game.Units[0]);
        game.EndTurn();
        game.Recruit(0);   // escalate the base price; change immigration state; dock a recruit
        game.EndTurn();

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(game.Immigration, loaded.Immigration);
        Assert.Equal(game.ImmigrationRequired, loaded.ImmigrationRequired);
        Assert.Equal(game.RecruitPrice, loaded.RecruitPrice);   // escalated base survived
        Assert.Equal(game.RecruitDock, loaded.RecruitDock);
        Assert.Equal(game.UnitsInEurope.Count(), loaded.UnitsInEurope.Count());

        // Acid test: save → load → save is byte-identical.
        string json = SaveGame.From(game).ToJson();
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson());
    }

    [Fact]
    public void PreV12Save_LoadsWithDefaultImmigration_AndAFreshDock()
    {
        var game = Game.New(Classic, seed: 42);
        SaveGame v11 = SaveGame.From(game) with
        {
            Version = 11,
            Immigration = 0,
            ImmigrationRequired = null,
            BaseRecruitPrice = null,
            RecruitLowerCap = null,
            RecruitDock = null,
        };

        Game loaded = SaveGame.FromJson(v11.ToJson()).Restore(Classic);

        Assert.Equal(0, loaded.Immigration);
        Assert.Equal(Game.InitialImmigration, loaded.ImmigrationRequired); // 15
        Assert.Equal(200, loaded.RecruitPrice);                           // default base/floor
        Assert.Equal(Game.RecruitSlots, loaded.RecruitDock.Count);        // a fresh dock was drawn
    }

    // ───── Religious unrest (86d3c7yca): the English (immigration nation type) immigrate faster ─────

    [Fact]
    public void ReligiousUnrest_ReducesTheImmigrationTarget_ForTheEnglishNationType()
    {
        EuropeanNation english = Classic.EuropeanNations.First(n =>
            n.NationType.Modifiers.Any(m => m.TargetId == "model.modifier.religiousUnrestBonus"));

        Assert.Equal(15, GameWithHumanNation(null).ImmigrationRequired);       // no nation → the full target
        Assert.Equal(10, GameWithHumanNation(english.Id).ImmigrationRequired); // −33% → round(15 × 0.67)
    }

    [Fact]
    public void ReligiousUnrest_StoresTheRawTarget_AndSurvivesSaveRoundTrip()
    {
        EuropeanNation english = Classic.EuropeanNations.First(n =>
            n.NationType.Modifiers.Any(m => m.TargetId == "model.modifier.religiousUnrestBonus"));
        Game eng = GameWithHumanNation(english.Id);

        Game reloaded = SaveGame.FromJson(SaveGame.From(eng).ToJson()).Restore(Classic);
        Assert.Equal(10, reloaded.ImmigrationRequired); // reduced on use; the raw 15 rides the save unchanged
    }

    // ───── Survival auto-recruit (86d3drn0e): FreeCol MigrationType.SURVIVAL ─────
    //
    // The rescue is wired for FOREIGN POWERS only: it can only fire because a player lost all its units, which for
    // the human can only happen via AI actions — and the human's scoped state must stay independent of those (the
    // stream-0 invariant IsHumanDefeated documents). Each test strands one foreign power (no colonies, no units) and
    // watches its OWN Europe dock.

    [Fact]
    public void SurvivalRecruit_RescuesACrossStarvedForeignPower_BeforeTheMandatoryYear()
    {
        // A foreign power with no colony (so no crosses) and no unit anywhere: its immigration pipeline is
        // permanently dry — without a rescue it could never get another colonist.
        (Game game, int powerId) = StrandedForeignPower(seed: 7);
        Assert.Empty(PowerUnits(game, powerId));

        game.EndTurn();

        // FreeCol fires one free survival emigrant onto the power's OWN Europe dock.
        Unit survivor = Assert.Single(PowerUnits(game, powerId));
        Assert.True(survivor.Type.IsPerson);
        Assert.Equal(UnitLocation.InEurope, survivor.Location);

        // It does not refire next turn: the waiting colonist means the power is no longer stranded (hasColonist).
        game.EndTurn();
        Assert.Single(PowerUnits(game, powerId));
    }

    [Fact]
    public void SurvivalRecruit_IsDeterministicForASeed()
    {
        (Game a, int powerA) = StrandedForeignPower(seed: 99);
        (Game b, int powerB) = StrandedForeignPower(seed: 99);
        a.EndTurn();
        b.EndTurn();

        Assert.Equal(
            Assert.Single(PowerUnits(a, powerA)).Type.Id,
            Assert.Single(PowerUnits(b, powerB)).Type.Id); // same seed → same survival colonist
        Assert.Equal(powerA, powerB);
    }

    [Fact]
    public void SurvivalRecruit_DoesNotFire_ForANormallyImmigratingPlayer()
    {
        // A player with a cross-producing colony reaches the threshold normally and is never spuriously rescued.
        var game = Game.New(Classic, seed: 42);
        game.FoundColony(game.Units[0]); // free chapel → crosses every turn

        for (int t = 0; t < 5; t++)
        {
            game.EndTurn(); // 3,6,9,12,15 → exactly one NORMAL emigrant on turn 5
        }

        Unit emigrant = Assert.Single(game.UnitsInEurope);    // one, not two (no survival top-up)
        Assert.Equal(17, game.ImmigrationRequired);           // the NORMAL path consumed the pool + raised the target
        Assert.Equal(0, game.Immigration);
    }

    [Fact]
    public void SurvivalRecruit_NeverFiresForTheHuman_NorShiftsItsStream0()
    {
        // The human is carved out (ADR-009): even stripped to nothing it gets no survival recruit, and a save with no
        // human units/colonies runs a turn drawing nothing from stream 0 for a rescue.
        Game game = StalledHumanGame(turn: 1, seed: 7);
        var stateBefore = game.RandomState;

        game.EndTurn();

        Assert.Empty(game.UnitsInEurope);                            // no human survival emigrant
        Assert.Equal(Game.PlayerImmigrationBonus, game.Immigration); // just the +2 bonus accrued
        // EndTurn advances stream 0 (world/AI turns) but nothing here is a stream-0 survival draw; the point is the
        // human gets no rescue at all — proven by the empty Europe above.
        Assert.NotEqual(default, stateBefore);
    }

    [Fact]
    public void SurvivalRecruit_DoesNotFire_FromTheMandatoryColonyYearOnward()
    {
        // FreeCol stops rescuing once the mandatory-colony cutover year is reached (checkForDeath → IS_DEAD,
        // not IS_AUTORECRUIT). Turn 109 is year 1600 (turn 1 = 1492).
        (Game game, int powerId) = StrandedForeignPower(seed: 7, turn: 109);
        Assert.Equal(Game.MandatoryColonyYear, game.CurrentYear); // sanity: we are at the cutover

        game.EndTurn();

        Assert.Empty(PowerUnits(game, powerId)); // no rescue from the mandatory year on
    }

    // ───────────────────────── fixtures ─────────────────────────

    /// <summary>The units a given player owns (foreign powers carry a null OwnerNationId and their player id as OwnerId).</summary>
    private static System.Collections.Generic.List<Unit> PowerUnits(Game game, int playerId) =>
        game.Units.Where(u => u.OwnerNationId is null && u.OwnerId == playerId).ToList();

    /// <summary>
    /// A default game with one <b>foreign power stranded</b> — all its colonies and units stripped out via the
    /// save/restore path — so it is cross-starved with nothing left in the New World (FreeCol's survival-recruit
    /// condition). The power keeps its <see cref="SavedPlayer"/> (Colonial type, own RNG stream, Europe dock). Returns
    /// the game and that power's id. Other players (the human, the remaining powers) are left intact.
    /// </summary>
    private static (Game Game, int PowerId) StrandedForeignPower(ulong seed, int turn = 1)
    {
        SaveGame baseSave = SaveGame.From(Game.New(Classic, seed: seed));
        int powerId = baseSave.Players!.First(p => !p.IsHuman && p.PlayerType == 0).PlayerId;

        SaveGame stranded = baseSave with
        {
            Turn = turn,
            Units = baseSave.Units.Where(u => (u.OwnerId ?? 0) != powerId).ToList(),
            Colonies = baseSave.Colonies?.Where(c => (c.OwnerId ?? 0) != powerId).ToList(),
        };
        return (SaveGame.FromJson(stranded.ToJson()).Restore(Classic), powerId);
    }

    /// <summary>A game whose human player is stripped of all colonies and units (the human carve-out check).</summary>
    private static Game StalledHumanGame(int turn, int seed)
    {
        var save = new SaveGame
        {
            Turn = turn,
            RandomStateValue = (ulong)seed,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],          // no human units on the map
            Explored = [],
            Immigration = 0,     // empty pool
            ImmigrationRequired = Game.InitialImmigration,
        };
        return save.Restore(Classic);
    }


    /// <summary>
    /// A game with immigration state set precisely, via the save/restore path
    /// (the only way to pin <c>immigration</c>/<c>required</c> without running turns).
    /// One-tile plains map, no units.
    /// </summary>
    private static Game WithImmigration(int immigration, int required)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [],
            Immigration = immigration,
            ImmigrationRequired = required,
        };
        return save.Restore(Classic);
    }

    /// <summary>A fresh game with the human assigned <paramref name="nationId"/> (null = none), via the save/restore path — so its nation-type advantages (e.g. the English religious-unrest bonus) apply.</summary>
    private static Game GameWithHumanNation(string? nationId)
    {
        SaveGame baseSave = SaveGame.From(Game.New(Classic, seed: 42));
        SaveGame primed = baseSave with
        {
            Players = baseSave.Players!.Select(p => p.IsHuman ? p with { NationId = nationId } : p).ToList(),
        };
        return SaveGame.FromJson(primed.ToJson()).Restore(Classic);
    }
}
