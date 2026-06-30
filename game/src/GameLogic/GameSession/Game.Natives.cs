using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// Player-vs-native scout/missionary actions that go beyond the peaceful speak-with-chief / learn-skill /
/// establish-mission set: <b>inciting a native settlement to war against a European rival</b> (a missionary action,
/// FreeCol <c>InGameController.incite</c>). The scout-initiated <b>demand-tribute-from-natives</b> action already lives
/// on the main <see cref="Game"/> partial (<see cref="Game.CheckDemandTribute"/>/<see cref="Game.DemandTribute(Unit, Position)"/>);
/// the on-map <c>NativeSettlementPanel</c> simply surfaces it. This partial holds the native-incite rules only — kept off
/// the 12k-line core file but the same <c>Game</c> class (ADR-006: every outcome is a Game oracle the panel forwards to).
/// </summary>
public partial class Game
{
    /// <summary>The role ability a unit must carry to incite a tribe (FreeCol <c>model.ability.inciteNatives</c>, granted by <c>model.role.missionary</c>).</summary>
    private const string InciteNativesAbility = "model.ability.inciteNatives";

    /// <summary>
    /// Alarm the incited tribe gains toward the rival on a successful incite (FreeCol <c>Tension.WAR_MODIFIER</c> =
    /// <c>Level.HATEFUL.limit</c> = 1000): a full war-level spike that drives the tribe's braves against that power.
    /// Applied to the settlement's per-rival alarm channel, nation-wide (every settlement of that nation), so the whole
    /// tribe turns on the rival — mirroring FreeCol applying the tension to the native <em>player</em>.
    /// </summary>
    public const int InciteWarAlarm = 1000;

    /// <summary>The minimum an incite ever costs (FreeCol <c>InGameController.incite</c> floors <c>goldToPay</c> at 650).</summary>
    internal const int InciteFloorCost = 650;

    /// <summary>The base bribe when the tribe is angrier at the inciter than at the rival — a hard sell (FreeCol's 10000).</summary>
    internal const int InciteBaseCostHostile = 10000;

    /// <summary>The base bribe when the tribe is at least as friendly to the inciter as to the rival — an easy sell (FreeCol's 5000).</summary>
    internal const int InciteBaseCostFriendly = 5000;

    /// <summary>Gold added per point the inciter's alarm exceeds the rival's (FreeCol <c>goldToPay += 20 * (payingValue - targetValue)</c>).</summary>
    internal const int InciteCostPerAlarmPoint = 20;

    /// <summary>
    /// The gold an incite of <paramref name="settlement"/> against the rival <paramref name="rivalId"/> costs the inciter
    /// (FreeCol <c>InGameController.incite</c>): based on the gap between the tribe's alarm toward the <b>inciter</b>
    /// (<c>payingValue</c>) and toward the <b>rival</b> (<c>targetValue</c>). When the tribe is angrier at the inciter
    /// than at the rival (a harder sell) the base is <see cref="InciteBaseCostHostile"/> (10000), else
    /// <see cref="InciteBaseCostFriendly"/> (5000); then <c>+ 20 × (inciterAlarm − rivalAlarm)</c>, floored at
    /// <see cref="InciteFloorCost"/> (650). So a tribe that already hates the rival incites cheaply, and one that likes
    /// the rival (but not you) is dear. Pure; draws no RNG (ADR-009). The inciter's perspective is the human channel
    /// when called for the human; the rival's is its own channel.
    /// </summary>
    /// <param name="settlement">The settlement being incited.</param>
    /// <param name="inciterId">The colonial player paying the bribe (the human is <see cref="HumanAlarmChannel"/>).</param>
    /// <param name="rivalId">The colonial player the tribe is being incited against.</param>
    internal int InciteNativesCost(NativeSettlement settlement, int inciterId, int rivalId)
    {
        int payingValue = settlement.AlarmFor(inciterId);
        int targetValue = settlement.AlarmFor(rivalId);
        int gold = payingValue > targetValue ? InciteBaseCostHostile : InciteBaseCostFriendly;
        gold += InciteCostPerAlarmPoint * (payingValue - targetValue);
        return System.Math.Max(gold, InciteFloorCost);
    }

    /// <summary>
    /// The live European rivals <paramref name="unit"/>'s owner could incite this settlement's tribe against — every
    /// colonial power other than the inciter (FreeCol offers <c>getLiveEuropeanPlayers(player)</c>), in stable player-id
    /// order. Empty in a solo game (no rival exists). A read helper for the panel's rival picker (ADR-006).
    /// </summary>
    public IReadOnlyList<Player> IncitableRivals(Unit unit) =>
        _players
            .Where(p => p.PlayerId != unit.OwnerId && p.PlayerType == PlayerType.Colonial)
            .OrderBy(p => p.PlayerId)
            .ToList();

    /// <summary>
    /// Whether <paramref name="unit"/> may incite <paramref name="settlement"/>'s tribe against the rival
    /// <paramref name="rivalId"/> now (FreeCol's <c>incite</c> preconditions): an on-map <b>missionary-role</b> unit
    /// (carrying <see cref="InciteNativesAbility"/>) with movement left, on or adjacent to the settlement, where
    /// <paramref name="rivalId"/> is a distinct live colonial power and the inciter can afford the bribe. A read oracle
    /// (ADR-006): no RNG, no mutation. The <see cref="MoveCheck.Cost"/> carries the incite price (FreeCol
    /// <see cref="InciteNativesCost"/>) so the panel can show it on the button and re-check affordability.
    /// </summary>
    public MoveCheck CheckInciteNatives(Unit unit, NativeSettlement settlement, int rivalId) =>
        CheckInciteNatives(PlayerById(unit.OwnerId) ?? _human, unit, settlement, rivalId);

