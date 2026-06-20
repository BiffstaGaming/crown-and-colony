using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Trade;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// Diplomatic-trade backend (P6, FreeCol <c>InGameController.csAcceptTrade</c>): the thin <see cref="Game"/> seam
/// that settles a proposed <see cref="DiplomaticTrade"/> between two colonial players. The treaty model itself
/// lives in <c>GameSession/Diplomacy/</c>; this partial only wires its <see cref="DiplomaticTrade.Apply"/> to the
/// game's existing mutators (gold/goods transfer, <see cref="SetStance"/>).
/// </summary>
/// <remarks>
/// In-memory only this slice — the trade is not persisted (no save change) and the AI does not yet evaluate or
/// counter offers (that lives in the foreign-power turn, a separate lane). Settling a trade draws no RNG (ADR-009):
/// every clause is a deterministic transfer.
/// </remarks>
public sealed partial class Game
{
    /// <summary>
    /// Settles an accepted treaty (FreeCol <c>csAcceptTrade</c>): applies each of its currently-valid clauses — gold,
    /// goods, and (once it lands) stance changes. The stable seam the rules/UI call once a <see cref="DiplomaticTrade"/>
    /// is agreed; clause application reaches the gold/goods/stance helpers below.
    /// </summary>
    public void SettleTrade(DiplomaticTrade trade) => trade.Apply(this);

    // ---- Gold clause (86d3c9u94) ----

    /// <summary>
    /// Whether a <see cref="Diplomacy.GoldTradeItem"/> could pay <paramref name="amount"/> gold from
    /// <paramref name="fromId"/> to <paramref name="toId"/> right now: both are distinct colonial powers, the amount
    /// is positive, and the payer can afford it (FreeCol <c>GoldTradeItem.isValid</c>).
    /// </summary>
    internal bool CanTransferGold(int fromId, int toId, int amount) =>
        amount > 0 && fromId != toId && IsColonialPlayer(fromId) && IsColonialPlayer(toId)
        && PlayerById(fromId)!.Gold >= amount;

    /// <summary>
    /// Moves <paramref name="amount"/> gold from <paramref name="fromId"/>'s treasury to <paramref name="toId"/>'s
    /// (the treaty gold-clause mutator). A no-op unless <see cref="CanTransferGold"/> holds, so an over-large or
    /// non-colonial transfer never drives a treasury negative.
    /// </summary>
    internal void TransferGold(int fromId, int toId, int amount)
    {
        if (!CanTransferGold(fromId, toId, amount))
        {
            return;
        }
        PlayerById(fromId)!.Gold -= amount;
        PlayerById(toId)!.Gold += amount;
    }

    // ---- Goods clause (86d3c9u94) ----
    // (Colony lookup reuses the existing private ColonyById in Game.Monarch.cs.)

    /// <summary>
    /// Whether a <see cref="Diplomacy.GoodsTradeItem"/> could move <paramref name="amount"/> of
    /// <paramref name="goodsId"/> from <paramref name="fromColonyId"/> to <paramref name="toColonyId"/>: the amount is
    /// positive, the goods id is a real ruleset type, both parties are distinct colonial powers each owning their
    /// named colony, and the source colony holds at least <paramref name="amount"/> (FreeCol <c>GoodsTradeItem.isValid</c>).
    /// </summary>
    internal bool CanTransferColonyGoods(
        int fromPlayerId, int toPlayerId,
        int fromColonyId, int toColonyId,
        string goodsId, int amount)
    {
        if (amount <= 0 || fromPlayerId == toPlayerId
            || !IsColonialPlayer(fromPlayerId) || !IsColonialPlayer(toPlayerId)
            || !Ruleset.GoodsTypes.Any(g => g.Id == goodsId))
        {
            return false;
        }
        Colony? from = ColonyById(fromColonyId);
        Colony? to = ColonyById(toColonyId);
        return from is not null && to is not null
            && from.OwnerId == fromPlayerId && to.OwnerId == toPlayerId
            && from.StoreOf(goodsId) >= amount;
    }

