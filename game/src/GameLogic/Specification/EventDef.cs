namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// The discrete kinds of <b>event effect</b> the runtime resolves when an event option is chosen (the reusable
/// effect vocabulary, 4c.4). Each kind names a small, deterministic action the engine already knows how to perform —
/// modelled on <see cref="DisasterEffectKind"/>. An effect id the parser does not recognise is dropped (forward-
/// compatible: a variant can carry a richer effect the base engine ignores). New kinds are added here and to the
/// resolver switch in <c>Game.Events.cs</c>.
/// </summary>
public enum EventEffectKind
{
    /// <summary>Register a duration-bounded <see cref="GameSession.TemporaryModifier"/> (FreeCol <c>Modifier.makeTimedModifier</c>):
    /// a timed percentage/additive bonus on a modifier target (a good, or <c>model.modifier.movementBonus</c>) for
    /// <see cref="EventEffect.Duration"/> turns, scoped to a colony when the event fired for one. Folds through the
    /// existing modifier machinery and auto-expires in the per-turn strip. Carries <see cref="EventEffect.TargetId"/>,
    /// <see cref="EventEffect.ModifierType"/>, <see cref="EventEffect.Value"/>, <see cref="EventEffect.Duration"/>.</summary>
    TimedModifier,

    /// <summary>Grant the player gold (<c>player.Gold += value</c>). <see cref="EventEffect.Value"/> is the amount (may be negative to levy).</summary>
    GrantGold,

    /// <summary>Grant the player liberty/bells (<c>player.Liberty += value</c>). <see cref="EventEffect.Value"/> is the amount.</summary>
    GrantLiberty,

    /// <summary>Spawn one free unit on the player's Europe dock (reuses <c>SpawnInEurope</c>, the John-Paul-Jones path).
    /// <see cref="EventEffect.TargetId"/> is the unit-type id; <see cref="EventEffect.RoleId"/> its optional military role.</summary>
    GrantUnit,

    /// <summary>Add goods to the event's colony (reuses <c>Colony.AddGoods</c>). <see cref="EventEffect.TargetId"/> is the
    /// goods id, <see cref="EventEffect.Value"/> the amount (negative removes). A no-op when the event has no colony scope.</summary>
    GrantGoods,

    /// <summary>Reveal the map around the event's colony (reuses <c>RevealAround</c>). <see cref="EventEffect.Value"/> is the
    /// reveal radius. A no-op when the event has no colony scope.</summary>
    RevealMap,

    /// <summary>Append a line to the human's history log (reuses <c>RecordHistory</c>, kind <c>HistoricalEvent</c>).
    /// <see cref="EventEffect.Text"/> is the log line. Purely a record — no game-state change beyond the log.</summary>
    RecordHistory,
}

/// <summary>
/// One effect an event option applies (the <c>&lt;effect&gt;</c> child of an <c>&lt;option&gt;</c>): its
/// <see cref="Kind"/> plus the fields that kind reads. Every field is optional; a kind reads only the ones it needs
/// (a <see cref="EventEffectKind.GrantGold"/> reads only <see cref="Value"/>; a <see cref="EventEffectKind.TimedModifier"/>
/// reads <see cref="TargetId"/>/<see cref="ModifierType"/>/<see cref="Value"/>/<see cref="Duration"/>). Modelled on
/// <see cref="DisasterEffect"/>.
/// </summary>
/// <param name="Kind">Which effect this is (drives which fields are read and which resolver branch runs).</param>
/// <param name="TargetId">The id the effect targets — a modifier target / goods id / unit-type id, per <see cref="Kind"/>; null when the kind needs none.</param>
/// <param name="ModifierType">How a <see cref="EventEffectKind.TimedModifier"/> combines with the base (default <see cref="Specification.ModifierType.Percentage"/>); ignored by other kinds.</param>
/// <param name="Value">The numeric payload — a modifier value, a gold/liberty/goods amount, or a reveal radius, per <see cref="Kind"/>.</param>
/// <param name="Duration">Turns a <see cref="EventEffectKind.TimedModifier"/> lasts (≥ 1); ignored by other kinds.</param>
/// <param name="RoleId">The military role for a <see cref="EventEffectKind.GrantUnit"/> (null = the unit's default role); ignored by other kinds.</param>
/// <param name="Text">The log line for a <see cref="EventEffectKind.RecordHistory"/>; ignored by other kinds.</param>
public sealed record EventEffect(
    EventEffectKind Kind,
    string? TargetId = null,
    ModifierType ModifierType = ModifierType.Percentage,
    double Value = 0,
    int Duration = 1,
    string? RoleId = null,
    string? Text = null);

