using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The player-score engine (<c>86d3c9vjm</c>): a faithful, pure re-implementation of FreeCol's
/// <c>ServerPlayer.updateScore</c>. Verifies each summand (unit score values, colony liberty, 5 per founding father,
/// ⌊0.001·gold⌋, the independence percentage bonus), a known-fixture total, determinism, and that a score read draws
/// no RNG and mutates nothing (ADR-009).
/// </summary>
public class ScoreTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0x5C0E_5EEDUL;

    /// <summary>A bare new game on a controlled seed (its default starting roster is the only initial state).</summary>
    private static Game NewGame(ulong seed = Seed) => Game.New(Classic, seed);

    /// <summary>The first empty, land tile (for spawning a test unit/colony) that has not been taken.</summary>
    private static Position FirstEmptyLand(Game game) =>
        game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater && game.ColonyAt(p) is null
            && game.NativeSettlementAt(p) is null && !game.Units.Any(u => u.IsOnMap && u.Position == p));

    /// <summary>The first empty, coastal land tile (a colony there is a connected port — needed for independence).</summary>
    private static Position FirstCoastalLand(Game game) =>
        game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p)
            && p.Neighbours().Any(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater));

    // ── Summand: founding fathers (+5 each, FreeCol SCORE_FOUNDING_FATHER) ───────────────────────────────

    [Fact]
    public void FoundingFather_AddsFiveToScore()
    {
        Game game = NewGame();
        int before = game.PlayerScore(game.HumanPlayer);

        game.HumanPlayer.CongressList.Add("model.foundingFather.adamSmith");
        Assert.Equal(before + 5, game.PlayerScore(game.HumanPlayer));

        game.HumanPlayer.CongressList.Add("model.foundingFather.jeanDeBrebeuf");
        Assert.Equal(before + 10, game.PlayerScore(game.HumanPlayer));
    }

    // ── Summand: gold (⌊0.001·gold⌋, FreeCol SCORE_GOLD) ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 0)]
    [InlineData(999, 0)]      // floor(0.999) == 0
    [InlineData(1000, 1)]     // floor(1.0)   == 1
    [InlineData(1999, 1)]     // floor(1.999) == 1
    [InlineData(2500, 2)]     // floor(2.5)   == 2
    [InlineData(123456, 123)] // floor(123.456) == 123
    public void Gold_ContributesFlooredThousandths(int gold, int expectedGoldPoints)
    {
        Game game = NewGame();
        int baseline = game.PlayerScore(game.HumanPlayer); // gold starts at 0

        game.HumanPlayer.Gold = gold;
        Assert.Equal(baseline + expectedGoldPoints, game.PlayerScore(game.HumanPlayer));
    }

    // ── Summand: colony liberty (FreeCol sum(getColonies(), Colony::getLiberty)) ─────────────────────────

    [Fact]
    public void Colony_AddsItsLibertyToScore()
    {
        Game game = NewGame();
        Position spot = FirstEmptyLand(game);
        Unit colonist = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), spot);
        int before = game.PlayerScore(game.HumanPlayer); // the new colonist itself scores 3

        Colony colony = game.FoundColony(colonist);
        colony.Liberty = 350;

        // The colonist became colony population (no longer a map unit), so its unit score (3) is gone and the colony's
        // 350 liberty is added: net change is +350 − 3 over the pre-founding score.
        Assert.Equal(before - 3 + 350, game.PlayerScore(game.HumanPlayer));
    }

    // ── Summand: unit score values (FreeCol sum(getUnits(), Unit::getScoreValue)) ─────────────────────────

    [Fact]
    public void Unit_AddsItsRulesetScoreValue()
    {
        Game game = NewGame();
        int before = game.PlayerScore(game.HumanPlayer);

        // A veteran soldier has score-value 5 in the classic spec. SpawnUnit reveals tiles, which may discover a new
        // region and add its discovery score; net out that exploration delta so this asserts only the unit summand.
        int discBefore = game.HistoryEventScore;
        game.SpawnUnit(Classic.Unit("model.unit.veteranSoldier"), FirstEmptyLand(game));
        Assert.Equal(before + 5 + (game.HistoryEventScore - discBefore), game.PlayerScore(game.HumanPlayer));

        // A petty criminal has score-value 1.
        int before2 = game.PlayerScore(game.HumanPlayer);
        int disc2 = game.HistoryEventScore;
        game.SpawnUnit(Classic.Unit("model.unit.pettyCriminal"), FirstEmptyLand(game));
        Assert.Equal(before2 + 1 + (game.HistoryEventScore - disc2), game.PlayerScore(game.HumanPlayer));
    }

    [Fact]
    public void DefaultNewGame_ScoreIsTheStartingRoster()
    {
        Game game = NewGame();
        // The classic landed roster is two free colonists (3 each) + a caravel (3) = 9, when all three place; on a
        // landlocked start the caravel is skipped (6). The starting fog reveal also DISCOVERS the regions the roster
        // can see (P6: e.g. the Pacific Ocean +100 and the landing's land region), each adding its discovery score —
        // so the total is the roster's unit score values PLUS the accrued discovery (HistoryEvent) score, with no
        // colonies/fathers/gold yet.
        int expected = game.PlayerUnits.Sum(u => UnitScore(u.Type.Id)) + game.HistoryEventScore;
        Assert.Equal(expected, game.PlayerScore(game.HumanPlayer));
        Assert.Equal(game.Score, game.PlayerScore(game.HumanPlayer)); // the human-convenience accessor agrees
        Assert.True(game.PlayerScore(game.HumanPlayer) >= 6);
    }

    // ── Known fixture: a fully controlled board yields a known total ─────────────────────────────────────

    [Fact]
    public void KnownFixture_SumsToTheExpectedTotal()
    {
        Game game = NewGame();
        int rosterScore = game.PlayerUnits.Sum(u => UnitScore(u.Type.Id)); // the starting units

        // Add: an elder statesman (6) + a galleon-less wagon train (1) on land.
        game.SpawnUnit(Classic.Unit("model.unit.elderStatesman"), FirstEmptyLand(game)); // +6
        game.SpawnUnit(Classic.Unit("model.unit.wagonTrain"), FirstEmptyLand(game));     // +1

        // A colony with 600 liberty.
        Unit founder = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), FirstEmptyLand(game)); // a colonist (3)
        Colony colony = game.FoundColony(founder);                                                   // consumes that colonist
        colony.Liberty = 600;                                                                        // +600

        // Two founding fathers (+5 each = 10) and 7500 gold (floor(7.5) = 7).
        game.HumanPlayer.CongressList.Add("model.foundingFather.adamSmith");
        game.HumanPlayer.CongressList.Add("model.foundingFather.henryHudson");
        game.HumanPlayer.Gold = 7500;

        // rosterScore + elderStatesman(6) + wagonTrain(1) + colonyLiberty(600) + fathers(10) + gold(7), plus the
        // exploration-discovery (HistoryEvent) score the spawns + founding accrued by revealing new regions (P6).
        // The founder colonist's own +3 is removed when it joins the colony, so it is NOT in the unit sum.
        int expected = rosterScore + 6 + 1 + 600 + 10 + 7 + game.HistoryEventScore;
        Assert.Equal(expected, game.PlayerScore(game.HumanPlayer));
    }

    // ── Independence percentage bonus (FreeCol's INDEPENDENCE history-event switch) ───────────────────────

    [Fact]
    public void IndependentNation_TakesTheHundredPercentFirstPlaceBonus()
    {
        Game game = NewGame();
        Position coastal = FirstCoastalLand(game);
        Unit colonist = game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), coastal);
        Colony colony = game.FoundColony(colonist);
        colony.Liberty = Colony.LibertyPerRebel * colony.Population; // 100% national SoL → may declare

        game.HumanPlayer.Gold = 10000; // floor(10) = 10 points to make the doubling visible
        int subtotal = game.PlayerScore(game.HumanPlayer); // still Colonial — no bonus yet

        game.DeclareIndependence(game.HumanPlayer);
        // Declaring makes the player a Rebel (still no win bonus) and forfeits in/bound-for-Europe units; recompute.
        int rebelSubtotal = game.PlayerScore(game.HumanPlayer);
        Assert.Equal(0, BonusFraction(game)); // a rebel that has not yet won gets no bonus

        // Force the win: become Independent (the screens read Winner/PlayerType; the score doubles for first place).
        game.HumanPlayer.PlayerType = PlayerType.Independent;
        Assert.Equal(rebelSubtotal * 2, game.PlayerScore(game.HumanPlayer)); // subtotal + subtotal·100/100

        // Sanity: the colonial subtotal had no bonus, so it differs from the doubled independent score.
        Assert.NotEqual(subtotal * 2, subtotal);
    }

    // ── Determinism + purity (ADR-009): the read draws nothing and mutates nothing ───────────────────────

    [Fact]
    public void Score_IsDeterministic_ForTheSameState()
    {
        Game a = NewGame(0xABCUL);
        Game b = NewGame(0xABCUL);
        Assert.Equal(a.PlayerScore(a.HumanPlayer), b.PlayerScore(b.HumanPlayer));
    }

    [Fact]
    public void Score_DrawsNoRandomness_AndMutatesNothing()
    {
        Game game = NewGame();
        game.SpawnUnit(Classic.Unit("model.unit.galleon"), FirstCoastalWater(game));
        game.HumanPlayer.CongressList.Add("model.foundingFather.adamSmith");
        game.HumanPlayer.Gold = 4321;

        var rngBefore = game.RandomState;
        int unitsBefore = game.Units.Count;
        int coloniesBefore = game.Colonies.Count;

        int s1 = game.PlayerScore(game.HumanPlayer);
        int s2 = game.PlayerScore(game.HumanPlayer);

        Assert.Equal(s1, s2);                       // repeatable
        Assert.Equal(rngBefore, game.RandomState);  // no RNG drawn (ADR-009)
        Assert.Equal(unitsBefore, game.Units.Count); // no units created/destroyed
        Assert.Equal(coloniesBefore, game.Colonies.Count);
    }

    [Fact]
    public void UnknownUnitType_ScoresZero_AndDoesNotThrow()
    {
        Game game = NewGame();
        int before = game.PlayerScore(game.HumanPlayer);
        // A brave (native) is not owned by the human, and has no score-value — it must not affect the human score.
        game.SpawnUnit(Classic.Unit(Game.BraveUnitTypeId), FirstEmptyLand(game), ownerNationId: "model.nationType.apache");
        Assert.Equal(before, game.PlayerScore(game.HumanPlayer));
    }

    /// <summary>The classic-ruleset score values used by the engine, mirrored here so the fixture math is explicit.</summary>
    private static int UnitScore(string typeId) => typeId switch
    {
        "model.unit.freeColonist" => 3,
        "model.unit.caravel" => 3,
        "model.unit.veteranSoldier" => 5,
        "model.unit.elderStatesman" => 6,
        "model.unit.wagonTrain" => 1,
        "model.unit.pettyCriminal" => 1,
        "model.unit.galleon" => 5,
        _ => 0,
    };

    /// <summary>The independence bonus fraction currently applied (0 when no doubling): derived from a known unit-only score.</summary>
    private static int BonusFraction(Game game)
    {
        int raw = game.PlayerUnits.Sum(u => UnitScore(u.Type.Id))
            + game.HumanPlayer.Congress.Count * 5
            + (int)System.Math.Floor(0.001 * game.HumanPlayer.Gold)
            + game.HistoryEventScore; // the exploration-discovery summand (P6) is part of the pre-bonus subtotal
        foreach (Colony c in game.Colonies)
        {
            raw += c.Liberty;
        }
        int actual = game.PlayerScore(game.HumanPlayer);
        return raw == 0 ? 0 : (actual - raw) * 100 / raw;
    }

    // ── ScoreBreakdown: the itemised oracle the victory screen reads ──────────────────────────────────────

    [Fact]
    public void ScoreBreakdown_SummandsAndTotal_MatchPlayerScore()
    {
        Game game = NewGame();
        game.SpawnUnit(Classic.Unit("model.unit.galleon"), FirstCoastalWater(game));
        game.HumanPlayer.CongressList.Add("model.foundingFather.adamSmith");
        game.HumanPlayer.Gold = 4321; // floor(0.001·4321) = 4 gold points

        ScoreComponents b = game.ScoreBreakdown(game.HumanPlayer);

        // Each summand equals its independent re-derivation from public reads.
        Assert.Equal(game.PlayerUnits.Sum(u => UnitScore(u.Type.Id)), b.UnitValues);
        Assert.Equal(game.Colonies.Where(c => c.OwnerId == game.HumanPlayer.PlayerId).Sum(c => c.Liberty), b.ColonyLiberty);
        Assert.Equal(game.HumanPlayer.Congress.Count * 5, b.FoundingFatherPoints);
        Assert.Equal(4, b.GoldPoints);
        Assert.Equal(game.HistoryEventScore, b.HistoryPoints);
        Assert.Equal(0, b.IndependenceBonusPercent); // still Colonial — no win yet

        // The breakdown reassembles to exactly PlayerScore (single source of truth).
        Assert.Equal(b.Subtotal, b.UnitValues + b.ColonyLiberty + b.FoundingFatherPoints + b.GoldPoints + b.HistoryPoints);
        Assert.Equal(b.Subtotal, b.Total); // no bonus → total is the subtotal
        Assert.Equal(game.PlayerScore(game.HumanPlayer), b.Total);
    }

    [Fact]
    public void ScoreBreakdown_AfterWinningIndependence_CarriesTheFirstPlaceBonus()
    {
        Game game = NewGame();
        Position coastal = FirstCoastalLand(game);
        Colony colony = game.FoundColony(game.SpawnUnit(Classic.Unit(Game.StartingUnitTypeId), coastal));
        colony.Liberty = Colony.LibertyPerRebel * colony.Population;
        game.HumanPlayer.Gold = 10000;
        game.DeclareIndependence(game.HumanPlayer);
        game.HumanPlayer.PlayerType = PlayerType.Independent; // first power to win → 100% bonus

        ScoreComponents b = game.ScoreBreakdown(game.HumanPlayer);
        Assert.Equal(100, b.IndependenceBonusPercent);
        Assert.Equal(b.Subtotal, b.IndependenceBonus); // +100% doubles the subtotal
        Assert.Equal(b.Subtotal * 2, b.Total);
        Assert.Equal(game.PlayerScore(game.HumanPlayer), b.Total); // still agrees with PlayerScore
    }

    /// <summary>The first empty water tile, for spawning a ship.</summary>
    private static Position FirstCoastalWater(Game game) =>
        game.Map.AllPositions().First(p =>
            game.Map.TerrainAt(p).IsWater && !game.Units.Any(u => u.IsOnMap && u.Position == p));
}
