# System: Federation Victory (Australian variant)

| | |
|---|---|
| **Status** | In development (Phase-4a core loop implemented) |
| **Last verified** | 2026-07-09 @ 429f4c7 (branch `worktree-agent-a1439d946c2dc04a3`) |
| **Code** | `game/src/GameLogic/GameSession/Game.Federation.cs`, `FederationPhase.cs`, `game/src/GameLogic/Colonies/Colony.cs` (FederationSupport), `game/presentation/FederationPanel.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/FederationVictoryTests.cs` (L1/L2), `game/presentation/tests/FederationPanelTests.cs` (L3) |
| **FreeCol reference** | `freecol/data/rules/classic/specification.xml` (`gameOptions.victoryConditions`), FreeCol `Player.checkForDeath` / victory checks (concept only — Federation is our own win path, not in FreeCol) |
| **Related systems** | [independence.md](independence.md), [founding-fathers.md](founding-fathers.md), [colonies.md](colonies.md), [game-modes.md](game-modes.md) |

## 1. How it works (plain English)

*Audience: anyone — no jargon, no class names.*

In the Australian scenario the player does **not** win by declaring war on the King and beating his army (the classic "War of Independence"). Instead the six Australian colonies — New South Wales, Victoria, Queensland, South Australia, Tasmania and Western Australia — must be persuaded, one at a time, to **federate** into a single Commonwealth of Australia by 1901. It is a political win, not a military one.

Each colony builds up **Federation Support** the same way it builds up rebel sentiment in the classic game: from its **Civic Voice** (the Australian name for Liberty Bells — produced by Town Halls, newspapers, schools and the like). The more Civic Voice a colony makes each turn, the more its Federation Support grows. As the movement grows the player also banks national **Convention Points**, a slower separate resource that represents the constitutional groundwork.

Once enough colonies are behind the idea, the player calls a **Federation Convention**; the convention drafts a **constitution**; and finally the colonies hold a **referendum**. If the referendum carries, the **Commonwealth is proclaimed and the player wins**. If it fails, the movement loses some ground but can try again — a failed vote never ends the game.

**The rules, in plain words:**
- Only the Australian scenario uses this. The classic game is completely unchanged — it still wins by War of Independence.
- Every Australian colony banks Federation Support from its Civic Voice each turn (just like it banks Liberty).
- A colony's Federation Support is shown as a percentage (0–100%), the same scale as Sons-of-Liberty support.
- The country is divided into six colony regions. A region's support is the average of the player's colonies in it.
- You can **call a Federation Convention** once at least **4 of the 6 regions are above 40% support** and you have banked at least **200 Convention Points**.
- After the convention is called, the constitution **drafts itself** once you have banked enough Convention Points (400).
- Once the constitution is drafted you can **put Federation to a referendum**, but only when **every colony region you hold is at 50% support or more**.
- The referendum is a chance-based vote: the stronger your support, the more likely it carries. Carry it and you win; fail it and you lose a little support and can try again.

**Worked example:**
> You hold colonies in New South Wales, Victoria, Queensland and South Australia. Each has a busy Town Hall, so their Federation Support climbs a few points every turn. After many turns all four regions pass 40% and you have 200 Convention Points banked — the **Call the Federation Convention** button lights up. You call it. A few turns later your Convention Points reach 400 and the draft constitution completes on its own. You keep building Civic Voice until all four regions reach 50%, then hit **Put Federation to a Referendum**. Support is high, the vote carries, and on the next turn the Commonwealth of Australia is proclaimed — you win.

**What the player sees and does:** In the Australian game a **Federation…** button appears in the bottom-right HUD (where the Declare-Independence button sits in the classic game). It opens the **Road to Federation** screen: the current phase, your Convention Points, a support bar for each of the six colonies, and the context action (Call Convention, or Put to Referendum) — greyed out with the reason shown until its requirements are met.

## 2. Detailed rules

*Audience: designers/testers — exact, but still readable.*

**Core resources**

| Resource | Where it lives | How it grows |
|---|---|---|
| Federation Support (per colony) | `Colony.FederationSupport` (points) / `Colony.FederationSupportPercent` (0–100) | Banks the same net Civic Voice (bells) the colony's Liberty pool receives, each turn; floored at 0, capped at the 100%-support ceiling (`RebelLibertyDivisor × population`, classic 200 × pop). |
| Region Federation Support | `Game.RegionFederationSupport(regionKey)` | Unweighted average of `FederationSupportPercent` over the human colonies whose tile lies in that region (0 when none). |
| Convention Points (national) | `Game.ConventionPoints` | Accrues 25% of each turn's positive net human Civic Voice (a sub-point remainder carries so low output still accrues); trails support as a slower axis. |

**Phase machine (`FederationPhase`)**