    /// <summary>
    /// Moves <paramref name="amount"/> of <paramref name="goodsId"/> from <paramref name="fromColonyId"/>'s warehouse
    /// to <paramref name="toColonyId"/>'s (the treaty goods-clause mutator). A no-op unless the colonies exist, so a
    /// stale clause never throws; the source is debited and the destination credited via <c>Colony.AddGoods</c>.
    /// </summary>
    internal void TransferColonyGoods(int fromColonyId, int toColonyId, string goodsId, int amount)
    {
        if (ColonyById(fromColonyId) is not { } from || ColonyById(toColonyId) is not { } to)
        {
            return;
        }
        from.AddGoods(goodsId, -amount);
        to.AddGoods(goodsId, amount);
    }

    // ---- Stance clause (86d3c9u3z) ----

    /// <summary>
    /// Whether a <see cref="Diplomacy.StanceTradeItem"/> could set a stance between <paramref name="a"/> and
    /// <paramref name="b"/>: they must be distinct colonial powers — the only pairs whose stance is tracked and the
    /// exact pairs for which <see cref="SetStance"/> is not a no-op (FreeCol <c>StanceTradeItem.isValid</c>).
    /// </summary>
    internal bool CanSetStance(int a, int b) => a != b && IsColonialPlayer(a) && IsColonialPlayer(b);

    // ---- Colony clause (86d3c9u94) ----

    /// <summary>
    /// Whether a <see cref="Diplomacy.ColonyTradeItem"/> could hand the colony <paramref name="colonyId"/> from
    /// <paramref name="fromId"/> to <paramref name="toId"/>: both are distinct colonial powers and the colony still
    /// exists and is owned by <paramref name="fromId"/> (FreeCol <c>ColonyTradeItem.isValid</c> — a colony id that
    /// still names the source's colony).
    /// </summary>
    internal bool CanTransferColony(int fromId, int toId, int colonyId) =>
        fromId != toId && IsColonialPlayer(fromId) && IsColonialPlayer(toId)
        && ColonyById(colonyId) is { } colony && colony.OwnerId == fromId;

    /// <summary>
    /// Hands the colony <paramref name="colonyId"/> over to <paramref name="toId"/> (the treaty colony-clause mutator),
    /// reusing the capture/transfer path (<see cref="CaptureColony"/>) so the former owner's ships caught in its port
    /// are resolved exactly as in a wartime seizure. A no-op unless the colony exists and is owned by
    /// <paramref name="fromId"/>, so a stale clause never throws. Draws no RNG — unlike a wartime capture there is no
    /// plunder roll; a negotiated handover transfers the colony intact (FreeCol settles the colony without sacking it).
    /// </summary>
    internal void TransferColony(int fromId, int toId, int colonyId)
    {
        if (ColonyById(colonyId) is not { } colony || colony.OwnerId != fromId)
        {
            return;
        }
        CaptureColony(colony, toId);
    }

    // ---- Unit clause (86d3c9u94) ----

    /// <summary>
    /// Whether a <see cref="Diplomacy.UnitTradeItem"/> could hand the unit <paramref name="unitId"/> from
    /// <paramref name="fromId"/> to <paramref name="toId"/>: both are distinct colonial powers and the unit still
    /// exists and is a (non-native) unit owned by <paramref name="fromId"/> (FreeCol <c>UnitTradeItem.isValid</c> —
    /// the unit is the source's and its type is available to the destination; we model the ownership half).
    /// </summary>
    internal bool CanTransferUnit(int fromId, int toId, int unitId) =>
        fromId != toId && IsColonialPlayer(fromId) && IsColonialPlayer(toId)
        && UnitById(unitId) is { OwnerNationId: null } unit && unit.OwnerId == fromId;

    /// <summary>
    /// Hands the unit <paramref name="unitId"/> over to <paramref name="toId"/> (the treaty unit-clause mutator): a
    /// pure reparent of its <see cref="Units.Unit.OwnerId"/> (FreeCol <c>unit.changeOwner</c>) — its position/location are
    /// untouched. A no-op unless the unit exists and is the source's non-native unit, so a stale clause never throws.
    /// </summary>
    internal void TransferUnit(int fromId, int toId, int unitId)
    {
        if (UnitById(unitId) is not { OwnerNationId: null } unit || unit.OwnerId != fromId)
        {
            return;
        }
        unit.OwnerId = toId;
    }

