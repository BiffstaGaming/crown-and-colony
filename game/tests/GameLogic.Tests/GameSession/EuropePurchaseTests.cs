using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Europe Train vs Purchase + artillery price escalation (<c>86d3c9qgy</c>, FreeCol <c>Specification</c>
/// trained/purchased partition + <c>ServerEurope.increasePrice</c>): priced units split into <b>trained</b>
/// specialists (skill &gt; 0) and <b>purchased</b> ships/artillery (skill 0); buying artillery ratchets <em>that
/// player's</em> artillery price +100 each time while ships/specialists stay flat. The escalated price persists
/// per player (save v29).
/// </summary>
public class EuropePurchaseTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Artillery = "model.unit.artillery";
    private const string Galleon = "model.unit.galleon";
    private const string ExpertFarmer = "model.unit.expertFarmer";
    private const string FreeColonist = "model.unit.freeColonist";

    // ---- Skill parse + the Train/Purchase partition ----

    [Fact]
    public void Skill_ParsesOffUnitTypes()
    {
        Assert.Equal(1, Classic.Unit(ExpertFarmer).Skill);  // a specialist
        Assert.Equal(0, Classic.Unit(Artillery).Skill);     // no skill
        Assert.Equal(0, Classic.Unit(FreeColonist).Skill);
    }

    [Fact]
    public void EuropeUnits_PartitionIntoTrainedSpecialistsAndPurchasedShipsAndArtillery()
    {
        Game game = Game.New(Classic, Seed);
        var trained = game.UnitTypesTrainedInEurope().Select(t => t.Id).ToHashSet();
        var purchased = game.UnitTypesPurchasedInEurope().Select(t => t.Id).ToHashSet();

        Assert.Contains(ExpertFarmer, trained);     // priced + skill > 0 → trained
        Assert.Contains(Artillery, purchased);      // priced + skill 0 → purchased
        Assert.Contains(Galleon, purchased);
        Assert.DoesNotContain(Artillery, trained);
        Assert.DoesNotContain(ExpertFarmer, purchased);
        Assert.DoesNotContain(FreeColonist, trained); // price 0 (recruited, not bought) → in neither list
        Assert.DoesNotContain(FreeColonist, purchased);
        Assert.Empty(trained.Intersect(purchased));   // disjoint
    }

    // ---- Artillery price escalation ----

    [Fact]
    public void BuyingArtillery_RatchetsItsPriceBy100_EachPurchase()
    {
        Game game = Game.New(Classic, Seed);
        game.HumanPlayer.Gold = 10_000;
        int basePrice = Classic.Unit(Artillery).Price; // 500

        Assert.Equal(basePrice, game.CheckBuyUnit(Artillery).Cost);
        game.BuyUnit(Artillery);
        Assert.Equal(basePrice + 100, game.CheckBuyUnit(Artillery).Cost); // 600
        game.BuyUnit(Artillery);
        Assert.Equal(basePrice + 200, game.CheckBuyUnit(Artillery).Cost); // 700
    }

    [Fact]
    public void BuyingAShip_KeepsItsPriceFlat()
    {
        Game game = Game.New(Classic, Seed);
        game.HumanPlayer.Gold = 10_000;
        int price = Classic.Unit(Galleon).Price;

        game.BuyUnit(Galleon);

        Assert.Equal(price, game.CheckBuyUnit(Galleon).Cost);               // ships don't escalate
        Assert.DoesNotContain(Galleon, game.HumanPlayer.UnitPriceOverrides.Keys);
    }

    [Fact]
    public void ArtilleryEscalation_IsPerPlayer_NotGlobal()
    {
        // The human buying artillery must not raise another colonial player's artillery price.
        Game game = Game.New(Classic, Seed);
        game.HumanPlayer.Gold = 10_000;
        game.BuyUnit(Artillery); // human's artillery now 600

        Player? other = game.Players.FirstOrDefault(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        if (other is not null)
        {
            Assert.DoesNotContain(Artillery, other.UnitPriceOverrides.Keys); // untouched
        }
        Assert.Equal(600, game.HumanPlayer.UnitPriceOverrides[Artillery]);
    }

    // ---- Persistence (v29) ----

    [Fact]
    public void EscalatedArtilleryPrice_RoundTripsThroughSave_V29()
    {
        Game game = Game.New(Classic, Seed);
        game.HumanPlayer.Gold = 10_000;
        game.BuyUnit(Artillery); // price now 600

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(55, SaveGame.CurrentVersion);
        Assert.Equal(600, restored.HumanPlayer.UnitPriceOverrides[Artillery]);
    }

    [Fact]
    public void AGameWhereNoArtilleryWasBought_OmitsTheUnitPricesToken()
    {
        string json = SaveGame.From(Game.New(Classic, Seed)).ToJson();
        Assert.DoesNotContain("UnitPrices", json); // byte-identical to v28 until a price escalates
    }
}
