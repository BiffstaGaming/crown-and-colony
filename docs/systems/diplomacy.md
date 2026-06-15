# System: Diplomacy (stance & tension)

| | |
|---|---|
| **Status** | Implemented — colonial-colonial **stance + tension recorded** (FP-6a) and the **tension→stance state machine** (war→cease-fire→peace, FP-6b). **Foreign powers now *wage* war** (slice 1c-2): a power at `War` with the human sends its armed units to attack the human's units. The AI does not yet *declare* war on its own (war still starts only from the human's attack) — that's a later slice. |
| **Last verified** | 2026-06-15 @ slice 1c-2 (foreign retaliation) |
| **Code** | `game/src/GameLogic/GameSession/Stance.cs`, `GameSession/Game.cs` (`StanceBetween`/`TensionBetween`/`SetStance`/`ChangeTension`/`DetectColonialContacts`/`DecayColonialTension`/`StanceFromTension`/`UpdateColonialStances`), `GameSession/Player.cs` (`Stances`/`Tensions`) |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/DiplomacyTests.cs` |
| **FreeCol reference** | `common/model/Stance.java`, `common/model/Tension.java`, `common/model/Player.java` (stance/tension maps, `setStance`, `makeContact`), `server/model/ServerPlayer.java` (`csChangeStance`) |
| **Related systems** | [players](players.md), [combat](combat.md), [natives](natives.md) (the parallel per-settlement alarm system), [turns](turns.md), [save-load](save-load.md) |

## 1. How it works (plain English)

*Audience: anyone — no jargon.*

Every European power keeps track of how it feels about every **other** European power: a **stance** (are we *uncontacted*, at *peace*, or at *war*?) and a hidden **tension** meter (a grudge level that climbs when you're wronged and slowly cools over time).

- You begin **uncontacted** with each rival — you simply haven't met. The first time one side actually *sees* the other (a unit or colony comes into view), you become **at peace** with each other. Meeting carries no grudge.
- **Attacking** a rival's unit makes you **at war** — both ways, immediately — and spikes the grudge to the top. (You don't have to be at war to attack; attacking is what *causes* the war.) **Exception — privateers:** a ship with the `piracy` ability can raid rival shipping (or be raided) **without** provoking war, flying no flag (see [combat](combat.md)).
- Each turn, tension **cools** a little on its own (the same slow fade the native nations' anger uses) — and the relationship **follows the grudge meter**: a war that has cooled enough becomes a **cease-fire** (an uneasy truce), and a cease-fire that keeps cooling drifts back to **peace**. (A flare-up the other way — peace straight to war from tension alone — needs a tension source we don't have yet, so in practice war only starts from an actual attack.)

**Important, this slice:** the relationship is tracked, evolves on its own, and the computer players now **act on War**: if you attack a rival European power (which puts you both at war), its armed units will come after your units (slice 1c-2 — see [combat](combat.md)). What's still missing: a computer player **declaring** war on its own (war only ever starts from *your* attack today — they don't yet pick fights or break the peace), assaulting your *colonies* (1c-3), the diplomacy actions (offer peace/alliance), and the Founding Fathers that care about diplomacy (de Witt, Franklin).

**The native nations are not part of this yet.** Your relationship with each native settlement is still its own separate "alarm" meter ([natives](natives.md)); folding the two systems together is future work.

**Worked example:**
> A rival builds a colony six tiles away. You can't see it, so you stay *uncontacted*. Your scout walks over a hill and the colony comes into view — now you're both *at peace*, tension 0. A few turns later you attack one of their wagon trains: instantly you're both *at war*, tension jumps to maximum, and from then on it ticks down a little each turn (but stays *war* until a future peace deal).

**What the player sees and does:** nothing new on screen yet — there's no diplomacy UI and the rivals remain off-screen under your fog. The state exists in the save and is queryable by the rules/tests.

## 2. Detailed rules

*Audience: designers/testers.*

- **Scope: colonial ↔ colonial only.** Stance/tension are tracked between `PlayerType.Colonial` players (the human + the foreign powers). Native players are **ignored** by every helper — they keep `NativeSettlement.Alarm`/`AlarmLevel` ([natives](natives.md)). This mirrors FreeCol's own split (Europeans flip stance instantly on an attack; natives accumulate settlement alarm and self-declare later) and avoids re-baselining the native system. Unifying them is deferred.
- **`Stance` values** (`Stance.cs`): `Uncontacted` (0, the default for any unrecorded pair) · `Peace` (1) · `War` (2) · `CeaseFire` (3). A faithful subset of FreeCol's `Uncontacted/Alliance/Peace/CeaseFire/War`. Values are explicit and **appended** (not in FreeCol's declaration order) so the FP-6a saved ordinals stay stable. `Alliance` is only set by a diplomacy *action* — deferred until those land (it appends as 4).
- **Directional storage.** Each `Player` holds its **own** view of every other player it has met: `Stances` (other `PlayerId` → `Stance`) and `Tensions` (other `PlayerId` → 0..`MaxTension`). An absent entry reads as `Uncontacted`/`0`. (FreeCol likewise stores per-player maps.)
- **Transitions** (all deterministic, no RNG, none gate any legality):

| Trigger | Effect |
|---|---|
| First contact: a colonial player's explored fog first covers a tile holding another colonial player's on-map unit or colony (`DetectColonialContacts`, run once per `EndTurn`) | both directions `Uncontacted` → `Peace`, tension unchanged (FreeCol `CONTACT_MODIFIER` = 0). Already-met pairs (Peace/War) are skipped |
| Attack: a colonial unit attacks another colonial player's unit (`Attack`) | both directions → `War`, tension += `TensionWar` (1000, clamped to `MaxTension` 1100). Recorded **before** combat resolves, so win/loss is irrelevant. Does **not** gate the attack. **Skipped entirely when the attacker or defender has `model.ability.piracy`** (a privateer raids without war — 1c-3d-i; the stance is left as-is, not reset) |
| Each turn (`DecayColonialTension`, run in `EndTurn`) | every recorded colonial-pair tension cools by `value/100 + 4` (floored at 0) — the same formula as native alarm decay |
| Each turn after decay (`UpdateColonialStances` → `StanceFromTension`, FP-6b) | each met pair's stance re-derives from its (cooled) tension (FreeCol `Stance.getStanceFromTension`, DELTA=10 hysteresis): **War → CeaseFire** at tension ≤ 590 (`CONTENT.limit−DELTA`); **CeaseFire → Peace** at ≤ 90 (`HAPPY.limit−DELTA`); **Peace/CeaseFire → War** above 1010 (`HATEFUL.limit+DELTA`); `Uncontacted` unchanged. Symmetric, ordered by id |

- **Numeric scale.** Tension is 0..`MaxTension` (1100 = FreeCol `Tension.Level.HATEFUL.limit` + 100); `TensionWar` (1000) is the FreeCol WAR modifier; the stance thresholds use `CONTENT.limit` 600, `HAPPY.limit` 100, `DELTA` 10. These mirror the native-alarm constants but are declared in `GameSession` to avoid a dependency on `GameLogic.Natives`.
- **Peace→War from tension alone is currently unreachable in play:** the only thing that raises colonial tension is an attack, which already sets War directly (and decay only lowers tension). The `StanceFromTension` Peace→War branch is implemented faithfully for when a non-attack tension source lands (e.g. land-taking), and is covered by a direct unit test.
- **Colonial-colony assault → war (1c-3e):** assaulting a rival **colony** (`AttackColony`) now also records mutual War + a tension spike (both ways, before the colony changes hands), the same hook as a unit attack — closing the FP-6a gap. (Capturing your own colony back is impossible; the trigger only fires between distinct colonial players.)

**Deviations from FreeCol:** colonial tension **decay** is a deliberate symmetry with native alarm — FreeCol has no European tension decay; the slice scope asks for it. Decay feeds the stance machine, which can only *de-escalate* from it (war→cease-fire→peace), never escalate (decay lowers tension; escalation needs >1010, which decay can't produce) — so it cannot surprise the player. The big FreeCol pieces intentionally **not** here (the rest of FP-6b): the AI *acting* on stance (choosing to declare/wage war, native raids), `Alliance` + the diplomacy actions that set it and their tension modifiers (alliance −500, peace-treaty −250, resume-war +750, inciter +250), and gating the human's attacks behind a war declaration (FreeCol lets the attack itself declare war — which is what we do).

## 3. Technical design

*Audience: developers / future sessions.*

- **`Stance` enum** (`GameSession/Stance.cs`) — `Uncontacted=0/Peace=1/War=2`; zero-value default means an absent map entry / pre-FP-6a save reads as `Uncontacted` for free. Serialized as its integer ordinal (no `JsonStringEnumConverter` configured), matching how `PlayerType` is stored.
- **`Player`** holds `_stance`/`_tension` (`Dictionary<int,…>`) with public read views `Stances`/`Tensions` and `internal` mutable views `StanceMap`/`TensionMap` (the established field + read-view + internal-mutable-view pattern).
- **`Game`** API: `StanceBetween(a,b)`/`TensionBetween(a,b)` (public reads; default Uncontacted/0); `SetStance(a,b,stance,symmetric=true)`/`ChangeTension(a,b,delta,symmetric=true)` (`internal`; both clamp/guard so they are a **no-op unless `a` and `b` are distinct colonial players** — the single place the colonial-only rule is enforced, so every call site is naturally native-safe). Constants `MaxTension`/`TensionWar`.
- **Transition sites:** `DetectColonialContacts` → `DecayColonialTension` → `UpdateColonialStances` run in `EndTurn`'s world-step block (beside `DecayNativeAlarm`); the attack→war record sits in `Attack` right after the defender is resolved, guarded by `defenderNation is null` (a colonial defender). All are append-only side-effects — they never read or modify `AreEnemies`/`CheckMove`/`CheckAttack`/fog, so legality is unchanged and the human's RNG stream 0 is untouched (no draws).
- **`StanceFromTension(current, tension)`** (`internal static`) is the pure FreeCol `Stance.getStanceFromTension` port (a `switch` expression; directly unit-tested). `UpdateColonialStances` applies it after decay, over met pairs (those with a stance entry; `Uncontacted` skipped) in id order, setting the new stance symmetrically.
- **Determinism:** contact iterates colonial players in `PlayerId` order over the already-deterministic `Explored` fog; decay + stance-update iterate keys/pairs in id order. No RNG anywhere.

**Persistence:** see [save-load](save-load.md). Save **v20, additive** (the wave convention): `SavedPlayer.Stances`/`SavedPlayer.Tensions` (`Dictionary<int,…>`, omitted via `WhenWritingNull` when empty) — so a never-contacted game (all current goldens / the human-only baseline) writes **no new bytes** and is byte-identical; older saves load `Uncontacted`/`0`. `ToSavedPlayer` writes them, `BuildPlayer` restores them via `RestoredPlayer`.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `DiplomacyTests`: defaults (Uncontacted/0), `SetStance` symmetric/directional, `ChangeTension` clamp [0,1100], native no-op, contact→Peace both ways, contact doesn't downgrade War, attack→War + tension both ways (+ attack allowed while Uncontacted = no gate), per-turn decay (`−v/100−4`), **`StanceFromTension` thresholds (11-case theory)**, **war→cease-fire and cease-fire→peace via `EndTurn` as tension cools**, save round-trip (incl. CeaseFire), no-contact byte-stability | ✅ |
| L2 Scenario | Always | the soak (25 seeds × 200 turns) round-trips byte-identically with diplomacy state recorded; the stream-0 guard (`ForeignPowerEconomyTests`) confirms diplomacy adds no stream-0 draws | ✅ |
| L3 Interaction | No new UI | — (no diplomacy screen yet) | — |
| L4 Visual | No new screen | goldens unchanged (diplomacy is not rendered) | — |
| L5 Soak | Always | covered by L2 soak above (invariants + byte-identical round-trip) | ✅ |

- **FreeCol cross-check:** stance set + transition semantics from `Stance.java`/`Player.java` (`makeContact` symmetric peace, attack → `csChangeStance(WAR)`); tension scale/WAR modifier from `Tension.java`; decay formula reused from our native-alarm port of `ServerPlayer`. Deferred FreeCol mechanics are listed in §2.

## 5. Open issues / TODO

- [x] Stance + tension data model, contact→Peace, attack→War, decay, persistence (FP-6a).
- [x] Tension→stance state machine — `CeaseFire` + `StanceFromTension`/`UpdateColonialStances` (war→cease-fire→peace) (FP-6b, this slice).
- [x] **The AI *wages* war** (slice 1c-2): a foreign power at `War` attacks the human's units (`RunForeignPowerTurn`, on its own RNG stream — see [combat](combat.md)). Native raids off alarm shipped in 1b.
- [ ] **The AI *declares* war on its own:** a tension source other than the human's attack (e.g. land-taking) so a power can break the peace; foreign-vs-foreign / foreign-vs-native combat.
- [x] Assaulting rival **colonies** (`AttackColony`, 1c-3e) — adds the colonial-colony attack→war trigger (capture itself in [combat](combat.md)).
- [ ] Diplomacy actions (offer peace/alliance/cease-fire, incite) and their tension modifiers.
- [ ] Founding-father diplomacy effects (de Witt, Franklin).
- [ ] Unify native alarm into the player-tension model.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-14 | FP-6a: diplomacy foundation — `Stance{Uncontacted,Peace,War}` + per-pair tension on `Player`; `Game` helpers; contact→Peace, attack→War (+1000 tension), per-turn decay; colonial-only (natives stay on alarm); save v20 additive (`Stances`/`Tensions`). Recorded only — no legality/AI change | FP-6a |
| 2026-06-15 | FP-6b: tension→stance state machine — added `Stance.CeaseFire` (=3, appended); `StanceFromTension` (FreeCol `getStanceFromTension`: war→cease-fire ≤590, cease-fire→peace ≤90, →war >1010, DELTA 10) applied each turn by `UpdateColonialStances` after decay. Deterministic, no RNG, no legality change. Save round-trips the new value (v20, ordinal stable) | FP-6b |
| 2026-06-15 | Slice 1c-2: the AI now **acts on War** — `RunForeignPowerTurn` sends a power's armed units after the human's nearest unit when `StanceBetween(power, human) == War` (combat on the power's own RNG stream; raises a `CombatNotice`). Still no AI-*declared* war (war starts only from the human's attack). See [combat](combat.md) | Phase 5 slice 1c-2 |
| 2026-06-15 | Slice 1c-3d-i: **privateer piracy** exception to attack→war — `Game.Attack` skips the stance/tension hook when the attacker or defender has `model.ability.piracy` (raid rival shipping without war; the stance is left as-is). Non-privateer attacks unchanged. See [combat](combat.md) | Phase 5 slice 1c-3d-i |
| 2026-06-15 | Slice 1c-3e: **colonial-colony assault → war** — `Game.AttackColony` records mutual War + tension (both ways, before ownership flips), closing the FP-6a "no colonial-colony attack trigger" gap. Capture itself in [combat](combat.md) | Phase 5 slice 1c-3e |
