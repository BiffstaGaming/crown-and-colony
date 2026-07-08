# Australian Federation Mode — Implementation Plan

**Status:** Planning kickoff, 2026-07-07. Decomposes the `[EPIC P8] Australia variant` (ClickUp `86d3b3r7h`).
**Owner steer (Chris, 2026-07-07):**
- **First milestone = a *playable Australian skeleton*** — a selectable Australian mode you can play end-to-end, reusing every proven engine system, with Australian content + a UI reskin. The deep Australian mechanics (real Federation victory, First Nations redesign, events) are later phases.
- **Map = an *authored six-colony* map** (a hand-shaped continent with NSW / Victoria / Queensland / SA / Tasmania / WA placed deliberately).
- **"Australian Pioneers" = the Founding Fathers equivalent** — the perk-granting historical figures recruited into the Federation Convention. (The design corpus in this folder calls them "Historical Figures"; **in-game they are "Australian Pioneers"** per Chris. Not the tile-improver unit.)

This plan is the bridge between the **design corpus** (`00_README.md` … `21_Research_Sources.md` in this folder — the *what/why*) and the **code** (the *how*). Read the design corpus for the vision; read this for the build order.

> **Progress — 2026-07-08:** **Phase 0 (variant skeleton) + the Australia map + the Australian Pioneers are SHIPPED** (`86d3kwtf9`/`86d3kwtp5`/`86d3kwtjb`). "Australian Federation" is selectable at New Game and plays on the **real Australia continent map** — a 60×40 grid (de-staggered from the FreeCol source's 30×80 half-row coordinates, so the continent reads wider than tall as it should; **New Zealand removed** — Australia only) converted from the FreeCol community map pack by Euzimar (GPL v2), embedded as `game/data/maps/australia.txt`. The variant's `<founding-fathers>` are now the **25 Australian Pioneers** (docs `07_*`–`12_*` → five categories, perks = reused classic effects, era-gated by age weights — no Federation figure early), recruited through the unchanged election machinery under the **Federation Convention** label. The rest of the spec is still a copy of classic; the remaining Phase-1 reskin (nations/place-names `86d3kwtrq`, unit roster `86d3kwtvc`, goods `86d3kwty1`, UI labels `86d3kwu0c`) is next — then the pioneers' cotton/silver stand-ins retarget to wool/gold when the goods land.

---

## 1. The decisive architectural finding

**The "different selectable mode" plumbing already exists** — ADR-018's transposability layer. Adding Australia is fundamentally a **content-authoring task**, not new engine code. Verified seams (see [game-modes](../systems/game-modes.md)):

| Concern | It already works via | To add Australia |
|---|---|---|
| Mode selection at New Game | `GameVariant` / `GameVariants.All` registry (`game/src/GameLogic/Specification/GameVariant.cs`); the New-Game *Scenario* dropdown is data-driven from the registry | Register one `GameVariants.Australia`; it auto-appears in the dropdown — **no UI code** |
| The rules the engine plays by | `Ruleset.LoadEmbedded(resource, difficulty)` parses any FreeCol-format spec | Author `game/data/rules/australia/specification.xml`; embed it in the `.csproj` |
| Threading the choice into a game | `GameController.PendingVariant` → `StartNewGame` → `_variant.LoadRuleset(...)` → `Game.New(ruleset, …)` | Unchanged — works once the variant is registered |
| Save / reload under the right ruleset | `SaveGame.Variant` (save **v15+**); `GameVariants.Resolve(id)` | Unchanged — an Australian save records `"australia"` and reloads correctly |
| Units / goods / nations / figures | Parsed purely from the spec XML into `Ruleset.UnitTypes` / `GoodsTypes` / `EuropeanNations` / `FoundingFathers` | Pure data in the Australia spec — **no code** unless a unit needs a genuinely novel ability |
| Map | `MapGenerator` (procedural) + `FixedMap` (authored `.txt`, e.g. the shipped `america.txt`) | Author `game/data/maps/australia.txt`; add a `MapSource.Australia` case + dropdown entry |

**The one hard constraint — the transposability contract.** The engine keys some machinery on *well-known ids* that a variant must keep even while renaming the display text:
- Core goods `model.goods.food` / `bells` / `crosses` / `grain`; ocean terrain `model.tile.ocean` / `highSeas`; core abilities `model.ability.navalUnit` / `foundColony` / `person`.
- So **"Liberty Bells → Civic Voice" is a display rename** — the underlying good stays `model.goods.bells` because the Sons-of-Liberty / production machinery reads that id. Same for crosses (immigration), the colony/liberty model, etc. Content (nations, figures, units, terrain) is **replaced**; structural anchors are **renamed at the display layer only**.

**FreeCol reality check:** Australia does **not** exist in FreeCol (it is an Americas-only game). There is no Australian map or ruleset to download. We reuse FreeCol's *data format* and our own `classic` spec as the template, and author the Australian content ourselves.

**Consequence:** a playable Australian-flavoured game is reachable almost entirely through data (Phases 0–2). The genuinely code-heavy work (the real Federation victory loop, the First Nations redesign, the event system) is deferred to Phase 4+.

---

## 2. Naming map (engine id ⇄ Australian display)

The reskin is a display-layer rename over kept structural ids, plus replaced content. Key mappings (full list in `19_UI_Text_Renaming_and_Lore.md`):

| Engine concept / id | Australian display name | Reskin type |
|---|---|---|
| Founding Fathers (`foundingFather.*`) | **Australian Pioneers** | content replace (author Australian figures) + label |
| Continental Congress | Federation Convention | label |
| Liberty Bells (`model.goods.bells`) | Civic Voice | **display rename, id kept** |
| Sons of Liberty | Federationists | label |
| Tories | Imperial Loyalists / Anti-Federationists | label |
| Crosses (`model.goods.crosses`) | (immigration driver — keep, reskin flavour) | id kept |
| Declare Independence / War of Independence | Put Constitution to Referendum / Federation Referendum sequence | label now; **real mechanic is Phase 4** |
| Royal Expeditionary Force | Imperial Pressure / Oversight | label now; mechanic later |
| Native settlements / alarm | First Nations Communities / Tension · Country Pressure | label now; **redesign is Phase 4** |
| European powers | British Australia (primary) + sandbox powers | content replace |

---

## 3. Build order

### Phase 0 — Variant skeleton *(proves the pipeline; ~1 slice)*
Register `GameVariants.Australia` pointing at `australia/specification.xml` (initially a **copy of classic**), embed it in the `.csproj`. Result: **"Australian Federation" is selectable in New Game and plays** (as classic content for now). Tests mirror `GameVariantTests` + `NewGameBridgeTests`. This de-risks everything: the plumbing is proven before any content authoring.

### Phase 1 — Australian content pass *(the reskin that makes it Australian; the bulk of the milestone)*
All data in `australia/specification.xml`, reusing classic mechanics via `<modifier>`/`<ability>`:
- **1a — Nations/powers:** British Australia as the primary playable power (+ optional sandbox powers per `04_*`); native nations → First Nations groups (`16_*`); colony-name lists → Australian place-names (`european-nation-names.properties`).
- **1b — Units:** the Australian roster (`17_*`) — Convict Labourer, Emancipist, Free Settler, expert types (shepherd/shearer, miner/digger, stockman, surveyor…), reusing colonist mechanics with Australian ids + names.
- **1c — Goods:** the Australian economy (`17_*`) — wool, gold, coal, hides, tallow, meat, copper… mapped onto the trade/production model (keeping the structural food/bells/crosses/grain anchors).
- **1d — Australian Pioneers (Founding Fathers):** author the historical-figure roster (`07_*`–`12_*`) with reused perk mechanics (`<modifier>`/`<ability>`), grouped by the design's five categories. **This is Chris's "Australian Pioneers."** Hard date/region gates come with Phase 4.
- **1e — UI reskin:** the display renames (§2) — surfaced through the ruleset's display names + presentation string tables (the localization task `86d3fq1w6` may provide the seam). Founding Fathers→Australian Pioneers, Liberty→Civic Voice, natives→First Nations, etc.

### Phase 2 — The authored six-colony map
Author `game/data/maps/australia.txt` (FreeCol scenario format): the continent outline + climate-appropriate terrain + the six colony regions + resource placement (`17_*`). Register `MapSource.Australia` in `FixedMap` + the New-Game map dropdown. `DecorateFixedMap` layers rivers/resources/regions. (A rough map can land first, polished with Phase 4.)

### Phase 3 — Art & assets
Freely-licensed, **GPL-v2-compatible** sprites for the new units/buildings/terrain (record source + licence in the Asset Register per the licensing rules). **First Nations art requires cultural sensitivity** — see `15_*` and the tone guidance in `19_*`; when in doubt, ask Chris.

### Phase 4+ — The deep Australian mechanics *(the "not just a reskin" vision; code-heavy)*
Deferred until the skeleton is playable and signed off:
- **Federation victory loop** (`05_*`, `06_*`) — Civic Voice → Federation Support per colony → referendums → Commonwealth proclamation, replacing independence/war. This is the one big **new mechanic** — not expressible as a pure modifier, so it needs an ability-keyed handler (ADR-017/018 pattern) plus victory-condition wiring.
- **First Nations redesign** (`15_*`, `16_*`, `18_*`) — Respect / Tension / Country Pressure / Agreements, replacing the conquest-native model. Historically sensitive; design-led.
- **Event system** (`13_*`, `14_*`) — conditional 1788–1901 events.
- **Novel Australian Pioneer effects** that exceed the modifier/ability vocabulary.

---

## 4. Task breakdown ⇄ kanban (synced 2026-07-08)

Granular tasks (one work-block each: lands with tests + docs + CI green) live on the kanban (list `901615382059`); future phases stay single `[EPIC]` tasks until their phase starts (rolling-wave). Current state:

**Shipped:**
1. **`86d3kwtf9` [P8] Variant skeleton** — `GameVariants.Australia`, selectable + playable. *(Phase 0)* ✅
2. **`86d3kwtp5` [P8] Australia map** — the continent terrain grid (de-staggered 60×40, NZ removed). *(Phase 2, terrain)* ✅
3. **`86d3kwtjb` [P8] Australian Pioneers** — the 25-figure roster + reused perks + Federation Convention label. *(Phase 1d)* ✅

**Ready for development:**
4. **`86d3kwtrq` [P8] Australian nations + place-names** — British Australia (+ optional sandbox powers per `04_*`); First Nations nation entries; Australian colony-name list. *(Phase 1a)*
5. **`86d3kwtvc` [P8] Australian unit roster** — convicts / settlers / experts as `<unit-type>` data. *(Phase 1b)*
6. **`86d3kwty1` [P8] Australian goods + economy mapping** — wool/gold/coal/etc. *(Phase 1c)*
7. **`86d3kwu0c` [P8] UI reskin** — Civic Voice / First Nations / category display names etc. (§2). *(Phase 1e)*
8. **`86d3mm1xr` [P8] Six colony regions + start sites** — NSW/Vic/Qld/SA/Tas/WA named regions + start sites on the map (**the milestone's six-colony requirement**). *(Phase 2 completion)*
9. **`86d3mm25r` [P8] Australian buildings reskin** — Government Stores / Sheep Station / Wool Shed… onto the classic chain (`17_*`; novel buildings → Phase 4). *(Phase 1)*
10. **`86d3mm2fb` [P8] Historical 1788 start** — First Fleet party + variant start year (+ Australia-appropriate father-age thresholds). *(Phase 1/2)*

**Backlog (granular):**
11. **`86d3mm2nv` [P8] Retarget Pioneer stand-ins** (cotton→wool, silver→gold) — *waiting on* `86d3kwty1`.
12. **`86d3mm2ug` [P8] Colony start scenarios** (doc `04_*` Mode 3, six selectable starts) — *waiting on* `86d3mm1xr`.
13. **`86d3kwu2q` [P8] Australian art pass** — GPL-compatible sprites + Asset Register. *(Phase 3, can overlap)*

**Phase 4+ epics — FULLY DECOMPOSED into subtasks (Chris's call, 2026-07-08, overriding the rolling-wave default). Every subtask names its engine-reuse seams:**
14. **`86d3mm2xq` [EPIC P8-4a] Federation victory loop** (`05_*`/`06_*`) — **10 subtasks** `4a.1`–`4a.10`: ADR (4a.1, sign-off first) → settlement-maturity/region-activation oracles (4a.2) → per-region Federation Support/Anti-Federation on the SoL machinery (4a.3) → Convention Points + the six-phase state machine on the liberty-accrual pattern (4a.4) → data-driven constitutional clauses on the father-record shape (4a.5) → referendums + failure/retry with timed-modifier momentum, NSW quota + WA late entry as options (4a.6) → Commonwealth proclamation victory + the five grades on the score system (4a.7) → Imperial Pressure reframe of the monarch/REF (4a.8) → the Federation panel/report UI (4a.9) → pacing/balance + a variant soak (4a.10).
15. **`86d3mm31k` [EPIC P8-4b] First Nations redesign** (`15_*`/`16_*`/`18_*`) — **11 subtasks** `4b.1`–`4b.11`: ADR mapping Respect/Tension/Country Pressure onto the alarm engine (4b.1, sign-off) → the ~19 cultural groups as the variant's nations with homeland regions (4b.2) → the new Respect axis wired to existing trade/gift/combat/land seams (4b.3) → Tension = the extended alarm engine (4b.4) → Country Pressure reusing the ambient-footprint computation (4b.5) → the relationship-state oracle + its gates (4b.6) → the five agreements as data + Check/command pairs (4b.7) → Interpreter/Mediator/Commissioner units (the Mediator reuses the mission-relief seam) (4b.8) → frontier legitimacy ↔ Federation (4b.9) → knowledge-exchange + group event content (4b.10) → the diplomacy UI reframe (4b.11). Sensitive content review with Chris throughout.
16. **`86d3mm38w` [EPIC P8-4c] Event system + 1788–1901 catalog + campaign story** (`03_*`/`13_*`/`14_*`) — **11 subtasks** `4c.1`–`4c.11`: ADR extending the natural-disaster machinery pattern (4c.1) → the `<event-def>` schema/parser with a named-condition vocabulary (4c.2) → the turn-loop runtime (eligibility → seeded draw → choice dialog → resolve; classic defines zero events so it replays byte-identically) (4c.3) → the reused effect vocabulary (timed modifiers, grants, pool injections, support/respect deltas) (4c.4) → the forced setup events (Sydney Cove, Warrane) (4c.5) → three catalog batches: 1788–1830 / 1830–1872 / 1872–1901 (4c.6–4c.8) → Pioneer hard gates + linked events replacing the age-weight approximation (4c.9) → the six-era progression oracle + frequency bands (4c.10) → the binding sensitive-content review pass (4c.11).
17. **`86d3mm3cp` [EPIC P8-4d] Novel Pioneer effects + Phase-4 buildings/improvements** — **8 subtasks** `4d.1`–`4d.8`: un-gate land-unit movement modifiers (the one small engine change; Magellan byte-identical) (4d.1) → economy buildings (Freezing Works et al. on the `required-ability` seam) (4d.2) → civic buildings (League/Convention Halls, Harbour Battery, Agreement Council) (4d.3) → new improvements incl. the agreement-gated pair (4d.4) → Hargraves' gold reveal + surge (4d.5) → the bespoke election-effects wave (Phillip/Macarthur/Angas/Chisholm/Leichhardt) (4d.6) → the Democracy figures onto the Federation loop (4d.7) → the content balance pass (4d.8).

Plus **`86d3mmdh7` [P8] Pioneer portraits** (under the art task `86d3kwu2q`) — all 25 figures have public-domain portraits (pre-1955 deaths; NLA/state libraries/Commons), Asset-Register recorded; Barak's image needs a cultural-protocol check with Chris.

Sequence: 4–7 + 9 in parallel (all data in one spec — mind merge order), 8 then 10 (start sites feed the 1788 landfall), 11 after the goods, 13 overlapping; the epics after the skeleton is signed off — within Phase 4: 4a.1/4b.1/4c.1 (the three ADRs) first, then 4c's engine unblocks 4b.10/4c.6+ and the gates; 4d.7 last-ish (needs 4a). Each task keeps the transposability anchors (§1) intact.

---

## 5. Open decisions / risks to flag as we go

- **Structural-anchor renames** must stay display-only (don't rename `model.goods.bells` etc. — see §1). A lint/test that the Australia spec still defines the well-known ids is worth adding in Phase 0.
- **Art licensing** (Phase 3) is the biggest external-dependency risk — GPL-v2-compatible Australian/colonial sprites may be scarce; budget time for sourcing or placeholders.
- **First Nations content** is historically sensitive; the design corpus is deliberately careful (`15_*`, `19_*` tone rules). Keep it design-led and check with Chris before shipping user-facing First Nations text/art.
- **Federation victory (Phase 4)** is the only part that clearly needs new engine code; everything in the skeleton is data. Scope it as its own mini-epic when Phase 4 starts.
- **Scope creep:** the design corpus is vast (30+ units, 25+ buildings, a full event catalog). The skeleton deliberately ships a *reduced, playable* subset first; resist authoring the entire corpus before it's playable.

---

## 6. Definition of done for the first milestone

"Australian Federation" is selectable at New Game; it loads an authored six-colony Australian map; you can found colonies, work the Australian economy (wool/gold/etc.), recruit **Australian Pioneers** (the reskinned Founding Fathers) into the Federation Convention, and the UI reads in Australian terms — all on the proven engine, with L1/L2 (+L3 where UI) green in CI and the system docs updated. The deep Federation/First-Nations/event systems are explicitly **out of scope** for this milestone and tracked as Phase 4+.
