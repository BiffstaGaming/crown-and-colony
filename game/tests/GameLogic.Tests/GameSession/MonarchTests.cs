using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The home-nation Monarch (independence arc item 1, <c>86d3c9qvr</c>): the weighted action chooser, the validity
/// oracle, and the per-turn tick. The tick uses an ephemeral monarch generator so it draws nothing from the human's
/// stream 0 — existing seeded games stay byte-identical (ADR-009).
/// </summary>
public class MonarchTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    /// <summary>A fresh game with the human's lone starting colonist turned into a colony, so the King has settlements.</summary>
    private static Game FoundedGame(ulong seed = Seed)
    {
        Game game = Game.New(Classic, seed);
        game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        return game;
    }

    // ── Weighted pick helper ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void WeightedRandom_PicksProportionally_AndIsDeterministic()
    {
        var choices = new (int, string)[] { (1, "a"), (3, "b") };
        var rng = new Pcg32Random(42);
        var counts = new Dictionary<string, int> { ["a"] = 0, ["b"] = 0 };
        for (int i = 0; i < 4000; i++)
        {
            counts[RandomChoice.WeightedRandom(rng, choices)]++;
        }

        // ~1:3 split; b should land roughly three times as often as a.
        Assert.InRange(counts["b"] / (double)counts["a"], 2.4, 3.6);

        // Determinism: the same seed replays the same picks.
        var r1 = new Pcg32Random(7);
        var r2 = new Pcg32Random(7);
        Assert.Equal(
            Enumerable.Range(0, 50).Select(_ => RandomChoice.WeightedRandom(r1, choices)),
            Enumerable.Range(0, 50).Select(_ => RandomChoice.WeightedRandom(r2, choices)));
    }

    [Fact]
    public void WeightedRandom_NoPositiveWeights_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            RandomChoice.WeightedRandom(new Pcg32Random(1), new (int, int)[] { (0, 1) }));

    // ── Chooser gate + weights ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetMonarchActionChoices_IsEmptyBeforeGrace()
    {
        Game game = FoundedGame();
        Assert.Empty(game.GetMonarchActionChoices(29)); // grace = (6 - dx)*10 = 30 at medium
        Assert.NotEmpty(game.GetMonarchActionChoices(30));
    }

    [Fact]
    public void GetMonarchActionChoices_IsEmptyWithoutSettlements()
    {
        Game game = Game.New(Classic, Seed); // no colony founded
        Assert.Empty(game.GetMonarchActionChoices(50));
    }

    [Fact]
    public void GetMonarchActionChoices_OffersNoActionAndTaxRise_WithTheFreeColWeights()
    {
        Game game = FoundedGame();
        var choices = game.GetMonarchActionChoices(50).ToDictionary(c => c.Action, c => c.Weight);

        Assert.Equal(Math.Max(200 - 50, 100), choices[MonarchAction.NoAction]); // max(150,100) = 150
        Assert.Equal(8, choices[MonarchAction.RaiseTaxAct]);  // 5 + dx (dx=3)
        Assert.Equal(8, choices[MonarchAction.RaiseTaxWar]);
        Assert.False(choices.ContainsKey(MonarchAction.SupportLand)); // never offered at medium (dx == 3)
        Assert.False(choices.ContainsKey(MonarchAction.AddToRef));    // REF modelled in item 6
    }

    [Fact]
    public void NoActionWeight_FloorsAt100_LateGame()
    {
        Game game = FoundedGame();
        var choices = game.GetMonarchActionChoices(250).ToDictionary(c => c.Action, c => c.Weight);
        Assert.Equal(100, choices[MonarchAction.NoAction]); // max(200-250, 100) = 100
    }

    // ── Validity oracle ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MonarchActionIsValid_TaxBounds()
    {
        Game game = FoundedGame();
        Player king = game.HumanPlayer;

        king.TaxRate = 64;
        Assert.True(game.MonarchActionIsValid(MonarchAction.RaiseTaxAct));  // 64 < 65
        king.TaxRate = 65;
        Assert.False(game.MonarchActionIsValid(MonarchAction.RaiseTaxAct)); // at the cap

        king.TaxRate = 31;
        Assert.True(game.MonarchActionIsValid(MonarchAction.LowerTaxWar));  // 31 > 30
        king.TaxRate = 30;
        Assert.False(game.MonarchActionIsValid(MonarchAction.LowerTaxWar)); // at the floor+10

        Assert.False(game.MonarchActionIsValid(MonarchAction.ForceTax));    // never chooseable
        Assert.False(game.MonarchActionIsValid(MonarchAction.Displeasure));
        Assert.True(game.MonarchActionIsValid(MonarchAction.NoAction));
    }

    [Fact]
    public void MonarchActionIsValid_HessianNeedsFiveThousandGold()
    {
        Game game = FoundedGame();
        game.HumanPlayer.Gold = 4999;
        Assert.False(game.MonarchActionIsValid(MonarchAction.HessianMercenaries));
        game.HumanPlayer.Gold = 5000;
        Assert.True(game.MonarchActionIsValid(MonarchAction.HessianMercenaries));
    }

    // ── The tick: determinism (stream 0 untouched) ───────────────────────────────────────────────────────

    [Fact]
    public void MonarchTick_IsByteIdenticalAcrossTwinGames_PastGrace()
    {
        // The whole point: two identical founded games run past the grace period stay byte-identical on stream 0,
        // i.e. the monarch's ephemeral RNG never perturbs the human stream.
        Game a = FoundedGame(7777);
        Game b = FoundedGame(7777);
        for (int i = 0; i < 40; i++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(a.RandomState, b.RandomState);
        Assert.Equal(a.HumanPlayer.TaxRate, b.HumanPlayer.TaxRate);
    }
}