/// <summary>
/// One choice an event offers the player (the <c>&lt;option&gt;</c> child of an <c>&lt;event-def&gt;</c>): an id, an
/// AI/auto-resolve <see cref="Weight"/>, and the <see cref="Effects"/> that fire when it is chosen. An event with a
/// single option is a "forced outcome" (no real choice); several options are a genuine dilemma the human picks between
/// and the AI/auto-resolver decides by weight.
/// </summary>
/// <param name="Id">The option id, unique within its event (e.g. <c>accept</c> / <c>refuse</c>). Referenced by <c>Game.ChooseEventOption</c>.</param>
/// <param name="Weight">The deterministic auto/AI pick weight (the heaviest option is chosen when unanswered; ties break by ordinal id). ≥ 0.</param>
/// <param name="Effects">The effects this option applies, in spec order.</param>
public sealed record EventOption(
    string Id,
    int Weight,
    IReadOnlyList<EventEffect> Effects);

/// <summary>
/// How an <see cref="EventDef"/> becomes eligible to fire (the <c>trigger</c> attribute).
/// </summary>
public enum EventTrigger
{
    /// <summary>The default: the event is eligible for the ordinary per-turn eligibility → seeded-draw pipeline, gated by
    /// its <see cref="EventDef.Requirements"/>, <see cref="EventDef.Weight"/>, cooldown and expiry.</summary>
    Normal,

    /// <summary>A forced <b>scenario-setup</b> event (4c.5): it fires <b>unconditionally and once</b> at scenario start (the
    /// first turn), bypassing the weighted draw and the requirement gates — the mechanism a variant uses to seed opening
    /// state (e.g. a first-settlement founding narrative). Always implicitly one-shot.</summary>
    ScenarioStart,
}

/// <summary>
/// A data-driven <b>historical event</b> (4c.2, the Australian Federation event engine): a game occurrence offered to a
/// player when its <see cref="Requirements"/> hold and its weighted draw comes up, letting the player pick one of its
/// <see cref="Options"/>, whose <see cref="EventOption.Effects"/> then resolve. Parsed from the spec's
/// <c>&lt;historical-events&gt;</c> section (each an <c>&lt;event-def&gt;</c>) — deliberately a <b>distinct</b> element
/// from the existing <c>&lt;events&gt;</c>/<see cref="SpecEvent"/> (the limit-gated declare-independence/Spanish-succession
/// occurrences), which it does not touch.
/// <para>
/// <b>Classic ships none of these.</b> The classic spec has no <c>&lt;historical-events&gt;</c> section, so the ruleset's
/// event-def list is empty and the runtime is a strict no-op (zero RNG draws, zero new save tokens) — the default game
/// stays byte-identical (ADR-006/009). A variant adds events by data alone.
/// </para>
/// </summary>
/// <param name="Id">The event id (e.g. <c>event.woolBoom</c>), unique in the ruleset. Used as the cooldown/one-shot key.</param>
/// <param name="Weight">The relative weight in the per-turn seeded draw among all currently-eligible events (higher = likelier). ≥ 1 for a normal event.</param>
/// <param name="Cooldown">Turns that must elapse after this event fires before it is eligible again (0 = no cooldown). Ignored for a one-shot event.</param>
/// <param name="OneShot">Whether the event fires at most once per game (after firing it is permanently ineligible). A <see cref="EventTrigger.ScenarioStart"/> event is always one-shot.</param>
/// <param name="EarliestYear">The earliest in-game year the event may fire (0 = no year floor). A convenience gate folded into eligibility alongside <see cref="Requirements"/>.</param>
/// <param name="ExpiryYear">The last in-game year the event may fire (0 = never expires). After this year the event is permanently ineligible.</param>
/// <param name="Trigger">How the event becomes eligible (<see cref="EventTrigger.Normal"/> pipeline vs. a forced <see cref="EventTrigger.ScenarioStart"/> event).</param>
/// <param name="Requirements">The <see cref="Limit"/> gates (reusing the existing limit/operand engine) that must all hold for a normal event to be eligible; empty = no extra gate. Ignored for a scenario-start event.</param>
/// <param name="Options">The choices offered; at least one. A single option is a forced outcome; several are a dilemma.</param>
public sealed record EventDef(
    string Id,
    int Weight,
    int Cooldown,
    bool OneShot,
    int EarliestYear,
    int ExpiryYear,
    EventTrigger Trigger,
    IReadOnlyList<Limit> Requirements,
    IReadOnlyList<EventOption> Options)
{
    /// <summary>Whether this is a forced scenario-setup event (fires once, unconditionally, at scenario start) — always one-shot.</summary>
    public bool IsScenarioStart => Trigger == EventTrigger.ScenarioStart;

    /// <summary>The option with the given id, or <c>null</c> if this event has no such option.</summary>
    /// <param name="optionId">The option id.</param>
    public EventOption? Option(string optionId) => Options.FirstOrDefault(o => o.Id == optionId);
}
