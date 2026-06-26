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

    private const string DutchId = "model.nation.dutch";
    private const string FrenchId = "model.nation.french";
    private const string EnglishId = "model.nation.english";
    private const string SpanishId = "model.nation.spanish";

    // The two easiest classic levels ship expertStartingUnits=true; medium and harder ship it false.
    private const string Easy = "model.difficulty.easy";
    private const string VeryEasy = "model.difficulty.veryEasy";

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
        Assert.Equal(63, save.Version); // the current save version (the human's chosen nation rides the existing NationId field, added at v49)
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

    // ---- Nation-specific starting roster (86d3e4br5) ----

    [Fact]
    public void DutchHuman_LandsAMerchantman_NotACaravel()
    {
        // The Dutch (trade) override the ship slot: a merchantman replaces the default caravel; the colonists are unchanged.
        var roster = HumanRoster(DutchId);
        Assert.Contains(roster, u => u.TypeId == "model.unit.merchantman");
        Assert.DoesNotContain(roster, u => u.TypeId == "model.unit.caravel");
        Assert.Equal(2, roster.Count(u => u.TypeId == "model.unit.freeColonist")); // pioneer + soldier still free colonists
    }

    [Fact]
    public void FrenchHuman_LandsAHardyPioneer()
    {
        // The French (cooperation) override the pioneer slot: a hardy pioneer; the ship stays the default caravel.
        var roster = HumanRoster(FrenchId);
        Assert.Single(roster, u => u.TypeId == "model.unit.hardyPioneer" && u.RoleId == "model.role.pioneer");
        Assert.Contains(roster, u => u.TypeId == "model.unit.caravel");
    }

    [Fact]
    public void SpanishHuman_LandsAVeteranSoldier()
    {
        // The Spanish (conquest) override the soldier slot with a (non-expert) veteran soldier — present even on medium,
        // because the spec marks it a regular start, not an expert-starting-units variant.
        var roster = HumanRoster(SpanishId);
        Assert.Single(roster, u => u.TypeId == "model.unit.veteranSoldier" && u.RoleId == "model.role.soldier");
        Assert.Contains(roster, u => u.TypeId == "model.unit.caravel");
    }

    [Fact]
    public void EnglishHuman_LandsTheDefaultRoster()
    {
        // The English (immigration) add no starting-unit override, so their roster equals the classic default:
        // a free-colonist pioneer + a free-colonist soldier + a caravel.
        var roster = HumanRoster(EnglishId);
        Assert.Equal(2, roster.Count(u => u.TypeId == "model.unit.freeColonist"));
        Assert.Single(roster, u => u.TypeId == "model.unit.freeColonist" && u.RoleId == "model.role.pioneer");
        Assert.Single(roster, u => u.TypeId == "model.unit.freeColonist" && u.RoleId == "model.role.soldier");
        Assert.Single(roster, u => u.TypeId == "model.unit.caravel");
    }

    [Fact]
    public void EnglishHuman_OnMedium_HasTheSameRosterAsTheNationLessHuman()
    {
        // The default-nation roster equals the English roster (immigration == default), so picking England on the
        // default level gives the same units the nation-less human gets — just landed under England's flag.
        Assert.Equal(
            RosterTypesAndRoles(Game.New(Classic, seed: 9)),
            RosterTypesAndRoles(Game.New(Classic, seed: 9, humanNationId: EnglishId)));
    }

    [Theory]
    [InlineData(Easy)]
    [InlineData(VeryEasy)]
    public void ExpertStartingUnits_OnEasyLevels_UpgradesTheSoldierSlotToAVeteran(string level)
    {
        // expertStartingUnits is on for veryEasy/easy: the default nation's soldier slot upgrades from a free colonist
        // to a veteran soldier (FreeCol overlays the expert map onto the default map by slot). English == default here.
        // The difficulty tuning is fixed at ruleset-load time, so load the ruleset at the easy level under test.
        Ruleset easy = Ruleset.LoadClassic(level);
        Assert.True(easy.Difficulty.ExpertStartingUnits); // sanity: this level enables experts
        var roster = RosterTypesAndRoles(Game.New(easy, seed: 9, humanNationId: EnglishId));
        Assert.Single(roster, u => u.TypeId == "model.unit.veteranSoldier" && u.RoleId == "model.role.soldier");
        Assert.Single(roster, u => u.TypeId == "model.unit.freeColonist" && u.RoleId == "model.role.pioneer"); // pioneer slot unchanged
        Assert.Single(roster, u => u.TypeId == "model.unit.caravel");
    }

    [Fact]
    public void ExpertStartingUnits_IsOffOnMedium_SoTheSoldierStaysAFreeColonist()
    {
        // The default (medium) level must NOT upgrade the soldier — that is what keeps the default game byte-identical.
        Assert.DoesNotContain(HumanRoster(EnglishId), u => u.TypeId == "model.unit.veteranSoldier");
        Assert.False(Classic.Difficulty.ExpertStartingUnits);
    }

    [Fact]
    public void DefaultNewGame_RosterIsUnchanged_TheClassicPioneerSoldierCaravel()
    {
        // The nation-less classic human keeps exactly today's roster (ADR-009): a free-colonist pioneer + free-colonist
        // soldier + caravel, three units, no veteran/merchantman/hardy pioneer.
        var roster = RosterTypesAndRoles(Game.New(Classic, seed: 9));
        Assert.Equal(3, roster.Count);
        Assert.Single(roster, u => u.TypeId == "model.unit.freeColonist" && u.RoleId == "model.role.pioneer");
        Assert.Single(roster, u => u.TypeId == "model.unit.freeColonist" && u.RoleId == "model.role.soldier");
        Assert.Single(roster, u => u.TypeId == "model.unit.caravel");
    }

    /// <summary>The human player's actual landed roster (unit type id + role id) for a nation, on the default (medium) level.</summary>
    private static System.Collections.Generic.List<(string TypeId, string RoleId)> HumanRoster(string? nationId) =>
        RosterTypesAndRoles(Game.New(Classic, seed: 9, humanNationId: nationId));

    private static System.Collections.Generic.List<(string TypeId, string RoleId)> RosterTypesAndRoles(Game game) =>
        game.PlayerUnits.OrderBy(u => u.Type.Id).ThenBy(u => u.RoleId).Select(u => (u.Type.Id, u.RoleId)).ToList();

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
