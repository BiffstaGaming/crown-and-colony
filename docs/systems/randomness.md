# System: Randomness

| | |
|---|---|
| **Status** | Implemented |
| **Last verified** | 2026-06-13 @ Phase 0 scaffold commit |
| **Code** | `game/src/GameLogic/Randomness/` |
| **Tests** | `game/tests/GameLogic.Tests/Randomness/` |
| **FreeCol reference** | n/a — foundational infrastructure (ADR-009), not a Colonization rule |
| **Related systems** | (all future systems that roll dice: combat, map generation, AI) |

## 1. How it works (plain English)

Every random event in the game — a battle's outcome, where mountains appear on the map, which immigrant shows up in Europe — comes from one dice-rolling service that the game fully controls. Each new game gets a "seed" number, and the same seed always produces exactly the same game: the same map, the same battle results, the same everything, even years from now on a different computer.

**The rules, in plain words:**
- One shared dice-roller, handed to systems that need it; nothing rolls its own dice.
- The same seed always replays identically.
- Different parts of the game (map-making vs. combat) get their own independent "streams," so extra dice rolls in one part never shuffle the outcomes of another.
- When you save a game, the dice-roller's position is saved too — loading resumes the exact future rolls.

**What the player sees and does:** nothing directly — but bugs become reproducible ("send me your seed"), and replays/scenario tests become possible.

## 2. Detailed rules

| Input / condition | Result |
|---|---|
| Same seed + same calls | Identical sequence, any platform, any .NET version |
| Same seed, different stream id | Independent sequences |
| `Next(max)` | Uniform integer in `[0, max)`; throws if `max <= 0` |
| `Next(min, max)` | Uniform integer in `[min, max)`; throws if `min >= max` |
| `NextDouble()` | Uniform double in `[0, 1)`, 53-bit precision |
| Save state → restore | Resumed generator continues the exact sequence |

**Deviations from original 1994 / FreeCol behavior:** n/a (infrastructure).

## 3. Technical design

**Domain model:**
- `IGameRandom` (`Randomness/IGameRandom.cs`) — the only randomness interface game logic may depend on. `System.Random` and `GD.Randf()` are banned everywhere (logic *and* presentation).
- `Pcg32Random` (`Randomness/Pcg32Random.cs`) — PCG32 XSH-RR implementation (O'Neill, pcg-random.org).
- `RandomState` — serializable `(State, Increment)` snapshot; `Pcg32Random.FromState()` resumes it.

**Why our own PRNG instead of seeded `System.Random`:** .NET does not guarantee a seeded `Random` produces the same sequence across framework versions. Our determinism contract (saves, replays, scenario tests, visual fixtures) must survive .NET upgrades, so the algorithm is pinned in our own code.

**Algorithms:** PCG32: 64-bit LCG state (multiplier `6364136223846793005`) + XSH-RR output permutation. Bounded ints via rejection sampling (no modulo bias). Doubles from two 32-bit draws → 53 mantissa bits.

**Integration points:** constructor-injected wherever needed. Convention (enforce in review): one stream id per subsystem (e.g. map gen, combat, AI), allocated in a future `RandomStreams` registry when the second consumer appears.

**Persistence:** `RandomState` per stream is part of the save game.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `Pcg32RandomTests`: same-seed identity (1000 draws), stream independence, save/resume identity, pinned golden sequence for seed 1, bounds (10k draws), uniformity sanity (60k draws/6 buckets), argument validation | ✅ 13/13 |
| L2 Scenario | Always | Covered implicitly by every future scenario test (they all depend on this determinism) | ✅ |
| L3 Interaction | No UI | — | — |
| L4 Visual | No screen | — | — |
| L5 Soak | Covered by global suite | — | — |

- **Pinned-sequence contract:** `KnownSeed_ProducesPinnedSequence` hardcodes seed 1's first 8 draws (`398737, 903413, …`), captured 2026-06-13 on .NET 10/win-x64. If this test ever fails, the algorithm changed — that invalidates every recorded game and fixture and requires an ADR, not a test update.
- **FreeCol cross-check:** n/a.

## 5. Open issues / TODO

- [x] Multiple independent streams now exist — the main/economy/human draws on stream 0, native settlement placement on stream 1 (`Game.NativeStreamId`), and each non-human player on `PlayerId + 1` (`Player.RngStreamId`). Allocated ad hoc (no formal `RandomStreams` registry was needed); revisit a registry only if stream-id collisions ever become a risk.
- [x] Save-game integration — the main `RandomState` is persisted in every save (`SaveGame.RandomStateValue`/`RandomIncrement`) and each non-human player's stream via `SavedPlayer.RngState`/`RngIncrement`; a loaded game resumes the exact sequence.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-13 | Initial implementation: IGameRandom, Pcg32Random, RandomState + full L1 suite | Phase 0 scaffold |
