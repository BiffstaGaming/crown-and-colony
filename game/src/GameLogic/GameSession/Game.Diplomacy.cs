using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession.Diplomacy;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

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

    // ---- Declare war on a rival (86d3fq0bf, FreeCol InGameController.declareWarFromPeace → csChangeStance(WAR)) ----

    /// <summary>
    /// Whether <paramref name="declarerId"/> may declare war on <paramref name="targetId"/> right now (the player-initiated
    /// declare-war oracle, ADR-006): both are distinct colonial powers and they are currently <b>met and not yet at war</b>
    /// — at <see cref="Stance.Peace"/>, <see cref="Stance.CeaseFire"/>, or <see cref="Stance.Alliance"/>. A pair that is
    /// <see cref="Stance.Uncontacted"/> has not met (war there is declared implicitly by the first attack, FreeCol's
    /// uncontacted-attack path), and a pair already at <see cref="Stance.War"/> has nothing to declare. The presentation
    /// enables its "Declare war" action from this; no RNG, no mutation.
    /// </summary>
    public bool CanDeclareWar(int declarerId, int targetId) =>
        declarerId != targetId && IsColonialPlayer(declarerId) && IsColonialPlayer(targetId)
        && StanceBetween(declarerId, targetId) is Stance.Peace or Stance.CeaseFire or Stance.Alliance;

    /// <summary>
    /// Declares war from <paramref name="declarerId"/> on <paramref name="targetId"/> (86d3fq0bf, FreeCol
    /// <c>ServerPlayer.csChangeStance(Stance.WAR, target, symmetric:true)</c>): the explicit player-initiated counterpart
    /// to war-by-attack / King-imposed war / a rival breaking the peace. Routes through
    /// <see cref="ApplyStanceWithTension"/>, so the pair becomes mutually <see cref="Stance.War"/> <b>and</b> the matching
    /// stance-change tension modifier is applied (FreeCol <c>old.getTensionModifier(WAR)</c> — Peace/Alliance→War spikes to
    /// <see cref="TensionWar"/>, CeaseFire→War adds <see cref="TensionResumeWarModifier"/>), and the war is recorded in the
    /// human's history (via <see cref="SetStance"/>). A no-op unless <see cref="CanDeclareWar"/> holds. Draws no RNG
    /// (ADR-009) — like every stance act it leaves the human's stream 0 byte-identical.
    /// </summary>
    /// <returns>True if war was declared; false if the demand was illegal (not a contacted at-peace rival).</returns>
    public bool DeclareWar(int declarerId, int targetId)
    {
        if (!CanDeclareWar(declarerId, targetId))
        {
            return false;
        }
        ApplyStanceWithTension(declarerId, targetId, Stance.War);
        return true;
    }

    // ---- Trade goods at a rival's colony (86d3fq0dj, FreeCol moveTrade → a TRADE-context carrier deal at a foreign port) ----

    /// <summary>
    /// Whether <paramref name="ship"/> may <b>sell</b> <paramref name="amount"/> of <paramref name="goodsId"/> into the
    /// rival colony at <paramref name="colonyPos"/> now (86d3fq0dj). A carrier-at-a-foreign-port trade (FreeCol
    /// <c>moveTrade</c>, the <see cref="Diplomacy.TradeContext.Trade"/> deal): the ship's owner must hold de Witt's
    /// <see cref="CanTradeWithForeignColonies"/> ability, the carrier must be on the map at or beside the colony, the
    /// colony must belong to a <b>different</b> colonial power the trader is <b>not at war with</b>, the amount positive,
    /// the good actually aboard and tradeable in the trader's market, and the colony able to pay (its owner can afford the
    /// price). The price is the rival owner's market <b>bid</b> for the lot. A read oracle (ADR-006) — no RNG, no mutation.
    /// </summary>
    public MoveCheck CheckSellToForeignColony(Unit ship, Position colonyPos, string goodsId, int amount)
    {
        MoveCheck gate = CheckForeignColonyTrade(ship, colonyPos, goodsId, amount, out Colony? colony, out Player? owner);
        if (!gate.Allowed)
        {
            return gate;
        }
        if (ship.CargoOf(goodsId) < amount)
        {
            return MoveCheck.No($"The ship is not carrying {amount} {goodsId}.");
        }
        int price = ForeignColonyBidPrice(owner!, goodsId, amount);
        if (owner!.Gold < price)
        {
            return MoveCheck.No($"The {NationDisplayName(colony!.OwnerId)} colony cannot afford that.");
        }
        return MoveCheck.Yes(price);
    }

    /// <summary>
    /// Sells goods from <paramref name="ship"/>'s hold into the adjacent rival colony for gold (86d3fq0dj, FreeCol
    /// <c>moveTrade</c>): the goods leave the hold and join the rival colony's warehouse; the rival owner pays from its
    /// treasury and the trader is credited (no European market tax — this is a foreign-port deal, not a Europe sale, and
    /// it does not move the trader's home market). Ends the ship's turn (opening a trade session, as the native-trade path
    /// does). Draws no RNG (a deterministic transfer at the quoted price). A no-op-safe command guarded by
    /// <see cref="CheckSellToForeignColony"/>.
    /// </summary>
    /// <returns>The gold the trader received.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckSellToForeignColony"/>.</exception>
    public int SellToForeignColony(Unit ship, Position colonyPos, string goodsId, int amount)
    {
        MoveCheck check = CheckSellToForeignColony(ship, colonyPos, goodsId, amount);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        int price = check.Cost;
        Colony colony = ColonyAt(colonyPos)!;
        Player owner = PlayerById(colony.OwnerId)!;
        ship.AddCargo(goodsId, -amount);
        colony.AddGoods(goodsId, amount);          // the lot joins the rival colony's warehouse (FreeCol moveGoods unit→settlement)
        owner.Gold -= price;                       // the rival colony's owner pays for the goods…
        PlayerById(ship.OwnerId)!.Gold += price;   // …and the trader is credited (no European market tax)
        ship.MovementLeft = 0;                     // opening a trade session ends the ship's turn
        return price;
    }

    /// <summary>
    /// Whether <paramref name="ship"/> may <b>buy</b> <paramref name="amount"/> of <paramref name="goodsId"/> from the
    /// rival colony at <paramref name="colonyPos"/> now (86d3fq0dj, the buy half of FreeCol <c>moveTrade</c>): the same
    /// gates as <see cref="CheckSellToForeignColony"/> (de Witt ability, adjacency, a non-war rival owner, a positive
    /// amount, a tradeable good), plus the colony must actually <b>hold</b> at least <paramref name="amount"/> of the good
    /// and the trader must be able to afford the rival owner's <b>ask</b> price for the lot. A read oracle — no RNG, no
    /// mutation.
    /// </summary>
    public MoveCheck CheckBuyFromForeignColony(Unit ship, Position colonyPos, string goodsId, int amount)
    {
        MoveCheck gate = CheckForeignColonyTrade(ship, colonyPos, goodsId, amount, out Colony? colony, out Player? owner);
        if (!gate.Allowed)
        {
            return gate;
        }
        if (colony!.StoreOf(goodsId) < amount)
        {
            return MoveCheck.No($"The colony does not have {amount} {goodsId} to sell.");
        }
        int price = ForeignColonyAskPrice(owner!, goodsId, amount);
        if (PlayerById(ship.OwnerId)!.Gold < price)
        {
            return MoveCheck.No("You cannot afford that.");
        }
        return MoveCheck.Yes(price);
    }

    /// <summary>
    /// Buys goods out of the adjacent rival colony's warehouse into <paramref name="ship"/>'s hold for gold (86d3fq0dj,
    /// FreeCol <c>moveTrade</c>): the goods leave the rival warehouse and go aboard; the trader pays from its treasury and
    /// the rival owner is credited. No European market tax, no home-market move; ends the ship's turn. Draws no RNG.
    /// Guarded by <see cref="CheckBuyFromForeignColony"/>.
    /// </summary>
    /// <returns>The gold the trader paid.</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckBuyFromForeignColony"/>.</exception>
    public int BuyFromForeignColony(Unit ship, Position colonyPos, string goodsId, int amount)
    {
        MoveCheck check = CheckBuyFromForeignColony(ship, colonyPos, goodsId, amount);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        int price = check.Cost;
        Colony colony = ColonyAt(colonyPos)!;
        Player owner = PlayerById(colony.OwnerId)!;
        colony.AddGoods(goodsId, -amount);         // drained from the rival colony's warehouse (FreeCol moveGoods settlement→unit)
        ship.AddCargo(goodsId, amount);
        PlayerById(ship.OwnerId)!.Gold -= price;   // the trader pays…
        owner.Gold += price;                       // …and the rival owner is credited
        ship.MovementLeft = 0;                     // opening a trade session ends the ship's turn
        return price;
    }

    /// <summary>The lot's value at the rival colony owner's market <b>bid</b> (what a foreign port pays the trader for goods sold in) — the foreign-trade sell price (FreeCol values a foreign-colony sale at the local market bid).</summary>
    private int ForeignColonyBidPrice(Player owner, string goodsId, int amount) => owner.Market.BidPrice(goodsId) * amount;

    /// <summary>The lot's cost at the rival colony owner's market <b>ask</b> (what a foreign port charges the trader for goods bought out) — the foreign-trade buy price.</summary>
    private int ForeignColonyAskPrice(Player owner, string goodsId, int amount) => owner.Market.AskPrice(goodsId) * amount;

    /// <summary>
    /// The shared precondition gate for a carrier trading at a rival colony (sell or buy): the ship is a carrier on the
    /// map adjacent to (or on) the colony tile, the colony belongs to a <b>different</b> colonial power the trader's owner
    /// holds <see cref="CanTradeWithForeignColonies"/> for and is <b>not at war with</b>, the amount is positive, and the
    /// good is a real, tradeable ruleset type. Emits the resolved <paramref name="colony"/> and its <paramref name="owner"/>
    /// for the caller's price step. Returns an allowed <see cref="MoveCheck.Yes(int)"/> (cost 0 — the caller sets the real
    /// price) or the first failing reason. No RNG, no mutation.
    /// </summary>
    private MoveCheck CheckForeignColonyTrade(Unit ship, Position colonyPos, string goodsId, int amount, out Colony? colony, out Player? owner)
    {
        colony = null;
        owner = null;
        if (!ship.Type.IsCarrier || !ship.IsOnMap)
        {
            return MoveCheck.No("Only a ship on the map can trade with a colony.");
        }
        if (!CanTradeWithForeignColonies(ship.OwnerId))
        {
            return MoveCheck.No("You cannot trade with foreign colonies (requires Jan de Witt).");
        }
        if (!Map.InBounds(colonyPos))
        {
            return MoveCheck.No("Target is off the map.");
        }
        if (ship.Position != colonyPos && !ship.Position.IsAdjacentTo(colonyPos))
        {
            return MoveCheck.No("The ship must be next to the colony to trade.");
        }
        if (ColonyAt(colonyPos) is not { } c)
        {
            return MoveCheck.No("There is no colony to trade with there.");
        }
        if (c.OwnerId == ship.OwnerId || !IsColonialPlayer(c.OwnerId))
        {
            return MoveCheck.No("That is not a rival colony.");
        }
        if (StanceBetween(ship.OwnerId, c.OwnerId) == Stance.War)
        {
            return MoveCheck.No("You are at war with that power — make peace before trading.");
        }
        if (amount <= 0)
        {
            return MoveCheck.No("Nothing to trade.");
        }
        if (!Ruleset.GoodsTypes.Any(g => g.Id == goodsId))
        {
            return MoveCheck.No("That is not a tradeable good.");
        }
        colony = c;
        owner = PlayerById(c.OwnerId);
        return MoveCheck.Yes(0);
    }

    // ---- Demand tribute from a rival EUROPEAN colony (86d3fq0ev, FreeCol moveTribute → makePeaceTreaty(TRIBUTE) + a gold clause) ----

    /// <summary>Tension the demanding power gains in the rival's eyes when it shakes down a European colony, paid or not (mirrors the native-demand insult; FreeCol routes a European tribute through diplomacy, where the threat sours relations either way).</summary>
    internal const int TensionDemandTribute = 200;

    /// <summary>The outcome of a <see cref="DemandTributeFromColony(Unit, Position)"/> attempt: <see cref="Paid"/> says whether the rival colony yielded, and <see cref="Gold"/> is the tribute extracted (0 on a refusal). Mirrors the native <see cref="TributeResult"/>.</summary>
    /// <param name="Paid">True when the rival paid tribute (gold &gt; 0); false when it refused.</param>
    /// <param name="Gold">The gold extracted (0 on a refusal).</param>
    public readonly record struct EuropeanTributeResult(bool Paid, int Gold);

    /// <summary>
    /// Whether <paramref name="unit"/> may demand tribute from the <b>rival European colony</b> at <paramref name="target"/>
    /// now (86d3fq0ev, FreeCol <c>moveTribute</c>'s European branch). The European analogue of
    /// <see cref="CheckDemandTribute"/>: a colonial (non-native) unit with offensive strength and movement left, standing
    /// on or beside a colony that belongs to a <b>met</b> rival colonial power (a stance is recorded — you cannot shake
    /// down a power you have never met). Unlike the native demand, war is not required — the threat of force is the lever
    /// at any non-uncontacted stance. A read oracle (ADR-006) — no RNG, no mutation.
    /// </summary>
    public MoveCheck CheckDemandTributeFromColony(Unit unit, Position target)
    {
        if (!unit.IsOnMap)
        {
            return MoveCheck.No("The unit is at sea or in Europe.");
        }
        if (unit.IsNative)
        {
            return MoveCheck.No("Native units do not demand tribute of colonies.");
        }
        if (!Map.InBounds(target))
        {
            return MoveCheck.No("Target is off the map.");
        }
        if (!unit.Position.IsAdjacentTo(target) && unit.Position != target)
        {
            return MoveCheck.No("Move next to the colony to demand tribute.");
        }
        if (unit.MovementLeft <= 0)
        {
            return MoveCheck.No("No movement left this turn.");
        }
        if (OffenceBase(unit) <= 0)
        {
            return MoveCheck.No($"A {unit.Type.ShortName} has no offensive strength — arm it first.");
        }
        if (ColonyAt(target) is not { } colony || colony.OwnerId == unit.OwnerId || !IsColonialPlayer(colony.OwnerId))
        {
            return MoveCheck.No("There is no rival colony to demand tribute of there.");
        }
        if (StanceBetween(unit.OwnerId, colony.OwnerId) == Stance.Uncontacted)
        {
            return MoveCheck.No("You have not met that power.");
        }
        return MoveCheck.Yes(0);
    }

    /// <summary>The gold a rival colony's treasury that the per-demand tribute cap draws from (mirrors the native <c>TributeGoldCap</c> = 100): a single demand never extracts more than this even from a rich, cowed rival.</summary>
    internal const int EuropeanTributeGoldCap = 100;

    /// <summary>
    /// The tribute a rival colony would yield to a demand right now (86d3fq0ev), as a pure function of the
    /// <b>military strength ratio</b> of the demander over the colony's owner and one RNG draw from
    /// <paramref name="random"/> — faithful to FreeCol routing a European tribute through <c>acceptDiplomaticTrade</c>,
    /// whose gold/stance verdict is strength-ratio-gated. A rival that is badly outmatched pays; an even or stronger one
    /// refuses:
    /// <list type="bullet">
    /// <item>ratio &lt; 0.5 (the demander is the weaker side) → refuses outright (0, no RNG drawn) — it will not be cowed.</item>
    /// <item>ratio in [0.5, 0.66] (an even match) → pays a token <c>roll·(ratio−0.5)·2</c> share, capped at
    /// <see cref="EuropeanTributeGoldCap"/>, but never more than its owner's treasury.</item>
    /// <item>ratio &gt; 0.66 (the demander dominates) → pays the full <c>roll</c> share, capped, treasury-limited.</item>
    /// </list>
    /// The <c>roll</c> is <c>random.Next(EuropeanTributeGoldCap + 1)</c> (0..100). <c>internal</c> so the band rule can be
    /// unit-tested directly with an injected RNG (ADR-006).
    /// </summary>
    internal int EvaluateColonyTributeDemand(Colony colony, int demanderId, IGameRandom random)
    {
        Player? owner = PlayerById(colony.OwnerId);
        if (owner is null)
        {
            return 0;
        }
        double ratio = StrengthRatio(demanderId, colony.OwnerId);
        if (ratio < 0.5)
        {
            return 0; // the demander is the weaker side — the rival refuses, no RNG drawn (FreeCol's strength gate)
        }
        int roll = random.Next(EuropeanTributeGoldCap + 1); // 0..100
        int share = ratio > 0.66
            ? roll                                          // dominant → full roll
            : (int)Math.Round(roll * (ratio - 0.5) * 2.0, MidpointRounding.AwayFromZero); // even match → token share scaling 0→1 across [0.5,0.66]
        return Math.Min(Math.Min(share, EuropeanTributeGoldCap), Math.Max(0, owner.Gold));
    }

    /// <summary>
    /// Demands tribute from the rival European colony at <paramref name="target"/> under threat of force (86d3fq0ev,
    /// FreeCol <c>moveTribute</c>'s European branch — <c>makePeaceTreaty(TRIBUTE)</c> + a <see cref="Diplomacy.GoldTradeItem"/>
    /// the rival pays). The colony's owner weighs the threat by the military strength ratio
    /// (<see cref="EvaluateColonyTributeDemand"/>): a badly-outmatched rival pays gold out of its treasury to the demander;
    /// an even-or-stronger one refuses. <b>Either way the demand is an act of aggression</b> — the rival's tension toward
    /// the demander rises by <see cref="TensionDemandTribute"/> (200), which (through the existing tension→stance machine)
    /// can tip a strained peace toward war — and the unit's turn ends. Paid gold moves from the rival owner's treasury to
    /// the demander's. The amount draws from the <b>demander's own RNG stream</b> (<see cref="RandomFor"/>), so a foreign
    /// power demanding never perturbs the human's stream 0 (ADR-009).
    /// </summary>
    /// <returns>Whether tribute was paid and how much (0 on a refusal).</returns>
    /// <exception cref="InvalidMoveException">Not allowed; see <see cref="CheckDemandTributeFromColony"/>.</exception>
    public EuropeanTributeResult DemandTributeFromColony(Unit unit, Position target) =>
        DemandTributeFromColony(unit, target, RandomFor(PlayerById(unit.OwnerId) ?? _human));

    /// <summary>The European-tribute resolution drawing from an explicit RNG (tests inject a fixed RNG to force the rolled amount), mirroring the native <see cref="DemandTribute(Unit, Position, IGameRandom)"/> overload.</summary>
    internal EuropeanTributeResult DemandTributeFromColony(Unit unit, Position target, IGameRandom random)
    {
        MoveCheck check = CheckDemandTributeFromColony(unit, target);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }

        Colony colony = ColonyAt(target)!;
        Player owner = PlayerById(colony.OwnerId)!;
        int gold = EvaluateColonyTributeDemand(colony, unit.OwnerId, random);
        if (gold > 0)
        {
            owner.Gold -= gold;                                  // paid out of the rival owner's treasury…
            (PlayerById(unit.OwnerId) ?? _human).Gold += gold;   // …to the demander
        }
        // Demanding is an act of aggression whether or not they paid — the rival resents the demander (directional, FreeCol
        // routes it through the tension machinery), which the tension→stance machine may later escalate to war.
        ChangeTension(colony.OwnerId, unit.OwnerId, TensionDemandTribute, symmetric: false);
        unit.MovementLeft = 0; // the demand ends the unit's turn
        return new EuropeanTributeResult(gold > 0, gold);
    }

    // ---- Stance-change tension modifiers (86d3c9u3z) ----

    /// <summary>Tension applied to a colonial pair when they ally (FreeCol <c>Tension.ALLIANCE_MODIFIER</c>).</summary>
    private const int TensionAllianceModifier = -500;

    /// <summary>Tension applied to a colonial pair when they sign a peace treaty (FreeCol <c>Tension.PEACE_TREATY_MODIFIER</c>).</summary>
    private const int TensionPeaceTreatyModifier = -250;

    /// <summary>Tension applied to a colonial pair when they agree a cease-fire (FreeCol <c>Tension.CEASE_FIRE_MODIFIER</c>).</summary>
    private const int TensionCeaseFireModifier = -250;

    /// <summary>Tension applied to a colonial pair when war resumes from a cease-fire (FreeCol <c>Tension.RESUME_WAR_MODIFIER</c>).</summary>
    internal const int TensionResumeWarModifier = 750;

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
            int score = EvaluateTradeItem(powerId, item, trade.Context);
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
    /// the power will not take this clause at any price. The <paramref name="context"/> the offer arose in shifts how a
    /// stance clause is weighed — a peace/cease-fire/alliance clause is <b>free</b> (scored 0) at
    /// <see cref="TradeContext.Contact"/> (FreeCol's <c>CONTACT</c> branch of <c>acceptDiplomaticTrade</c>: formalising
    /// first contact is never a cost and never refused), and otherwise weighed by the military strength ratio. Pure;
    /// draws no RNG.
    /// </summary>
    internal int EvaluateTradeItem(int powerId, TradeItem item, TradeContext context = TradeContext.Diplomatic) => item switch
    {
        GoldTradeItem gold => item.Source == powerId ? -gold.Amount : gold.Amount,
        GoodsTradeItem goods => EvaluateGoods(powerId, goods),
        StanceTradeItem stance => EvaluateStance(powerId, stance.Source == powerId ? stance.Destination : stance.Source, stance.Stance, context),
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
        return (int)Math.Round(revenue * (1.0 - power.TaxRate / 100.0), MidpointRounding.AwayFromZero); // after-tax sale value of what we receive
    }

    /// <summary>
    /// Stance value (FreeCol <c>StanceTradeItem.evaluateFor</c>): by the power's military strength ratio against the
    /// other party. Going to war is worth taking only when we're not badly outmatched (ratio &lt; 0.33 → invalid,
    /// &lt; 0.5 → a negative); making peace/cease-fire/alliance is the reverse (ratio &gt; 0.66 → invalid, &gt; 0.5 →
    /// negative, &lt; 0.33 → a strong +1000). An uncontacted stance is invalid.
    /// <para><b>First contact</b> (FreeCol's <c>CONTACT</c> branch in <c>acceptDiplomaticTrade</c>): when
    /// <paramref name="context"/> is <see cref="TradeContext.Contact"/>, a peace/cease-fire/alliance clause is forced
    /// neutral (scored 0) — never a cost and never invalid — so formalising "we have just met" with a peace treaty is
    /// always taken regardless of the strength ratio. War clauses are unaffected.</para>
    /// <para><b>Benjamin Franklin</b> (FreeCol's <c>franklin</c> branch): when the <em>other</em> party
    /// (<paramref name="otherId"/>) holds <c>alwaysOfferedPeace</c>, the same neutral-0 forcing applies — the AI always
    /// accepts a Franklin power's peace offer (and a too-strong opponent no longer rejects it).</para>
    /// </summary>
    private int EvaluateStance(int powerId, int otherId, Stance stance, TradeContext context = TradeContext.Diplomatic)
    {
        double ratio = StrengthRatio(powerId, otherId);
        int value = (int)Math.Round(100 * ratio, MidpointRounding.AwayFromZero);
        switch (stance)
        {
            case Stance.War:
                if (ratio < 0.33) return InvalidTradeItem;
                return ratio < 0.5 ? -value : value;
            case Stance.Peace or Stance.CeaseFire or Stance.Alliance:
                if (context == TradeContext.Contact) return 0;       // first contact: a peace clause is free (FreeCol CONTACT branch)
                if (OtherPartyAlwaysOffersPeace(otherId)) return 0;  // Franklin: the other party's peace is always taken
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
        return -(int)Math.Round(50.0 / ratio, MidpointRounding.AwayFromZero); // round-half-up to match FreeCol's Java Math.round (50.0/ratio is positive here), and the project convention
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

    // ---- AI treaty counter-offers (86d3c9uar) — the prune + counter + give-up loop ----

    /// <summary>
    /// The patience threshold in FreeCol's give-up roll (<c>EuropeanAIPlayer.acceptDiplomaticTrade</c>:
    /// <c>randomInt("Enough diplomacy?", 1 + version) &gt; 5</c>). At round/version 0 the draw is from
    /// <c>Next(1)</c> = always 0, so a power never gives up on the first counter; the upper bound grows with each
    /// round, so the chance of exceeding 5 (and abandoning the haggle) climbs as the negotiation drags on.
    /// </summary>
    private const int CounterPatienceThreshold = 5;

    /// <summary>The smallest residual deficit for which the power prefers to <b>halve a gold clause it pays</b> rather than drop a clause outright (FreeCol's <c>value &gt;= 50</c> magic-number guard in the prune loop).</summary>
    private const int GoldCounterFloor = 50;

    /// <summary>
    /// Produces the colonial power <paramref name="powerId"/>'s <b>counter-offer</b> to a treaty it has been offered but
    /// cannot accept as-is — a faithful subset of FreeCol <c>EuropeanAIPlayer.acceptDiplomaticTrade</c>'s prune + counter
    /// + give-up loop. Returns a new <see cref="DiplomaticTrade"/> (the received offer with the clauses the power values
    /// negatively pruned, and a gold clause it would pay <b>halved</b> rather than dropped where that alone closes the
    /// gap) that nets the power ≥ 0, or <c>null</c> to signal "give up / reject" (nothing left to offer, too many
    /// unacceptable clauses, or the power's patience ran out). Convenience: see <see cref="ShouldAcceptTrade"/> /
    /// <see cref="EvaluateTrade"/> for the accept/reject side.
    /// </summary>
    /// <param name="powerId">The colonial power weighing the offer (the recipient that may counter).</param>
    /// <param name="received">The treaty offered to <paramref name="powerId"/>.</param>
    /// <param name="round">
    /// The counter round, FreeCol's <c>DiplomaticTrade.getVersion()</c> — 0 for the first answer to a fresh offer, then
    /// incremented each time the power counters again. Higher rounds tolerate fewer unacceptable clauses and raise the
    /// chance the power simply gives up (the patience roll). Defaults to 0.
    /// </param>
    /// <remarks>
    /// <b>Determinism (ADR-009):</b> the only randomness is the give-up "patience" roll, drawn from
    /// <paramref name="powerId"/>'s <b>own</b> RNG stream via <see cref="RandomFor"/> — never the human's stream 0. A
    /// human (<c>powerId == _human.PlayerId</c>) never counters (the human decides at the negotiation table), so this is
    /// AI-only and the human's seeded game stays byte-identical. The clause valuation is the pure
    /// <see cref="EvaluateTradeItem"/>; building the counter mutates nothing (the new trade is a fresh value object).
    /// <b>Approximations vs FreeCol</b> (documented): the Franklin "always offered peace" special-case and the
    /// CONTACT-context peace exemption are out of scope (no founding-father diplomacy / contact negotiation yet); the
    /// per-round version counter is supplied by the caller rather than carried on the (un-persisted) trade.
    /// </remarks>
    public DiplomaticTrade? CounterOffer(int powerId, DiplomaticTrade received, int round = 0)
    {
        // The human never auto-counters (it negotiates by hand) and a non-colonial / empty offer is not negotiable.
        if (PlayerById(powerId) is not { PlayerType: PlayerType.Colonial } power
            || power.PlayerId == _human.PlayerId || received.Items.Count == 0)
        {
            return null;
        }

        // Score every clause once (FreeCol scores into a map, keyed by item): positive = good for us, negative = a cost,
        // InvalidTradeItem = a clause we will not take at any price.
        var scored = new List<(TradeItem Item, int Score)>(received.Items.Count);
        int unacceptable = 0;
        foreach (TradeItem item in received.Items)
        {
            int score = EvaluateTradeItem(powerId, item, received.Context);
            scored.Add((item, score));
            if (score == InvalidTradeItem)
            {
                unacceptable++;
            }
        }

        // Reject outright when too large a fraction of the clauses are unacceptable (FreeCol:
        // ratio > 0.5 − 0.5·version). The tolerance shrinks each round — a power that has already haggled is less
        // willing to keep pruning a deal that is mostly things it hates.
        double ratio = (double)unacceptable / (unacceptable + received.Items.Count);
        if (ratio > 0.5 - 0.5 * round)
        {
            return null;
        }

        // Drop the unacceptable clauses and keep the rest with their scores (FreeCol removes INVALID items, sums the
        // rest). If pruning the invalid clauses leaves nothing, there is no counter to make.
        var kept = scored.Where(s => s.Score != InvalidTradeItem).ToList();
        if (kept.Count == 0)
        {
            return null;
        }

        // Give up? FreeCol's patience roll — randomInt(1 + version) > 5. At round 0 the bound is 1 (always 0, never
        // gives up); the bound grows with the round, so a long haggle eventually exceeds the threshold and the power
        // walks away. Drawn from the POWER'S OWN stream (never the human's stream 0), so the human game is byte-stable.
        if (RandomFor(power).Next(1 + round) > CounterPatienceThreshold)
        {
            return null;
        }

        int value = kept.Sum(s => s.Score);

        // Build the counter from the kept clauses, pruning the negatives until the running sum is non-negative — in
        // ascending score order (most-disliked first), exactly as FreeCol does. A gold clause WE would pay is HALVED
        // (rather than dropped) when that alone closes the remaining deficit (value ≥ GoldCounterFloor); every other
        // negative clause is dropped. Positives are always kept.
        var counterItems = new List<TradeItem>();
        foreach ((TradeItem item, int score) in kept.OrderBy(s => s.Score))
        {
            if (value >= 0)
            {
                counterItems.Add(item); // sum already non-negative → keep the remaining (non-worse) clauses
                continue;
            }

            value -= score; // remove this clause's contribution from the running total (score < 0 here, so value rises)
            if (value >= GoldCounterFloor && item is GoldTradeItem gold && gold.Source == powerId)
            {
                // Counter a smaller gold payment instead of dropping it: halve what we pay, keep the rest of the deficit.
                int reduced = gold.Amount - value / 2;
                value /= 2;
                if (reduced > 0)
                {
                    counterItems.Add(new GoldTradeItem(gold.Source, gold.Destination, reduced));
                }
            }
            // else: drop the clause (do not re-add it) — value now reflects its removal.
        }

        // A counter is only worth proposing when it nets us ≥ 0 and still has at least one clause (FreeCol:
        // PROPOSE_TRADE iff value ≥ 0 && !empty, else reject).
        if (value < 0 || counterItems.Count == 0)
        {
            return null;
        }

        var counter = new DiplomaticTrade(received.ProposerId, received.RecipientId, received.Context);
        foreach (TradeItem item in counterItems)
        {
            counter.Add(item);
        }
        return counter;
    }

    // ---- AI proactive alliance + cease-fire proposals (86d3drn4f) ----

    /// <summary>
    /// The strength ratio at or above which a power feels <b>strong enough</b> to seek an alliance against a shared enemy
    /// (FreeCol's <c>EuropeanAIPlayer.determineStances</c>/<c>acceptDiplomaticTrade</c> uses the same military-strength
    /// ratio for stance decisions). At ≥ 0.5 the power is at least an even match for its prospective ally's enemy, so an
    /// alliance is a net gain rather than a liability it would be dragged into.
    /// </summary>
    private const double AllianceStrengthThreshold = 0.5;

    /// <summary>
    /// The half-width of the "stalemate" band around an even strength ratio (0.5) within which a power offers a
    /// <b>cease-fire</b> rather than fighting on: a war is judged stalemated when neither side has a decisive edge —
    /// <c>|ratio − 0.5| ≤ </c> this value (so 0.35..0.65). Outside the band one side is winning and presses on (a
    /// winner has no reason to stop; a loser sues for outright peace via the existing sue-for-peace path).
    /// </summary>
    private const double CeaseFireStalemateBand = 0.15;

    private readonly List<DiplomaticTrade> _pendingHumanProposals = []; // transient: treaties AI powers have offered the human this turn, awaiting the (backlogged) negotiation UI; not saved

    /// <summary>
    /// Treaties the foreign powers have proactively offered the <b>human</b> this turn (alliance / cease-fire), queued
    /// for the human's negotiation UI to answer (the UI is backlogged — this is the seam it will read). Transient and
    /// <b>not saved</b>; cleared each round and drained by the presentation via <see cref="TakePendingHumanProposals"/>.
    /// An AI-to-AI proactive offer never lands here — it is negotiated immediately on the proposer's own RNG stream.
    /// </summary>
    public IReadOnlyList<DiplomaticTrade> PendingHumanProposals => _pendingHumanProposals;

    /// <summary>Drains and clears the queued <see cref="PendingHumanProposals"/> (the negotiation UI reads them once). Clearing here keeps the seam from growing unbounded if no UI consumes it yet.</summary>
    public IReadOnlyList<DiplomaticTrade> TakePendingHumanProposals()
    {
        var taken = _pendingHumanProposals.ToList();
        _pendingHumanProposals.Clear();
        return taken;
    }

    /// <summary>Clears any queued <see cref="PendingHumanProposals"/> (called at the start of each round so the seam holds only this round's offers — belt-and-braces if the UI did not drain them).</summary>
    internal void ClearPendingHumanProposals() => _pendingHumanProposals.Clear();

    /// <summary>
    /// A foreign power's <b>proactive</b> diplomacy on its own turn (FreeCol <c>EuropeanAIPlayer.determineStances</c> +
    /// the proposal side of <c>acceptDiplomaticTrade</c>): beyond suing for peace when losing, a power now <em>offers</em>
    /// treaties off its own back —
    /// <list type="bullet">
    /// <item><b>Alliance</b> — when <paramref name="power"/> is militarily <b>strong</b> (strength ratio ≥
    /// <see cref="AllianceStrengthThreshold"/>) and at <see cref="Stance.Peace"/> (friendly) with another colonial power
    /// with whom it <b>shares an enemy</b> (both are at <see cref="Stance.War"/> with some third colonial power), it
    /// proposes an alliance to that friend.</item>
    /// <item><b>Cease-fire</b> — when a war it is in is <b>stalemated</b> (neither side has a decisive strength edge:
    /// <c>|ratio − 0.5| ≤ </c> <see cref="CeaseFireStalemateBand"/>), it proposes a cease-fire truce to the rival.</item>
    /// </list>
    /// An offer to <b>another AI</b> is settled immediately by routing it through the existing
    /// <see cref="NegotiateTrade"/> backend on <paramref name="power"/>'s own RNG stream (the answerer accepts / counters /
    /// gives up). An offer to the <b>human</b> is queued in <see cref="PendingHumanProposals"/> for the backlogged
    /// negotiation UI (it is <em>not</em> applied here — the human decides at the table). Both proposal kinds are built at
    /// <see cref="TradeContext.Diplomatic"/>.
    /// </summary>
    /// <remarks>
    /// <b>Determinism (ADR-009):</b> the only randomness is the AI-to-AI <see cref="NegotiateTrade"/> give-up roll, drawn
    /// from each AI power's <b>own</b> stream via <see cref="RandomFor"/> — never the human's stream 0. Queuing a
    /// human-bound proposal draws nothing. So the human's seeded game stays byte-identical and the default game (no war,
    /// no alliance opportunity) is unchanged. Both prospective-partner lists are iterated in stable <see cref="Player.PlayerId"/>
    /// order and snapshotted, since a settled alliance/cease-fire mutates stance mid-iteration. <b>Faithful subset:</b>
    /// FreeCol weighs each candidate via the full nation-summary/strength machinery and may offer gold-for-peace; we offer
    /// a single bare stance clause and rely on the existing strength-ratio evaluation to accept or decline it.
    /// </remarks>
    /// <param name="power">The foreign power taking its proactive-diplomacy turn (a non-human colonial power; a no-op otherwise).</param>
    public void ProposeProactiveTreaties(Player power)
    {
        if (power.IsHuman || power.PlayerType != PlayerType.Colonial)
        {
            return;
        }

        // 1) Cease-fire on a stalemated war (offered before alliance: ending a deadlocked war frees the power up).
        foreach (Player rival in ColonialPowersAtWarWith(power))
        {
            double ratio = StrengthRatio(power.PlayerId, rival.PlayerId);
            if (Math.Abs(ratio - 0.5) <= CeaseFireStalemateBand)
            {
                OfferStance(power, rival, Stance.CeaseFire);
            }
        }

        // 2) Alliance with a strong-and-friendly power that shares an enemy.
        foreach (Player friend in ColonialPowersAtPeaceWith(power))
        {
            if (StrengthRatio(power.PlayerId, friend.PlayerId) >= AllianceStrengthThreshold
                && SharesAnEnemy(power, friend))
            {
                OfferStance(power, friend, Stance.Alliance);
            }
        }
    }

    /// <summary>The colonial powers (the human + foreign powers) <paramref name="power"/> is at <see cref="Stance.War"/> with, in stable id order (snapshotted — a settled truce mutates stance mid-iteration).</summary>
    private List<Player> ColonialPowersAtWarWith(Player power) =>
        _players.Where(p => p.PlayerId != power.PlayerId && p.PlayerType == PlayerType.Colonial
                            && StanceBetween(power.PlayerId, p.PlayerId) == Stance.War)
                .OrderBy(p => p.PlayerId).ToList();

    /// <summary>The colonial powers <paramref name="power"/> is at <see cref="Stance.Peace"/> with, in stable id order (snapshotted — a settled alliance mutates stance mid-iteration).</summary>
    private List<Player> ColonialPowersAtPeaceWith(Player power) =>
        _players.Where(p => p.PlayerId != power.PlayerId && p.PlayerType == PlayerType.Colonial
                            && StanceBetween(power.PlayerId, p.PlayerId) == Stance.Peace)
                .OrderBy(p => p.PlayerId).ToList();

    /// <summary>Whether <paramref name="power"/> and <paramref name="friend"/> share a common enemy — some <em>third</em> colonial power both are at <see cref="Stance.War"/> with (FreeCol's "enemy of my enemy" alliance trigger).</summary>
    private bool SharesAnEnemy(Player power, Player friend) =>
        _players.Any(third => third.PlayerId != power.PlayerId && third.PlayerId != friend.PlayerId
                              && third.PlayerType == PlayerType.Colonial
                              && StanceBetween(power.PlayerId, third.PlayerId) == Stance.War
                              && StanceBetween(friend.PlayerId, third.PlayerId) == Stance.War);

    /// <summary>
    /// Offers a single-clause stance treaty from <paramref name="proposer"/> to <paramref name="target"/>: against
    /// another AI it is negotiated immediately via <see cref="NegotiateTrade"/> (the proposer's own stream); against the
    /// human it is queued in <see cref="PendingHumanProposals"/> for the negotiation UI (applied only when the human
    /// later accepts). Built at <see cref="TradeContext.Diplomatic"/>.
    /// </summary>
    private void OfferStance(Player proposer, Player target, Stance stance)
    {
        var offer = new DiplomaticTrade(proposer.PlayerId, target.PlayerId, TradeContext.Diplomatic)
            .Add(new StanceTradeItem(proposer.PlayerId, target.PlayerId, stance));

        if (target.IsHuman)
        {
            _pendingHumanProposals.Add(offer); // surfaced for the (backlogged) negotiation UI; the human decides — never auto-applied
        }
        else
        {
            NegotiateTrade(proposer, target, offer); // AI-to-AI: settled now on the proposer's own RNG stream, or the talks collapse
        }
    }
}