    // ---- Incite clause (86d3c9u94) ----

    /// <summary>
    /// Whether an <see cref="Diplomacy.InciteTradeItem"/> could incite <paramref name="warmakerId"/> to war against
    /// <paramref name="victimId"/> on behalf of <paramref name="beneficiaryId"/>: the victim is a colonial power
    /// distinct from both the <paramref name="warmakerId"/> and the <paramref name="beneficiaryId"/>, and both of those
    /// are colonial powers (FreeCol <c>InciteTradeItem.isValid</c>: victim ≠ source ≠ destination).
    /// </summary>
    internal bool CanIncite(int warmakerId, int beneficiaryId, int victimId) =>
        victimId != warmakerId && victimId != beneficiaryId
        && IsColonialPlayer(warmakerId) && IsColonialPlayer(beneficiaryId) && IsColonialPlayer(victimId);

    /// <summary>
    /// Settles an incitement (FreeCol <c>csAcceptTrade</c> incite branch: <c>source.csChangeStance(WAR, victim)</c>):
    /// puts the <paramref name="warmakerId"/> (the clause's source — the power that agrees to fight) and the
    /// <paramref name="victimId"/> at <see cref="Stance.War"/> (symmetric, with the war stance-change tension modifier),
    /// then adds the war-inciter spike (<see cref="TensionWarInciter"/> = 250, FreeCol <c>TENSION_ADD_WAR_INCITER</c>)
    /// to the victim's tension toward the <paramref name="beneficiaryId"/> (the power who instigated it). The gold the
    /// beneficiary pays for the favour travels as a separate <see cref="Diplomacy.GoldTradeItem"/> clause in the same
    /// treaty. A no-op unless all three are distinct colonial powers; draws no RNG.
    /// </summary>
    internal void Incite(int warmakerId, int beneficiaryId, int victimId)
    {
        if (!CanIncite(warmakerId, beneficiaryId, victimId))
        {
            return;
        }
        ApplyStanceWithTension(warmakerId, victimId, Stance.War);
        ChangeTension(victimId, beneficiaryId, TensionWarInciter, symmetric: false); // the victim resents the instigator
    }

    // ---- Stance-change tension modifiers (86d3c9u3z) ----

    /// <summary>Tension applied to a colonial pair when they ally (FreeCol <c>Tension.ALLIANCE_MODIFIER</c>).</summary>
    private const int TensionAllianceModifier = -500;

    /// <summary>Tension applied to a colonial pair when they sign a peace treaty (FreeCol <c>Tension.PEACE_TREATY_MODIFIER</c>).</summary>
    private const int TensionPeaceTreatyModifier = -250;

    /// <summary>Tension applied to a colonial pair when they agree a cease-fire (FreeCol <c>Tension.CEASE_FIRE_MODIFIER</c>).</summary>
    private const int TensionCeaseFireModifier = -250;

    /// <summary>Tension applied to a colonial pair when war resumes from a cease-fire (FreeCol <c>Tension.RESUME_WAR_MODIFIER</c>).</summary>
    private const int TensionResumeWarModifier = 750;

    /// <summary>Tension a victim gains toward the power that incited war against it (FreeCol <c>Tension.TENSION_ADD_WAR_INCITER</c>).</summary>
    internal const int TensionWarInciter = 250;

