# System: Events & limits (generic limit-evaluation engine)

| | |
|---|---|
| **Status** | Implemented (subset: the operand/operator kinds the classic `<event>`/`<limit>` elements use; Spanish-succession year gate routed through the engine as proof) |
| **Last verified** | 2026-06-21 @ generic event/limit evaluation engine (`86d3drpha`) |
| **Code** | `game/src/GameLogic/Specification/Limit.cs` (model + parse helpers in `Ruleset.cs`), `game/src/GameLogic/GameSession/Game.Limits.cs` (engine) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/LimitEngineTests.cs` |
| **FreeCol reference** | `freecol/src/.../common/model/Limit.java`, `Operand.java`, `Event.java`; `Specification.getEvents()`/`getEvent()`/`getLimits()`; `freecol/.../server/model/ServerGame.java` (`spanishSuccessionReady`); `<events>`/`<limit>` in `data/rules/classic/specification.xml` |
| **Related systems** | [independence](independence.md) (the events that use it), [ruleset-data](ruleset-data.md) (spec parsing), [scoring](scoring.md) (event score values) |

## 1. How it works (plain English)

Some big moments in the game only happen when the world is in a particular state — you can only **declare independence** once enough colonists are rebels and it isn't too late in history; the **War of the Spanish Succession** (where a fading European rival is swallowed by a dominant one) only happens from the year 1600 on. In the original game these conditions are baked into the rules file as little comparisons called **limits**, each attached to an **event**.

A limit is just a comparison: "the player's rebel percentage is **at least** 50", or "the year is **at least** 1600". An event (like "declare independence") can have several limits, and it can only fire when **all** of them are true.

This system reads those comparisons straight from the rules file and checks them against the live game, instead of the conditions being hard-coded in our C#. The pay-off: a future variant (the planned Australia scenario) can move the Spanish-succession trigger year, or change the independence rebel threshold, **just by editing the data file** — no code change.

**Worked example:**
> The rules file says the Spanish succession's year limit is `year ≥ 1600`. Each turn the engine reads that limit, looks up the current year, and answers true or false. In 1599 it answers false (nothing happens); the first turn the calendar reaches 1600 it answers true, and the succession can proceed. The number 1600 lives in the data file, not in the code.

**What the player sees and does:** nothing new directly — this is the plumbing under existing moments (independence, the Spanish succession). The player still just plays; the engine decides when those moments are allowed to fire.

## 2. Detailed rules

A `<limit>` is `leftHandSide  operator  rightHandSide`. Each side is an **operand** that resolves to a whole number; the operator compares them.

**Operators** (FreeCol `Limit.Operator`): `eq`, `lt`, `gt`, `le`, `ge`.

**Operand kinds we evaluate** (the faithful subset the classic ruleset actually uses — FreeCol `Operand.OperandType` × `ScopeLevel`):

| Operand | Scope | Resolves to | Used by (classic) |
|---|---|---|---|
| literal `value` | — | the constant itself | every right-hand side that is a number (50, 1, 1600) |
| `year` | game | the current in-game year | independence year gate, succession year gate |
| `option` (+ `type`) | game | a named integer game option | independence year gate (`model.option.lastColonialYear` = 1800) |
| `method-name="getSoL"` | player | the player's national Sons-of-Liberty % | independence rebel gate, succession weak/strong gates |
| `settlements` | player | count of the player's colonies, optionally filtered by `method-name="isConnectedPort" method-value="true"` (connected ports only) | independence coastal-colony gate |
| `units` | player | count of the player's (non-native) units | the wagon-train build cap (parsed; see note) |
| `foundingFathers` | player | count of elected founding fathers | none in classic (kept for fidelity) |

**Event-fires rule:** an event fires when **all** of its limits hold (FreeCol AND-s an event's limits, e.g. `ServerGame.spanishSuccessionReady`). `Game.CheckSpecEvent(eventId, player)` returns that AND. An unknown event id, or an event with no limits, returns **false** (so any hard-coded fallback path stays in charge).

**Null-operand rule (faithful to FreeCol):** if either side resolves to `null` — i.e. an operand kind outside the supported subset (e.g. a `settlement`-scoped operand, which no classic event uses) — the limit evaluates **true**. A null operand never constrains (`Limit.evaluate`: `lhs==null || rhs==null → true`). This makes the subset safe: an unrecognised operand widens, never wrongly narrows.

**The classic `<events>` section** (`specification.xml`):

| Event | Score | Limits (all must hold) |
|---|---|---|
| `model.event.declareIndependence` | 100 | `independence.rebels` (`getSoL ≥ 50`), `independence.coastalColonies` (connected-port `settlements ≥ 1`), `independence.year` (`year ≤ lastColonialYear`) |
| `model.event.spanishSuccession` | 0 | `spanishSuccession.year` (`year ≥ 1600`), `spanishSuccession.weakestPlayer` (`getSoL < 50`), `spanishSuccession.strongestPlayer` (`getSoL > 50`) |

**Deviations from original 1994 / FreeCol behavior:**
- **Faithful subset, not the whole `Operand` machine.** We resolve the operand kinds the classic events/limits use (above). FreeCol's fuller machinery — arbitrary `invokeMethod` reads on any object, `BUILDINGS` counts, settlement-scoped operands — is not evaluated; such an operand resolves to `null` (no constraint). Documented and covered by a test.
- **One limit routed through the engine as proof, not all of them.** The independence gates (`CheckDeclareIndependence`) and the succession's weak/strong-player gates remain in their existing hand-written form for now; only the **Spanish-succession year gate** is read from the spec limit via the engine (`SpanishSuccessionYearReached`). The classic limit (`year ≥ 1600`) equals the constant it replaced, so the default game's trigger is byte-identical. The remaining gates are candidates to migrate later (open issue).
- The standalone **wagon-train build limit** (`<limit>` inside `model.unit.wagonTrain`) keeps its own narrow evaluator (`UnitBuildLimitOk`, `units < settlements` at player scope) — not yet folded onto the shared `Limit` model. Same result; a later cleanup can unify them.

## 3. Technical design

**Domain model** (`Specification/Limit.cs`, all immutable records/enums):
- `LimitOperator` (`Eq`/`Lt`/`Gt`/`Le`/`Ge`), `OperandType` (`None`/`Units`/`Settlements`/`FoundingFathers`/`Year`/`Option`), `LimitScopeLevel` (`None`/`Settlement`/`Player`/`Game`) — mirror FreeCol's enums.
- `Operand(OperandType, ScopeLevel, Value, MethodName, MethodValue, Type)` — one side of a comparison.
- `Limit(Id, LeftHandSide, Operator, RightHandSide)` — the comparison.
- `SpecEvent(Id, ScoreValue, Limits)` with `Limit(id)` lookup — a parsed `<event>`.

**Data sources / parsing** (`Specification/Ruleset.cs`):
- `ParseEvents(<events>)` → `Dictionary<string, SpecEvent>`; exposed as `Ruleset.Events` (list) and `Ruleset.Event(id)`. A missing `<events>` section yields an empty map (a spec without events still loads; the hard-coded fallbacks take over).
- `ParseLimit(<limit>)` (internal — reusable for unit-build limits later) → `ParseOperand` reads `operand-type`/`scope-level`/`value`/`method-name`/`method-value`/`type`. Unknown operand type → `None`; unknown operator → `RulesetFormatException`. Duplicate event id → `RulesetFormatException`.

**Algorithms** (`GameSession/Game.Limits.cs`, all pure — no RNG, no mutation):
- `EvaluateLimit(Limit, Player)` — resolves both operands, applies the operator; a null operand → `true` (FreeCol parity).
- `ResolveOperand` → `ResolveGameOperand` (year, option) / `ResolvePlayerOperand` (`getSoL` via `NationalSonsOfLiberty`, `units`/`settlements`/`foundingFathers` counts; the connected-port predicate via `CountSettlements` + `IsColonyCoastal`). Option ids map to parsed `Ruleset` values (`ResolveIntOption`: currently `model.option.lastColonialYear → Ruleset.LastColonialYear`).
- `CheckSpecEvent(eventId, player)` — `Ruleset.Event(id)`; AND of all its limits; unknown/limitless → `false`.

**Integration points:** the registry is checked in the **EndTurn world-advance band**. `Game.RunSpanishSuccession` (in `Game.Independence.cs`) now calls `SpanishSuccessionYearReached()`, which evaluates the spec limit `model.limit.spanishSuccession.year` through `EvaluateLimit` (falling back to the `SpanishSuccessionYear = 1600` constant only when the spec carries no such limit). Everything else in `RunSpanishSuccession` (the weak/strong SoL pairing, the asset transfer, the `_spanishSuccessionDone` once-flag) is unchanged.

**Persistence:** none. Events/limits are ruleset-derived (re-read on load); the engine adds no game state, so there is **no save-version bump** (ADR-006). The succession's existing `_spanishSuccessionDone` flag persists exactly as before.

**Byte-identity (ADR-006/009):** the only behavioural-path change is the succession's year gate, and the classic spec limit (`year ≥ 1600`) is identical to the constant it replaced. No new RNG draws anywhere. The default classic game fires nothing new and consolidates on exactly the same turn it always did.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `LimitEngineTests`: classic parses both events + their limits; year/option/getSoL/settlements operand resolution; the five operators (literal `[Theory]`); null-operand → true; `CheckSpecEvent` AND gate (all-hold fires / any-fail blocks / unknown→false); the connected-port settlement filter | ✅ |
| L2 Scenario | Always | `LimitEngineTests.SpanishSuccessionYearGate_…CrossingExactlyAt1600` (the flag never trips pre-1600 driving a full game forward) + the existing `IndependenceTests.SpanishSuccession_…OnTheTurnAt1600` E2E, which now flows through the engine unchanged | ✅ |
| L3 Interaction | No UI | — | — |
| L4 Visual | No screen | — | — |
| L5 Soak | Covered by global suite | default game unaffected (no new event fires) | — |

- **FreeCol cross-check:** the model mirrors `Limit`/`Operand`/`Event`; the engine mirrors `Limit.evaluate(Game/Player)` (including the null-operand → true rule) and the per-event AND gate (`spanishSuccessionReady`). The classic `<events>` values are parsed verbatim from FreeCol's `specification.xml`, so they match by construction.

## 5. Open issues / TODO

- [ ] Migrate the remaining hand-written gates onto the engine: `CheckDeclareIndependence`'s rebels/coastal/year limits, and the succession's weak/strong-player SoL limits (currently still in C#). Behaviour is already equal; this is a consolidation.
- [ ] Fold the standalone wagon-train `UnitBuildLimit` onto the shared `Limit` model (`ParseLimit` is already reusable) so there is one limit evaluator.
- [ ] Broaden the operand subset only if a variant needs it (e.g. `buildings` counts, settlement scope) — add with tests when a real `<event>` requires it.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-21 | **Initial: generic event/limit evaluation engine** (`86d3drpha`, FreeCol `Limit`/`Operand`/`Event`). New `Specification/Limit.cs` model (`LimitOperator`/`OperandType`/`LimitScopeLevel`/`Operand`/`Limit`/`SpecEvent`); `Ruleset.ParseEvents`/`ParseLimit` parse the classic `<events>` section (both events + their limits), exposed via `Ruleset.Events`/`Event(id)`. New `Game.Limits.cs` engine: `EvaluateLimit` (operator + operand resolution: year, `lastColonialYear` option, `getSoL`, unit/settlement/father counts, the `isConnectedPort` settlement filter; null-operand → true) and `CheckSpecEvent` (AND of an event's limits). **Wired as proof:** `RunSpanishSuccession`'s year gate now evaluates the spec limit `model.limit.spanishSuccession.year` (`year ≥ 1600`) via the engine (`SpanishSuccessionYearReached`), replacing the hardcoded `CurrentYear < 1600` — byte-identical (classic limit = the old constant). Faithful subset (documented); the independence/weak/strong gates + the wagon-train build limit not yet migrated. No new state → **no save bump**; default game fires nothing new (ADR-006/009). +23 L1/L2 (`LimitEngineTests`); existing Independence/Spanish E2E green | `86d3drpha` |