    /// <summary>Whether <paramref name="player"/>'s <paramref name="unit"/> may incite the tribe against <paramref name="rivalId"/> (the owner-scoped form the rules engine and AI use).</summary>
    internal MoveCheck CheckInciteNatives(Player player, Unit unit, NativeSettlement settlement, int rivalId)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (!Ruleset.Role(unit.RoleId).GrantedAbilities.GetValueOrDefault(InciteNativesAbility))
        {
            return MoveCheck.No($"A {unit.Type.ShortName} cannot incite the natives — only a missionary can.");
        }
        if (unit.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (unit.Position != settlement.Position && !unit.Position.IsAdjacentTo(settlement.Position))
        {
            return MoveCheck.No("Move next to the settlement to incite its tribe.");
        }
        if (rivalId == player.PlayerId)
        {
            return MoveCheck.No("You cannot incite a tribe against yourself.");
        }
        if (PlayerById(rivalId) is not { PlayerType: PlayerType.Colonial })
        {
            return MoveCheck.No("There is no such rival power to incite against.");
        }
        int cost = InciteNativesCost(settlement, player.PlayerId, rivalId);
        if (player.Gold < cost)
        {
            return MoveCheck.No($"The chief demands {cost} gold to make war on them — you cannot afford it.");
        }
        return MoveCheck.Yes(cost);
    }

    /// <summary>
    /// The outcome of an <see cref="InciteNatives(Unit, NativeSettlement, int)"/> attempt: <see cref="Cost"/> is the gold
    /// paid (0 if the incite did not go through) and <see cref="RivalId"/> is the power the tribe was turned against. A
    /// single-shot result the presentation reports as "the tribe will war on the X (N gold)".
    /// </summary>
    /// <param name="Incited">True when the tribe was turned against the rival (the bribe was paid).</param>
    /// <param name="Cost">The gold paid to the chief (0 on a no-op).</param>
    /// <param name="RivalId">The rival the tribe was incited against.</param>
    public readonly record struct InciteNativesResult(bool Incited, int Cost, int RivalId);

    /// <summary>
    /// Incites <paramref name="settlement"/>'s tribe to war against the rival <paramref name="rivalId"/> with the human's
    /// missionary <paramref name="unit"/> (FreeCol <c>InGameController.incite</c>): pays the chief the
    /// <see cref="InciteNativesCost"/> bribe, then raises that nation's alarm toward the rival by
    /// <see cref="InciteWarAlarm"/> (1000 — war level, every settlement of the tribe) and the rival's colonial tension
    /// toward the inciter by <see cref="TensionWarInciter"/> (250). The tribe's resulting hostility drives its braves at
    /// the rival under the existing native-AI rules; the actual stance change is left to fall out naturally. The gold
    /// simply leaves the inciter's purse (no native treasury is modelled, as with scout beads / plunder). Ends the unit's
    /// turn. Deterministic — draws no RNG (ADR-009).
    /// </summary>
    /// <returns>Whether the tribe was incited and how much it cost.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckInciteNatives(Unit, NativeSettlement, int)"/>.</exception>
    public InciteNativesResult InciteNatives(Unit unit, NativeSettlement settlement, int rivalId) =>
        InciteNatives(_human, unit, settlement, rivalId);

