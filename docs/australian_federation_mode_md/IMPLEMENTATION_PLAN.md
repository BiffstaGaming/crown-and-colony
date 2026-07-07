# Australian Federation Mode — Implementation Plan

**Status:** Planning kickoff, 2026-07-07. Decomposes the `[EPIC P8] Australia variant` (ClickUp `86d3b3r7h`).
**Owner steer (Chris, 2026-07-07):**
- **First milestone = a *playable Australian skeleton*** — a selectable Australian mode you can play end-to-end, reusing every proven engine system, with Australian content + a UI reskin. The deep Australian mechanics (real Federation victory, First Nations redesign, events) are later phases.
- **Map = an *authored six-colony* map** (a hand-shaped continent with NSW / Victoria / Queensland / SA / Tasmania / WA placed deliberately).
- **"Australian Pioneers" = the Founding Fathers equivalent** — the perk-granting historical figures recruited into the Federation Convention. (The design corpus in this folder calls them "Historical Figures"; **in-game they are "Australian Pioneers"** per Chris. Not the tile-improver unit.)

This plan is the bridge between the **design corpus** (`00_README.md` … `21_Research_Sources.md` in this folder — the *what/why*) and the **code** (the *how*). Read the design corpus for the vision; read this for the build order.

> **Progress — 2026-07-07:** **Phase 0 (variant skeleton) + the Australia map are SHIPPED** (`86d3kwtf9`/`86d3kwtp5`). "Australian Federation" is selectable at New Game and plays on the **real Australia continent map** — a 30×80 grid converted from the FreeCol community map pack by Euzimar (GPL v2; all-standard terrain ids resolve 1:1 to our ruleset), embedded as `game/data/maps/australia.txt`. The spec is still a copy of classic (`game/data/rules/australia/`); the **content reskin** (Phase 1 — nations, units, goods, Australian Pioneers, labels) is next. So the "first Australia run-through" is launchable now (Australian terrain, classic content); the Australian *content* is being layered in.

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

## 4. First-milestone task breakdown (the "Playable Australian skeleton")

Proposed granular tasks (one work-block each: lands with tests + docs + CI green), to become ClickUp tasks under the P8 epic:

1. **`[P8] Variant skeleton`** — register `GameVariants.Australia` (copy-of-classic spec), csproj embed, `GameVariantTests`/`NewGameBridge` coverage. *(Phase 0)*
2. **`[P8] Australian nations + place-names`** — British Australia + First Nations nation entries; Australian colony-name list. *(Phase 1a)*
3. **`[P8] Australian unit roster`** — convicts / settlers / experts as `<unit-type>` data. *(Phase 1b)*
4. **`[P8] Australian goods + economy mapping`** — wool/gold/coal/etc. onto the production/market model. *(Phase 1c)*
5. **`[P8] Australian Pioneers (Founding-Father roster)`** — the historical figures + reused perks, by category. *(Phase 1d)*
6. **`[P8] UI reskin`** — Founding Fathers→Australian Pioneers, Liberty→Civic Voice, natives→First Nations display renames. *(Phase 1e)*
7. **`[P8] Authored six-colony australia.txt map`** — continent + regions + resources; `MapSource.Australia` + dropdown. *(Phase 2)*
8. **`[P8] Australian art pass (units/terrain)`** — GPL-compatible sprites + Asset Register entries. *(Phase 3, can overlap)*

Sequence: **1 first** (proves the pipeline), then 2–6 in parallel (all data in one spec — mind merge order), then 7, with 8 overlapping. Each keeps the transposability anchors (§1) intact.

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
