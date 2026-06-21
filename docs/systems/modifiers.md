# System: Modifiers (and temporary, duration-bounded ones)

| | |
|---|---|
| **Status** | Implemented (permanent father/nation/resource modifiers shipped; temporary-modifier TTL system added — `86d3drpgz`) |
| **Last verified** | 2026-06-21 @ temporary-modifier TTL added (`86d3drpgz`): a duration-bounded modifier folds while active and is stripped on the `EndTurn` after its last turn; classic registers none, so the default game is byte-identical. +6 L1 (`TemporaryModifierTests`); targeted modifier/ruleset/father filter 135 green |
| **Code** | `game/src/GameLogic/Specification/FoundingFather.cs` (`ModifierType`, `ModifierMath`, `FatherModifier`), `game/src/GameLogic/GameSession/TemporaryModifier.cs` (the duration-bounded wrapper), `game/src/GameLogic/GameSession/Game.cs` (`_temporaryModifiers` registry, `RegisterTemporaryModifier`, `ActiveTemporaryModifiers`, `RemoveExpiredTemporaryModifiers`, the fold in `ApplyGoodsModifiers`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/TemporaryModifierTests.cs` (window math + apply-then-expire + default-game-empty), `FoundingFatherEffectsTests.cs` (permanent goods modifiers) |
| **FreeCol reference** | `common/model/Modifier.java` (`makeTimedModifier`, `getValue(Turn)`, `applyTo(number, Turn)`), `common/model/Feature.java` (`firstTurn`/`lastTurn`/`duration`/`temporary`, `appliesTo(Turn)`, `isOutOfDate(Turn)`), `common/model/Player.java` line 4289 (writes only `isTemporary() && !isOutOfDate(turn)` modifiers), `server/model/ServerPlayer.java` (`makeTimedModifier` + `cs.addModifier` for disaster effects; goods-party modifier removal) |
| **Related systems** | [founding-fathers](founding-fathers.md) (permanent father modifiers), [colonies](colonies.md) (bell/cross production folds), [turns](turns.md) (the per-turn expiry tick), [save-load](save-load.md) |

## 1. How it works (plain English)

A **modifier** is a bonus or penalty applied to a number in the game — "+50% bell production", "+3 movement for ships", "−100% on the price of native land". Most modifiers are **permanent**: an elected Founding Father or a nation's national advantage grants them and they stay in force for the rest of the game.

A **temporary modifier** is the same idea but with a **clock**. It is active only for a set number of turns and then disappears on its own. Think of a one-off event bonus — "your colonies produce +50% bells for the next 5 turns" — that you want to apply now and have vanish automatically when it runs out, with no bookkeeping.

**The rules, in plain words:**
- A temporary modifier has a **start turn** and a **last turn**. It counts while the current turn is inside that window (start ≤ now ≤ last).
- It is checked **once each turn**, at the end-of-turn tick. The first turn the clock has run past its last turn, the modifier is removed.
- It is removed on the turn **after** its last turn — so a modifier whose last turn is turn 7 still counts on turn 7, and is gone on turn 8.
- While active it folds into the value it targets exactly like a permanent modifier (same arithmetic).

**Worked example:**
> An event grants "+100 bells per turn for 2 turns", registered on turn 1. On turn 1 the colony's bell total is boosted; on turn 2 (the last turn) it is still boosted; when the game advances to turn 3 the modifier expires and bell production drops back to normal — automatically, with nothing for the player to dismiss.

**What the player sees and does:** nothing directly — this is plumbing. The player sees the effect (a temporarily larger/smaller number) for the modifier's lifetime. **The classic ruleset ships no temporary modifiers at all**, so in the base game nothing is ever registered and nothing ever expires; the system exists so a future event or the Australia variant can grant a timed bonus.

## 2. Detailed rules

- A temporary modifier carries a payload (the value/type/target/scope — a `FatherModifier`), a **first turn** (inclusive) and a **last turn** (inclusive).
- **Active window** (FreeCol `Feature.appliesTo(Turn)`): active when `firstTurn ≤ turn ≤ lastTurn`.
- **Expiry** (FreeCol `Feature.isOutOfDate(Turn)`): expired when `turn > lastTurn`.
- **Duration → last turn** (FreeCol `Modifier.makeTimedModifier`): a modifier registered with a duration `d` starting on turn `s` has `lastTurn = s + d − 1`. A duration of 1 is active only on the start turn. Duration must be ≥ 1.
- **Per-turn strip**: the expiry check runs once per `EndTurn`, immediately after the turn counter advances. So a modifier is active for exactly its window of turns and removed the moment the game enters the turn after its last.
- **Application**: while active, a temporary modifier targeting a goods id joins the permanent father/nation modifiers for that goods in `ApplyGoodsModifiers`, applied in ascending index order then folded (FreeCol `FeatureContainer.applyModifiers`).

| Input / condition | Result |
|---|---|
| `MakeTimed(template, duration=3, start=5)` | window `[5, 7]`; active on turns 5, 6, 7 |
| current turn = 4, window `[5,7]` | not active (before window); does not fold |
| current turn = 7, window `[5,7]` | active (last turn); folds |
| current turn = 8, window `[5,7]` | expired; stripped at the `EndTurn` that entered turn 8; does not fold |
| classic default game, any turn | registry empty — nothing registered, queried, or stripped |
| `MakeTimed(..., duration=0, ...)` | rejected (`ArgumentOutOfRangeException`) |

**Deviations from original 1994 / FreeCol behavior:** **Faithful subset.** FreeCol attaches temporary `Modifier`s to several object kinds (a `Player`, a `Colony`, any `FreeColGameObject`) and serializes the still-active ones. We model only the **bounded-lifetime + per-turn-strip** behaviour, on a single **game-level transient registry**, and fold active ones through the existing goods-modifier path. The `increment`/`getValue(Turn)` ramp (a value that changes over the window, used by the colony goods-party decay) is **not** modelled — no classic content needs it, and the goods-party mechanic itself is not yet implemented. Because classic registers none, this divergence is invisible in the default game.

## 3. Technical design

**Domain model:**
- `TemporaryModifier` (`GameSession/TemporaryModifier.cs`) — an immutable record wrapping a `FatherModifier Payload` plus `int FirstTurn`/`int LastTurn`. `AppliesTo(turn)` and `IsOutOfDate(turn)` mirror FreeCol `Feature`; the static `MakeTimed(template, duration, start)` factory mirrors `Modifier.makeTimedModifier`.
- `FatherModifier` / `ModifierType` / `ModifierMath` (`Specification/FoundingFather.cs`) — the value/type/scope payload and the shared `apply` arithmetic, reused unchanged so a temporary modifier folds identically to a permanent one.

**Data sources:** none in the classic ruleset (no XML feeds a temporary modifier today). A future event or variant would build a `FatherModifier` payload and register it through the seam below.

**Algorithms & formulas:**
- `Game._temporaryModifiers` — a `List<TemporaryModifier>`, the registry. Empty in the default game.
- `Game.RegisterTemporaryModifier(modifier)` (internal) — the only entry point; adds to the registry.
- `Game.ActiveTemporaryModifiers(targetId)` — the registry entries matching `targetId` whose `AppliesTo(Turn)` is true; folded by `ApplyGoodsModifiers` (`modifiers.AddRange(ActiveTemporaryModifiers(goodsId).Select(m => m.Payload))`).
- `Game.RemoveExpiredTemporaryModifiers()` — `_temporaryModifiers.RemoveAll(m => m.IsOutOfDate(Turn))`, called from `EndTurn` immediately after `Turn++`.

**Integration points:** `EndTurn` calls `RemoveExpiredTemporaryModifiers()` as its last step (after the turn counter advances), mirroring FreeCol's per-new-turn temporary-modifier removal. `ApplyGoodsModifiers` folds active ones alongside the permanent father/nation modifiers.

**Persistence:** **none.** The registry is transient and never written to the save (`SaveGame` has no temporary-modifier field). Because the classic registry is always empty, omitting it costs nothing and the save version is unchanged (still v52) and the default game is byte-identical (ADR-009). If a future variant registers persistent timed modifiers, the still-active set would need a save slice and a version bump at that point (it does not now).

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `TemporaryModifierTests` (window math, apply-then-expire across `EndTurn`, target isolation, duration guard, default-game-empty over 10 turns) | ✅ |
| L2 Scenario | Always | covered by the existing turn/soak scenarios — the default game registers none, so behaviour is unchanged | ✅ |
| L3 Interaction | No UI | — | — |
| L4 Visual | No screen | — | — |
| L5 Soak | Covered by global suite | not bumped (no save change) — soak not re-run | — |

- **FreeCol cross-check:** window/expiry semantics pinned to `Feature.appliesTo`/`isOutOfDate` and `Modifier.makeTimedModifier` (`lastTurn = start + duration − 1`); the per-turn strip mirrors the temporary-modifier removal driven from `csNewTurn` and the serialization filter `m.isTemporary() && !m.isOutOfDate(turn)` (`Player.java` line 4289). The `increment` ramp is deliberately omitted (see deviations).

## 5. Open issues / TODO

- [ ] If/when a real timed event lands (or the colony goods-party decay), wire the `increment`/`getValue(Turn)` ramp and persist still-active temporary modifiers (save slice + version bump) — not needed while classic registers none.
- [ ] Generalise `ActiveTemporaryModifiers` beyond goods targets (combat, movement, …) if a future temporary modifier targets those; today only `ApplyGoodsModifiers` folds them.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-21 | **Temporary-modifier TTL system** (`86d3drpgz`). Added `TemporaryModifier` (a duration-bounded `FatherModifier` with `FirstTurn`/`LastTurn`, `AppliesTo`/`IsOutOfDate`, and a `MakeTimed` factory — mirroring FreeCol `Modifier.makeTimedModifier` + `Feature.appliesTo`/`isOutOfDate`), a transient game-level registry (`Game._temporaryModifiers`) with a register seam, an active-query folded into `ApplyGoodsModifiers`, and a per-turn strip (`RemoveExpiredTemporaryModifiers`) run at the end of `EndTurn` after the turn advances. A modifier is active through its `lastTurn` and removed on the next turn. **Classic ships none**, so the registry is always empty, nothing is serialized, the save version is unchanged (v52), and the default game is byte-identical (ADR-009). Faithful subset: no `increment` ramp, single game-level registry, goods targets only. +6 L1 (`TemporaryModifierTests`); targeted modifier/ruleset/father filter 135 green. | `86d3drpgz` |
