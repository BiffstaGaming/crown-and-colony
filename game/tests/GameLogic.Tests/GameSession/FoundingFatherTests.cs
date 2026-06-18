using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

public class FoundingFatherTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void Spec_DefinesTheClassicFathers_AcrossFiveCategories()
    {
        Assert.Equal(25, Classic.FoundingFathers.Count);
        foreach (FatherType type in Enum.GetValues<FatherType>())
        {
            Assert.Equal(5, Classic.FoundingFathers.Count(f => f.Type == type));
        }

        FoundingFather smith = Classic.Father("model.foundingFather.adamSmith");
        Assert.Equal(FatherType.Trade, smith.Type);
        Assert.Equal((2, 8, 6), (smith.Weight1, smith.Weight2, smith.Weight3));
    }

    [Theory]
    // FreeCol getTotalFoundingFatherCost: count==0 ? factor : 2*(count+1)*factor+1.
    // Verified against Player.java:1544. Factor 24 = classic veryEasy; 40 = medium (the default).
    [InlineData(24, new[] { 24, 97, 145, 193, 241 })]
    [InlineData(40, new[] { 40, 161, 241, 321, 401 })]
    public void CostFormula_MatchesFreeCol(int factor, int[] expected)
    {
        for (int elected = 0; elected < expected.Length; elected++)
        {
            Assert.Equal(expected[elected], Game.FoundingFatherCost(elected, factor));
        }
    }

    [Fact]
    public void NewGame_OffersOneFatherPerEligibleCategory()
    {
        var game = Game.New(Classic, seed: 42);

        Assert.NotEmpty(game.OfferedFathers);
        Assert.True(game.OfferedFathers.Count <= 5);
        // Distinct categories — at most one offer per type.
        var types = game.OfferedFathers.Select(id => Classic.Father(id).Type).ToList();
        Assert.Equal(types.Count, types.Distinct().Count());
        Assert.Null(game.CurrentFather);
        Assert.Empty(game.Congress);
    }

    [Fact]
    public void Offers_AreDeterministicForASeed()
    {
        Assert.Equal(
            Game.New(Classic, seed: 99).OfferedFathers,
            Game.New(Classic, seed: 99).OfferedFathers);
    }

    [Fact]
    public void TownHallBells_BecomeLiberty_EachTurn_AndLeaveTheWarehouse()
    {
        var game = Game.New(Classic, seed: 42);
        var colony = game.FoundColony(game.Units[0]);

        game.EndTurn(); // town hall makes 1 bell, converted to liberty
        Assert.Equal(1, game.Liberty);
        Assert.Equal(0, colony.StoreOf("model.goods.bells")); // not left as tradeable stock

        game.EndTurn();
        Assert.Equal(2, game.Liberty);
    }

    [Fact]
    public void ChosenFather_IsElected_WhenLibertyReachesTheCost()
    {
        var game = Game.New(Classic, seed: 42);
        game.FoundColony(game.Units[0]);
        string target = game.OfferedFathers[0];
        game.ChooseFather(target);

        // Town hall yields 1 liberty/turn; the first father costs 40 (classic medium factor).
        for (int turn = 0; turn < 39; turn++)
        {
            game.EndTurn();
        }
        Assert.Empty(game.Congress);        // 39 liberty < 40
        Assert.Equal(target, game.CurrentFather);

        game.EndTurn();                     // 40th liberty → elect
        Assert.Contains(target, game.Congress);
        Assert.Equal(0, game.Liberty);      // cost consumed
        Assert.Null(game.CurrentFather);    // ready to choose the next
        Assert.NotEmpty(game.OfferedFathers);
        Assert.DoesNotContain(target, game.OfferedFathers); // elected, not re-offered
    }

    [Fact]
    public void ChooseFather_RejectsAnUnofferedFather()
    {
        var game = Game.New(Classic, seed: 42);
        string notOffered = Classic.FoundingFathers
            .First(f => !game.OfferedFathers.Contains(f.Id)).Id;

        Assert.Throws<InvalidMoveException>(() => game.ChooseFather(notOffered));
    }

    [Fact]
    public void SaveRoundTrip_PreservesCongressLibertyAndOffers()
    {
        var game = Game.New(Classic, seed: 42);
        game.FoundColony(game.Units[0]);
        game.ChooseFather(game.OfferedFathers[0]);
        game.EndTurn();
        game.EndTurn();

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(game.Liberty, loaded.Liberty);
        Assert.Equal(game.CurrentFather, loaded.CurrentFather);
        Assert.Equal(game.OfferedFathers, loaded.OfferedFathers);
        Assert.Equal(game.Congress, loaded.Congress);
    }

    [Fact]
    public void PreV10Save_LoadsWithEmptyCongress_AndNoLiberty()
    {
        var game = Game.New(Classic, seed: 42);
        SaveGame v9 = SaveGame.From(game) with
        {
            Version = 9,
            Liberty = 0,
            Congress = null,
            CurrentFather = null,
            OfferedFathers = null,
        };

        Game loaded = SaveGame.FromJson(v9.ToJson()).Restore(Classic);

        Assert.Equal(0, loaded.Liberty);
        Assert.Empty(loaded.Congress);
        Assert.Null(loaded.CurrentFather);
    }
}