    /// <summary>
    /// The tension delta to apply when a colonial pair's stance changes from <paramref name="oldStance"/> to
    /// <paramref name="newStance"/> via an act of diplomacy — a faithful port of FreeCol <c>Stance.getTensionModifier</c>
    /// (the realistic transitions only): allying calms (peace→alliance −500, cease-fire→alliance −750, war→alliance
    /// −1000), a peace treaty calms (cease-fire→peace −250, war→peace −500), a cease-fire calms a war (war→cease-fire
    /// −250), and going to war from peace spikes to maximum (peace/alliance→war +1000 = <see cref="TensionWar"/>)
    /// while war from a cease-fire is a smaller +750 (<see cref="TensionResumeWarModifier"/>). A no-change transition
    /// (the same stance both sides, including war→war) is 0. Pure; draws no RNG.
    /// </summary>
    internal static int StanceTensionModifier(Stance oldStance, Stance newStance) => newStance switch
    {
        Stance.Alliance => oldStance switch
        {
            Stance.Peace => TensionAllianceModifier,
            Stance.CeaseFire => TensionAllianceModifier + TensionPeaceTreatyModifier,
            Stance.War => TensionAllianceModifier + TensionCeaseFireModifier + TensionPeaceTreatyModifier,
            _ => 0,
        },
        Stance.Peace => oldStance switch
        {
            Stance.CeaseFire => TensionPeaceTreatyModifier,
            Stance.War => TensionCeaseFireModifier + TensionPeaceTreatyModifier,
            _ => 0,
        },
        Stance.CeaseFire => oldStance == Stance.War ? TensionCeaseFireModifier : 0,
        Stance.War => oldStance switch
        {
            Stance.CeaseFire => TensionResumeWarModifier,
            Stance.War => 0,
            _ => TensionWar, // Uncontacted/Peace/Alliance → War
        },
        _ => 0,
    };

    /// <summary>
    /// Sets a colonial pair's mutual stance to <paramref name="newStance"/> <b>and</b> applies the matching
    /// stance-change tension modifier (FreeCol <c>ServerPlayer.csChangeStance</c>): reads the current stance, then
    /// <see cref="SetStance"/> + <see cref="ChangeTension"/> by <see cref="StanceTensionModifier"/>. The diplomacy
    /// apply path (the <see cref="Diplomacy.StanceTradeItem"/> clause and <see cref="Incite"/>) routes through here so
    /// a treaty's stance change carries its tension consequence; the generic <see cref="SetStance"/> used elsewhere
    /// (contact, the tension→stance machine, monarch-imposed war/peace) is left tension-free, by design. A no-op
    /// unless <paramref name="a"/> and <paramref name="b"/> are distinct colonial powers; draws no RNG.
    /// </summary>
    internal void ApplyStanceWithTension(int a, int b, Stance newStance)
    {
        if (!CanSetStance(a, b))
        {
            return;
        }
        int delta = StanceTensionModifier(StanceBetween(a, b), newStance);
        SetStance(a, b, newStance);
        if (delta != 0)
        {
            ChangeTension(a, b, delta);
        }
    }

    // ---- AI treaty evaluation (86d3c9uar) — PURE; NOT wired into the turn loop ----

    /// <summary>The sentinel a clause returns when it is unacceptable to the evaluator (FreeCol <c>TradeItem.INVALID_TRADE_ITEM</c> = <c>int.MinValue</c>).</summary>
    internal const int InvalidTradeItem = int.MinValue;

    /// <summary>The gold a power assigns per point of a colony's population when weighing it in a treaty — a documented proxy for FreeCol's full <c>Colony.evaluateFor</c> (which sums every work-location's worth). FreeCol's exact figure is far richer; this keeps the sign and rough magnitude faithful without porting the colony-valuation engine.</summary>
    private const int ColonyValuePerPopulation = 300;

    /// <summary>The gold a power assigns to a single unit when weighing it in a treaty — a documented flat proxy for FreeCol's price-based <c>Unit.evaluateFor</c>.</summary>
    private const int UnitTradeValue = 200;

    /// <summary>The minimum colony/unit count below which an AI power refuses to trade one away (FreeCol <c>Colony.TRADE_MARGIN</c> = 5 for colonies, the <c>&lt; 10</c> unit guard in <c>UnitTradeItem.evaluateFor</c>).</summary>
    private const int ColonyTradeMargin = 5;
    private const int UnitTradeMargin = 10;

