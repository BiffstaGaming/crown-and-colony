# System: Historical events (data-driven event engine)

| | |
|---|---|
| **Status** | Implemented (engine + effect vocabulary + forced-setup mechanism; the classic ruleset defines no events, so it is dormant there. Real Australian Federation event *content* is authored by a separate stream against this schema) |
| **Last verified** | 2026-07-09 @ era-frequency bands (4c.10, `86d3mmbzc`); Australia catalogue batches 2 & 3 (`86d3mmbg9`); event engine 4c.2–4c.5 (`86d3mmajb`/`86d3mmang`/`86d3mmb16`/`86d3mmb3r`) |
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
4. **Era-frequency band (4c.10):** if any are eligible, seed the throwaway generator (from the player's RNG state read without advancing it, XOR turn/player, on the reserved **event stream 105**), then apply the **per-turn era fire chance** — a single `rng.Next(100) >= chance → skip this turn` gate, modelled on the natural-disaster percentage gate. The chance is a pure read off the calendar year (see the "Era-frequency bands" table below). **Outside the 1788–1901 window the chance is 100**, so the gate is *skipped entirely* — no roll is taken and the draw is unperturbed, exactly as before 4c.10 (this is why the classic 1492 calendar is unaffected).
5. **Draw:** if the era gate passed, pick one eligible event by weight on the same stream-105 generator. Together the (optional) era roll and this draw are the **only** RNG the engine consumes.
6. **Offer vs. resolve:** a multi-option event for the **human** becomes a pending offer (recorded as fired so it is not re-drawn); a single-option event or an **AI** event resolves immediately.
7. **Resolve:** the chosen option's effects apply in spec order. The human's pending offer is answered by `ChooseEventOption(id)`, or auto-resolved to the heaviest-weight option at the next `EndTurn` if ignored.

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

**Era-frequency bands (4c.10):** once an event is eligible, whether one actually fires *this turn* is gated by a per-turn probability chosen by the game's **era** — derived purely from the calendar year (the six Australian eras of doc 13's "Recommended event frequency" table). Early *Survival* years are sparse; the *Gold Rush* and *Federation* eras are busy. This shapes cadence **without** touching which events are eligible or how they are weighted against each other — it only decides how often the turn produces *any* event.

| Era | Years (lower-bound inclusive) | Doc 13 label | Per-turn fire chance |
|---|---|---|---|
| *(Pre — outside the window)* | before 1788 (e.g. the whole classic 1492 calendar) | — | **100% (gate skipped — inert)** |
| Survival | 1788–1796 | Frequent survival | 20% |
| Expansion | 1797–1829 | Moderate | 35% |
| Colony formation | 1830–1850 | Moderate | 35% |
| Gold rush | 1851–1871 | Frequent | 60% |
| Infrastructure | 1872–1888 | Moderate | 35% |
| Federation | 1889–1901+ | Frequent | 60% |

Notes: the boundaries and chances are **documented code constants** in `Game.Events.cs` (`EraForYear` / `EraFireChance`), not yet a data-driven spec section (that spec section is deferred — see §5). A `100`-chance era (Pre) skips the RNG roll entirely, so the throwaway event-stream generator advances exactly as it did pre-4c.10 and the draw is identical there — the reason a classic game is untouched. The chances render doc 13's *Frequent → high / Moderate → lower* intent; the exact percentages are a tuning judgement (see §5). Note doc 13 labels the earliest 1788–97 band "Frequent survival events" (there is a lot of *authored* survival content), but the founding years are deliberately given the **sparsest** per-turn chance so the opening is not swamped with popups — the "frequency" is in the density of eligible content, the band throttles how many actually surface per turn.

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
- `EventDef` — one event: id, weight, cooldown, one-shot flag, earliest/expiry year, `EventTrigger` (`Normal`/`ScenarioStart`), `Requirements` (`IReadOnlyList<Limit>`), `Options`, and the **presentation-text** fields `Name`/`Prompt` (nullable — the popup title + dilemma context) with `DisplayName` falling back to the humanized id.
- `EventOption` — a choice: id, auto/AI weight, `Effects`, and the presentation-text `Label` (nullable) with `DisplayLabel` falling back to the humanized id. The text fields are **presentation-only — never read by the resolver.**
- `EventEffect` — one effect: `EventEffectKind` + the fields that kind reads.
- `EventEffectKind`, `EventTrigger` — the extensible enums.

**Data sources:** the spec `<historical-events>` section (each an `<event-def>`), parsed by `Ruleset.ParseHistoricalEvents`. Deliberately a **distinct** element from the pre-existing `<events>`/`SpecEvent` (`ParseEvents`) — no element-name collision. Requirements reuse `ParseLimit`/`ParseOperand`; effects are parsed by `ParseEventEffect`. Surfaced as `Ruleset.HistoricalEvents` / `Ruleset.HistoricalEvent(id)` (empty/null for classic).

