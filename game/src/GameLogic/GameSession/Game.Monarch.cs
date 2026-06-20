using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Trade;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The home-nation Monarch (FreeCol <c>Monarch</c> + the server's per-turn monarch tick): each turn past a grace
/// period the player's King weighs a list of actions and may act — raise/lower tax, grow the Royal Expeditionary
/// Force, declare war/peace, or offer mercenaries/support. This file holds the weighted chooser and the per-turn
/// tick; each action's effect is wired in its own slice (items 2-6 of the independence arc).
///
/// <para><b>Determinism (ADR-009).</b> The Monarch is the human's own King, but its roll must not perturb the
/// human's stream 0 (which would change every existing seeded game past the grace turn). So the tick draws from an
/// <em>ephemeral</em> generator seeded off the human's current stream state (read non-destructively) plus the turn
/// — it consumes nothing from stream 0, yet is fully reproducible across save/load (the human state and the turn
/// are both persisted). A turn whose gate fails draws nothing at all.</para>
/// </summary>
public sealed partial class Game
{
    // Monarch action tuning is difficulty-driven (model.difficulty.monarch + refSize): meddling, tax cap/spread,
    // mercenary pricing, support size, boycott factor and the REF base composition all read Ruleset.Difficulty.Monarch
    // (default = classic medium, byte-identical to the old consts). See Specification/MonarchOptions.cs.
    private MonarchOptions MonarchOpts => Ruleset.Difficulty.Monarch;

    // FreeCol code-level monarch constants (Monarch.java / ServerPlayer.java hardcodes — not difficulty options).
    private const int MonarchMinTaxRate = 20;        // Monarch.MINIMUM_TAX_RATE
    private const int MonarchMinimumMercPrice = 200; // Monarch.MONARCH_MINIMUM_PRICE
    private const int HessianMinimumPrice = 5000;    // Monarch.HESSIAN_MINIMUM_PRICE
    private const int ForceTaxExtra = 3;             // ServerPlayer.csRaiseTax FORCE_TAX surcharge
    private const int TeaPartyBellDuration = 25;     // colonyGoodsParty modifier duration (turns) → +50% bells decaying −2%/turn

    // Classic mercenaryUnit land force (specification.xml): veteran soldiers, armed or mounted. (The naval man-o-war
    // mercenary and ability-driven type selection are a faithful-subset simplification.)
    private const string MercenaryUnitTypeId = "model.unit.veteranSoldier";
    private const string MercenarySoldierRoleId = "model.role.soldier";
    private const string MercenaryDragoonRoleId = "model.role.dragoon";
    private const string NavalSupportUnitTypeId = "model.unit.frigate"; // SUPPORT_SEA free naval aid (faithful-subset naval supportUnit)

    // Royal Expeditionary Force (item 6) unit/role identities (the counts are difficulty-driven, see MonarchOpts).
    private const string KingsRegularUnitTypeId = "model.unit.kingsRegular";
    private const string ManOWarUnitTypeId = "model.unit.manOWar";
    private const string RefInfantryRoleId = "model.role.infantry";
    private const string RefCavalryRoleId = "model.role.cavalry";

    private Force? _refForce; // the King's growing expeditionary force; null until grown (re-derives the base on demand)

    /// <summary>The Royal Expeditionary Force as it stands, or null if it has never been grown beyond the base (which is re-derivable). For the save.</summary>
    internal Force? RefForceOrNull => _refForce;

    /// <summary>The REF force, materialising the base composition on first need (FreeCol <c>Monarch.getExpeditionaryForce</c>).</summary>
    internal Force EnsureRefForce() => _refForce ??= BuildBaseRef();

    /// <summary>Installs a restored REF force (save load).</summary>
    internal void SetRefForce(Force force) => _refForce = force;

    private Force BuildBaseRef()
    {
        MonarchOptions mon = MonarchOpts;
        var force = new Force();
        force.AddLand(KingsRegularUnitTypeId, RefInfantryRoleId, mon.RefBaseInfantry);
        force.AddLand(KingsRegularUnitTypeId, RefCavalryRoleId, mon.RefBaseCavalry);
        force.AddLand(ArtilleryUnitTypeId, null, mon.RefBaseArtillery);
        force.AddNaval(ManOWarUnitTypeId, null, mon.RefBaseManOWar);
        return force;
    }

    /// <summary>
    /// Grows the REF (FreeCol <c>Monarch.addToREF</c>): add a man-o-war when the navy can't yet carry the land force
    /// (capacity &lt; spaceRequired × 1.1), otherwise 1-3 land units — king's regulars (1-in-3 mounted) or artillery.
    /// </summary>
    internal void AddToRef(IGameRandom rng)
    {
        Force force = EnsureRefForce();
        if (force.NavalCapacity(Ruleset) < force.SpaceRequired(Ruleset) * 1.1)
        {
            force.AddNaval(ManOWarUnitTypeId, null, 1);
            return;
        }
        int number = 1 + rng.Next(3); // 1-3
        if (rng.Next(2) == 0)
        {
            string role = rng.Next(3) == 0 ? RefCavalryRoleId : RefInfantryRoleId; // king's regulars, 1-in-3 mounted
            force.AddLand(KingsRegularUnitTypeId, role, number);
        }
        else
        {
            force.AddLand(ArtilleryUnitTypeId, null, number);
        }
    }

    private PendingMonarchDemand? _pendingMonarchDemand; // a monarch demand awaiting the human's accept/reject; persisted across save/load since v46 (86d3c9rk6)

    /// <summary>
    /// The monarch demand awaiting the human's response (FreeCol <c>MonarchSession</c>), or null. The presentation
    /// layer (P7) reads this to prompt and answers via <see cref="RespondToMonarch"/> (ADR-006). Persisted in the save
    /// since v46 so a game saved while the demand dialog is open reloads with the demand still pending (86d3c9rk6).
    /// </summary>
    public PendingMonarchDemand? PendingMonarchDemand => _pendingMonarchDemand;

    /// <summary>Re-installs a saved pending monarch demand on load (v46). The human answers it via <see cref="RespondToMonarch"/> exactly as if the King had just made it.</summary>
    internal void RestorePendingMonarchDemand(PendingMonarchDemand demand) => _pendingMonarchDemand = demand;

    /// <summary>RNG stream reserved for the Monarch — above every per-player stream and the LCR stream so a monarch
    /// roll never correlates with another stream. The tick re-seeds this stream id from the human's live state each
    /// turn, so nothing is persisted for it and stream 0 is untouched.</summary>
    private const ulong MonarchStreamId = 101;

    /// <summary>True once a privateer has attacked the human this game (gates SUPPORT_SEA). Set by the privateer-combat hook (not yet built — privateers arrive later); transient/derived, not persisted.</summary>
    internal bool AttackedByPrivateers { get; set; }

    /// <summary>
    /// Whether the Monarch could legally take <paramref name="action"/> right now (FreeCol <c>Monarch.actionIsValid</c>)
    /// — a pure, RNG-free predicate over the human's current state. The chooser only offers valid actions.
    /// </summary>
    internal bool MonarchActionIsValid(MonarchAction action) => action switch
    {
        MonarchAction.NoAction => true,
        MonarchAction.RaiseTaxAct or MonarchAction.RaiseTaxWar => _human.TaxRate < MonarchOpts.MaximumTaxRate,
        MonarchAction.ForceTax => false,
        MonarchAction.LowerTaxWar or MonarchAction.LowerTaxOther => _human.TaxRate > MonarchMinTaxRate + 10,
        MonarchAction.WaiveTax => true,
        MonarchAction.AddToRef => true, // the ruleset always has REF land + naval unit types (king's regular, man-o-war)
        MonarchAction.DeclarePeace => MonarchPotentialFriends().Any(),
        MonarchAction.DeclareWar => MonarchPotentialEnemies().Any(),
        MonarchAction.SupportSea => AttackedByPrivateers && !_human.SupportSeaGranted && !_human.MonarchDispleasure,
        MonarchAction.SupportLand or MonarchAction.MonarchMercenaries =>
            HumanIsAtWar() && !_human.MonarchDispleasure && HumanHasSettlements(),
        MonarchAction.HessianMercenaries => _human.Gold >= HessianMinimumPrice && HumanHasSettlements(),
        MonarchAction.Displeasure => false,
        _ => false,
    };

    /// <summary>
    /// The weighted list of monarch actions for <paramref name="turn"/> (FreeCol <c>Monarch.getActionChoices</c>):
    /// empty before the grace period, with no colonies, or once the revolution has begun; otherwise NO_ACTION
    /// (weight <c>max(200 − turn, 100)</c>) plus each valid action at its FreeCol weight.
    /// </summary>
    internal IReadOnlyList<(int Weight, MonarchAction Action)> GetMonarchActionChoices(int turn)
    {
        var choices = new List<(int Weight, MonarchAction Action)>();
        int dx = 1 + MonarchOpts.Meddling; // 3 at medium
        int grace = (6 - dx) * 10;         // 30 at medium

        if (turn < grace || !HumanHasSettlements() || _human.PlayerType != PlayerType.Colonial)
        {
            return choices; // the King does nothing in the early game, with no colonies, or after independence
        }

        void Add(MonarchAction action, int weight)
        {
            if (weight > 0 && MonarchActionIsValid(action))
            {
                choices.Add((weight, action));
            }
        }

        Add(MonarchAction.NoAction, Math.Max(200 - turn, 100));
        Add(MonarchAction.RaiseTaxAct, 5 + dx);
        Add(MonarchAction.RaiseTaxWar, 5 + dx);
        Add(MonarchAction.LowerTaxWar, 5 - dx);
        Add(MonarchAction.LowerTaxOther, 5 - dx);
        Add(MonarchAction.AddToRef, 10 + dx);
        Add(MonarchAction.DeclarePeace, 6 - dx);
        Add(MonarchAction.DeclareWar, 5 + dx);
        if (_human.Gold >= MonarchMinimumMercPrice)
        {
            Add(MonarchAction.MonarchMercenaries, 6 - dx);
        }
        else if (dx < 3)
        {
            Add(MonarchAction.SupportLand, 3 - dx); // never offered at medium (dx == 3)
        }
        Add(MonarchAction.SupportSea, 6 - dx);
        Add(MonarchAction.HessianMercenaries, 6 - dx);
        return choices;
    }

    /// <summary>
    /// One monarch tick, run once per round in <see cref="EndTurn"/>'s world-advance band. Draws nothing from
    /// stream 0 when the gate fails; otherwise picks one weighted action from an ephemeral monarch generator and
    /// dispatches it. See the class remarks for the determinism rationale.
    /// </summary>
    private void RunMonarchTick()
    {
        IReadOnlyList<(int Weight, MonarchAction Action)> choices = GetMonarchActionChoices(Turn);
        if (choices.Count == 0)
        {
            return; // gate failed — the King does nothing and no randomness is drawn
        }

        RandomState humanState = _random.SaveState(); // non-destructive read of stream 0
        var monarchRng = new Pcg32Random(humanState.State + (ulong)Turn, MonarchStreamId);
        MonarchAction action = RandomChoice.WeightedRandom(monarchRng, choices);
        DispatchMonarchAction(action, monarchRng);
    }

    /// <summary>Applies a chosen monarch action. Effects are wired in their own slices; an unwired action is a no-op this turn.</summary>
    internal void DispatchMonarchAction(MonarchAction action, IGameRandom rng)
    {
        switch (action)
        {
            case MonarchAction.NoAction:
                break;
            case MonarchAction.RaiseTaxAct:
            case MonarchAction.RaiseTaxWar:
                if (GetMostValuableGoods(_human) is { } goods) // nothing worth taxing → no demand (FreeCol skips)
                {
                    _pendingMonarchDemand = new PendingMonarchDemand(
                        action, TaxRaise: RaiseTaxAmount(rng),
                        GoodsId: goods.GoodsId, ColonyId: goods.ColonyId, GoodsAmount: goods.Amount);
                }
                break;
            case MonarchAction.LowerTaxWar:
            case MonarchAction.LowerTaxOther:
                SetTax(_human, LowerTaxAmount(rng)); // immediate goodwill, no player choice
                break;
            case MonarchAction.WaiveTax:
                break; // message only — no change
            case MonarchAction.MonarchMercenaries:
            case MonarchAction.HessianMercenaries:
                if (LoadMercenaries(rng) is { } offer) // an offer trimmed to what the player can afford (or none)
                {
                    _pendingMonarchDemand = new PendingMonarchDemand(action, Offer: offer.Force, Price: offer.Price);
                }
                break;
            case MonarchAction.SupportSea:
                GrantSupport(GetSupport(naval: true)); // free naval aid after privateer raids
                _human.SupportSeaGranted = true;       // one-shot
                break;
            case MonarchAction.SupportLand:
                GrantSupport(GetSupport(naval: false)); // free land aid (unreachable via the chooser at medium; handler kept for fidelity)
                break;
            case MonarchAction.AddToRef:
                AddToRef(rng); // the King grows the army he'll send if you rebel
                break;
            case MonarchAction.DeclareWar:
                // The King drags you into his war with a rival European power (FreeCol MONARCH_DECLARE_WAR):
                // king-chosen, no player veto. The gate guarantees a peace-standing target exists.
                ImposeMonarchStance(Stance.War, MonarchPotentialEnemies(), rng);
                break;
            case MonarchAction.DeclarePeace:
                // The King makes peace for you with a power you're at war/cease-fire with (FreeCol MONARCH_DECLARE_PEACE).
                ImposeMonarchStance(Stance.Peace, MonarchPotentialFriends(), rng);
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// The King imposes a <paramref name="stance"/> on the human toward one eligible European power he picks
    /// (FreeCol <c>MONARCH_DECLARE_WAR</c>/<c>MONARCH_DECLARE_PEACE</c>) — no player choice. The target is drawn from
    /// <paramref name="candidates"/> on the monarch's own RNG (never stream 0); a no-op when none are eligible
    /// (the action gate normally prevents that). Stance only — no tension delta and no save change (stances persist).
    /// </summary>
    private void ImposeMonarchStance(Stance stance, IEnumerable<Player> candidates, IGameRandom rng)
    {
        List<Player> eligible = candidates.OrderBy(p => p.PlayerId).ToList();
        if (eligible.Count == 0)
        {
            return;
        }
        Player target = eligible[rng.Next(eligible.Count)];
        SetStance(_human.PlayerId, target.PlayerId, stance);
    }

    /// <summary>
    /// The human's response to the <see cref="PendingMonarchDemand"/> (ADR-006 mutator). For a tax demand: accept
    /// raises the tax; reject forces a small extra rise (+3) when the taxed goods are already gone — the full
    /// Boston-Tea-Party reject (goods still in the colony) lands in item 3 (86d3c9r4w).
    /// </summary>
    /// <exception cref="InvalidOperationException">No monarch demand is pending.</exception>
    public void RespondToMonarch(bool accept)
    {
        if (_pendingMonarchDemand is not { } demand)
        {
            throw new InvalidOperationException("No monarch demand is pending.");
        }
        _pendingMonarchDemand = null;

        if (demand.Action is MonarchAction.RaiseTaxAct or MonarchAction.RaiseTaxWar)
        {
            if (accept)
            {
                SetTax(_human, demand.TaxRaise);
            }
            else if (ColonyById(demand.ColonyId) is not { } colony || colony.StoreOf(demand.GoodsId!) < demand.GoodsAmount)
            {
                SetTax(_human, demand.TaxRaise + ForceTaxExtra); // FORCE_TAX: goods removed, the King raises it anyway
            }
            else
            {
                HoldTeaParty(colony, demand.GoodsId!, demand.GoodsAmount); // Boston Tea Party — dump, boycott, rebel surge
            }
        }
        else if (demand.Action is MonarchAction.MonarchMercenaries or MonarchAction.HessianMercenaries)
        {
            if (accept)
            {
                _human.Gold -= demand.Price;
                foreach (ForceEntry entry in demand.Offer!)
                {
                    for (int i = 0; i < entry.Count; i++)
                    {
                        SpawnInEurope(entry.UnitTypeId, entry.RoleId, _human.PlayerId);
                    }
                }
            }
            else if (_human.Gold >= demand.Price)
            {
                _human.MonarchDispleasure = true; // declined an offer it could afford → the King sulks (no more support)
            }
        }
    }

    /// <summary>
    /// Builds a mercenary offer (FreeCol <c>Monarch.loadMercenaries</c>): 2-3 groups of 1-2 veteran soldiers, each
    /// armed or mounted, priced at the European purchase price × 65%, trimmed to what the player can afford. Returns
    /// null when nothing affordable can be offered.
    /// </summary>
    internal (IReadOnlyList<ForceEntry> Force, int Price)? LoadMercenaries(IGameRandom rng)
    {
        int groups = 2 + rng.Next(2); // 2-3
        var entries = new List<ForceEntry>();
        for (int i = 0; i < groups; i++)
        {
            int n = 1 + rng.Next(Math.Min(groups, 2)); // 1-2 per group
            string role = rng.Next(2) == 0 ? MercenarySoldierRoleId : MercenaryDragoonRoleId;
            entries.Add(new ForceEntry(MercenaryUnitTypeId, role, n));
        }

        int unitPrice = EuropeUnitPrice(_human, Ruleset.Unit(MercenaryUnitTypeId)) * MonarchOpts.MercenaryPricePercent / 100;
        if (unitPrice <= 0)
        {
            return null;
        }
        int offered = entries.Sum(e => e.Count);
        int affordable = Math.Min(offered, _human.Gold / unitPrice); // the King only offers what the treasury can buy
        if (affordable <= 0)
        {
            return null;
        }

        // Trim the entry list down to `affordable` units total (keeping the earlier groups).
        var trimmed = new List<ForceEntry>();
        int remaining = affordable;
        foreach (ForceEntry e in entries)
        {
            if (remaining <= 0)
            {
                break;
            }
            int take = Math.Min(e.Count, remaining);
            trimmed.Add(e with { Count = take });
            remaining -= take;
        }
        return (trimmed, affordable * unitPrice);
    }

    /// <summary>
    /// The free military aid a SUPPORT action grants (FreeCol <c>Monarch.getSupport</c>): SUPPORT_SEA = one naval
    /// support ship; SUPPORT_LAND at the medium support level (2) = two mounted veterans. RNG-free at medium (the
    /// composition is fixed).
    /// </summary>
    internal IReadOnlyList<ForceEntry> GetSupport(bool naval) => naval
        ? [new ForceEntry(NavalSupportUnitTypeId, null, 1)]
        : [new ForceEntry(MercenaryUnitTypeId, MercenaryDragoonRoleId, MonarchOpts.SupportLandMountedUnits)];

    /// <summary>Delivers a free force to the human's Europe dock (the King's military support — no gold cost).</summary>
    private void GrantSupport(IReadOnlyList<ForceEntry> force)
    {
        foreach (ForceEntry entry in force)
        {
            for (int i = 0; i < entry.Count; i++)
            {
                SpawnInEurope(entry.UnitTypeId, entry.RoleId, _human.PlayerId);
            }
        }
    }

    /// <summary>Creates a unit on a player's Europe dock in the given role (mercenary/support delivery, mirrors <see cref="BuyUnit(string)"/>).</summary>
    private void SpawnInEurope(string unitTypeId, string? roleId, int ownerId)
    {
        var unit = new Unit(_nextUnitId++, Ruleset.Unit(unitTypeId), new Position(0, 0))
        {
            Location = UnitLocation.InEurope,
            OwnerId = ownerId,
            RoleId = roleId ?? RoleType.DefaultRoleId,
        };
        _units.Add(unit);
    }

    /// <summary>
    /// A Boston Tea Party (FreeCol <c>csRaiseTax</c> reject branch): the colony dumps the demanded goods overboard,
    /// the good is boycotted (arrears = its sale price × 300), and rebel sentiment surges (+50% bell output for 25
    /// turns, decaying). The tax is <b>not</b> raised.
    /// </summary>
    private void HoldTeaParty(Colony colony, string goodsId, int amount)
    {
        colony.AddGoods(goodsId, -amount);
        _human.Market.SetArrears(goodsId, _human.Market.BidPrice(goodsId) * MonarchOpts.ArrearsFactor);
        colony.TeaPartyBellTurns = TeaPartyBellDuration;
    }

    /// <summary>Whether the player can pay off a good's boycott back-taxes (FreeCol <c>payArrears</c>): the good is boycotted and the player can afford the arrears.</summary>
    public MoveCheck CheckPayArrears(string goodsId)
    {
        int arrears = _human.Market.Arrears(goodsId);
        if (arrears <= 0)
        {
            return MoveCheck.No("That good is not under boycott.");
        }
        if (_human.Gold < arrears)
        {
            return MoveCheck.No($"Lifting the boycott costs {arrears} gold.");
        }
        return MoveCheck.Yes(arrears);
    }

    /// <summary>Pays a good's boycott back-taxes, lifting the boycott (FreeCol <c>payArrears</c>).</summary>
    /// <exception cref="InvalidMoveException">The boycott cannot be paid off; see <see cref="CheckPayArrears"/>.</exception>
    public void PayArrears(string goodsId)
    {
        MoveCheck check = CheckPayArrears(goodsId);
        if (!check.Allowed)
        {
            throw new InvalidMoveException(check.Reason!);
        }
        _human.Gold -= check.Cost;
        _human.Market.SetArrears(goodsId, 0);
    }

    /// <summary>The new tax rate after a raise: <c>min(tax + 1 + rnd[0, 3 + turn/40), 65)</c> (FreeCol <c>Monarch.raiseTax</c>).</summary>
    internal int RaiseTaxAmount(IGameRandom rng)
    {
        int divisor = Math.Max(1, (6 - MonarchOpts.TaxAdjustment) * 10); // 40 at medium
        int adjust = 1 + rng.Next(3 + Turn / divisor);
        return Math.Min(_human.TaxRate + adjust, MonarchOpts.MaximumTaxRate);
    }

    /// <summary>The new tax rate after a reduction: <c>max(tax − 1 − rnd[0, 8), 20)</c> (FreeCol <c>Monarch.lowerTax</c>).</summary>
    internal int LowerTaxAmount(IGameRandom rng)
    {
        int adjust = 1 + rng.Next(Math.Max(1, 10 - MonarchOpts.TaxAdjustment)); // 1 + rnd[0,8) at medium
        return Math.Max(_human.TaxRate - adjust, MonarchMinTaxRate);
    }

    /// <summary>Sets a player's sales tax (FreeCol <c>csSetTax</c>); the rate is read live wherever tax applies.</summary>
    private static void SetTax(Player player, int rate) => player.TaxRate = rate;

    /// <summary>
    /// The player's most valuable tradeable colony goods the King would tax (FreeCol <c>Player.getMostValuableGoods</c>):
    /// the highest sale-value stockpile across its colonies (amount capped at one cargo), not boycotted. Null if none.
    /// </summary>
    internal ValuableGoods? GetMostValuableGoods(Player player)
    {
        ValuableGoods? best = null;
        int bestValue = -1;
        foreach (Colony colony in _colonies.Where(c => c.OwnerId == player.PlayerId).OrderBy(c => c.Id))
        {
            foreach ((string goodsId, int stored) in colony.Stores.OrderBy(kv => kv.Key))
            {
                if (stored <= 0 || !player.Market.IsTradeable(goodsId) || !player.Market.CanTrade(goodsId))
                {
                    continue; // skip empty, non-tradeable, and boycotted goods
                }
                int amount = Math.Min(stored, Market.CargoChunk);
                int value = player.Market.BidPrice(goodsId) * amount;
                if (value > bestValue)
                {
                    best = new ValuableGoods(goodsId, colony.Id, amount);
                    bestValue = value;
                }
            }
        }
        return best;
    }

    private Colony? ColonyById(int id) => _colonies.FirstOrDefault(c => c.Id == id);

    private bool HumanHasSettlements() => _colonies.Any(c => c.OwnerId == _human.PlayerId);

    private bool HumanIsAtWar() => _human.Stances.Values.Any(s => s == Stance.War);

    private IEnumerable<Player> MonarchPotentialEnemies() =>
        _players.Where(p => p.PlayerId != _human.PlayerId && p.PlayerType == PlayerType.Colonial
            && _human.Stances.GetValueOrDefault(p.PlayerId) == Stance.Peace);

    private IEnumerable<Player> MonarchPotentialFriends() =>
        _players.Where(p => p.PlayerId != _human.PlayerId && p.PlayerType == PlayerType.Colonial
            && _human.Stances.GetValueOrDefault(p.PlayerId) is Stance.War or Stance.CeaseFire);
}