| Phase | Ordinal | Meaning | Advances when |
|---|---:|---|---|
| ColonialMaturity | 0 | Default; support still gathering. A classic game never leaves this. | Player calls a convention (manual) once the gate opens. |
| ConventionCalled | 1 | Convention called; constitution being drafted. | Convention Points reach 400 (automatic, at end of turn). |
| ConstitutionDrafted | 2 | Constitution complete; a referendum may be put. | Player puts a referendum (manual). |
| Referendum | 3 | A referendum has been held; if it failed, the movement rebuilds here. | A carried referendum → Commonwealth (automatic, at end of turn). |
| Commonwealth | 4 | Terminal. Human has won by Federation. | — (`Game.Winner` reports the human). |

**Action gates**

| Action | Method | Requirements (all must hold) |
|---|---|---|
| Call Convention | `CheckCallConvention` / `CallConvention` | Federation victory enabled · phase = ColonialMaturity · ≥ 4 of 6 regions at ≥ 40% support · ≥ 200 Convention Points. |
| Put to Referendum | `CheckPutToReferendum` / `HoldReferendum` | Federation victory enabled · phase = ConstitutionDrafted or Referendum (retry) · at least one settled region · **every** settled region at ≥ 50% support. |

**Referendum roll (deterministic, ADR-009):** `HoldReferendum` seeds a dedicated generator (`Pcg32Random`, stream 107) from the human's own RNG state read **without advancing** stream 0, mixed with the turn and the attempt count. It rolls `0–99`; the vote **carries** when `roll < averageSettledSupport` (so support 100 always carries, 0 never does). A carried vote sets a "carried" flag that `ResolveCommonwealthFederation` reads next turn to proclaim the Commonwealth. A failed vote increments the attempt count and sheds ~10% of each settled colony's banked support (anti-Federation momentum), leaving the phase at Referendum for a retry.

**Winner:** `Game.Winner` checks Federation **first** — when the victory is enabled and the phase has reached Commonwealth, the human wins. Off (and skipped) for classic.

**Deviations from original 1994 / FreeCol behavior:**
- **This whole system is new** — Colonization 1994 and FreeCol have no Federation win. It is the Australian variant's replacement for the War-of-Independence victory (ADR-021).
- **Imperial Pressure is political/economic only.** Per ADR-021, no Royal-Expeditionary-Force invasion is wired into the Federation path; the classic monarch/REF machinery is left entirely untouched. The King can still tax and bluster, but Federation is won at the ballot box, not on the battlefield.
- **Simplified from the full design.** The design doc (`05_Federation_Victory_System.md`) lists six narrative phases, per-colony historical support targets (NSW 57%, Tasmania 94%, …), Anti-Federation Sentiment, constitutional clauses, the NSW quota-failure rule, the Western-Australia late-entry rule, and five victory **grades**. Phase-4a implements the mechanically-distinct **core loop** only (the five states above, uniform 40%/50% thresholds, a single Commonwealth win). The richer content is deferred (see §5).

## 3. Technical design

*Audience: developers / future Claude sessions.*

**Domain model:**
- `FederationPhase` (enum) — the five-state machine; ordinal-persisted.
- `Game.Federation.cs` (partial `Game`) — owns the phase, Convention Points, referendum state, the accrual hook, the per-region aggregates, the action commands/gates, and the end-turn resolution. Pure C#, engine-free (ADR-006).
- `Colony.FederationSupport` / `AddFederationSupport` / `FederationSupportPercent` — the per-colony banked support, modelled exactly on `Liberty` / `AddLiberty` / `SonsOfLiberty`.
- `FederationPanel` (presentation) — renders the oracles and forwards the two commands; no rules.

**Data sources:** `game/data/rules/australia/specification.xml` → `gameOptions.victoryConditions` → `model.option.victoryFederation` (`true` for Australia). Parsed in `Ruleset.cs` (`ParseBooleanOption`, fallback `false`) into `Ruleset.VictoryFederation`. Classic ships no such option, so it defaults false.

**Algorithms & formulas** (all in `Game.Federation.cs`):
- Accrual — `AccrueFederationSupport(player, colony, netCivicVoice)`, called from the liberty-accumulation loop in `Game.cs` beside `colony.AddLiberty(net)`. Gated on `Ruleset.VictoryFederation`. `_conventionPoints += net × 25 / 100` with a hundredths remainder carry (`_conventionPointsHundredths`), human-only.
- Region average — `RegionFederationSupport` = `Σ FederationSupportPercent / count` over `HumanColoniesInRegion` (resolved via `Map.RegionOf(c.Position)?.Key`). The six keys are `FederationRegionKeys`.
- Referendum — `HoldReferendum`, stream `FederationStreamId = 107` (see §2).
- Resolution — `ResolveCommonwealthFederation`, called from `EndTurn` beside `ResolveWarOfIndependence`; drafts the constitution and proclaims the Commonwealth (both automatic transitions). A strict no-op in classic.

