using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The human's New-Game nation pick (86d3drn5x): <c>Game.New(humanNationId: …)</c> seeds the human
/// <see cref="Player.NationId"/>, so the human now plays with that nation's <b>advantage</b> (the same nation-type
/// modifiers the foreign powers already fold) and that nation's <b>colony-name</b> list. The <b>default</b> call (no
/// nation) stays the classic nation-less human — byte-identical to before (ADR-009). Save round-trips with and without
/// a nation.
/// </summary>
public class HumanNationTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Furs = "model.goods.furs";
    private const string TradeBonus = "model.modifier.tradeBonus";

    /// <summary>The Dutch nation — the only classic power with the trade advantage (tradeBonus −50%).</summary>
    private static readonly string Dutch =
        Classic.EuropeanNations.First(n => n.NationType.Modifiers.Any(m => m.TargetId == TradeBonus)).Id;

    [Fact]
    public void New_WithNation_SeedsTheHumanNation()
    {
        Game game = Game.New(Classic, seed: 7, humanNationId: Dutch);
        Assert.Equal(Dutch, game.HumanPlayer.NationId);
    }

    [Fact]
    public void New_WithoutNation_LeavesTheHumanNationLess()
    {
        Game game = Game.New(Classic, seed: 7);
        Assert.Null(game.HumanPlayer.NationId);
    }

    [Fact]
    public void New_WithUnknownOrNonSelectableNation_FallsBackToNationLess()
    {
        // An unknown id, a REF id, and a native id are all rejected → the classic nation-less human (no advantage).
        Assert.Null(Game.New(Classic, seed: 7, humanNationId: "model.nation.nonsense").HumanPlayer.NationId);
        Assert.Null(Game.New(Classic, seed: 7, humanNationId: Dutch + "REF").HumanPlayer.NationId);
        Assert.Null(Game.New(Classic, seed: 7, humanNationId: "model.nation.apache").HumanPlayer.NationId);
    }

    [Fact]
    public void DutchHuman_GetsTheTradeAdvantage_AbsorbingLessOnASale()
    {
        // The Dutch market (tradeBonus −50%) absorbs half the sale volume, so a Dutch human's furs price falls slower.
        int dutch = MarketAmountAfterColonySale(Dutch);
        int plain = MarketAmountAfterColonySale(null);
        Assert.True(dutch < plain,
            $"the Dutch market absorbed {dutch}, a no-nation market {plain} — the trade advantage should absorb less");
    }

    [Fact]
    public void DutchHuman_FoundsWithDutchColonyNames()
    {
        Game game = Game.New(Classic, seed: 7, humanNationId: Dutch);
        Colony colony = game.FoundColony(FounderColonist(game));
        // The Dutch list leads with "New Amsterdam" (FreeCol classic order); a no-nation human uses the default list.
        Assert.Equal(Classic.EuropeanNation(Dutch).ColonyNames[0], colony.Name);
        Assert.Equal("New Amsterdam", colony.Name);
    }

    [Fact]
    public void NoNationHuman_FoundsWithTheDefaultColonyNames_NotADutchOne()
    {
        Game game = Game.New(Classic, seed: 7);
        Colony colony = game.FoundColony(FounderColonist(game));
        Assert.NotEqual(Classic.EuropeanNation(Dutch).ColonyNames[0], colony.Name);
    }

    [Fact]
    public void HumanNation_IsExcludedFromTheForeignPowerRoster()
    {
        // FreeCol removes the human's nation from the AI pool: no foreign power may share the human's chosen nation.
        Game game = Game.New(Classic, seed: 7, humanNationId: Dutch);
        Assert.DoesNotContain(game.Players.Where(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial),
            p => p.NationId == Dutch);
    }

    [Fact]
    public void DefaultNewGame_IsByteIdentical_ToANoNationGame()
    {
        // ADR-009: the default Game.New (no nation arg) must stay byte-identical to an explicit-null nation game — the
        // nation pick is purely additive and an unpicked game is unchanged. Same seed, same save bytes.
        string defaulted = SaveGame.From(Game.New(Classic, seed: 123)).ToJson();
        string explicitNull = SaveGame.From(Game.New(Classic, seed: 123, humanNationId: null)).ToJson();
        Assert.Equal(defaulted, explicitNull);
    }

    [Fact]
    public void NoNationSave_OmitsTheHumanNationId()
    {
        // A nation-less human carries a null NationId, which the serializer (WhenWritingNull) omits — the structural
        // guarantee behind the v48 byte-identity. (The whole-save JSON still mentions "NationId" for the foreign
        // powers/natives, which legitimately carry one; only the HUMAN's entry omits it.)
        SaveGame save = SaveGame.From(Game.New(Classic, seed: 123));
        SavedPlayer human = save.Players!.Single(p => p.IsHuman);
        Assert.Null(human.NationId);
    }

    [Fact]
    public void DutchSave_RoundTrips_PreservingTheHumanNation()
    {
        Game original = Game.New(Classic, seed: 55, humanNationId: Dutch);
        SaveGame save = SaveGame.From(original);
        Assert.Equal(49, save.Version); // v49 records the human's chosen nation
        Assert.Equal(Dutch, save.Players!.Single(p => p.IsHuman).NationId); // the picked nation IS persisted on the human
        Game loaded = SaveGame.FromJson(save.ToJson()).Restore(Classic);
        Assert.Equal(Dutch, loaded.HumanPlayer.NationId); // …and restores
    }

    [Fact]
    public void NoNationSave_RoundTrips_LeavingTheHumanNationLess()
    {
        Game loaded = SaveGame.FromJson(SaveGame.From(Game.New(Classic, seed: 55)).ToJson()).Restore(Classic);
        Assert.Null(loaded.HumanPlayer.NationId);
    }

    /// <summary>Founds a colony for a human of <paramref name="nationId"/> (or no nation), sells 600 furs from it, and returns the market's resulting absorbed volume — the lower the value, the gentler the price fall (the trade advantage).</summary>
    private static int MarketAmountAfterColonySale(string? nationId)
    {
        Game game = Game.New(Classic, seed: 42, humanNationId: nationId);
        Colony colony = game.FoundColony(FounderColonist(game));
        colony.AddGoods(Furs, 600);
        game.SellColonyGoods(colony, Furs, 600);
        return game.HumanPlayer.Market.AmountInMarket(Furs);
    }

    /// <summary>A human-owned on-map colonist able to found a colony (the starting pioneer/soldier carry roles; a plain colonist founds cleanly).</summary>
    private static Unit FounderColonist(Game game) =>
        game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
}
