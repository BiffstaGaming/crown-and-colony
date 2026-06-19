using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Native land purchase (<c>86d3c9tha</c>, FreeCol <c>Player.getLandPrice</c> / <c>ServerPlayer.csClaimLand</c>):
/// a native-owned tile is bought (gold to the tribe — we keep no native treasury, so it just leaves the buyer) or
/// taken (+200 alarm); Peter Minuit makes it free + peaceful. A claimed tile becomes a saved override (v26) that
/// the native-ownership re-derivation honours so it never reverts to the natives.
/// </summary>
public class NativeLandPurchaseTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Minuit = "model.foundingFather.peterMinuit";

    private const int Factor = 60;   // classic-medium landPriceFactor
    private const int Base = 100;    // getLandPrice +100
    private const int TakenAlarm = 200; // TENSION_ADD_LAND_TAKEN

    // Land is valued from the UNMODIFIED tile potential (no founding-father boosts) — mirror that here.
    private static int NonFoodSum(Game game, Position tile) =>
        Classic.GoodsTypes.Where(g => !g.IsFood).Sum(g => game.TileYieldPotential(tile, g.Id));

    /// <summary>A new game + a native-owned land tile that is NOT a settlement tile (so it is purchasable).</summary>
    private static (Game game, Position tile, string nation) Staged()
    {
        Game game = Game.New(Classic, Seed);
        (Position tile, string nation) = game.Map.NativeOwners
            .Where(kv => game.NativeSettlementAt(kv.Key) is null)
            .OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X)
            .Select(kv => (kv.Key, kv.Value)).First();
        return (game, tile, nation);
    }

    // ---- Price ----

    [Fact]
    public void LandPrice_OnANativeTile_IsFactorTimesNonFoodYieldPlusBase()
    {
        (Game game, Position tile, _) = Staged();
        Assert.Equal(Factor * NonFoodSum(game, tile) + Base, game.LandPrice(tile));
    }

    [Fact]
    public void LandPrice_ExcludesFoodFromTheSum()
    {
        // A native-owned tile that yields food (grain/fish): the price omits that food from the sum.
        (Game game, Position tile, _) = (Game.New(Classic, Seed), default(Position), "");
        Position foodTile = game.Map.NativeOwners.Keys
            .Where(p => game.NativeSettlementAt(p) is null
                && Classic.GoodsTypes.Where(g => g.IsFood).Sum(g => game.TileYield(p, g.Id)) > 0)
            .OrderBy(p => p.Y).ThenBy(p => p.X)
            .First();

        int withFood = Factor * Classic.GoodsTypes.Sum(g => game.TileYieldPotential(foodTile, g.Id)) + Base;
        Assert.Equal(Factor * NonFoodSum(game, foodTile) + Base, game.LandPrice(foodTile)); // food excluded
        Assert.True(game.LandPrice(foodTile) < withFood); // and that genuinely drops the food yield
    }

    [Fact]
    public void LandPrice_IgnoresFoundingFatherYieldModifiers()
    {
        // Henry Hudson (+100% furs) must NOT inflate the land price — FreeCol values land from the unmodified
        // tile potential (getPotentialProduction with a null owner), not the acting player's father-boosted yield.
        Game game = Game.New(Classic, Seed);
        Position furTile = game.Map.AllPositions()
            .First(p => game.NativeSettlementAt(p) is null && game.TileYieldPotential(p, "model.goods.furs") > 0);
        game.Map.SetNativeOwner(furTile, "model.nationType.apache"); // make it native land so a price applies
        int before = game.LandPrice(furTile);
        Assert.True(before > Base); // the fur yield genuinely contributes to the price

        game.HumanPlayer.CongressList.Add("model.foundingFather.henryHudson");

        Assert.Equal(before, game.LandPrice(furTile)); // unchanged — founding fathers do not value land
    }

    [Fact]
    public void LandPrice_IsZero_OffNativeLand()
    {
        Game game = Game.New(Classic, Seed);
        Position notNative = game.Map.AllPositions()
            .First(p => !game.Map.TerrainAt(p).IsWater && !game.Map.IsNativeOwned(p));
        Assert.Equal(0, game.LandPrice(notNative));
    }

    [Fact]
    public void LandPrice_IsZero_UnderPeterMinuit()
    {
        (Game game, Position tile, _) = Staged();
        Assert.True(game.LandPrice(tile) > 0); // priced before Minuit
        game.HumanPlayer.CongressList.Add(Minuit);
        Assert.Equal(0, game.LandPrice(tile)); // Minuit's landPaymentModifier −100% → free
    }

    // ---- Pay ----

    [Fact]
    public void ClaimByPaying_DeductsThePrice_AndTransfersTheTile()
    {
        (Game game, Position tile, _) = Staged();
        game.HumanPlayer.Gold = 100_000;
        int price = game.LandPrice(tile);

        game.ClaimLandByPaying(tile);

        Assert.Equal(100_000 - price, game.HumanPlayer.Gold); // paid exactly the price (nothing credited anywhere)
        Assert.False(game.Map.IsNativeOwned(tile));           // no longer native land
        Assert.True(game.Map.IsClaimedFromNatives(tile));     // recorded as the player's
    }

    [Fact]
    public void ClaimByPaying_WithoutEnoughGold_Throws_AndChangesNothing()
    {
        (Game game, Position tile, _) = Staged();
        game.HumanPlayer.Gold = 0;

        Assert.Throws<InvalidMoveException>(() => game.ClaimLandByPaying(tile));
        Assert.Equal(0, game.HumanPlayer.Gold);
        Assert.True(game.Map.IsNativeOwned(tile));            // still the natives'
        Assert.False(game.Map.IsClaimedFromNatives(tile));
    }

    [Fact]
    public void ClaimByPaying_UnderMinuit_IsFree()
    {
        (Game game, Position tile, _) = Staged();
        game.HumanPlayer.CongressList.Add(Minuit);
        game.HumanPlayer.Gold = 5;

        game.ClaimLandByPaying(tile); // price 0 → no gold needed

        Assert.Equal(5, game.HumanPlayer.Gold);
        Assert.True(game.Map.IsClaimedFromNatives(tile));
    }

    // ---- Steal ----

    [Fact]
    public void ClaimByStealing_TakesFree_AndAngersTheOwningNation()
    {
        (Game game, Position tile, string nation) = Staged();
        game.HumanPlayer.Gold = 1000;
        var alarmBefore = game.NativeSettlements.Where(s => s.NationTypeId == nation)
            .ToDictionary(s => s.Id, s => s.Alarm);

        game.ClaimLandByStealing(tile);

        Assert.Equal(1000, game.HumanPlayer.Gold);            // no gold changes hands
        Assert.False(game.Map.IsNativeOwned(tile));
        Assert.True(game.Map.IsClaimedFromNatives(tile));
        // every settlement of the robbed nation gains +200 alarm (clamped at MaxAlarm)
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId == nation), s =>
            Assert.Equal(System.Math.Min(NativeSettlement.MaxAlarm, alarmBefore[s.Id] + TakenAlarm), s.Alarm));
        // a different nation's settlements are untouched
        Assert.All(game.NativeSettlements.Where(s => s.NationTypeId != nation), s => Assert.Equal(0, s.Alarm));
    }

    // ---- Persistence + re-derivation ----

    [Fact]
    public void ClaimedLand_SurvivesReDerivation_AndRoundTripsThroughSave()
    {
        (Game game, Position tile, _) = Staged();
        game.HumanPlayer.Gold = 100_000;
        game.ClaimLandByPaying(tile);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(39, SaveGame.CurrentVersion);
        Assert.True(restored.Map.IsClaimedFromNatives(tile));  // the override round-trips
        Assert.False(restored.Map.IsNativeOwned(tile));        // and the re-derivation on load honours it (not re-claimed)
        Assert.Equal(
            game.Map.NativeOwners.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).ToList(),
            restored.Map.NativeOwners.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).ToList());
    }

    [Fact]
    public void AGameWithNoPurchases_OmitsTheClaimedTilesToken()
    {
        Game game = Game.New(Classic, Seed);
        Assert.Empty(game.Map.ClaimedFromNatives);
        Assert.DoesNotContain("ClaimedTiles", SaveGame.From(game).ToJson());
    }
}
