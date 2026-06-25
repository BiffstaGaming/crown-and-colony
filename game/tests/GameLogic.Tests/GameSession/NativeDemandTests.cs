using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Native tribute demands (FreeCol <c>IndianDemandMission</c>): a hostile brave beside a human colony it can't
/// simply storm (defended, or only gold/food to take) demands tribute instead of pillaging. The human accepts
/// (pays — goods/gold leave the colony and the nation's alarm cools by 150) or refuses (no change; the brave may
/// raid next turn via the existing native AI). A demand auto-refuses if the human ignores it. Demand creation and
/// resolution are RNG-free, so the human's stream 0 stays byte-stable (ADR-009). Transient — never saved.
/// </summary>
public class NativeDemandTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Brave = "model.unit.brave";
    private const string Artillery = "model.unit.artillery";
    private const string Tobacco = "model.goods.tobacco";   // storable, non-food, non-military, refined? no (raw New World)
    private const string Coats = "model.goods.coats";       // storable, refined, pricey unit
    private const string TradeGoods = "model.goods.tradeGoods";
    private const string Muskets = "model.goods.muskets";   // military
    private const string Rum = "model.goods.rum";           // refined (made-from sugar), non-military
    private const string Tools = "model.goods.tools";       // building material (+ refined)

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    /// <summary>Founds a colony with an UNEQUIPPED founder so the warehouse starts empty — the starting pioneer/soldier's banked tools/muskets (correct per FreeCol, covered by ColonyFoundingEquipmentTests) aren't under test in these native scenarios.</summary>
    private static Colony FoundCleanColony(Game game)
    {
        Unit f = game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony);
        f.RoleId = RoleType.DefaultRoleId;
        f.RoleCount = 0;
        return game.FoundColony(f);
    }

    /// <summary>A fresh game with a human-founded colony (for direct <c>SelectDemand</c> ladder tests).</summary>
    private static (Game game, Colony colony) Stage(ulong seed = 7)
    {
        Game game = Game.New(Classic, seed);
        Colony colony = FoundCleanColony(game);
        return (game, colony);
    }

    /// <summary>Sets a colony's food store to exactly <paramref name="amount"/>.</summary>
    private static void SetFood(Colony colony, int amount) => colony.AddGoods(Colony.FoodId, amount - colony.Food);

    // ---- SelectDemand ladder (direct, every branch) --------------------------------------------------------

    [Fact]
    public void SelectDemand_WhenCalmAndFed_DemandsFoodAtCutoff()
    {
        // Branch 1 (FreeCol food-at-cutoff): Content or calmer + food >= cutoff. cutoff = clamp(food/2,30,100).
        // Unreachable under our Displeased+ demand gate (single alarm channel), implemented/tested for fidelity.
        (Game game, Colony colony) = Stage();
        SetFood(colony, 200); // cutoff = clamp(100,30,100) = 100

        (string? goods, int amount) = game.SelectDemand(colony, AlarmLevel.Content)!.Value;
        Assert.Equal(Colony.FoodId, goods);
        Assert.Equal(100, amount);
    }

    [Fact]
    public void SelectDemand_WhenDispleased_DemandsAStorableGoodsStack()
    {
        // Branch 2: Displeased or calmer → a non-food, non-military storable stack. capAmount(100) = 50.
        (Game game, Colony colony) = Stage();
        SetFood(colony, 0);
        colony.AddGoods(Tobacco, 100);

        (string? goods, int amount) = game.SelectDemand(colony, AlarmLevel.Displeased)!.Value;
        Assert.Equal(Tobacco, goods);
        Assert.Equal(50, amount);
    }

    [Fact]
    public void SelectDemand_RanksByStackValue_NotUnitPrice()
    {
        // The critic's fix: FreeCol's getSalePrice is the STACK total (amount * unit price), not the unit price.
        // coats has the higher UNIT price but trade goods the higher STACK value here, so the demand picks trade goods.
        (Game game, Colony colony) = Stage();
        SetFood(colony, 0);
        colony.AddGoods(Coats, 5);          // pricey per unit, small stack
        colony.AddGoods(TradeGoods, 100);   // cheap per unit, big stack
        Assert.True(game.Market.BidPrice(Coats) > game.Market.BidPrice(TradeGoods)); // coats wins on unit price…
        Assert.True(100 * game.Market.BidPrice(TradeGoods) > 5 * game.Market.BidPrice(Coats)); // …trade goods on stack value

        (string? goods, _) = game.SelectDemand(colony, AlarmLevel.Displeased)!.Value;
        Assert.Equal(TradeGoods, goods); // stack value, not unit price
    }

    [Fact]
    public void SelectDemand_WhenAngry_PrefersMilitaryGoods()
    {
        // Branch 3 (Angry+ skips the priciest-stack branch): military → building → trade → refined, first present.
        (Game game, Colony colony) = Stage();
        SetFood(colony, 0);
        colony.AddGoods(Muskets, 100);     // military — wins
        colony.AddGoods(TradeGoods, 100);  // would win the trade rung, but military comes first

        (string? goods, int amount) = game.SelectDemand(colony, AlarmLevel.Hateful)!.Value;
        Assert.Equal(Muskets, goods);
        Assert.Equal(50, amount);
    }

    [Fact]
    public void SelectDemand_WhenAngry_DemandsFood_ViaTheBuildingMaterialRung()
    {
        // 86d3c18n8: food is now a building material (the freeColonist's required-goods food=200). At Angry/Hateful
        // branch 2 (priciest non-food non-military) is skipped, so the category ladder runs: military → building →
        // trade → refined. A food-only colony hits the building rung and is demanded its food — as FreeCol does.
        (Game game, Colony colony) = Stage();
        SetFood(colony, 100); // only food present; the food-cutoff branch 1 is gated to Content-or-calmer, so skipped here

        (string? goods, int amount) = game.SelectDemand(colony, AlarmLevel.Hateful)!.Value;
        Assert.Equal(Colony.FoodId, goods);
        Assert.Equal(50, amount); // capAmount(100) = clamp(50,30,100) = 50
    }

    [Fact]
    public void SelectDemand_WhenAngry_PrefersFoodOverARawTradeStack()
    {
        // FreeCol's category ladder ranks the building-material rung (which now includes food) ABOVE trade/refined.
        // Tobacco is a raw farmed good (not building/trade/refined), so it would only be reached by the priciest
        // fallback — the building rung claims food first. This is the common Angry/Hateful FreeCol food demand.
        (Game game, Colony colony) = Stage();
        SetFood(colony, 100);
        colony.AddGoods(Tobacco, 100);

        (string? goods, _) = game.SelectDemand(colony, AlarmLevel.Hateful)!.Value;
        Assert.Equal(Colony.FoodId, goods); // food (building rung) beats the raw tobacco stack at Angry/Hateful
    }

    [Fact]
    public void SelectDemand_WhenDispleased_StillPrefersNonFoodOverFood()
    {
        // At Displeased-or-calmer branch 2 runs first and excludes food, so a non-food stack still wins there —
        // the food-via-building-rung change only bites at Angry/Hateful (where branch 2 is skipped).
        (Game game, Colony colony) = Stage();
        SetFood(colony, 100);
        colony.AddGoods(Tobacco, 100);

        (string? goods, _) = game.SelectDemand(colony, AlarmLevel.Displeased)!.Value;
        Assert.Equal(Tobacco, goods); // branch 2 (non-food) wins at Displeased
    }

    [Fact]
    public void SelectDemand_WhenAngry_FallsThroughToRefined()
    {
        // No military / building / trade goods present → the refined rung (rum is made-from sugar).
        (Game game, Colony colony) = Stage();
        SetFood(colony, 0);
        colony.AddGoods(Rum, 80);

        (string? goods, int amount) = game.SelectDemand(colony, AlarmLevel.Hateful)!.Value;
        Assert.Equal(Rum, goods);
        Assert.Equal(40, amount); // capAmount(80) = clamp(40,30,100) = 40
    }

    [Fact]
    public void SelectDemand_WhenAngry_PrefersBuildingMaterialOverRefined()
    {
        // tools is BOTH a building material and refined; the building rung comes before refined, so tools wins
        // over plain refined rum.
        (Game game, Colony colony) = Stage();
        SetFood(colony, 0);
        colony.AddGoods(Tools, 100);
        colony.AddGoods(Rum, 100);

        (string? goods, _) = game.SelectDemand(colony, AlarmLevel.Hateful)!.Value;
        Assert.Equal(Tools, goods);
    }

    [Theory]
    [InlineData(40, 30)]   // floored at GOODS_DEMAND_MIN
    [InlineData(100, 50)]  // count/2 at dx=3
    [InlineData(300, 100)] // capped at one cargo load
    public void SelectDemand_CapsTheAmount_Between30And100(int stocked, int expected)
    {
        (Game game, Colony colony) = Stage();
        SetFood(colony, 0);
        colony.AddGoods(Tobacco, stocked);

        (_, int amount) = game.SelectDemand(colony, AlarmLevel.Displeased)!.Value;
        Assert.Equal(expected, amount);
    }

    [Fact]
    public void SelectDemand_NoGoods_DemandsAGoldTwentieth()
    {
        (Game game, Colony colony) = Stage();
        SetFood(colony, 0);                 // nothing storable, no food worth taking
        game.HumanPlayer.Gold = 1000;

        (string? goods, int amount) = game.SelectDemand(colony, AlarmLevel.Hateful)!.Value;
        Assert.Null(goods);                 // a gold demand
        Assert.Equal(50, amount);           // 1000 / 20
    }

    [Fact]
    public void SelectDemand_NoGoodsSmallGold_DemandsAllTheGold()
    {
        (Game game, Colony colony) = Stage();
        SetFood(colony, 0);
        game.HumanPlayer.Gold = 10;         // 10 / 20 == 0 → take it all

        (string? goods, int amount) = game.SelectDemand(colony, AlarmLevel.Hateful)!.Value;
        Assert.Null(goods);
        Assert.Equal(10, amount);
    }

    [Fact]
    public void SelectDemand_NoGoodsNoGold_IsEmptyHanded()
    {
        (Game game, Colony colony) = Stage();
        SetFood(colony, 0);
        game.HumanPlayer.Gold = 0;

        Assert.Null(game.SelectDemand(colony, AlarmLevel.Hateful)); // nothing to take
    }

    // ---- AI path + accept / refuse / auto-resolve ----------------------------------------------------------

    /// <summary>A defended human colony (so pillage can't fire) with tobacco, and an enraged brave beside it.</summary>
    private static (Game game, Colony colony, Unit brave) StageDemand(ulong seed = 7, int tobacco = 100)
    {
        Game game = Game.New(Classic, seed);
        Colony colony = FoundCleanColony(game);
        if (tobacco > 0)
        {
            colony.AddGoods(Tobacco, tobacco);
        }
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position); // a human defender → not pillageable, only demandable

        string nation = game.NativeSettlements.First().NationTypeId;
        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit brave = game.SpawnUnit(Classic.Unit(Brave), adj, nation);
        foreach (NativeSettlement s in game.NativeSettlements.Where(s => s.NationTypeId == nation))
        {
            game.ChangeNativeAlarm(s, NativeSettlement.MaxAlarm); // Hateful → it demands
        }
        return (game, colony, brave);
    }

    [Fact]
    public void AnAlarmedBrave_BesideADefendedColony_RaisesADemand_DuringEndTurn()
    {
        (Game game, Colony colony, Unit brave) = StageDemand();

        game.EndTurn();

        NativeDemand demand = Assert.IsType<NativeDemand>(game.PendingDemand);
        Assert.Equal(colony.Id, demand.ColonyId);
        Assert.Equal(brave.OwnerNationId, demand.DemandingNationId);
        // EndTurn produces food, and food is a building material (86d3c18n8), so a Hateful demand lands on food via
        // the building-material rung (it out-ranks the raw tobacco stack) — the common FreeCol Angry/Hateful case.
        Assert.Equal(Colony.FoodId, demand.GoodsId);
        Assert.InRange(demand.Amount, 30, 100); // capAmount clamps to [30,100]
        Assert.Equal(colony.OwnerId, game.Colonies.First(c => c.Id == colony.Id).OwnerId); // not captured — still the human's
    }

    [Fact]
    public void AcceptPendingDemand_TakesTheGoods_AndCoolsTheNationsAlarm()
    {
        (Game game, Colony colony, Unit brave) = StageDemand();
        game.EndTurn();
        NativeDemand demand = game.PendingDemand!;
        NativeSettlement home = game.NativeSettlements.First(s => s.NationTypeId == brave.OwnerNationId);
        int alarmBefore = home.Alarm;
        string demanded = demand.GoodsId!;                 // whatever the ladder picked (food after EndTurn production)
        colony.AddGoods(demanded, demand.Amount);          // ensure the colony can pay in full, so the transfer is exact
        int heldBefore = colony.StoreOf(demanded);

        Assert.True(game.AcceptPendingDemand());

        Assert.Null(game.PendingDemand);                                   // resolved
        Assert.Equal(heldBefore - demand.Amount, colony.StoreOf(demanded)); // the demanded tribute left the colony
        Assert.Equal(System.Math.Max(0, alarmBefore - 150), home.Alarm);  // appeased by 150
    }

    [Fact]
    public void AcceptPendingDemand_GoldDemand_DebitsTheHumansGold()
    {
        // A gold demand needs a colony with nothing storable AND no food — but EndTurn production restocks food
        // (it's storable), so stage the demand directly (CreateNativeDemand) on a drained colony instead of via End Turn.
        (Game game, Colony colony, Unit brave) = StageDemand(tobacco: 0);
        colony.AddGoods(Colony.FoodId, -colony.Food); // drain the colony so only gold is left to demand
        game.HumanPlayer.Gold = 1000;
        Player nation = game.Players.First(p => p.NationId == brave.OwnerNationId);

        game.CreateNativeDemand(nation, brave, colony);
        NativeDemand demand = game.PendingDemand!;
        Assert.Null(demand.GoodsId);          // gold
        Assert.Equal(50, demand.Amount);      // 1000 / 20

        Assert.True(game.AcceptPendingDemand());
        Assert.Equal(950, game.HumanPlayer.Gold);
    }

    [Fact]
    public void RefusePendingDemand_ClearsIt_WithNoTransferOrTensionChange()
    {
        (Game game, Colony colony, Unit brave) = StageDemand();
        game.EndTurn();
        NativeSettlement home = game.NativeSettlements.First(s => s.NationTypeId == brave.OwnerNationId);
        int alarmBefore = home.Alarm;
        int tobaccoBefore = colony.StoreOf(Tobacco);

        game.RefusePendingDemand();

        Assert.Null(game.PendingDemand);
        Assert.Equal(tobaccoBefore, colony.StoreOf(Tobacco)); // nothing taken
        Assert.Equal(alarmBefore, home.Alarm);                // tension unchanged (refusal's bite is the next-turn raid)
    }

    [Fact]
    public void AcceptPendingDemand_AfterTheColonyIsGone_IsANoOp()
    {
        (Game game, Colony colony, _) = StageDemand();
        game.EndTurn();
        Assert.NotNull(game.PendingDemand);

        // The colony is captured by a rival before the human answers (cross-turn edge the critic flagged).
        colony.OwnerId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

        Assert.False(game.AcceptPendingDemand()); // nothing to pay — no throw, no transfer
        Assert.Null(game.PendingDemand);
    }

    [Fact]
    public void AnIgnoredDemand_IsAutoRefused_AtTheNextEndTurn()
    {
        (Game game, Colony colony, _) = StageDemand();
        game.EndTurn();
        Assert.NotNull(game.PendingDemand);
        int tobaccoBefore = colony.StoreOf(Tobacco);

        game.EndTurn(); // the human never answered → the demand is refused at the top of the turn (no payment)

        Assert.Equal(tobaccoBefore, colony.StoreOf(Tobacco)); // the ignored demand cost nothing
    }

    // ---- ADR-009 byte-stability --------------------------------------------------------------------------

    [Fact]
    public void Accepting_IsRngFree_StreamZeroCursorUnmoved()
    {
        (Game game, _, _) = StageDemand();
        game.EndTurn();
        Assert.NotNull(game.PendingDemand);

        var before = game.RandomState;
        game.AcceptPendingDemand();
        Assert.Equal(before, game.RandomState); // paying tribute draws no randomness
    }

    [Fact]
    public void RaisingDemands_DoesNotTouchTheHumansStream0()
    {
        // Same seed + identical defended colony/brave in both games; only one nation is enraged, so only it raises
        // (and auto-refuses, unanswered) demands turn after turn. That game's braves diverge (demand vs wander), yet
        // the human's stream 0 and scoped state stay byte-identical: demand creation/auto-refuse draw no RNG and
        // never touch human state (ADR-009).
        (Game calm, _, _) = StageCalmTwin(seed: 13);
        (Game demanding, _, _) = StageDemand(seed: 13);

        for (int turn = 0; turn < 30; turn++)
        {
            calm.EndTurn();
            demanding.EndTurn();
        }

        Assert.NotEqual(SaveGame.From(calm).ToJson(), SaveGame.From(demanding).ToJson()); // the braves genuinely diverged…
        Assert.Equal(calm.RandomState, demanding.RandomState);                            // …yet stream 0 is untouched
        Assert.Equal(calm.HumanPlayer.Gold, demanding.HumanPlayer.Gold);
        Assert.Equal(calm.HumanPlayer.Immigration, demanding.HumanPlayer.Immigration);
        Assert.Equal(calm.HumanPlayer.RecruitDock, demanding.HumanPlayer.RecruitDock);
    }

    /// <summary>The calm twin of <see cref="StageDemand"/>: identical defended colony + brave, but the nation is NOT enraged.</summary>
    private static (Game game, Colony colony, Unit brave) StageCalmTwin(ulong seed)
    {
        Game game = Game.New(Classic, seed);
        Colony colony = FoundCleanColony(game);
        colony.AddGoods(Tobacco, 100);
        game.SpawnUnit(Classic.Unit(Artillery), colony.Position);
        string nation = game.NativeSettlements.First().NationTypeId;
        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit brave = game.SpawnUnit(Classic.Unit(Brave), adj, nation);
        // No alarm change — the nation stays calm, so the brave wanders rather than demanding.
        return (game, colony, brave);
    }
}