    /// <summary>Incites on behalf of <paramref name="player"/> (the unit's owner) — the owner-scoped form for the AI / foreign powers.</summary>
    internal InciteNativesResult InciteNatives(Player player, Unit unit, NativeSettlement settlement, int rivalId)
    {
        MoveCheck check = CheckInciteNatives(player, unit, settlement, rivalId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        int cost = check.Cost;
        player.Gold -= cost; // the bribe leaves the inciter's purse (no native treasury modelled)

        // Turn the whole tribe against the rival: a war-level alarm spike on the rival's per-settlement channel, applied
        // nation-wide (FreeCol applies WAR_MODIFIER to the native player; we track alarm per settlement, so every
        // settlement of this nation gets it). The rival then resents the instigator (colonial tension +250).
        foreach (NativeSettlement s in _nativeSettlements.Where(s => s.NationTypeId == settlement.NationTypeId))
        {
            ChangeNativeAlarm(s, rivalId, InciteWarAlarm); // also propagates a share to the tribe channel (PropagateToTribe)
        }
        RaiseTribeTension(settlement.NationTypeId, rivalId, InciteWarAlarm); // the whole NATION goes to war (FreeCol applies WAR_MODIFIER to the native player itself)
        ChangeTension(rivalId, player.PlayerId, TensionWarInciter, symmetric: false); // FreeCol TENSION_ADD_WAR_INCITER

        unit.MovementLeft = 0; // the audience ends the unit's turn
        return new InciteNativesResult(Incited: true, Cost: cost, RivalId: rivalId);
    }

    // ============================================================================================================
    // Haggle honours the named price (86d3fpzqd) — FreeCol's price-carrying trade.
    //
    // The haggle loop (Game.TryHaggleSell/TryHaggleBuy) surfaces an offer/counter; the deal closes at the AGREED
    // price, not the settlement's standard valuation. FreeCol's NativeTrade carries the negotiated price through to
    // the moveGoods (NativeAIPlayer.handleTrade ACK at the player's offer), so a hard bargain actually pays off. We
    // mirror that with price-carrying overloads that validate the agreed price against the haggle band — the panel
    // can only present a figure the haggle oracle could have produced, so the UI cannot mint free gold (ADR-006).
    // ============================================================================================================

    /// <summary>
    /// The widest a settlement will ever stray from its standard price during a haggle (FreeCol's haggle ladder is
    /// bounded by the patience roll: <see cref="NativeHaggleNumber"/> walk-down/up steps of ×9/10 · ×11/10). A deal at
    /// more than this multiple of the standard valuation could never arise from the haggle loop, so a price-carrying
    /// trade rejects it — the engine's guard that the agreed figure is one the haggle oracle could have reached, not a
    /// number the UI invented. Selling: the player can push the price up to ~×(11/10)^3 ≈ 1.331 above standard before
    /// the chief is certain to walk; buying: down to ~×(9/10)^3 ≈ 0.729. We clamp generously (the chief never pays/charges
    /// more than 2× / less than ½× standard) so legitimate haggled deals always pass and only absurd figures are caught.
    /// </summary>
    private const int HaggleSellPriceCeilingPercent = 200; // a seller can never extract more than 2× the standard price
    private const int HaggleBuyPriceFloorPercent = 50;     // a buyer can never pay less than ½ the standard price

    /// <summary>
    /// Sells goods from a ship to an adjacent settlement at an <b>agreed haggled price</b> (FreeCol's price-carrying
    /// trade — the deal closes at the figure the chief accepted in <see cref="TryHaggleSell"/>, not the standard
    /// <see cref="NativeSalePrice(NativeSettlement, string, int)"/>). Identical to <see cref="SellToNatives(Unit, NativeSettlement, string, int)"/> in
    /// every other respect (cargo moves into the store, gold credited with no European tax, goodwill, re-priced wanted
    /// goods, ends the ship's turn) — only the gold figure differs. <paramref name="agreedPrice"/> is validated to be
    /// non-negative and no more than <see cref="HaggleSellPriceCeilingPercent"/>% of the standard price (a deal the
    /// haggle ladder could plausibly reach); a figure outside that band falls back to the standard price, so the UI can
    /// never mint gold. Goodwill scales with the gold actually paid (FreeCol ALARM_BONUS_SELL).
    /// </summary>
    /// <returns>The gold received (the agreed price).</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSellToNatives"/>.</exception>
    public int SellToNatives(Unit ship, NativeSettlement settlement, string goodsId, int amount, int agreedPrice) =>
        SellToNatives(_human, ship, settlement, goodsId, amount, agreedPrice);

    /// <summary>Sells at an agreed haggled price on behalf of <paramref name="player"/> (the ship's owner).</summary>
    internal int SellToNatives(Player player, Unit ship, NativeSettlement settlement, string goodsId, int amount, int agreedPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        MoveCheck check = CheckSellToNatives(ship, settlement, goodsId, amount);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        int standard = check.Cost;
        int price = ClampHaggledSalePrice(agreedPrice, standard);
        ship.AddCargo(goodsId, -amount);
        settlement.AddGoods(goodsId, amount); // the goods join the settlement's store (FreeCol moveGoods unit→settlement)
        player.Gold += price; // natives pay in gold at the agreed price; no European market tax
        ChangeNativeAlarm(settlement, player.PlayerId, -Math.Max(1, price / 50)); // goodwill scales with what was paid (FreeCol ALARM_BONUS_SELL)
        RecomputeWantedGoods(settlement); // the fuller store re-prices its cravings (FreeCol csSell → updateWantedGoods)
        ship.MovementLeft = 0; // opening a trade session ends the ship's turn
        return price;
    }

    /// <summary>Buys goods from an adjacent settlement at an <b>agreed haggled price</b> (FreeCol's price-carrying trade — the figure the chief accepted in <see cref="TryHaggleBuy"/>, not the standard <see cref="NativeBuyPrice"/>).</summary>
    /// <returns>The gold paid (the agreed price).</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckBuyFromNatives"/>.</exception>
    public int BuyFromNatives(Unit ship, NativeSettlement settlement, string goodsId, int amount, int agreedPrice) =>
        BuyFromNatives(_human, ship, settlement, goodsId, amount, agreedPrice);

    /// <summary>Buys at an agreed haggled price on behalf of <paramref name="player"/> (the ship's owner). The agreed price must be affordable and within the haggle floor (it can never undercut <see cref="HaggleBuyPriceFloorPercent"/>% of standard).</summary>
    internal int BuyFromNatives(Player player, Unit ship, NativeSettlement settlement, string goodsId, int amount, int agreedPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        MoveCheck check = CheckBuyFromNatives(ship, settlement, goodsId, amount);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        int standard = check.Cost;
        int price = ClampHaggledBuyPrice(agreedPrice, standard);
        if (player.Gold < price)
        {
            throw new InvalidMoveException("You cannot afford that."); // a haggle can't raise the price above the affordability gate, but guard anyway
        }
        settlement.AddGoods(goodsId, -amount); // drained from the settlement's store (FreeCol moveGoods settlement→unit)
        ship.AddCargo(goodsId, amount);
        player.Gold -= price; // the agreed price; no European market tax
        ChangeNativeAlarm(settlement, player.PlayerId, -Math.Max(1, price / 200)); // a little goodwill (FreeCol ALARM_BONUS_BUY)
        RecomputeWantedGoods(settlement); // the emptier store re-prices its cravings (FreeCol csBuy → updateWantedGoods)
        ship.MovementLeft = 0; // opening a trade session ends the ship's turn
        return price;
    }

    /// <summary>The haggled sale price the engine will honour: the player's agreed figure, clamped to [standard, <see cref="HaggleSellPriceCeilingPercent"/>% of standard] — a seller never accepts less than the standard offer, and the chief never pays beyond the haggle ceiling (an out-of-band figure from the UI degrades to the standard price). Pure, RNG-free.</summary>
    private static int ClampHaggledSalePrice(int agreedPrice, int standard)
    {
        int ceiling = standard * HaggleSellPriceCeilingPercent / 100;
        return agreedPrice < standard || agreedPrice > ceiling ? standard : agreedPrice;
    }

    /// <summary>The haggled buy price the engine will honour: the player's agreed figure, clamped to [<see cref="HaggleBuyPriceFloorPercent"/>% of standard, standard] — a buyer never pays more than the standard ask, and the chief never sells below the haggle floor (an out-of-band figure degrades to the standard price). Pure, RNG-free.</summary>
    private static int ClampHaggledBuyPrice(int agreedPrice, int standard)
    {
        int floor = standard * HaggleBuyPriceFloorPercent / 100;
        return agreedPrice > standard || agreedPrice < floor ? standard : agreedPrice;
    }

    // ============================================================================================================
    // Settlement goods store driven by native production over time (86d3fpzvy) — FreeCol IndianSettlement production.
    //
    // FreeCol's ServerIndianSettlement.csNewTurn produces each tile good (getTotalProductionOf) into the goods store
    // every turn, capped at the warehouse. We do not simulate the natives' tiles, so production is ABSTRACTED: each
    // turn a settlement replenishes the raw New-World goods it gathers (the ones it can trade), sized by its
    // population, biased toward the goods it is shortest of — so a frontier camp's store actually refills between
    // visits and a fuller good grows slower (FreeCol's stock-fill curve). Drawn on the NATION's own RNG stream, never
    // the human's stream 0 (ADR-009); the seeded variance keeps soak twin-determinism intact.
    // ============================================================================================================

    /// <summary>Base units of a raw good a settlement gathers per resident per turn (the abstracted tile yield, tuned so a size-5 camp refills a tradeable lot over several turns — FreeCol produces the full tile potential each turn; we under-produce deliberately so trade stays worthwhile).</summary>
    private const int NativeProductionPerResident = 2;

    /// <summary>The ceiling a settlement lets a single good's store reach from production — one full trade hold (FreeCol caps at the warehouse; <see cref="NativeTradeCargoSize"/> is our store-trade unit). Production never pushes a good above this; player sales still can.</summary>
    private const int NativeProductionStoreCeiling = NativeTradeCargoSize;

    /// <summary>
    /// Replenishes <paramref name="settlement"/>'s general goods store from one turn of abstracted native production
    /// (FreeCol <c>ServerIndianSettlement.csNewTurn</c> producing its tile goods): the settlement gathers the raw,
    /// storable, tradeable New-World goods it trades, <see cref="NativeProductionPerResident"/> units per resident
    /// scaled down as that good's store fills toward <see cref="NativeProductionStoreCeiling"/> (the stock-fill curve —
    /// a good it already has plenty of grows slowly), plus a small per-good variance drawn on
    /// <paramref name="rng"/> (the nation's stream). A good already at/above the ceiling produces nothing. After
    /// replenishing, the settlement re-prices its wanted goods (FreeCol's per-turn <c>updateWantedGoods</c>). RNG-free
    /// for the human (stream 0 untouched): only the nation's stream is drawn (ADR-009).
    /// </summary>
    /// <param name="settlement">The settlement producing this turn.</param>
    /// <param name="rng">The owning nation's RNG stream (never the human's stream 0).</param>
    internal void ProduceNativeGoods(NativeSettlement settlement, IGameRandom rng)
    {
        bool changed = false;
        // Deterministic good order (spec order) so the per-good variance draws are stable for a seed.
        foreach (GoodsType good in Ruleset.GoodsTypes
            .Where(g => g.IsFarmed && g.IsNewWorldGoods && g.IsStorable && g.IsTradeable && !g.IsMilitary && !g.IsFood))
        {
            int current = settlement.GeneralStockOf(good.Id);
            if (current >= NativeProductionStoreCeiling)
            {
                continue; // brimming with this good — produce no more of it (FreeCol warehouse cap)
            }
            // Base gather scaled by the headroom left (stock-fill curve: a fuller store grows slower), plus 0–1 unit
            // of variance per resident on the nation's stream. Always at least 1 unit of headroom is produced.
            int headroom = NativeProductionStoreCeiling - current;
            int baseGather = NativeProductionPerResident * settlement.Size * headroom / NativeProductionStoreCeiling;
            int variance = rng.Next(0, settlement.Size + 1); // 0..Size, nation stream
            int produced = Math.Min(headroom, Math.Max(1, baseGather + variance));
            if (produced > 0)
            {
                settlement.AddGoods(good.Id, produced);
                changed = true;
            }
        }
        if (changed)
        {
            RecomputeWantedGoods(settlement); // the refreshed store re-prices its cravings (FreeCol csNewTurn → updateWantedGoods)
        }
    }

    /// <summary>
    /// One turn of production for every settlement of <paramref name="nation"/> (the per-nation driver called from the
    /// native turn — FreeCol runs <c>csNewTurn</c> for each settlement during its owner's turn). Settlements iterate in
    /// stable id order; each draws only on <paramref name="nation"/>'s stream (ADR-009). Also seeds the tribe-wide
    /// tension channel on first touch so a loaded game has a sensible base. Public entry the turn loop calls.
    /// </summary>
    /// <param name="nation">The native nation taking its turn.</param>
    internal void ProduceNativeNationGoods(Player nation)
    {
        IGameRandom rng = RandomFor(nation);
        foreach (NativeSettlement settlement in _nativeSettlements
            .Where(s => s.NationTypeId == nation.NationId)
            .OrderBy(s => s.Id))
        {
            ProduceNativeGoods(settlement, rng);
            ProduceNativeMilitaryGoods(settlement, rng);
        }
    }

    // ============================================================================================================
    // Per-turn military-goods production / refill (86d3fpzna / 86d3fpzzj) — FreeCol IndianSettlement military goods.
    //
    // FreeCol's ServerIndianSettlement.csNewTurn produces military goods each turn too: MUSKETS are manufactured
    // from tools (a refined good, getTotalProductionOf = getUnitCount() when the settlement is short of muskets) and
    // HORSES are bred (<= MAX_HORSES_PER_TURN = 2/turn), both capped at the warehouse and driven by getWantedGoodsAmount
    // for military goods (= enough to fully arm the settlement's currently-unarmed braves). We do NOT simulate the
    // natives' tiles/armoury, so this is ABSTRACTED the same way ProduceNativeGoods abstracts raw goods: each turn a
    // settlement slowly accumulates muskets (every camp) and horses (capitals / village-or-larger camps — mirroring
    // the generator seed's capability so a small camp never breeds horses it could never seed), up to a per-settlement
    // ceiling sized by population, by a small Size-scaled increment with a little nation-stream variance. So a tribe
    // that armed its braves and spent its stock REPLENISHES over the following turns and can re-arm later (closing the
    // arm-braves deviation 86d3fpzzj: stock is now PRODUCED, not just seeded). Drawn only on the NATION's own stream,
    // never the human's stream 0 (ADR-009) — the seeded variance keeps soak twin-determinism intact.
    // ============================================================================================================

    /// <summary>Base units of a military good a settlement manufactures/breeds per resident per turn (the abstracted FreeCol musket-manufacture / horse-breeding rate; under-produced deliberately so arming a brave is a real investment, not free every turn).</summary>
    private const int NativeMilitaryProductionPerResident = 1;

    /// <summary>
    /// The most muskets/horses production lets a settlement accumulate per resident — so a bigger camp keeps enough on
    /// hand to re-arm several braves, a small one only one or two. Capped overall at <see cref="NativeMilitaryStoreCeilingMax"/>.
    /// </summary>
    private const int NativeMilitaryStorePerResident = BraveEquipGoods / 2; // ~half an armed-brave's worth per resident

    /// <summary>The absolute ceiling production lets a single military good's stock reach — a few braves' worth (FreeCol caps military goods at the wanted amount + warehouse; we cap at a fixed few half-units so a tribe never hoards an army's arsenal from production alone). Player/redistribution can still exceed it; production simply stops contributing above it.</summary>
    private const int NativeMilitaryStoreCeilingMax = 4 * BraveEquipGoods; // up to four braves' worth (100)

    /// <summary>
    /// Refills <paramref name="settlement"/>'s muskets (every camp) and, for a capital or a settlement of size ≥
    /// <see cref="MountedProductionSizeThreshold"/>, its horses, by one turn of abstracted native arms manufacture
    /// (FreeCol <c>ServerIndianSettlement.csNewTurn</c> military-goods production): each military good gathers
    /// <see cref="NativeMilitaryProductionPerResident"/> units per resident plus 0–1 per resident of
    /// <paramref name="rng"/> variance (the nation's stream), scaled down as the good's stock fills toward this
    /// settlement's ceiling (population-sized, capped at <see cref="NativeMilitaryStoreCeilingMax"/>) — a stock already
    /// at/above the ceiling produces nothing. So a camp that armed braves and drew its stock to 0 recovers over the next
    /// turns and can re-arm; a camp brimming with arms accumulates no more from production. RNG-free for the human
    /// (stream 0 untouched): only the nation's stream is drawn (ADR-009). Capitals/larger camps refill horses too,
    /// mirroring the generator's seed capability so production never gives a small camp horses it was never seeded with.
    /// </summary>
    /// <param name="settlement">The settlement manufacturing arms this turn.</param>
    /// <param name="rng">The owning nation's RNG stream (never the human's stream 0).</param>
    internal void ProduceNativeMilitaryGoods(NativeSettlement settlement, IGameRandom rng)
    {
        int ceiling = Math.Min(NativeMilitaryStoreCeilingMax, Math.Max(BraveEquipGoods, NativeMilitaryStorePerResident * settlement.Size));
        // Muskets first (every camp can arm), then horses (only the better/larger camps mount), in a stable order so the
        // per-good variance draws are deterministic for a seed (the same good order the generator seed uses).
        RefillMilitaryGood(settlement, MusketsId, ceiling, rng);
        if (settlement.IsCapital || settlement.Size >= MountedProductionSizeThreshold)
        {
            RefillMilitaryGood(settlement, HorsesId, ceiling, rng);
        }
    }

    /// <summary>Capital / size threshold at/above which a settlement also manufactures horses (mirrors the generator's <c>MountedStockSizeThreshold</c> so production matches the seed's capability — small camps arm but never breed horses).</summary>
    private const int MountedProductionSizeThreshold = 7;

    /// <summary>
    /// Adds one turn of production of a single military good to <paramref name="settlement"/>: the Size-scaled base
    /// gather plus a little nation-stream variance, scaled down by the headroom left below <paramref name="ceiling"/>
    /// (the stock-fill curve — a fuller arsenal grows slower), and never pushing the good above the ceiling. A no-op
    /// when the good is already at/above the ceiling, so a well-armed camp produces nothing (and draws no variance for
    /// that good — only an under-stocked good ever touches the stream, keeping byte-stability tight).
    /// </summary>
    private void RefillMilitaryGood(NativeSettlement settlement, string goodsId, int ceiling, IGameRandom rng)
    {
        int current = settlement.StockOf(goodsId);
        if (current >= ceiling)
        {
            return; // already holds a full arsenal from production — manufacture no more (FreeCol wanted-amount/warehouse cap)
        }
        int headroom = ceiling - current;
        int baseGather = NativeMilitaryProductionPerResident * settlement.Size * headroom / ceiling; // stock-fill curve
        int variance = rng.Next(0, settlement.Size + 1); // 0..Size on the nation's stream
        int produced = Math.Min(headroom, Math.Max(1, baseGather + variance));
        if (produced > 0)
        {
            settlement.AddStock(goodsId, produced);
        }
    }

    // ============================================================================================================
    // Nation-level (tribe-wide) tension (86d3fpzkq) — FreeCol's player-level Tension on the native nation, distinct
    // from the per-settlement alarm (IndianSettlement.getAlarm). FreeCol keeps BOTH: an act against one camp also
    // stirs the whole nation a little (csModifyTension propagates a fraction nation-wide), and the nation's tension —
    // not just one camp's — gates the AI's willingness to war.
    //
    // We model the nation channel as a TRANSIENT accumulator keyed by nationTypeId → (playerId → tension), seeded on
    // first touch from the max of that nation's per-settlement channels (so a loaded game starts coherent without a
    // save field — no save bump). Acts raise it; it decays each turn alongside the settlement alarm. Reads feed the
    // report's tribe band and the uprising check. Deterministic, RNG-free.
    // ============================================================================================================

    /// <summary>Tribe-wide tension by nation type id → (colonial player id → tension 0..MaxAlarm). Transient (not serialized): seeded lazily from the per-settlement channels (<see cref="TribeTensionFor"/>), so a save round-trips byte-identically (ADR-009).</summary>
    private readonly Dictionary<string, Dictionary<int, int>> _tribeTension = [];

    /// <summary>The fraction (percent) of a per-settlement alarm change that also stirs the whole nation (FreeCol propagates a share of a settlement act to the player-level Tension). A minor share — the nation warms/cools more slowly than any single camp.</summary>
    private const int TribeTensionPropagationPercent = 25;

    private Dictionary<int, int> TribeChannelsFor(string nationTypeId)
    {
        if (!_tribeTension.TryGetValue(nationTypeId, out Dictionary<int, int>? channels))
        {
            // Seed from the nation's settlements: the tribe is, at least, as tense as its angriest camp toward each
            // player (FreeCol's nation tension tracks alongside the settlements). Deterministic, RNG-free.
            channels = [];
            foreach (NativeSettlement s in _nativeSettlements.Where(s => s.NationTypeId == nationTypeId))
            {
                foreach (KeyValuePair<int, int> kv in s.AlarmChannels)
                {
                    channels[kv.Key] = Math.Max(channels.GetValueOrDefault(kv.Key), kv.Value);
                }
            }
            _tribeTension[nationTypeId] = channels;
        }
        return channels;
    }

    /// <summary>
    /// The tribe-wide tension <paramref name="nationTypeId"/> holds toward the colonial player <paramref name="playerId"/>
    /// (FreeCol the native <em>player</em>'s Tension, 0..<c>MaxAlarm</c>) — a channel distinct from any single
    /// settlement's alarm. Seeded on first read from the nation's angriest settlement toward that player; raised by
    /// <see cref="RaiseTribeTension"/> when an act stirs the nation, and decayed each turn. A read oracle (ADR-006).
    /// </summary>
    /// <param name="nationTypeId">The native nation type id (a settlement's <see cref="NativeSettlement.NationTypeId"/>).</param>
    /// <param name="playerId">The colonial player whose channel to read (the human is <see cref="HumanAlarmChannel"/>).</param>
    public int TribeTensionFor(string nationTypeId, int playerId) =>
        TribeChannelsFor(nationTypeId).GetValueOrDefault(playerId);

    /// <summary>
    /// The tribe-wide tension band of <paramref name="nationTypeId"/> toward <paramref name="playerId"/> (FreeCol
    /// <c>Tension.getLevel</c> on the native player) against the data-overridable thresholds. The report tab and the
    /// uprising check read this — the nation-level channel, NOT the per-settlement <see cref="NativeSettlement.AlarmLevel"/>.
    /// </summary>
    public AlarmLevel TribeAlarmLevelFor(string nationTypeId, int playerId)
    {
        NativeTensionOptions t = Ruleset.Difficulty.NativeTension;
        int tension = TribeTensionFor(nationTypeId, playerId);
        return tension <= t.HappyMax ? AlarmLevel.Happy
            : tension <= t.ContentMax ? AlarmLevel.Content
            : tension <= t.DispleasedMax ? AlarmLevel.Displeased
            : tension <= t.AngryMax ? AlarmLevel.Angry
            : AlarmLevel.Hateful;
    }

    /// <summary>
    /// Raises (or, with a negative <paramref name="delta"/>, eases) <paramref name="nationTypeId"/>'s tribe-wide tension
    /// toward <paramref name="playerId"/>, clamped to [0, <c>MaxAlarm</c>]. The nation-level counterpart of
    /// <see cref="ChangeNativeAlarm(NativeSettlement, int, int)"/> — called when an act stirs the whole tribe (e.g. an
    /// incite, or the propagated share of a per-settlement act, <see cref="PropagateToTribe"/>). RNG-free.
    /// </summary>
    internal void RaiseTribeTension(string nationTypeId, int playerId, int delta)
    {
        Dictionary<int, int> channels = TribeChannelsFor(nationTypeId);
        int next = Math.Clamp(channels.GetValueOrDefault(playerId) + delta, 0, Ruleset.Difficulty.NativeTension.MaxAlarm);
        if (next <= 0)
        {
            channels.Remove(playerId);
        }
        else
        {
            channels[playerId] = next;
        }
    }

    /// <summary>
    /// Propagates a fraction (<see cref="TribeTensionPropagationPercent"/>) of a per-settlement alarm gain to the owning
    /// tribe's nation channel (FreeCol's csModifyTension spreading a share of a settlement act nation-wide). Only positive
    /// changes propagate — appeasing one camp does not calm the whole nation (FreeCol eases the nation only via decay).
    /// Called from <c>Game.ChangeNativeAlarm</c> after a settlement's alarm rises. RNG-free.
    /// </summary>
    /// <param name="settlement">The settlement whose alarm just changed.</param>
    /// <param name="playerId">The colonial player whose channel changed.</param>
    /// <param name="delta">The signed alarm change applied to the settlement (only positive deltas stir the nation).</param>
    internal void PropagateToTribe(NativeSettlement settlement, int playerId, int delta)
    {
        if (delta <= 0)
        {
            return;
        }
        RaiseTribeTension(settlement.NationTypeId, playerId, delta * TribeTensionPropagationPercent / 100);
    }

    /// <summary>
    /// Decays every nation's tribe-wide tension one turn (FreeCol the native player's per-turn Tension decay, mirroring
    /// the per-settlement <c>DecayNativeAlarm</c>): each channel loses <c>value/DecayDivisor + DecayBase</c>, dropping to
    /// nothing as it cools. Called once per turn from the native turn driver (<see cref="DecayTribeTensionForNation"/>).
    /// RNG-free; a channel that reaches 0 is removed, so an all-calm nation holds no channels.
    /// </summary>
    internal void DecayTribeTensionForNation(string nationTypeId)
    {
        NativeTensionOptions t = Ruleset.Difficulty.NativeTension;
        Dictionary<int, int> channels = TribeChannelsFor(nationTypeId);
        foreach (int playerId in channels.Keys.ToList())
        {
            int alarm = channels[playerId];
            int next = Math.Max(0, alarm - (alarm / t.DecayDivisor + t.DecayBase));
            if (next <= 0)
            {
                channels.Remove(playerId);
            }
            else
            {
                channels[playerId] = next;
            }
        }
    }

    // ============================================================================================================
    // Gifts FROM natives, realism pass (86d3fpzx1) — FreeCol IndianSettlement.getRandomGift + the AI gift throttle.
    //
    // Before: a Happy brave beside a human colony had a 1-in-8 chance to drop a FIXED 25 tobacco, with no store
    // backing and no cooldown — a settlement could rain gifts forever. FreeCol draws the gift from the settlement's
    // actual goods store (only a good it holds above GIFT_THRESHOLD + KEEP_RAW_MATERIAL), an amount in
    // [GIFT_MINIMUM, GIFT_MAXIMUM], and throttles delivery (per-turn gift probability + one mission in flight). We
    // now: (a) draw the gift good + amount from the home settlement's GeneralStock, (b) deduct it from the store, and
    // (c) gate each settlement on a per-settlement cooldown (NativeSettlement.LastGiftTurn). The gift chance + the
    // good/amount picks draw on the NATION's stream, never the human's stream 0 (ADR-009).
    // ============================================================================================================

    /// <summary>Per-turn gift chance for an eligible (Happy, off-cooldown, store-backed, colony-adjacent) brave — ~1-in-8 (drawn on the nation's stream).</summary>
    private const int NativeGiftChanceDenominator = 8;

    /// <summary>FreeCol <c>IndianSettlement.GIFT_MINIMUM</c> — the smallest parcel a settlement will part with.</summary>
    private const int NativeGiftMinimum = 10;

    /// <summary>FreeCol <c>IndianSettlement.GIFT_MAXIMUM</c> — the largest single gift.</summary>
    private const int NativeGiftMaximum = 80;

    /// <summary>The store a settlement keeps back before a good is giftable (FreeCol <c>KEEP_RAW_MATERIAL</c> 50 + <c>GIFT_THRESHOLD</c> 25): it only gives away a genuine surplus.</summary>
    private const int NativeGiftThreshold = 75;

    /// <summary>Turns a settlement must wait between gifts (our explicit cooldown standing in for FreeCol's gift-probability + one-mission-in-flight throttle).</summary>
    public const int NativeGiftCooldownTurns = 10;

    /// <summary>
    /// A friendly tribe's brave brings a <b>store-backed</b> gift to an adjacent human colony (FreeCol
    /// <c>IndianBringGiftMission</c> + <c>getRandomGift</c>): when the brave's home settlement is
    /// <see cref="AlarmLevel.Happy"/>, off cooldown (<see cref="NativeSettlement.LastGiftTurn"/> +
    /// <see cref="NativeGiftCooldownTurns"/> ≤ the turn), holds a surplus of some tradeable good (above
    /// <see cref="NativeGiftThreshold"/>), and stands beside a human colony, a per-turn chance (the nation's stream)
    /// draws one such good and a parcel of <see cref="NativeGiftMinimum"/>..<see cref="NativeGiftMaximum"/> units from
    /// the settlement's <see cref="NativeSettlement.GeneralStock"/>, <b>deducts it from the store</b>, leaves it in the
    /// colony's warehouse, stamps the cooldown, and records a <see cref="ColonyGiftNotice"/>. Returns true when a gift
    /// was delivered (the brave's turn is then spent). Pure goodwill — no alarm change, no brave consumed. The chance
    /// and the good/amount picks are drawn only when the brave is already eligible, so an ineligible brave never
    /// perturbs the native stream (ADR-009). Replaces the old fixed-25-tobacco <c>TryBringGift</c>.
    /// </summary>
    private bool TryBringGiftFromStore(Player nation, Unit brave)
    {
        if (HomeSettlement(nation, brave) is not { AlarmLevel: AlarmLevel.Happy } home)
        {
            return false;
        }
        if (Turn - home.LastGiftTurn < NativeGiftCooldownTurns && home.LastGiftTurn != 0)
        {
            return false; // still on cooldown — one gift per settlement per cooldown window
        }
        Colony? colony = _colonies
            .Where(c => IsHumanOwned(c) && brave.Position.IsAdjacentTo(c.Position))
            .OrderBy(c => c.Position.Y).ThenBy(c => c.Position.X)
            .FirstOrDefault();
        if (colony is null)
        {
            return false;
        }

        // The goods the settlement holds a genuine surplus of (above the keep-back threshold), tradeable + non-military,
        // in stable id order so the random pick is deterministic for a seed. Food is EXCLUDED (matching
        // ProduceNativeGoods): gifting food into a human colony could push it to FoodForGrowth and birth a colonist,
        // adding a tile worker and thus an extra stream-0 experience roll per later turn — so a native-stream gift
        // would perturb the human's stream 0 (ADR-009). Keep gifts to non-food goods so the human stream stays stable.
        List<string> giftable = home.GeneralStock
            .Where(kv => kv.Value > NativeGiftThreshold
                && Ruleset.Goods(kv.Key).IsTradeable && !Ruleset.Goods(kv.Key).IsMilitary
                && !Ruleset.Goods(kv.Key).IsFood)
            .Select(kv => kv.Key)
            .OrderBy(k => k, System.StringComparer.Ordinal)
            .ToList();
        if (giftable.Count == 0)
        {
            return false; // nothing to spare — no gift this turn (FreeCol getRandomGift returns null)
        }

        IGameRandom rng = RandomFor(nation);
        if (rng.Next(NativeGiftChanceDenominator) != 0)
        {
            return false; // the gift roll (nation's stream) did not fire
        }

        string goodsId = giftable[rng.Next(giftable.Count)];
        int available = home.GeneralStockOf(goodsId) - (NativeGiftThreshold - NativeGiftMinimum); // surplus above keep-back, down to GIFT_MINIMUM
        int span = Math.Max(1, Math.Min(available, NativeGiftMaximum) - NativeGiftMinimum + 1);
        int amount = Math.Min(NativeGiftMaximum, NativeGiftMinimum + rng.Next(span));
        amount = Math.Min(amount, home.GeneralStockOf(goodsId)); // never give more than the store holds

        home.AddGoods(goodsId, -amount); // the gift leaves the settlement's store (FreeCol moveGoods settlement→unit→colony)
        RecomputeWantedGoods(home);       // the emptier store re-prices its cravings
        home.LastGiftTurn = Turn;         // stamp the cooldown
        colony.AddGoods(goodsId, amount);
        _colonyGiftNotices.Add(new ColonyGiftNotice(nation.NationId!, colony.Name, goodsId, amount, colony.Position));
        return true;
    }
}
