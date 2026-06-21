using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Scenarios;

/// <summary>
/// <b>Differential-fidelity harness (L2, task 86d3drphu).</b> A reusable scaffold that runs a
/// <em>scripted, seeded</em> scenario on our engine and asserts the observable outcome against a
/// value <b>extracted from FreeCol's own source or data</b> — so a silent divergence from the
/// reference behaviour fails CI rather than passing unnoticed.
///
/// <para>
/// Each case is one <see cref="FidelityInvariant{T}"/>: a name, the FreeCol citation it pins (file +
/// line + the documented value), the scripted action that drives our engine, and the expected
/// outcome read straight off the FreeCol reference. The <see cref="Verify{T}"/> runner executes the
/// scenario and asserts equality, surfacing the citation in the failure message. The point is the
/// <em>structure</em>: dropping a new FreeCol-cross-checked invariant in is a one-liner, and every
/// one of them is a guard rail against drift.
/// </para>
///
/// <para>
/// All cases are deterministic — seeded RNG (ADR-009), no wall-clock, no ambient state. Tagged
/// <c>[Trait("Category","Fidelity")]</c> so the fidelity suite can be run in isolation
/// (<c>--filter "FullyQualifiedName~Fidelity"</c>). This is <b>test infrastructure</b>: it changes
/// no production code; a real fidelity bug found here is documented, not patched here.
/// </para>
/// </summary>
[Trait("Category", "Fidelity")]
public class DifferentialFidelityTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    // ───────────────────────────── The reusable scaffold ─────────────────────────────

    /// <summary>
    /// One FreeCol-cross-checked invariant: a scripted action run on our engine whose observable
    /// outcome must equal a value taken from FreeCol's source/data (cited in <see cref="FreeColRef"/>).
    /// </summary>
    /// <typeparam name="T">The observable outcome type being compared.</typeparam>
    private sealed record FidelityInvariant<T>(
        string Name,
        string FreeColRef,
        Func<T> Observe,
        T Expected);

    /// <summary>
    /// Runs an invariant's scripted scenario and asserts the engine's observed outcome equals the
    /// FreeCol reference value — the failure message names the invariant and quotes the FreeCol
    /// citation, so a divergence reads as "we drifted from FreeCol's documented X", not a bare mismatch.
    /// </summary>
    private static void Verify<T>(FidelityInvariant<T> inv)
    {
        T actual = inv.Observe();
        Assert.True(
            EqualityComparer<T>.Default.Equals(inv.Expected, actual),
            $"Fidelity divergence in '{inv.Name}': engine produced {actual}, " +
            $"but FreeCol's documented value is {inv.Expected}.\n  FreeCol reference: {inv.FreeColRef}");
    }

    // ───────────────────────────── Invariant 1: combat odds ─────────────────────────────

    [Fact]
    public void Fidelity_CombatOdds_MatchFreeColAttackOverTotal()
    {
        // A fortified free-colonist soldier on hills, attacked by an armed brave. FreeCol resolves the
        // round by victory = attack / (attack + defence) with the SimpleCombatModel percentage modifiers
        // (role values from the classic spec: armedBrave offence +2; soldier offence +2, defence +1):
        //   attacker: armed brave (brave base offence 1 + role +2 = 3) × ATTACK_BONUS(+50%)            = 4.5
        //   defender: soldier (colonist base defence 1 + role +1 = 2) × hills(+100%) × FORTIFIED(+50%)
        //             = 2 × 2 × 1.5                                                                    = 6
        //   odds = 4.5 / (4.5 + 6) = 0.428571…
        double armedBrave = Classic.Unit("model.unit.brave").Offence + Classic.Role("model.role.armedBrave").Offence;
        double soldier = Classic.Unit("model.unit.freeColonist").Defence + Classic.Role("model.role.soldier").Defence;

        var inv = new FidelityInvariant<double>(
            Name: "combat odds = attack / (attack + defence)",
            // SimpleCombatModel.java:110  `double victory = attackPower / (attackPower + defencePower);`
            // ATTACK_BONUS +50%, FORTIFIED +50% (Modifier indices), hills defence +100% (classic spec).
            FreeColRef: "freecol SimpleCombatModel.java:110 (victory = attack/(attack+defence)); "
                + "ATTACK_BONUS +50%, FORTIFIED +50%, model.tile.hills defenceBonus +100%",
            Observe: () =>
            {
                double atk = CombatModel.AttackPower(armedBrave, new AttackContext());
                double def = CombatModel.DefencePower(soldier, new DefenceContext(TerrainDefenceBonus: 100, Fortified: true));
                return Math.Round(CombatModel.WinProbability(atk, def), 6);
            },
            Expected: Math.Round(4.5 / (4.5 + 6.0), 6));

        Verify(inv);
    }

    [Fact]
    public void Fidelity_CombatResolution_BandsMatchFreeColPartition()
    {
        // FreeCol's CombatModel partitions the [0,1) roll: the first 10% of the win range is a great
        // win, the rest of the win range a win, the last 10% of the loss range a great loss (land).
        // For win=0.5 the bands are: greatWin [0,0.05), win [0.05,0.5), loss [0.5,0.95), greatLoss
        // [0.95,1). We draw safely *inside* each band (not on the exact edges, which IEEE-754 rounding
        // makes ambiguous), then a seeded sample confirms every draw lands in a valid band (ADR-009).
        const double win = 0.5;

        var inv = new FidelityInvariant<bool>(
            Name: "land combat resolution bands (greatWin/win/loss/greatLoss)",
            // CombatModel.Resolve mirrors FreeCol SimpleCombatModel.generateAttackResult banding:
            // great win on the first 10% of the win range; great loss on the last 10% of the loss range.
            FreeColRef: "freecol SimpleCombatModel.java generateAttackResult — 10% great-win / 10% great-loss partition",
            Observe: () =>
            {
                bool greatWin = CombatModel.Resolve(win, new FixedRandom(0.02)) == CombatResult.GreatWin;   // [0, 0.05)
                bool normalWin = CombatModel.Resolve(win, new FixedRandom(0.30)) == CombatResult.Win;       // [0.05, 0.5)
                bool loss = CombatModel.Resolve(win, new FixedRandom(0.70)) == CombatResult.Loss;           // [0.5, 0.95)
                bool greatLoss = CombatModel.Resolve(win, new FixedRandom(0.98)) == CombatResult.GreatLoss; // [0.95, 1)
                return greatWin && normalWin && loss && greatLoss;
            },
            Expected: true);

        Verify(inv);

        // A seeded sample never produces an out-of-range result.
        var random = new Pcg32Random(seed: 0xC0FFEEUL);
        var valid = new[] { CombatResult.GreatWin, CombatResult.Win, CombatResult.Loss, CombatResult.GreatLoss };
        for (int i = 0; i < 1000; i++)
        {
            Assert.Contains(CombatModel.Resolve(win, random), valid);
        }
    }

    // ───────────────────────────── Invariant 2: market price movement ─────────────────────────────

    [Fact]
    public void Fidelity_MarketPriceMovement_MatchesFreeColSupplyFormula()
    {
        // FreeCol MarketData.price(): paidForSale = round(initialPrice · initialAmount / amountInMarket).
        // Sugar seeds at amount 1500, initial sell price 2. Selling 600 (six 100-unit chunks) raises the
        // inventory to 2100; 2 · 1500/2100 = 1.43 → rounds to 1, so the bid falls 2 → 1. The seller banks
        // 600 × 2 = 1200 (chunks priced before each step moved the market, at 0% tax).
        const string sugar = "model.goods.sugar";

        var priceInv = new FidelityInvariant<int>(
            Name: "market sell-price falls as supply grows (sugar 1500→2100 ⇒ bid 2→1)",
            // MarketData.java:322-324  amountPrice = initialPrice * (initialAmount / (float) amountInMarket)
            FreeColRef: "freecol MarketData.java:322-324 (price() supply formula); "
                + "classic spec sugar market initial-amount=1500 initial-price=2",
            Observe: () =>
            {
                var market = new Market(Classic);
                market.Sell(sugar, 600, taxPercent: 0);
                return market.BidPrice(sugar);
            },
            Expected: 1);
        Verify(priceInv);

        var inventoryInv = new FidelityInvariant<int>(
            Name: "selling raises the market inventory by the amount sold",
            FreeColRef: "freecol MarketData.java addGoodsToMarket (amountInMarket += amount); sugar 1500 + 600",
            Observe: () =>
            {
                var market = new Market(Classic);
                market.Sell(sugar, 600, taxPercent: 0);
                return market.AmountInMarket(sugar);
            },
            Expected: 2100);
        Verify(inventoryInv);

        // Drive the same movement through the *whole engine* (found a colony, sell its stock) so the
        // fidelity check covers the real sale path, not just the Market unit. 0% tax ⇒ full 1200 credited.
        var revenueInv = new FidelityInvariant<int>(
            Name: "engine colony sale credits FreeCol-priced revenue (600 sugar @ bid 2, 0% tax)",
            FreeColRef: "freecol Market.sell()/MarketData.price() — each chunk priced at the current bid before it moves the market",
            Observe: () =>
            {
                Game game = Game.New(Classic, seed: 7, startingGold: 0, startingTax: 0);
                Colony colony = game.FoundColony(game.Units[0]);
                colony.AddGoods(sugar, 600);
                return game.SellColonyGoods(colony, sugar, 600);
            },
            Expected: 1200);
        Verify(revenueInv);
    }

    // ───────────────────────────── Invariant 3: native tension delta ─────────────────────────────

    [Fact]
    public void Fidelity_LandTakenTension_MatchesFreeColTensionAdd()
    {
        // FreeCol Tension.TENSION_ADD_LAND_TAKEN = 200: grabbing native land without paying adds 200
        // alarm. Starting from a calm settlement (alarm 0, band HAPPY since 0 <= HAPPY.limit=100), a
        // single land-take pushes alarm to exactly 200, which is in (HAPPY.limit, CONTENT.limit] ⇒ the
        // band steps HAPPY → CONTENT. We drive the engine's ChangeNativeAlarm by the constant our code
        // pins to that FreeCol value (Game.LandTakenAlarm) and assert both the value and the band step.
        var deltaInv = new FidelityInvariant<int>(
            Name: "land-taken tension delta = TENSION_ADD_LAND_TAKEN",
            // Tension.java:45  public static final int TENSION_ADD_LAND_TAKEN = 200;
            FreeColRef: "freecol Tension.java:45 (TENSION_ADD_LAND_TAKEN = 200)",
            Observe: () =>
            {
                (Game game, NativeSettlement s) = CalmSettlement(seed: 0xC0FFEEUL);
                int before = s.Alarm;
                game.ChangeNativeAlarm(s, Game.LandTakenAlarm);
                return s.Alarm - before;
            },
            Expected: 200);
        Verify(deltaInv);

        var bandInv = new FidelityInvariant<AlarmLevel>(
            Name: "alarm 200 sits in the CONTENT band (Tension.Level limits)",
            // Tension.java:72-76  HAPPY(100), CONTENT(600), … ; getLevel = first level whose limit >= value.
            FreeColRef: "freecol Tension.java:72-76 (Level.HAPPY=100, CONTENT=600) + getLevel()",
            Observe: () =>
            {
                (Game game, NativeSettlement s) = CalmSettlement(seed: 0xC0FFEEUL);
                game.ChangeNativeAlarm(s, Game.LandTakenAlarm);
                return s.AlarmLevel;
            },
            Expected: AlarmLevel.Content);
        Verify(bandInv);
    }

    // ───────────────────────────── Invariant 4: Sons of Liberty conversion ─────────────────────────────

    [Theory]
    // liberty, population → expected SoL%  (FreeCol floor((liberty·100)/(200·pop)))
    [InlineData(0, 4, 0)]      // no liberty → 0%
    [InlineData(400, 4, 50)]   // 400·100 / (200·4) = 50%   → +1 production bonus tier
    [InlineData(800, 4, 100)]  // 800·100 / (200·4) = 100%  → +2 production bonus tier
    [InlineData(300, 4, 37)]   // 300·100 / 800 = 37.5 → floor 37
    public void Fidelity_SonsOfLiberty_MatchesFreeColLibertyConversion(int liberty, int population, int expectedSol)
    {
        // FreeCol Colony.calculateSoLPercentage: membership = floor(liberty·100 / (LIBERTY_PER_REBEL·uc)),
        // LIBERTY_PER_REBEL = 200, clamped 0..100. Build a colony of the given population, bank the given
        // liberty, and read SonsOfLiberty straight off the engine.
        var inv = new FidelityInvariant<int>(
            Name: $"SoL% for liberty={liberty}, pop={population}",
            // Colony.java:1294  membership = (liberty * 100.0f) / (LIBERTY_PER_REBEL * uc); LIBERTY_PER_REBEL=200 (Colony.java:66)
            FreeColRef: "freecol Colony.java:1294 calculateSoLPercentage + Colony.java:66 LIBERTY_PER_REBEL=200",
            Observe: () =>
            {
                Colony colony = ColonyOfSize(population);
                colony.Liberty = liberty;
                return colony.SonsOfLiberty;
            },
            Expected: expectedSol);
        Verify(inv);
    }

    [Theory]
    // liberty (pop 4) → expected production bonus tier (FreeCol: SoL>=100 +2, >=50 +1, else 0 for a small colony)
    [InlineData(800, 2)]  // SoL 100 → +2
    [InlineData(400, 1)]  // SoL 50  → +1
    [InlineData(0, 0)]    // SoL 0, tory count 4 (not > Bad=6) → 0
    public void Fidelity_ProductionBonus_MatchesFreeColTiers(int liberty, int expectedBonus)
    {
        // FreeCol Colony.calculateProductionBonus tiers off SoL%/tory count. A 4-pop colony never trips
        // a penalty tier (tory count maxes at 4, below the >6/>10 thresholds), so 0/+1/+2 only here.
        var inv = new FidelityInvariant<int>(
            Name: $"production bonus tier for liberty={liberty} (pop 4)",
            // Colony.calculateProductionBonus: SoL>=VERY_GOOD(100) → +2; >=GOOD(50) → +1; tory>VERY_BAD(10) → -2; >BAD(6) → -1.
            FreeColRef: "freecol Colony.java calculateProductionBonus (SoL>=100 +2, >=50 +1, tory>6 -1, tory>10 -2)",
            Observe: () =>
            {
                Colony colony = ColonyOfSize(4);
                colony.Liberty = liberty;
                return colony.ProductionBonus;
            },
            Expected: expectedBonus);
        Verify(inv);
    }

    // ───────────────────────────── Invariant 5: calendar turn→year/season ─────────────────────────────

    [Fact]
    public void Fidelity_CalendarMapping_MatchesFreeColTurnYearAndSeason()
    {
        // FreeCol Turn.getTurnYear / getTurnSeason with the classic gameOptions.years group:
        // startingYear=1492, seasonYear=1600, seasons=2. Turn 1 = 1492 (one turn/year before 1600);
        // from 1600 a year is two turns (Spring, Autumn). We pin the documented boundary cases.
        Calendar cal = Calendar.Classic;

        var startYearInv = new FidelityInvariant<int>(
            Name: "turn 1 maps to the starting year 1492",
            // Turn.java getTurnYear: linear year = turn-1+startingYear; 1+1492-1 = 1492 (< seasonYear).
            FreeColRef: "freecol Turn.java getTurnYear; classic gameOptions.years startingYear=1492",
            Observe: () => cal.YearForTurn(1),
            Expected: 1492);
        Verify(startYearInv);

        var preSeasonInv = new FidelityInvariant<int>(
            Name: "a pre-1600 turn has no season (returns -1)",
            FreeColRef: "freecol Turn.java getTurnSeason: -1 before seasonYear=1600",
            Observe: () => cal.SeasonForTurn(100), // linear year 1591, before the split
            Expected: -1);
        Verify(preSeasonInv);

        // The season-split boundary: linear year 1600 is the first split year. Turn N where
        // turn-1+1492 = 1600 ⇒ turn 109 is Spring 1600; turn 110 is Autumn 1600; turn 111 is Spring 1601.
        var splitSpringInv = new FidelityInvariant<(int year, int season)>(
            Name: "first split turn is Spring 1600",
            // Turn.java getTurnYear/getTurnSeason: from seasonYear, year = seasonYear + (linear-seasonYear)/seasons;
            // season = (linear-seasonYear) % seasons. linear 1600 ⇒ year 1600, season 0 (Spring).
            FreeColRef: "freecol Turn.java getTurnYear/getTurnSeason; seasonYear=1600, seasons=2",
            Observe: () => (cal.YearForTurn(109), cal.SeasonForTurn(109)),
            Expected: (1600, 0));
        Verify(splitSpringInv);

        var splitAutumnInv = new FidelityInvariant<(int year, int season)>(
            Name: "second split turn is Autumn 1600",
            FreeColRef: "freecol Turn.java getTurnYear/getTurnSeason; linear 1601 ⇒ year 1600, season 1 (Autumn)",
            Observe: () => (cal.YearForTurn(110), cal.SeasonForTurn(110)),
            Expected: (1600, 1));
        Verify(splitAutumnInv);

        var nextYearInv = new FidelityInvariant<(int year, int season)>(
            Name: "two turns after the split rolls to Spring 1601",
            FreeColRef: "freecol Turn.java getTurnYear/getTurnSeason; linear 1602 ⇒ year 1601, season 0",
            Observe: () => (cal.YearForTurn(111), cal.SeasonForTurn(111)),
            Expected: (1601, 0));
        Verify(nextYearInv);
    }

    // ───────────────────────────── Scenario helpers ─────────────────────────────

    /// <summary>Deterministic RNG returning a fixed NextDouble — for exercising combat-band partition edges.</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    /// <summary>A new game whose first native settlement has been calmed to alarm 0 (HAPPY) — the scenario start for tension cases.</summary>
    private static (Game game, NativeSettlement settlement) CalmSettlement(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        NativeSettlement s = game.NativeSettlements.First();
        game.ChangeNativeAlarm(s, -s.Alarm); // start at 0 (Happy)
        return (game, s);
    }

    /// <summary>Founds a colony and sets its population directly (the SoL denominator) — matching the existing SoL-test setup pattern.</summary>
    private static Colony ColonyOfSize(int population)
    {
        Game game = Game.New(Classic, seed: 0xC0FFEEUL);
        Colony colony = game.FoundColony(game.Units[0]);
        colony.Population = population;
        return colony;
    }
}
