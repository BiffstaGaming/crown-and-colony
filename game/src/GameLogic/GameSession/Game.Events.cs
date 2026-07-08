using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The data-driven <b>historical-event engine</b> (Australian Federation, tasks 4c.2–4c.5): each turn a colonial
/// player's eligible <see cref="EventDef"/>s are gathered, one is drawn by seeded weight, offered to the player (the
/// human is prompted; an AI/unanswered offer auto-resolves), and the chosen option's <see cref="EventEffect"/>s are
/// applied. One-shot and cooldown bookkeeping lives in <c>_eventLastFiredTurn</c>.
/// <para>
/// <b>Strict no-op in classic.</b> The classic ruleset defines no historical events
/// (<see cref="Ruleset.HistoricalEvents"/> is empty), so <see cref="RollHistoricalEvents"/> guard-returns
/// <em>before any RNG</em> — no draw on any stream — and <c>_eventLastFiredTurn</c> stays empty so its save token is
/// omitted. The default game is therefore byte-identical (ADR-006/009), exactly like the natural-disaster roll it is
/// modelled on. A variant adds events by data alone.
/// </para>
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// The historical event currently awaiting the human's choice, or <c>null</c> if none (4c.3). Transient per-turn UI
    /// state — created during the human's turn (<see cref="RollHistoricalEvents"/>), answered via
    /// <see cref="ChooseEventOption"/>, and auto-resolved to its default option at the next <see cref="EndTurn"/> if
    /// ignored; never saved. Always <c>null</c> in the classic game (no events).
    /// </summary>
    public EventOffer? PendingEventOffer => _pendingEventOffer;

    /// <summary>A historical event offered to the human awaiting a choice: the event and the colony it fired for (if any).</summary>
    /// <param name="EventId">The offered <see cref="EventDef.Id"/>.</param>
    /// <param name="ColonyId">The colony the event fired for (colony-scoped effects target it), or <c>null</c> for a player-wide event.</param>
    /// <param name="OptionIds">The option ids the player may choose between (the offered set <see cref="ChooseEventOption"/> validates against).</param>
    public sealed record EventOffer(string EventId, int? ColonyId, IReadOnlyList<string> OptionIds);

    /// <summary>
    /// The per-turn historical-event roll for one colonial player (4c.3), modelled on <see cref="RollNaturalDisasters"/>.
    /// Guard-returns <b>before any RNG</b> when the ruleset defines no events — so the classic default game draws nothing
    /// and stays byte-identical. When events exist:
    /// <list type="number">
    /// <item><b>Forced scenario-start events</b> (4c.5) fire first, unconditionally and once, at scenario start (turn 1) —
    /// bypassing the weighted draw and requirement gates. Each is auto-resolved (no offer) so opening state is seeded
    /// deterministically.</item>
    /// <item>Then the ordinary pipeline: gather every <see cref="EventDef"/> currently <see cref="IsEventEligible">eligible</see>
    /// for this player, draw one by seeded weight on the reserved <see cref="EventStreamId"/> stream (seeded from the
    /// player's own state read WITHOUT advancing stream 0), and either offer it to the human or auto-resolve it for an AI.</item>
    /// </list>
    /// At most one drawn event fires per player per turn (like the disaster roll).
    /// </summary>
    /// <param name="player">The colonial player whose turn is being processed.</param>
    private void RollHistoricalEvents(Player player)
    {
        IReadOnlyList<EventDef> all = Ruleset.HistoricalEvents;
        if (all.Count == 0)
        {
            return; // classic default: no events, no draw on any stream, byte-identical (ADR-009)
        }

        // Colony-scoped effects (grantGoods / revealMap / a colony-scoped timedModifier) target the player's first
        // colony, if any; a player with no colony resolves those effects as no-ops.
        int? colonyId = ColoniesOf(player).Select(c => (int?)c.Id).FirstOrDefault();

        // (1) Forced scenario-start events (4c.5): fire once, unconditionally, at scenario start. These need no RNG —
        // there is no choice to draw and they auto-resolve — so they never touch the event stream.
        if (Turn <= 1)
        {
            foreach (EventDef setup in all.Where(e => e.IsScenarioStart && !HasEventFired(e)))
            {
                FireEvent(player, setup, colonyId, rng: null); // scenario setup auto-resolves; colony-scoped effects target the first colony
            }
        }

        // (2) The ordinary weighted draw over the currently-eligible normal events.
        List<EventDef> eligible = all.Where(e => e.Trigger == EventTrigger.Normal && IsEventEligible(e, player)).ToList();
        if (eligible.Count == 0)
        {
            return; // nothing to draw → no RNG consumed
        }

        // A dedicated event generator seeded off this player's own RNG state — never advancing the human's stream 0 (we
        // read its saved state word read-only). The turn and player id mix in so successive turns/players draw independently.
        ulong baseState = RandomFor(player).SaveState().State;
        var rng = new Pcg32Random(baseState ^ ((ulong)Turn << 3) ^ ((ulong)player.PlayerId << 40), EventStreamId);

        var weighted = eligible.Select(e => (Math.Max(1, e.Weight), e)).ToList();
        EventDef drawn = RandomChoice.WeightedRandom(rng, weighted);

        if (player.IsHuman && drawn.Options.Count > 1)
        {
            // A genuine dilemma for the human: offer it and let the presentation prompt. Recorded as fired now (so it
            // is not re-drawn while the offer is pending); the chosen/auto-resolved option's effects apply on answer.
            _pendingEventOffer = new EventOffer(drawn.Id, colonyId, drawn.Options.Select(o => o.Id).ToList());
            MarkEventFired(drawn);
        }
        else
        {
            // A single-option event, or an AI player: resolve immediately (deterministic auto-pick), no prompt.
            FireEvent(player, drawn, colonyId, rng);
        }
    }

    /// <summary>
    /// Whether a normal <see cref="EventDef"/> is currently eligible to be drawn for <paramref name="player"/> (4c.3):
    /// not already fired (one-shot), off cooldown, within its year window, its <see cref="EventDef.Requirements"/> all
    /// satisfied, and — for the human — no event offer already pending. Pure (no RNG, no state change), mirroring the
    /// limit engine's read-only evaluation.
    /// </summary>
    /// <param name="ev">The event to test.</param>
    /// <param name="player">The player it would fire for.</param>
    /// <returns>True iff the event may be drawn this turn.</returns>
    private bool IsEventEligible(EventDef ev, Player player)
    {
        if (player.IsHuman && _pendingEventOffer is not null)
        {
            return false; // one pending offer at a time
        }
        if (ev.OneShot && HasEventFired(ev))
        {
            return false; // a spent one-shot event never fires again
        }
        if (ev.ExpiryYear > 0 && CurrentYear > ev.ExpiryYear)
        {
            return false; // past its expiry window
        }
        if (ev.EarliestYear > 0 && CurrentYear < ev.EarliestYear)
        {
            return false; // before its earliest year
        }
        if (!ev.OneShot && ev.Cooldown > 0 && _eventLastFiredTurn.TryGetValue(ev.Id, out int last) && Turn - last < ev.Cooldown)
        {
            return false; // still cooling down since it last fired
        }
        return ev.Requirements.All(limit => EvaluateLimit(limit, player)); // reuse the existing limit/operand engine
    }

    /// <summary>Whether the event has ever fired this game (its id is recorded in <c>_eventLastFiredTurn</c>).</summary>
    private bool HasEventFired(EventDef ev) => _eventLastFiredTurn.ContainsKey(ev.Id);

    /// <summary>Records that the event fired on the current turn (for one-shot/cooldown gating).</summary>
    private void MarkEventFired(EventDef ev) => _eventLastFiredTurn[ev.Id] = Turn;

    /// <summary>
    /// Fires an event immediately for <paramref name="player"/> (no human prompt): marks it fired, picks the resolving
    /// option (the sole option, or — for a multi-option AI/auto event — the deterministic <see cref="AutoSelectOption"/>
    /// pick), and applies that option's effects. <paramref name="rng"/> is unused today (effect resolution is
    /// deterministic) but threaded through for a future randomised effect; a scenario-setup event passes <c>null</c>.
    /// </summary>
    private void FireEvent(Player player, EventDef ev, int? colonyId, IGameRandom? rng)
    {
        MarkEventFired(ev);
        EventOption option = ev.Options.Count == 1 ? ev.Options[0] : AutoSelectOption(ev);
        ApplyEventOption(player, ev, option, colonyId);
    }

    /// <summary>
    /// The deterministic, RNG-free auto/AI resolver for a multi-option event (4c.3), modelled on
    /// <see cref="SelectFoundingFatherFor"/>: the option with the greatest <see cref="EventOption.Weight"/>, ties broken
    /// by ordinal option-id order — so an unanswered human offer and an AI event both resolve reproducibly without
    /// consuming any RNG stream.
    /// </summary>
    /// <param name="ev">The event whose options to choose among (assumed non-empty).</param>
    /// <returns>The option to resolve.</returns>
    private static EventOption AutoSelectOption(EventDef ev) => ev.Options
        .OrderByDescending(o => o.Weight)
        .ThenBy(o => o.Id, StringComparer.Ordinal)
        .First();

    /// <summary>
    /// The human's choice of which offered event option to take (4c.3), modelled on <see cref="ChooseFather"/>: validates
    /// the id against the currently-offered set (throws <see cref="InvalidMoveException"/> otherwise), then resolves that
    /// option's effects and clears the pending offer.
    /// </summary>
    /// <param name="optionId">The offered option id to choose.</param>
    /// <exception cref="InvalidMoveException">No event is pending, or the id is not among the offered options.</exception>
    public void ChooseEventOption(string optionId)
    {
        if (_pendingEventOffer is not { } offer)
        {
            throw new InvalidMoveException("No event is awaiting a choice.");
        }
        if (!offer.OptionIds.Contains(optionId))
        {
            throw new InvalidMoveException($"{optionId} is not an offered option for event {offer.EventId}.");
        }
        EventDef ev = Ruleset.HistoricalEvent(offer.EventId)
            ?? throw new InvalidMoveException($"Offered event {offer.EventId} is not in the ruleset.");
        EventOption option = ev.Option(optionId)
            ?? throw new InvalidMoveException($"{optionId} is not an option of event {offer.EventId}.");
        _pendingEventOffer = null;
        ApplyEventOption(_human, ev, option, offer.ColonyId);
    }

    /// <summary>
    /// Auto-resolves a historical-event offer the human ended the turn without answering (4c.3), modelled on
    /// <see cref="RefusePendingDemand"/>: the deterministic default option (<see cref="AutoSelectOption"/>) is applied
    /// and the offer cleared. A no-op when nothing is pending (the classic case).
    /// </summary>
    private void AutoResolvePendingEventOffer()
    {
        if (_pendingEventOffer is not { } offer)
        {
            return;
        }
        _pendingEventOffer = null;
        if (Ruleset.HistoricalEvent(offer.EventId) is { } ev)
        {
            ApplyEventOption(_human, ev, AutoSelectOption(ev), offer.ColonyId);
        }
    }

    /// <summary>
    /// Applies one event option's effect list (4c.4, the reusable effect vocabulary) for <paramref name="player"/>,
    /// scoped to <paramref name="colonyId"/> where an effect is colony-scoped. Each <see cref="EventEffect"/> routes to
    /// an existing applier (the same ones founding-father election / natural disasters use). Effects apply in spec order.
    /// </summary>
    private void ApplyEventOption(Player player, EventDef ev, EventOption option, int? colonyId)
    {
        foreach (EventEffect effect in option.Effects)
        {
            ApplyEventEffect(player, ev, effect, colonyId);
        }
    }

    /// <summary>
    /// Applies a single <see cref="EventEffect"/> (4c.4) via the existing engine appliers:
    /// <list type="bullet">
    /// <item><see cref="EventEffectKind.TimedModifier"/> → <see cref="RegisterTemporaryModifier"/> a
    /// <see cref="TemporaryModifier"/> (value = a <see cref="FatherModifier"/> folded by <c>ModifierMath.Apply</c>),
    /// colony-scoped when the event fired for a colony, auto-expiring in <see cref="RemoveExpiredTemporaryModifiers"/>;</item>
    /// <item><see cref="EventEffectKind.GrantGold"/>/<see cref="EventEffectKind.GrantLiberty"/> → the player's treasury/liberty;</item>
    /// <item><see cref="EventEffectKind.GrantUnit"/> → <see cref="SpawnInEurope"/> (the John-Paul-Jones path);</item>
    /// <item><see cref="EventEffectKind.GrantGoods"/> → <c>Colony.AddGoods</c> on the event's colony;</item>
    /// <item><see cref="EventEffectKind.RevealMap"/> → <see cref="RevealAround(Player, World.Position, int)"/> the event's colony;</item>
    /// <item><see cref="EventEffectKind.RecordHistory"/> → a <see cref="HistoryEventKind.HistoricalEvent"/> log line (human only).</item>
    /// </list>
    /// A colony-scoped effect with no colony (a player-wide event) is a no-op for that effect.
    /// </summary>
    private void ApplyEventEffect(Player player, EventDef ev, EventEffect effect, int? colonyId)
    {
        Colony? colony = colonyId is { } cid ? _colonies.FirstOrDefault(c => c.Id == cid) : null;
        switch (effect.Kind)
        {
            case EventEffectKind.TimedModifier:
                // A duration-bounded modifier on the given target, colony-scoped when the event has a colony (so a
                // production bonus damps/boosts only that colony) or player-wide otherwise (e.g. a movement bonus).
                // At EventProductionIndex — above the worker/father indices — so it folds on the final value.
                var payload = new FatherModifier(effect.TargetId ?? "", effect.ModifierType, effect.Value, EventModifierIndex);
                int duration = Math.Max(1, effect.Duration);
                RegisterTemporaryModifier(TemporaryModifier.MakeTimed(payload, duration, Turn, colony?.Id));
                break;
            case EventEffectKind.GrantGold:
                player.Gold = Math.Max(0, player.Gold + (int)effect.Value);
                break;
            case EventEffectKind.GrantLiberty:
                player.Liberty = Math.Max(0, player.Liberty + (int)effect.Value);
                break;
            case EventEffectKind.GrantUnit:
                if (effect.TargetId is { } unitTypeId && Ruleset.UnitTypes.Any(u => u.Id == unitTypeId))
                {
                    SpawnInEurope(unitTypeId, effect.RoleId, player.PlayerId);
                }
                break;
            case EventEffectKind.GrantGoods:
                if (colony is not null && effect.TargetId is { } goodsId)
                {
                    colony.AddGoods(goodsId, (int)effect.Value);
                }
                break;
            case EventEffectKind.RevealMap:
                if (colony is not null)
                {
                    RevealAround(player, colony.Position, (int)effect.Value);
                }
                break;
            case EventEffectKind.RecordHistory:
                if (player.IsHuman && effect.Text is { } text)
                {
                    RecordHistory(HistoryEventKind.HistoricalEvent, text);
                }
                break;
        }
    }

    /// <summary>
    /// The modifier index an event's <see cref="EventEffectKind.TimedModifier"/> applies at — high (like the disaster
    /// production index), so it folds <b>after</b> the worker (30) and founding-father (40) modifiers, on the final value.
    /// </summary>
    private const int EventModifierIndex = 110;

    /// <summary>
    /// Restores the persisted per-event last-fired turns to a freshly loaded game (v71+; omit-when-empty). A save with no
    /// such token (the classic case, or a pre-v71 save) passes nothing and the map stays empty — the pre-persistence
    /// behaviour. Called once by <see cref="Persistence.SaveGame.Restore"/>.
    /// </summary>
    /// <param name="lastFired">The saved event id → last-fired turn pairs.</param>
    internal void RestoreEventState(IReadOnlyDictionary<string, int> lastFired)
    {
        foreach ((string id, int turn) in lastFired)
        {
            _eventLastFiredTurn[id] = turn;
        }
    }

    /// <summary>The per-event last-fired turns (event id → turn), for save serialization (omit-when-empty). Empty in the classic game.</summary>
    internal IReadOnlyDictionary<string, int> EventLastFiredTurn => _eventLastFiredTurn;
}
