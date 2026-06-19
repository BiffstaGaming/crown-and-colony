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

    // ───────────────────────── fixtures ─────────────────────────

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