**Algorithms & integration** (`GameSession/Game.Events.cs`):
- `RollHistoricalEvents(Player)` — the per-turn pipeline, hooked in `RunPlayerTurn` next to `RollNaturalDisasters`. Guard-returns before any RNG when there are no events. Draw is on reserved stream `EventStreamId = 105`, seeded from `RandomFor(player).SaveState().State` (read-only, stream 0 never advances) — mirrors `RollNaturalDisasters`.
- **Era-frequency band (4c.10):** `EventEra` (the six eras + a `Pre` sentinel), `EraForYear(int)` (pure year→era, lower-bound inclusive, mirrors `Ruleset.AgeForYear`), `CurrentEventEra` (`EraForYear(CurrentYear)`), and `EraFireChance(EventEra)` (per-era 0–100 chance; `Pre` → 100). Applied in `RollHistoricalEvents` between the throwaway-generator seed and the weighted draw: `if (chance < 100 && rng.Next(100) >= chance) return;`. The `< 100` guard means a full-chance era takes **no** roll, so outside the 1788–1901 window the generator state and draw are identical to pre-4c.10 (byte-stability of the classic path is unaffected — the `count == 0` guard already short-circuits classic before any of this). Era boundaries (`EraSurvivalStart` … `EraFederationStart`) and per-era chances (`EraChanceSurvival` … `EraChanceFederation`) are `private const` tuning constants.
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
| L2 Scenario | Always | `HistoricalEventTests` — gated draw, one-shot, cooldown, expiry, offer→choose, auto-resolve, each effect kind, scenario-start, save round-trip; `ByteStabilityTwin_EventsOff` + `ClassicDefault_FiresNoEvents_NoDrawsNoTokens`; **era bands (4c.10)** — `EraForYear_MapsBoundaryYearsToTheCorrectEra` (13 boundary/interior years), `EraFireChance_IsInertOutsideTheWindow_AndModulatedInside`, `CurrentEventEra_ReadsOffTheCalendarYear`, `EraBand_IsInertOnTheClassicCalendar_EventStillFiresEveryTurn`, `EraBand_FiresSparselyInSurvival_AndBusilyInGoldRush`, `EraBand_IsDeterministic_SameSeedSameCalendarSameCadence` | ✅ |
| L3 Interaction | If the system has UI | (event prompt UI is a later presentation slice) | ⬜ |
| L4 Visual | If the system has a screen | (later) | ⬜ |
| L5 Soak | Covered by global suite | `SoakTests` (25 seeds × 200 turns, byte-identical) + `NaturalDisasterTests.ByteStabilityTwin` — both green with the engine present-but-dormant | ✅ |

- **FreeCol cross-check:** not applicable (no FreeCol equivalent). Cross-checked instead against our own disaster/limit/father machinery it mirrors.

## 5. Open issues / TODO

