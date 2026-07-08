# System: Historical events (data-driven event engine)

| | |
|---|---|
| **Status** | Implemented (engine + effect vocabulary + forced-setup mechanism; the classic ruleset defines no events, so it is dormant there. Real Australian Federation event *content* is authored by a separate stream against this schema) |
| **Last verified** | 2026-07-08 @ Australia catalogue batches 2 & 3 (`86d3mmbg9`); event engine 4c.2–4c.5 (`86d3mmajb`/`86d3mmang`/`86d3mmb16`/`86d3mmb3r`) |
| **Code** | `game/src/GameLogic/Specification/EventDef.cs` (schema), `Ruleset.cs` (`ParseHistoricalEvents`), `game/src/GameLogic/GameSession/Game.Events.cs` (runtime + effect vocabulary) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/HistoricalEventTests.cs` |
| **FreeCol reference** | Concept only (FreeCol has no equivalent scripted-event system); reuses our own limit engine ([events-limits](events-limits.md)) and disaster machinery ([modifiers](modifiers.md)) as templates |
| **Related systems** | [events-limits](events-limits.md) (the `<limit>` engine the requirements reuse), [modifiers](modifiers.md) (timed modifiers), [founding-fathers](founding-fathers.md) (the grant appliers), [colonies](colonies.md) (colony-scoped effects) |

## 1. How it works (plain English)

Some scenarios want **story moments** that fire as the game unfolds — a wool boom, a gold strike, the founding of a settlement — each with a choice for the player and consequences either way. This engine lets a scenario author add those moments as **data in the rules file**: no code change, just a new `<event-def>` entry.

An event is: a set of **requirements** that must be true (a year, a colony count, a rebel percentage — the same comparisons the [limits engine](events-limits.md) already understands), a **weight** (how likely it is to come up when several events are eligible), and one or more **options** the player can pick between, each with **effects** (gain gold, get a free colonist, a temporary production boost, a note in your history log).

Each turn the game gathers every event whose requirements currently hold, picks one at random (weighted), and either **offers it to you** (if it is a real dilemma with more than one option) or just **resolves it** (a single-outcome event, or an AI player's event). If you are offered a choice and end your turn without answering, the game picks the sensible default for you (the heaviest-weighted option). An event can be **one-shot** (fires at most once ever) or have a **cooldown** (a minimum number of turns before it can fire again), and can carry an **earliest** and **expiry** year.

There is also a **forced setup event**: an event marked `trigger="scenarioStart"` fires **once, unconditionally, at the very start of the game** — the mechanism a scenario uses to seed opening state (for example, establishing the first settlement). It never asks and never repeats.

**The classic game ships no events at all**, so none of this ever runs in a standard game — it stays exactly as it was, down to the last byte. The Australian Federation variant adds real events later; this is the engine they plug into.

**Worked example:**
> A scenario defines a `event.woolBoom`: requirement "year ≥ 1830", weight 5, two options — *"Invest in more flocks"* (+500 gold, +50% wool for 5 turns) or *"Sell now"* (+1000 gold). From 1830 on, whenever this event comes up in the weighted draw, you are asked which to do. Pick "Invest" and your treasury rises and your wool output jumps for five turns, then returns to normal.

**What the player sees and does:** an event prompt with its options (presentation layer, later); picking an option applies its effects. Nothing appears in a classic game.

## 2. Detailed rules

Per-turn, per colonial player, in `RunPlayerTurn` (right after the natural-disaster roll):

1. **Guard:** if the ruleset defines no historical events, return immediately — **before any RNG**. (Classic → always this path.)
2. **Forced setup (turn ≤ 1):** every `scenarioStart` event that has not fired yet fires now, unconditionally, auto-resolved (no draw, no prompt). Each is one-shot.
3. **Eligibility:** gather every `normal` event that is eligible for this player: not a spent one-shot, off cooldown, within `[earliest-year, expiry-year]`, all `<requires>` limits true, and (for the human) no event offer already pending.
4. **Draw:** if any are eligible, seed a throwaway generator from the player's RNG state (read without advancing it) XOR turn/player on the reserved **event stream 105**, and pick one by weight. This is the **only** RNG the engine consumes.
5. **Offer vs. resolve:** a multi-option event for the **human** becomes a pending offer (recorded as fired so it is not re-drawn); a single-option event or an **AI** event resolves immediately.
6. **Resolve:** the chosen option's effects apply in spec order. The human's pending offer is answered by `ChooseEventOption(id)`, or auto-resolved to the heaviest-weight option at the next `EndTurn` if ignored.

| Input / condition | Result |
|---|---|
| Ruleset has no `<historical-events>` | Engine never runs; zero RNG, zero save tokens (byte-identical) |
| `scenarioStart` event, turn 1, not yet fired | Fires unconditionally, once, auto-resolved |
| Normal event, requirements unmet | Not eligible; not drawn |
| Normal event, one-shot, already fired | Never eligible again |
| Normal event, within cooldown of last firing | Not eligible until `turn − lastFired ≥ cooldown` |
| Current year > `expiry-year` (> 0) | Never eligible again |
| Current year < `earliest-year` (> 0) | Not yet eligible |
| Multi-option event drawn for the human | Offered (`PendingEventOffer`); nothing applied until chosen |
| Multi-option event drawn for an AI | Auto-resolved to the heaviest-weight option (RNG-free) |
| Human ignores an offer through `EndTurn` | Auto-resolved to the heaviest-weight option |
| `ChooseEventOption` with an unoffered id | `InvalidMoveException` |
| Unknown `<effect kind>` in the spec | Dropped at parse time (forward-compatible) |

**Effect vocabulary** (an `<option>`'s `<effect>` children):

| `kind` | Attributes read | Action |
|---|---|---|
| `timedModifier` | `target`, `type`, `value`, `duration` | Register a `TemporaryModifier` (colony-scoped if the event has a colony) at index 110, auto-expiring after `duration` turns |
| `grantGold` | `value` | `player.Gold += value` (floored at 0) |
| `grantLiberty` | `value` | `player.Liberty += value` (floored at 0) |
| `grantUnit` | `target` (unit-type id), `role` | Spawn one free unit on the player's Europe dock (`SpawnInEurope`) |
| `grantGoods` | `target` (goods id), `value` | `colony.AddGoods` on the event's colony (no-op if no colony) |
| `revealMap` | `value` (radius) | `RevealAround` the event's colony (no-op if no colony) |
| `recordHistory` | `text` | Append a `HistoricalEvent` line to the human's history log |

**Deviations from original 1994 / FreeCol behavior:** *This is a new-content engine with no 1994/FreeCol equivalent — it is additive scenario tooling. It reuses our existing limit engine and modifier/grant appliers so it introduces no new game maths. The classic ruleset carries no events, so classic behaviour is unchanged (byte-identical, guarded by the soak + `HistoricalEventTests.ByteStabilityTwin_EventsOff`).*

## 3. Technical design

**Domain model** (`Specification/EventDef.cs`):
- `EventDef` — one event: id, weight, cooldown, one-shot flag, earliest/expiry year, `EventTrigger` (`Normal`/`ScenarioStart`), `Requirements` (`IReadOnlyList<Limit>`), `Options`.
- `EventOption` — a choice: id, auto/AI weight, `Effects`.
- `EventEffect` — one effect: `EventEffectKind` + the fields that kind reads.
- `EventEffectKind`, `EventTrigger` — the extensible enums.

**Data sources:** the spec `<historical-events>` section (each an `<event-def>`), parsed by `Ruleset.ParseHistoricalEvents`. Deliberately a **distinct** element from the pre-existing `<events>`/`SpecEvent` (`ParseEvents`) — no element-name collision. Requirements reuse `ParseLimit`/`ParseOperand`; effects are parsed by `ParseEventEffect`. Surfaced as `Ruleset.HistoricalEvents` / `Ruleset.HistoricalEvent(id)` (empty/null for classic).

**Algorithms & integration** (`GameSession/Game.Events.cs`):
- `RollHistoricalEvents(Player)` — the per-turn pipeline, hooked in `RunPlayerTurn` next to `RollNaturalDisasters`. Guard-returns before any RNG when there are no events. Draw is on reserved stream `EventStreamId = 105`, seeded from `RandomFor(player).SaveState().State` (read-only, stream 0 never advances) — mirrors `RollNaturalDisasters`.
- `IsEventEligible` — the pure eligibility read (one-shot/cooldown/year/`Requirements` via `EvaluateLimit`).
- `ChooseEventOption(string)` — the human command (validates the offered set, throws `InvalidMoveException`).
- `AutoResolvePendingEventOffer` — the `EndTurn` timeout, hooked next to `AcceptPendingFirstContactOnTimeout`.
- `AutoSelectOption` — the deterministic RNG-free auto/AI resolver (heaviest weight, ties by ordinal id) — mirrors `SelectFoundingFatherFor`.
- `ApplyEventEffect` — the effect switch, routing each kind to an existing applier.

**Integration points:** turn loop (`RunPlayerTurn`, `EndTurn`); the limit engine (`Game.Limits.cs`); the temporary-modifier registry (`RegisterTemporaryModifier` / `RemoveExpiredTemporaryModifiers`); `SpawnInEurope`, `Colony.AddGoods`, `RevealAround`, `RecordHistory`.

**Persistence (save v71):** the only new save state is `EventLastFiredTurn` (event id → the turn it last fired), needed so one-shot and cooldown gating survive a save/reload. It is **omit-when-empty** — the classic game fires no events, so the map is empty and the token is absent, keeping a default save byte-identical to v70. `PendingEventOffer` is **transient** (re-offered/auto-resolved; never saved). The v70→71 bump touched the ~40 version-pin test sentinels.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `HistoricalEventTests` — schema+parser round-trip, unknown-effect drop, duplicate-id throw | ✅ |
| L2 Scenario | Always | `HistoricalEventTests` — gated draw, one-shot, cooldown, expiry, offer→choose, auto-resolve, each effect kind, scenario-start, save round-trip; `ByteStabilityTwin_EventsOff` + `ClassicDefault_FiresNoEvents_NoDrawsNoTokens` | ✅ |
| L3 Interaction | If the system has UI | (event prompt UI is a later presentation slice) | ⬜ |
| L4 Visual | If the system has a screen | (later) | ⬜ |
| L5 Soak | Covered by global suite | `SoakTests` (25 seeds × 200 turns, byte-identical) + `NaturalDisasterTests.ByteStabilityTwin` — both green with the engine present-but-dormant | ✅ |

- **FreeCol cross-check:** not applicable (no FreeCol equivalent). Cross-checked instead against our own disaster/limit/father machinery it mirrors.

## 5. Open issues / TODO

- [ ] Event-prompt presentation (L3/L4) — the human-facing UI for `PendingEventOffer`.
- [x] **Batch 1 authored** (86d3mmbfn) — 10 non-sensitive 1788–1830 events (supply, drought, bushfire, flood, harvest, merino wool, whaling, free settlers, escaped convicts) + the **Sydney Cove** forced setup event (86d3mmb3r), all in `game/data/rules/australia/specification.xml` `<historical-events>`. Every effect is player-scoped (safe before the first colony). First Nations *first-contact/frontier* content is still deliberately EXCLUDED — it goes through the sombre-framing review (4c.11, `86d3mmc1x`) before authoring.
- [x] **Batches 2 & 3 authored** (86d3mmbg9) — 9 events for 1830–1872 (transportation ends, squatting runs, wool boom, inland exploration, the 1851 gold rush, payable field, gold-immigration surge, Eureka Stockade dilemma, gold-escort robbery, first railway/telegraph) and 12 for 1872–1901 (Overland Telegraph, Broken Hill, refrigerated meat, intercolonial railway, Marvellous Melbourne, 1893 bank crash, Shearers' Strike dilemma, SA women's suffrage, Federation convention, Federation referendum, Federation drought). Player-scoped effects throughout, except two settlement-gated colony effects (inland-exploration `revealMap`, payable-field `grantGoods`) that carry a `settlements>=1` `<requires>` limit. Note: the spec has no `model.goods.meat` (commented out; folded into food), so refrigerated-meat export is modelled as gold + a `food` production boost. Six-era frequency bands (4c.10) still remain.
- [ ] Event-prompt presentation (L3/L4) — the human-facing UI for `PendingEventOffer` (single-option events auto-apply; multi-option dilemmas currently auto-resolve to the heaviest option until the UI lands).
- [ ] Consider colony *selection* for colony-scoped effects when a player has several colonies (currently the first).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-08 | **Australian catalogue — batches 2 & 3 authored** (`86d3mmbg9`): 9 `<event-def>`s for 1830–1872 (transportation-ends, squatting, wool boom, inland exploration, 1851 gold rush, payable field, gold-immigration surge, Eureka Stockade dilemma, gold-escort robbery, first railway/telegraph) + 12 for 1872–1901 (Overland Telegraph, Broken Hill, refrigerated meat, intercolonial railway, Marvellous Melbourne, 1893 bank crash, Shearers' Strike dilemma, SA women's suffrage, Federation convention, Federation referendum, Federation drought) appended to the Australia spec `<historical-events>`. Player-scoped effects, bar two `settlements>=1`-gated colony effects (inland-exploration `revealMap`, payable-field `grantGoods`). First Nations content still excluded (4c.11). Classic unchanged → soak byte-identical. +1 L1/L2 guard (`AustraliaVariantTests.AustraliaCatalog_CarriesBatchesTwoAndThree_...`), incl. a per-effect goods/unit-id validity sweep. | (this commit) |
| 2026-07-08 | **Australian catalogue — batch 1 + setup event** (`86d3mmbfn`/`86d3mmb3r`): 10 non-sensitive 1788–1830 `<event-def>`s (supply/drought/bushfire/flood/harvest/merino-wool/whaling/free-settlers/escaped-convicts) + the forced `event.sydneyCoveEstablished` (scenario-start, one-shot) in the Australia spec. Player-scoped effects only (colony-independent). Classic still defines none → byte-identical. +1 L1 (`AustraliaVariantTests.AustraliaCatalog_...`). First Nations contact content deliberately excluded (4c.11 review). | (this commit) |
| 2026-07-08 | Initial documentation — event schema/parser (4c.2), runtime (4c.3), effect vocabulary (4c.4), forced-setup mechanism (4c.5); save v70→71 for `EventLastFiredTurn` | 4248dc7 |
