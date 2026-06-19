using CrownAndColony.GameLogic.Randomness;

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
    // FreeCol monarch constants. TODO(86d3c9rg6): route MonarchMeddling/MaxTax through ruleset gameOptions.
    private const int MonarchMeddling = 2;          // GameOptions.MONARCH_MEDDLING (medium); dx = 1 + meddling = 3
    private const int MonarchMaxTaxRate = 65;       // GameOptions.MAXIMUM_TAX
    private const int MonarchMinTaxRate = 20;       // Monarch.MINIMUM_TAX_RATE
    private const int MonarchMinimumMercPrice = 200; // Monarch.MONARCH_MINIMUM_PRICE
    private const int HessianMinimumPrice = 5000;    // Monarch.HESSIAN_MINIMUM_PRICE

    /// <summary>RNG stream reserved for the Monarch — above every per-player stream and the LCR stream so a monarch
    /// roll never correlates with another stream. The tick re-seeds this stream id from the human's live state each
    /// turn, so nothing is persisted for it and stream 0 is untouched.</summary>
    private const ulong MonarchStreamId = 101;

    /// <summary>True once a privateer has attacked the human this game (gates SUPPORT_SEA). Set with the support slice (item 5).</summary>
    private bool AttackedByPrivateers { get; set; }

    /// <summary>
    /// Whether the Monarch could legally take <paramref name="action"/> right now (FreeCol <c>Monarch.actionIsValid</c>)
    /// — a pure, RNG-free predicate over the human's current state. The chooser only offers valid actions.
    /// </summary>
    internal bool MonarchActionIsValid(MonarchAction action) => action switch
    {
        MonarchAction.NoAction => true,
        MonarchAction.RaiseTaxAct or MonarchAction.RaiseTaxWar => _human.TaxRate < MonarchMaxTaxRate,
        MonarchAction.ForceTax => false,
        MonarchAction.LowerTaxWar or MonarchAction.LowerTaxOther => _human.TaxRate > MonarchMinTaxRate + 10,
        MonarchAction.WaiveTax => true,
        // REF unit types are modelled in item 6 (86d3c9v4j); until then the King has no force to add to.
        MonarchAction.AddToRef => false,
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
        int dx = 1 + MonarchMeddling;   // 3 at medium
        int grace = (6 - dx) * 10;      // 30 at medium

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
        DispatchMonarchAction(action);
    }

    /// <summary>Applies a chosen monarch action. Effects are wired in their own slices; an unwired action is a no-op this turn.</summary>
    private void DispatchMonarchAction(MonarchAction action)
    {
        switch (action)
        {
            case MonarchAction.NoAction:
                break;
            // RAISE_TAX / LOWER_TAX / WAIVE_TAX -> item 2 (86d3c9r2m); mercenaries + DISPLEASURE -> item 4 (86d3c9rep);
            // SUPPORT_SEA/LAND -> item 5 (86d3c9rag); ADD_TO_REF -> item 6 (86d3c9v4j); DECLARE_WAR/PEACE -> monarch
            // diplomacy slice. Until each lands, an offered-but-unwired action passes harmlessly this turn.
            default:
                break;
        }
    }

    private bool HumanHasSettlements() => _colonies.Any(c => c.OwnerId == _human.PlayerId);

    private bool HumanIsAtWar() => _human.Stances.Values.Any(s => s == Stance.War);

    private IEnumerable<Player> MonarchPotentialEnemies() =>
        _players.Where(p => p.PlayerId != _human.PlayerId && p.PlayerType == PlayerType.Colonial
            && _human.Stances.GetValueOrDefault(p.PlayerId) == Stance.Peace);

    private IEnumerable<Player> MonarchPotentialFriends() =>
        _players.Where(p => p.PlayerId != _human.PlayerId && p.PlayerType == PlayerType.Colonial
            && _human.Stances.GetValueOrDefault(p.PlayerId) is Stance.War or Stance.CeaseFire);
}