**Integration points:** turn resolution order in `Game.cs` — `ResolveWarOfIndependence()` then `ResolveCommonwealthFederation()`. Accrual rides the existing per-colony bells→liberty bake. `Game.Winner` (in `Game.Independence.cs`) checks Federation first. The HUD (`GameController`) shows the Australia-only Federation button (sharing the Independence grid slot; the two are mutually exclusive by `VictoryFederation`) and opens `FederationPanel`.

**Persistence (SaveGame v72):** four additive **omit-when-default** slices, so a classic game writes none of them and serialises byte-identically to v71 (ADR-009):
- `SavedColony.FederationSupport` — omitted when 0.
- `SaveGame.FederationPhase` (int?) — omitted for the default `ColonialMaturity` (0).
- `SaveGame.ConventionPoints` (int?) — omitted when 0.
- `SaveGame.Referendum` (`SavedReferendum`: attempts + carried) — omitted when no referendum has been held.

The Convention-Points sub-point remainder (`_conventionPointsHundredths`) is **ephemeral** (not persisted); it is only ever touched by an Australia game, so it never shifts the classic soak, and at most a fraction of a point is lost across a save/reload on the slow points axis.

## 4. Verification

*How we know this works — the testing contract for this system (see `docs/TESTING.md` for layer definitions).*

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `FederationVictoryTests` — option parse; classic accrues nothing; accrual from Civic Voice; region aggregate; phase gates/advances; referendum determinism; persistence round-trip + classic-omits-tokens. | ✅ |
| L2 Scenario | Always | `FederationVictoryTests.FullySupportedFederation_ReachesTheCommonwealthWin_ViaWinner` (full loop → `Game.Winner`); classic byte-stability proven by the global `SoakTests`. | ✅ |
| L3 Interaction | Yes (has UI) | `FederationPanelTests` — renders phase + Convention Points + six region rows; Call-Convention disabled with reason until thresholds met. | ✅ |
| L4 Visual | Deferred | No Australia in-game HUD golden yet; classic goldens unaffected (button/panel invisible in classic). | ⬜ |
| L5 Soak | Covered by global suite | `SoakTests` (25 seeds × 200 turns, classic) stays byte-identical — the Federation loop is gated off. | ✅ |

- **FreeCol cross-check:** N/A for the win path itself (Federation is our own design). The **accrual** reuses the FreeCol-faithful bells→Liberty bake unchanged, and Federation Support mirrors the FreeCol Sons-of-Liberty percentage formula (`points × 100 / (LibertyPerRebel × population)`, clamped 0–100).
- **Local results (2026-07-09):** L1/L2 `dotnet test … --filter "Category!=Soak"` → 2875 passed / 0 failed (14 new Federation tests). Soak `--filter "Category=Soak"` → 5 passed / 0 failed (classic byte-identical). L3 `FederationPanelTests` → 2 passed; golden tests → 10 passed (no regression).

## 5. Open issues / TODO

- [ ] **Full five victory grades deferred** (Bare / Stable / Reform / Treaty / Economic Commonwealth) — Phase-4a ships a single Commonwealth win. Grading needs legitimacy, First-Nations relations, debt/infrastructure and reform-clause inputs that don't exist yet.
- [ ] **Per-colony historical support targets deferred** — the core loop uses uniform 40% (convention) / 50% (referendum) thresholds instead of the design's per-colony targets (NSW 57%, Tasmania 94%, WA 70%, …).
- [ ] **Constitutional clauses deferred** — no clause drafting/effects; the constitution "drafts" on a Convention-Points gate only.
- [ ] **Anti-Federation Sentiment deferred** — modelled implicitly as low support / the failed-referendum support drain, not as a separate tracked resource.
- [ ] **NSW quota-failure and WA late-entry special rules deferred.**
- [ ] **Phases 1–2 prerequisites (Colonial Maturity / Federation Movement gates) not wired** — settlement-maturity/region-activation oracles exist (`SettlementMaturityOracleTests`, doc 06) but are not yet gates on the loop.
- [ ] **AI opponents do not pursue Federation** — the loop is human-only (it is the human's win path).
- [ ] **L4 Australia HUD golden** — add once the Australia in-game HUD is a stable capture target.
- [ ] **New-Game victory override seam** — `VictoryFederation` is parse-time only (no in-game toggle, unlike the three defeat conditions).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-09 | Initial implementation & documentation — Phase-4a core Federation victory loop: per-colony Federation Support, Convention Points, `FederationPhase` state machine, seeded referendum, Commonwealth win via `Game.Winner`, SaveGame v72 persistence (omit-when-default), Federation HUD panel. Imperial Pressure kept political only (no REF); five victory grades deferred. | (this commit) |