- [ ] Event-prompt presentation (L3/L4) — the human-facing UI for `PendingEventOffer`.
- [x] **Batch 1 authored** (86d3mmbfn) — 10 non-sensitive 1788–1830 events (supply, drought, bushfire, flood, harvest, merino wool, whaling, free settlers, escaped convicts) + the **Sydney Cove** forced setup event (86d3mmb3r), all in `game/data/rules/australia/specification.xml` `<historical-events>`. Every effect is player-scoped (safe before the first colony). First Nations *first-contact/frontier* content is still deliberately EXCLUDED — it goes through the sombre-framing review (4c.11, `86d3mmc1x`) before authoring.
- [x] **Batches 2 & 3 authored** (86d3mmbg9) — 9 events for 1830–1872 (transportation ends, squatting runs, wool boom, inland exploration, the 1851 gold rush, payable field, gold-immigration surge, Eureka Stockade dilemma, gold-escort robbery, first railway/telegraph) and 12 for 1872–1901 (Overland Telegraph, Broken Hill, refrigerated meat, intercolonial railway, Marvellous Melbourne, 1893 bank crash, Shearers' Strike dilemma, SA women's suffrage, Federation convention, Federation referendum, Federation drought). Player-scoped effects throughout, except two settlement-gated colony effects (inland-exploration `revealMap`, payable-field `grantGoods`) that carry a `settlements>=1` `<requires>` limit. Note: the spec has no `model.goods.meat` (commented out; folded into food), so refrigerated-meat export is modelled as gold + a `food` production boost.
- [x] **Six-era frequency bands (4c.10)** — per-turn era fire-chance gate (`EraForYear`/`EraFireChance` in `Game.Events.cs`) modulating event cadence across doc 13's six eras (Survival 20% → Expansion/ColonyFormation/Infrastructure 35% → GoldRush/Federation 60%; inert = 100% outside 1788–1901). Byte-identical classic (soak green). Delivered as **code constants**, not a spec section — see the follow-up below.
- [ ] **Move the era-frequency bands to a data-driven spec section** — the boundaries + per-era chances are currently `private const` in `Game.Events.cs`. A `<frequency-bands>` (or similar) block under the Australia spec would let the variant tune cadence without a code change, matching the "rules/data as XML" architecture. Deferred because the spec file is owned by a separate stream. Recommendation: do this when the spec and event-runtime work rejoin one owner.
- [ ] Event-prompt presentation (L3/L4) — the human-facing UI for `PendingEventOffer` (single-option events auto-apply; multi-option dilemmas currently auto-resolve to the heaviest option until the UI lands).
- [ ] Consider colony *selection* for colony-scoped effects when a player has several colonies (currently the first).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-09 | **Event popup-text layer (WS1.1a)**: added presentation-only text to the event schema so a *polished* event dialog can render — `EventDef.Name`/`Prompt` (popup title + dilemma context, from `name`/`prompt` attributes) and `EventOption.Label` (choice-button text, from `label`). Each exposes a `Display*` property that falls back to the **humanized id** when unauthored (`event.merinoSheep` → "Merino Sheep", `invest` → "Invest"), so partial authoring degrades gracefully. Parsed in `ParseHistoricalEvents`/`ParseEventOption`; **never read by the resolver** (zero logic/RNG impact) — classic ships no events → byte-identical (soak green). Foundation for the event-choice popup (WS1.1b) + the 31-event text authoring. +1 L1 (`HistoricalEventTests`: name/prompt/label parse + humanized fallback). | (this commit) |
| 2026-07-09 | **Era-frequency bands (4c.10)**: added a per-turn era fire-chance gate to `RollHistoricalEvents` — `EventEra` (six eras + `Pre` sentinel), `EraForYear`/`CurrentEventEra` (pure year→era, lower-bound inclusive), `EraFireChance` (Survival 20% / Expansion·ColonyFormation·Infrastructure 35% / GoldRush·Federation 60% / Pre 100%). The gate rolls on the reserved event stream 105 *only* when chance < 100, so any year outside 1788–1901 (the whole classic calendar) takes no roll and the draw is unperturbed — classic stays byte-identical (soak green). Boundaries + chances are documented code constants (a data-driven `<frequency-bands>` spec section is deferred — spec owned by another stream). +6 L1/L2 tests in `HistoricalEventTests`. | (this commit) |
| 2026-07-08 | **Australian catalogue — batches 2 & 3 authored** (`86d3mmbg9`): 9 `<event-def>`s for 1830–1872 (transportation-ends, squatting, wool boom, inland exploration, 1851 gold rush, payable field, gold-immigration surge, Eureka Stockade dilemma, gold-escort robbery, first railway/telegraph) + 12 for 1872–1901 (Overland Telegraph, Broken Hill, refrigerated meat, intercolonial railway, Marvellous Melbourne, 1893 bank crash, Shearers' Strike dilemma, SA women's suffrage, Federation convention, Federation referendum, Federation drought) appended to the Australia spec `<historical-events>`. Player-scoped effects, bar two `settlements>=1`-gated colony effects (inland-exploration `revealMap`, payable-field `grantGoods`). First Nations content still excluded (4c.11). Classic unchanged → soak byte-identical. +1 L1/L2 guard (`AustraliaVariantTests.AustraliaCatalog_CarriesBatchesTwoAndThree_...`), incl. a per-effect goods/unit-id validity sweep. | (this commit) |
| 2026-07-08 | **Australian catalogue — batch 1 + setup event** (`86d3mmbfn`/`86d3mmb3r`): 10 non-sensitive 1788–1830 `<event-def>`s (supply/drought/bushfire/flood/harvest/merino-wool/whaling/free-settlers/escaped-convicts) + the forced `event.sydneyCoveEstablished` (scenario-start, one-shot) in the Australia spec. Player-scoped effects only (colony-independent). Classic still defines none → byte-identical. +1 L1 (`AustraliaVariantTests.AustraliaCatalog_...`). First Nations contact content deliberately excluded (4c.11 review). | (this commit) |
| 2026-07-08 | Initial documentation — event schema/parser (4c.2), runtime (4c.3), effect vocabulary (4c.4), forced-setup mechanism (4c.5); save v70→71 for `EventLastFiredTurn` | 4248dc7 |