    /// <summary>
    /// Whether the power with id <paramref name="powerId"/> would <b>accept</b> a proposed treaty (FreeCol
    /// <c>EuropeanAIPlayer.acceptDiplomaticTrade</c>'s core test): no clause is unacceptable to it and the deal nets it
    /// ≥ 0 value. Convenience over <see cref="EvaluateTrade"/>.
    /// </summary>
    public bool ShouldAcceptTrade(int powerId, DiplomaticTrade trade) => EvaluateTrade(powerId, trade).Accept;

    /// <summary>
    /// Scores a proposed <see cref="DiplomaticTrade"/> from the point of view of the colonial power with id
    /// <paramref name="powerId"/> — a faithful subset of FreeCol <c>EuropeanAIPlayer.acceptDiplomaticTrade</c>: each
    /// clause is valued via <see cref="EvaluateTradeItem"/> (gold paid is negative / gold received positive; goods at
    /// market prices; a stance/incite by the relative military strength; a colony/unit by a population/flat proxy), and
    /// the power <see cref="TradeEvaluation.Accept"/>s exactly when <b>no</b> clause is unacceptable to it (returns the
    /// <see cref="InvalidTradeItem"/> sentinel) and the summed <see cref="TradeEvaluation.Value"/> is ≥ 0.
    /// </summary>
    /// <remarks>
    /// <b>Pure</b> — it reads the game and returns a verdict; it changes nothing and draws no RNG (ADR-009), so it is
    /// safe to call repeatedly (e.g. from a future negotiation UI). It is deliberately <b>not</b> called from
    /// <c>RunForeignPowerTurn</c>: wiring the AI to actually answer or propose treaties during its turn is the
    /// follow-up that pairs with the negotiation screen. The multi-round haggling, Franklin "always offered peace", and
    /// patience cut-off of FreeCol's full method are out of scope here — this is the single-shot accept/reject core.
    /// A non-colonial <paramref name="powerId"/>, or an empty trade, scores as a reject.
    /// </remarks>
    public TradeEvaluation EvaluateTrade(int powerId, DiplomaticTrade trade)
    {
        if (PlayerById(powerId) is not { PlayerType: PlayerType.Colonial } || trade.Items.Count == 0)
        {
            return new TradeEvaluation(Accept: false, Value: 0, UnacceptableItems: 0);
        }

        int value = 0;
        int unacceptable = 0;
        foreach (TradeItem item in trade.Items)
        {
            int score = EvaluateTradeItem(powerId, item);
            if (score == InvalidTradeItem)
            {
                unacceptable++;
            }
            else
            {
                value += score;
            }
        }

        bool accept = unacceptable == 0 && value >= 0;
        return new TradeEvaluation(accept, value, unacceptable);
    }

    /// <summary>
    /// Values one clause of a treaty from <paramref name="powerId"/>'s point of view (FreeCol the per-clause
    /// <c>TradeItem.evaluateFor</c>): positive = good for the power, negative = a cost, <see cref="InvalidTradeItem"/> =
    /// the power will not take this clause at any price. Pure; draws no RNG.
    /// </summary>
    internal int EvaluateTradeItem(int powerId, TradeItem item) => item switch
    {
        GoldTradeItem gold => item.Source == powerId ? -gold.Amount : gold.Amount,
        GoodsTradeItem goods => EvaluateGoods(powerId, goods),
        StanceTradeItem stance => EvaluateStance(powerId, stance.Source == powerId ? stance.Destination : stance.Source, stance.Stance),
        InciteTradeItem incite => EvaluateIncite(powerId, incite.Victim),
        ColonyTradeItem colony => EvaluateColony(powerId, colony),
        UnitTradeItem unit => EvaluateUnit(powerId, unit),
        _ => InvalidTradeItem,
    };

    /// <summary>Goods value (FreeCol <c>GoodsTradeItem.evaluateFor</c>): the giver loses the buy-back cost; the receiver gains the after-tax sale revenue, both at the power's market.</summary>
    private int EvaluateGoods(int powerId, GoodsTradeItem goods)
    {
        Player power = PlayerById(powerId)!;
        Market market = power.Market;
        if (goods.Source == powerId)
        {
            return -market.AskPrice(goods.GoodsId) * goods.Amount; // cost to replace what we give away
        }
        int revenue = market.BidPrice(goods.GoodsId) * goods.Amount;
        return (int)Math.Round(revenue * (1.0 - power.TaxRate / 100.0)); // after-tax sale value of what we receive
    }

    /// <summary>
    /// Stance value (FreeCol <c>StanceTradeItem.evaluateFor</c>): by the power's military strength ratio against the
    /// other party. Going to war is worth taking only when we're not badly outmatched (ratio &lt; 0.33 → invalid,
    /// &lt; 0.5 → a negative); making peace/cease-fire/alliance is the reverse (ratio &gt; 0.66 → invalid, &gt; 0.5 →
    /// negative, &lt; 0.33 → a strong +1000). An uncontacted stance is invalid.
    /// </summary>
    private int EvaluateStance(int powerId, int otherId, Stance stance)
    {
        double ratio = StrengthRatio(powerId, otherId);
        int value = (int)Math.Round(100 * ratio);
        switch (stance)
        {
            case Stance.War:
                if (ratio < 0.33) return InvalidTradeItem;
                return ratio < 0.5 ? -value : value;
            case Stance.Peace or Stance.CeaseFire or Stance.Alliance:
                if (ratio > 0.66) return InvalidTradeItem;
                if (ratio > 0.5) return -value;
                return ratio < 0.33 ? 1000 : value;
            default: // Uncontacted
                return InvalidTradeItem;
        }
    }

    /// <summary>Incite value (FreeCol <c>InciteTradeItem.evaluateFor</c>): already at war with the victim → 0; allied with the victim → invalid; otherwise <c>−round(50 / strengthRatio)</c> (the weaker we are versus the victim, the costlier inciting them is).</summary>
    private int EvaluateIncite(int powerId, int victimId)
    {
        switch (StanceBetween(powerId, victimId))
        {
            case Stance.Alliance:
                return InvalidTradeItem;
            case Stance.War:
                return 0; // not invalid — the other side may not know our stance
            default:
                break;
        }
        double ratio = StrengthRatio(powerId, victimId);
        if (ratio <= 0)
        {
            return InvalidTradeItem; // no strength → cannot make war (avoids div-by-zero)
        }
        return -(int)Math.Round(50.0 / ratio);
    }

    /// <summary>Colony value (proxy for FreeCol <c>ColonyTradeItem.evaluateFor</c>): an AI won't give up a colony when it would drop below <see cref="ColonyTradeMargin"/>; otherwise ± its population-proxy worth (negative when the power is the giver).</summary>
    private int EvaluateColony(int powerId, ColonyTradeItem item)
    {
        if (ColonyById(item.ColonyId) is not { } colony)
        {
            return InvalidTradeItem;
        }
        bool giving = item.Source == powerId;
        if (giving && ColoniesOf(PlayerById(powerId)!).Count() < ColonyTradeMargin)
        {
            return InvalidTradeItem;
        }
        int worth = colony.Population * ColonyValuePerPopulation;
        return giving ? -worth : worth;
    }

    /// <summary>Unit value (proxy for FreeCol <c>UnitTradeItem.evaluateFor</c>): an AI won't give a unit away when it holds fewer than <see cref="UnitTradeMargin"/>; otherwise ± <see cref="UnitTradeValue"/> (negative when the power is the giver).</summary>
    private int EvaluateUnit(int powerId, UnitTradeItem item)
    {
        bool giving = item.Source == powerId;
        if (giving && _units.Count(u => u.OwnerId == powerId && u.OwnerNationId is null) < UnitTradeMargin)
        {
            return InvalidTradeItem;
        }
        return giving ? -UnitTradeValue : UnitTradeValue;
    }

    /// <summary>FreeCol <c>Player.strengthRatio(ours, theirs)</c> over land military power: <c>ours == 0 ? 0 : ours / (ours + theirs)</c>.</summary>
    private double StrengthRatio(int aId, int bId)
    {
        double ours = PlayerById(aId) is { } a ? LandPowerOf(a) : 0;
        double theirs = PlayerById(bId) is { } b ? LandPowerOf(b) : 0;
        return ours == 0.0 ? 0.0 : ours / (ours + theirs);
    }
}
