# Australian Federation Mode — Road to 100% (Polished)

**Created:** 2026-07-09. **Author:** Claude, from a full re-read of the 22-doc design corpus + a two-agent audit of the live code (requirements vs. current state).
**Purpose:** the complete, prioritised, sequenced plan to take Australian Mode from *"playable skeleton + reskin"* to a **polished, good-looking, faithful 100%** — the bar Chris set on 2026-07-09 ("a POLISHED GOOD LOOKING UI/GAMEPLAY").
**Relationship to [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md):** that doc is the *build order that got us here* (Phases 0–4). This doc is the *forward plan to 100%* and supersedes its "Phase 4+ / Remaining" sections. Every item lands the project way: tests at every required layer + same-commit docs + CI-green (see `docs/TESTING.md`, `docs/DOCUMENTATION.md`).

---

## 0. Status update — 2026-07-15 (verified code audit)

> **⚠ This roadmap (written 2026-07-09) is now substantially STALE — verify against code before acting on it.** A full three-agent audit of the live code/data/tests on 2026-07-15 found much of Workstreams 1, 3, and 4 already shipped by intervening sessions without this doc (or the kanban) being updated. Ground truth as of 2026-07-15:
>
> - **WS1 (Playable & Polished Core): DONE except 1.4.** 1.1a/1.1b (event popup + text), 1.2 (Commonwealth victory + honest addendum), 1.3 (variant art seam), 1.5 (Mode-3 colony select) are all shipped with tests. **1.4 (24 of 25 Pioneer portraits)** remains — asset-sourcing Chris vets (only `williamBarak.jpg` present).
> - **WS2 (Visual Identity): 2.7 UI-polish DONE;** 2.1–2.6/2.8 are art/asset work gated on the **art-approach decision (§5.2)** + licensing.
> - **WS3 (Federation Depth): 3.1/3.2/3.5/3.6/3.7/3.8 DONE.** 3.3 PARTIAL (drafting UI + 3-of-7 clauses; rest sign-off/system-gated). **3.4 (victory grades) needs WS5; 3.9 (AI Federation) is a design-judgment call** (Federation is human-only by design).
> - **WS4 (Living World): 4.1/4.2 PARTIAL.** The conditional/linked-event machinery + **ten** catalogue prerequisites are live (figure-gates + linked chains). **Unblocked remaining:** more non-sensitive WS4.2 gates, and **4.3** (founding-father `earliest_year` gates — still coarse age-weight). 4.4 needs ~28 backing systems; 4.5/4.6 are sensitive-content sign-off gated.
> - **WS5 (First Nations): only the 8 named cultural-group nations exist in data (5.2 partial).** Everything else is design-led + **ADR-022 / cultural-direction / ICIP-consultation gated (Chris).**
> - **WS6 (Content/Balance/Ship): 6.3 PARTIAL** (real Federation-era buildings/improvements shipped; pastoral set missing). **Unblocked:** 6.1 distinct goods, 6.2 economic units, 6.3 pastoral buildings, and **6.7's test slice — the Australian mode has ZERO L4 goldens / L5 soak coverage** (a binding-requirement gap). 6.4/6.6 need a playtest; 6.5 deferred.
>
> **Net:** the mode is near *engine-complete*. The highest-value remaining work is mostly **gated on Chris's decisions** (art approach, First Nations direction, playtest, sensitive-content sign-off, game name). The genuinely-unblocked autonomous work is: WS4.2/4.3 event & Pioneer depth, WS6.1/6.2/6.3 content, and the WS6.7 test-coverage gap. **TODO: reconcile the §1 table + all six §3 workstream item states + the ClickUp kanban to this audit.**

## 1. Where we are today (honest current state)

A two-agent audit (design-requirements sweep + live-code inventory) puts the mode at roughly:

| Layer | State | One-line reality |
|---|---|---|
| **GameLogic** | **~80%** | Rich Australia spec (25 Pioneers, 31 events, 1788–1901), a working Federation victory loop, an event engine, bespoke Pioneer effects. Missing: First Nations Respect/Tension, victory grades/clauses, deep event prerequisites, per-colony difficulty. |
| **Presentation** | **~70%** | Thorough *text* reskin threaded through ~60 panels on a **real custom parchment/wood theme** (`ColonyTheme.cs`, Cardo font — not raw Godot). **Two player-facing holes:** no event-choice UI, and the Commonwealth win reuses the wrong classic victory screen. Colony-select not wired. |
| **Australian art** | **~0%** | Every pixel is classic FreeCol (American/European colonial). Australian-ness is **text labels only**. 25 Pioneer portraits render blank; First Nations communities use FreeCol "Indian camp" art; units/goods/buildings are FreeCol sprites. `ColonyArt.Load()` hard-codes `res://assets/freecol/` — **not variant-aware**, so no Australian art *could* load even if it existed. |

**The headline:** the *engine* is most of the way there; the game **does not yet look Australian and has two holes that make the campaign feel unfinished** — its 31 narrative events are invisible (human choices silently auto-resolve to the default), and its climactic Federation win shows *"Every rival European power has been swept from the New World."* Fixing those, giving the game a visual identity, then completing the deep systems is the road to 100%.

### The two confirmed player-facing holes (top of the list)
1. **Event-choice UI is absent.** `Game.PendingEventOffer` / `ChooseEventOption` are built, public, and tested in GameLogic — but **no presentation file consumes them** (verified by grep across `game/`). A human's multi-choice historical dilemma (Macquarie's public works, the Gold Rush response, Eureka) silently resolves to option 1 at end of turn. The entire narrative layer is invisible.
2. **No Commonwealth victory screen.** `VictoryPanel.cs` has no Federation/Commonwealth/Referendum awareness. `Game.Winner` returns the human at `FederationPhase.Commonwealth`, but because the human's `PlayerType` isn't `Independent`, the screen prints the classic *"swept the rivals from the New World"* text (with 0 rivals) and the nation-id tail ("English") instead of "Commonwealth of Australia." The campaign payoff is broken.

---

## 2. Definition of 100% (what "done + polished" means)

Australian Mode is 100% when a player can:
- Start (choosing Mode 1 historical, or a Mode-3 colony, on a good-looking Australian map), and **every screen looks intentionally Australian** — Pioneer portraits, unit/building/goods art, flags, a title, an Australian visual theme — not FreeCol art with Australian captions.
- Play the **living 1788–1901 campaign**: era by era, **interactive** historical-event dilemmas with real choices; recruit Pioneers whose portraits and multi-clause perks match their design; watch the six colonies mature.
- Pursue the **Federation victory** through a polished, legible dashboard: per-colony support toward real targets, convention, constitutional clauses, referendums (incl. the NSW-quota and WA-late-entry cases), ending on a **proper Commonwealth victory screen** — graded, and carrying doc 19's historically-honest addendum.
- Engage **First Nations** through the designed Respect/Tension/Country/Agreements systems (not the classic conquest model), represented respectfully and reviewed to ICIP protocol.
- All of it **balanced** (doc 20 axes), **tone-compliant** (doc 19), **tested** at every required layer, **documented**, and **CI-green** — with the sensitive framings signed off by Chris and First Nations content reviewed.

---

## 3. The plan — six workstreams

Ordered by the recommended execution priority. Each item is a work-block (lands with tests + docs + CI-green). Items marked 🔒 are **sign-off / cultural-consultation gated** (cannot ship autonomously). Items marked 🎨 are **art/asset** (licensing-gated). Existing kanban ids are noted where they exist.

### Workstream 1 — Playable & Polished Core *(make what exists feel finished — highest impact, lowest risk)*
> Goal: close the two holes, unblock all art, and surface the systems already built. After this, the game *plays* like a finished product even before the deep systems land.

- **1.1 Event-choice popup UI — two parts.** *(Investigation finding: the 31 events carry only ids + a `recordHistory` outcome line — no name, prompt, or choice-labels in `EventDef`/`EventOption`/the spec. A panel over raw ids would read "event.merinoSheep / invest / ignore" — not polished. So:)*
  - **1.1a Event-presentation-text layer.** Add `name` + `prompt` to `<event-def>` and `label` to `<option>`, parsed onto `EventDef.Name`/`Prompt` + `EventOption.Label` with a **humanized-id fallback** (partial authoring degrades gracefully); author the text for the 31 events (reuse the existing `recordHistory` outcome text as the result line). L1 parse + fallback tests.
  - **1.1b Event-choice popup.** A styled dialog that consumes `PendingEventOffer` and calls `ChooseEventOption`, to doc 19's template (name → historical context → game context → 2–4 labelled choice buttons). Wire into `GameController.RefreshView` (same pattern as `EmigrationChoicePanel`). Sombre styling + no reward-popup for sensitive events (doc 13). **This turns the whole 31-event system on.** L3 + L1 wiring tests.
- **1.2 Commonwealth victory + failure screens.** Make `VictoryPanel` Federation-aware: the Commonwealth proclamation text (doc 19), the **historically-honest addendum** (mandatory), "Commonwealth of Australia" as the winner name, plus the referendum-failure and low-legitimacy-warning screens. L3 golden + L1.
- **1.3 Variant-aware art seam.** `ColonyArt` gains a variant art root + per-asset fallback to FreeCol (so missing Australian art degrades gracefully). **This is the prerequisite for every art item below** — pure plumbing, no assets yet. L1/L3.
- **1.4 🎨 25 Pioneer portraits.** Public-domain portraits (pre-1955 deaths — NLA / state libraries / Wikimedia Commons), Asset-Register recorded, loaded via 1.3. 🔒 **William Barak's image needs a cultural-protocol check with Chris.** Biggest single visual win (every Pioneer currently renders blank). (kanban `86d3mmdh7`)
- **1.5 Mode-3 colony-select wiring.** Surface `AustraliaColonyStart` in the New-Game dialog: an Australia-only "Starting colony" dropdown showing each colony's **difficulty tier + identity blurb** (doc 04 table). *(This is the M-D task; its data layer is trivial — the open question is what each difficulty tier does mechanically; see §5 Decisions.)* (kanban `86d3mm2ug`)

### Workstream 2 — Australian Visual Identity *(the "good-looking" core)*
> Goal: the game stops looking like FreeCol. This is the heart of Chris's ask and the largest external-dependency (licensing) risk — sequence art production incrementally on the 1.3 seam.

- **2.1 Art direction + Australian theme.** Define the visual language (the docs specify *tone*, not art direction — I'll propose one): a colonial-1788–1901 palette, a **Southern Cross / Federation motif**, six distinguishable colony-region colours, layered as an Australian variant of `ColonyTheme` (keep the parchment base; add Australian accents). A short written art-direction spec + the themed Godot resources.
- **2.2 Game title + menu/splash art.** Decide the name (doc 19 options — e.g. *From Colony to Commonwealth*) and produce a wordmark/logo + an Australian main-menu background (currently FreeCol `ui/map.jpg`). 🔒 name/trademark decision (see §5).
- **2.3 🎨 Nation flags & colony emblems.** British-Australia + six colony badges (Southern Cross family) for the HUD, reports, and Federation panel.
- **2.4 🎨 Australian unit / building / goods art.** GPL-v2-compatible sprites or tasteful recolours for the reskinned roster (Convict/Emancipist/Digger/Shepherd; Government Stores/Wool Shed/Telegraph Office/Federation halls; Wool/Gold icons). Incremental — highest-visibility first.
- **2.5 🎨 Australian terrain art pass.** Outback / bush / tropical-north / coast tiles so the continent reads as Australia, not America.
- **2.6 🎨🔒 First Nations community art.** Camp/community art replacing FreeCol "Indian village." **Cultural-consultation / ICIP gated** — do NOT produce before the doc-15/16 review; coordinate with Workstream 5.
- **2.7 UI polish pass.** Lift the panels from "functional-but-plain" to "designed": the `FederationPanel` (bare labels + default `ProgressBar`s → styled six-colony support gauges with emblems), the Pioneer-attained popup (doc 19 template), the event popup (2.1 styling), HUD era indicator, colony-maturity badges. Visual goldens.
- **2.8 Opening-cinematic visual pass.** Give the sober 1788→1901 beats supporting imagery (currently text-only).

### Workstream 3 — Federation Depth *(completes 4a — the victory system, faithfully)*
> Goal: the designed Federation system in full, with a legible UI. Builds on the working core loop.

- **3.1 🔒 ADR-021 sign-off.** The Federation-victory design decision awaits Chris's approval on its documented defaults.
- **3.2 Per-colony support targets.** Replace the uniform 40/50% with doc 05's real targets (NSW 57%, Qld 56%, SA 80%, Tas 94%, Vic 94%, WA 70%). L1/L2.
- **3.3 Constitutional clauses.** The 7 clauses (doc 05) as data on the father-record shape + a drafting UI that spends Convention Points; a constitution-progress meter. L1/L2 + L3.
- **3.4 Five victory grades + score breakdown.** Bare / Stable / Reform / Treaty / Economic Commonwealth, scored across doc 20's six categories, shown on the 1.2 victory screen as graded end-cards.
- **3.5 Anti-Federation Sentiment.** Track + display per colony with cause tags (doc 05).
- **3.6 NSW quota-failure + WA late-entry.** The two special referendum rules (doc 05), surfaced distinctly in the referendum UI.
- **3.7 Colony-maturity prerequisites.** Wire the Outpost/Township/Colonial-Town/Colonial-Capital gates (doc 06) to region activation + the Federation phases; maturity badges in the colony UI. (Oracles largely exist — `SettlementMaturityOracleTests` — but aren't wired to the loop.)
- **3.8 Six-phase mapping + era indicator.** Map the mechanical 5-state machine to doc 05's six named phases in the UI; add a HUD era/year indicator (doc 03/06).
- **3.9 AI pursues Federation.** So the campaign isn't human-only.

### Workstream 4 — Living World: Events & Pioneers *(completes 4c — conditional depth)*
> Goal: events fire on *gameplay state*, chain into stories, and Pioneers gate on era + prerequisites — the doc 01 §2 centrepiece. Depends on 1.1 (the event UI) to be player-visible.

- **4.1 EventDef schema + runtime extension.** Add the designed gate kinds — **figure-attained, relationship/tension, crisis, building/resource** — and **followup/linked-event chaining** (docs 13/14). Today only year + generic `<limit>` gates exist.
- **4.2 Flagship conditional events.** Re-author the marquee events with real prerequisites + 3 choices + alternative outcomes: Macquarie Governorship (the canonical example), Eureka-requires-Lalor + goldfield unrest, Gold-Rush-requires-Hargraves, Tenterfield-requires-Parkes, First-Contact-around-Warrane (coordinate w/ WS5). L2.
- **4.3 Pioneer hard date-gates + linked events (4c.9).** Replace the coarse 3-band age-weight approximation with each figure's `earliest_year` + typed prerequisites + linked-event chains (docs 07–12). L1/L2.
- **4.4 Full multi-clause Pioneer perks — *first slice shipped 2026-07-11*.** A full scope of 25 Pioneers × ~4 clauses found the clean, no-new-system subset was **four Federation-support second-clauses** — shipped: Angas +5 SA, Mary Lee +5 SA, Barton +3 NSW, Griffith +5 hardest colony (all Australia-only, VictoryFederation-gated, byte-identical; see [founding-fathers](../systems/founding-fathers.md) changelog). **The rest stay deferred on ~28 missing backing systems** — the biggest reused gaps, each unblocking a cluster of clauses: **terrain/tile-feature modifier scope** (Sturt river-move/trade, Kidman terrain move, Stuart arid move); **building/unit construction-cost & rush-buy modifier** (Angas/Macquarie/Reibey/Jervois/Ruse/Stuart); **new buildable tile-improvement framework** (Merino Stud, Stock Route, Goldfield, Overland Route Survey); **building-type-scoped goods / per-building points generation** (Mort/Kidman/Reibey); plus per-clause gaps: spoilage-disable (Mort), drought system (Kidman), distinct-Gold price volatility (Hargraves), distance-decay (Todd/Reibey), starvation-override (Phillip/Ruse), emancipist civic-penalty (Macquarie/Reibey), policy/reform + reform-event systems (many Social-Reform clauses), FN tension (Phillip; Barak sign-off-gated), failed-referendum recovery (Barton/Quick), per-turn convention-point generation (Barton/Quick), founding-distance (Flinders), resource-discovery-chance (Flinders/Leichhardt), expedition-supply (Leichhardt), militia/raid (Lalor/Jervois), victory-grade framework (Mary Lee). **DATA-blocked (not system-blocked — one-line spec adds once the reskin id lands):** free `sheep` good (Macarthur), `Government Stores`/`Telegraph Office`/`Federation League Hall` buildings, `Digger`/`Federation Campaigner`/`Family Settler` units. **Small follow-ups (not a missing "system"):** Leichhardt's scout-scoped +2 sight needs unit-type scope honoured in the sight fold; Barton "referendum callable >50" + Quick "convention −25%" are victory-gate live-reads → do with the Federation balance pass. L1/L2.
- **4.5 Truth-telling / historical log UI.** A ledger that records the sombre colonial-progress moments (dispossession, disease, frontier violence) per doc 15 principle 6.
- **4.6 🔒 Sensitive-content review pass (4c.11).** Binding review of all event text with Chris (Smallpox, Eureka, Chinese-migration, frontier events).

### Workstream 5 — First Nations *(4b — the deep, sensitive redesign)* 🔒
> Goal: replace the inherited classic-conquest native model with the designed Respect / Tension / Country / Agreements systems. **Historically sensitive; design-led; ICIP-governed; Chris sign-off + First Nations consultation throughout.** Largest single system; scope with Chris before starting (see §5).

- **5.1 🔒 ADR-022 sign-off + consultation/ICIP protocol.** Agree the approach and the review process before any user-facing content.
- **5.2 The ~19 cultural groups** (expand from the 8 named) with homeland regions (doc 16), each as a variant nation. 🔒
- **5.3 Respect / Tension / Country Pressure axes** on the extended alarm engine (doc 15/18).
- **5.4 Agreements (7 types) + Knowledge Exchange (8 skills)** as data + check/command pairs (doc 15).
- **5.5 Interpreter / Mediator / Federation-Commissioner units** (doc 18; Mediator reuses the mission-relief seam).
- **5.6 Relationship-state model + First Nations diplomacy UI** (7 states, doc 18) — replacing the native-alarm panels. 🎨🔒
- **5.7 Frontier legitimacy ↔ Federation** (doc 18) — ties WS5 to WS3's victory grades.
- **5.8 Replace conversion/missionary mechanics** with Agreement Council / Mediation (doc 15 §4).
- **5.9 Country visualisation on the map** — connection/law/routes, not tile-ownership (doc 15 principle 2). 🎨🔒

### Workstream 6 — Content, Balance & Ship
> Goal: fill remaining content, balance to the doc-20 targets, and ship.

- **6.1 Distinct goods** — promote the reused stand-ins (silver→**Gold**, cotton→**Wool**) and add the missing designed goods (Coal, Meat/Frozen Meat, Hides, Tallow, Copper, Sandalwood, Pearls, Newspapers, Telegraph Wire, Rails — doc 17), where they carry distinct mechanics.
- **6.2 Distinct units** — Interpreter/Mediator, Federation Campaigner, Statesman, Telegraph Worker, Drover/Shearer, etc. (doc 17), beyond the current display-reskins.
- **6.3 Novel buildings/improvements completion** — the remaining doc-17 set (Cattle Station, Shearing Shed as a distinct building, Pasture/Cattle Run/Waterhole/Eel-Trap improvements).
- **6.4 Balance pass (4d.8).** The 7 difficulty axes + Easy/Normal/Hard modifier table + anti-snowball + exploit-prevention (doc 20). Needs playtest.
- **6.5 Mode 2 sandbox** *(currently deferred per Chris — include only if reinstated; see §5).* Rival powers + per-power traits + alternate victory labels (doc 04).
- **6.6 Playtest acceptance** — the doc-20 six playtest questions as the acceptance gate.
- **6.7 Release polish** — game name/trademark finalised, Asset Register complete, Australia added to the L5 nightly soak + visual-golden set, QA report.

---

## 4. Recommended sequence & rationale

**Polish-first, then depth.** A shallow-but-polished game beats a deep-but-broken one — and Workstream 1 is low-risk, high-impact, and unblocks everything visual.

1. **Workstream 1** (core holes + art seam + portraits + colony-select) — do this first; it makes the game *feel* finished and turns on the already-built event + Federation systems.
2. **Workstream 2** in parallel with 3/4 once the 1.3 art seam lands — art production is long-lead and licensing-gated, so start sourcing early and land it incrementally.
3. **Workstream 3** (Federation depth) — completes the campaign's spine; mostly data + UI on a working loop.
4. **Workstream 4** (events/Pioneers) — needs 1.1's event UI to be visible; makes the world feel alive.
5. **Workstream 5** (First Nations) — start the 5.1 ADR/consultation early (long-lead, gated) but sequence the build deliberately and design-led; it's the largest and most sensitive.
6. **Workstream 6** (content/balance/ship) — continuous, with the balance + playtest pass last.

Dependencies: 1.3 → all of WS2; 1.1 → WS4 visibility; WS3.7 needs the maturity oracles (exist); WS5.7 ties into WS3.4 grades; WS4.2's Warrane event needs WS5's FN state.

---

## 5. Decisions needed from Chris (these gate execution)

1. **Priority order** — confirm *polish-first* (WS1 → WS2 → depth), or reweight.
2. **Art approach** — the biggest external dependency. Options: (a) source public-domain / GPL-compatible art piece-by-piece (slow, free, licence-safe — my default); (b) commission/generate a cohesive set (needs a licence-clean pipeline); (c) tasteful FreeCol recolours as an interim while sourcing. Portraits are easy (public-domain); units/terrain/FN art are the hard part.
3. **First Nations depth (WS5)** — pursue the full redesign now, defer it behind the rest, or ship a reduced-but-respectful interim? It's the largest system and needs cultural consultation — how do you want to run that review?
4. **Per-colony difficulty (WS1.5 / M-D)** — what should each tier *do* mechanically (docs give tiers + flavour, no magnitudes)? e.g. "Hard" (Tas/WA) = fewer starting resources / harsher isolation; "low Federation support" (Qld/WA) = a starting-support penalty or higher regional target.
5. **Game name / trademark** (WS2.2) — pick from doc 19's options (or your own) so the title/logo work can start; also the eventual non-"Colonization" release name.
6. **Mode 2 sandbox** (WS6.5) — stays dropped (Mode-1-only, as chosen), or reinstate as a later selectable mode?
7. **Sign-offs pending** — ADR-021 (Federation), ADR-022 (First Nations), and the already-merged sensitive framings (intro beats, tribe names, Eureka/Shearers events) await your review.

---

## 6. Cross-cutting (every workstream)

- **Testing** (`docs/TESTING.md`): L1/L2 for logic, L3 for UI, L4 visual goldens for the new screens/theme, L5 nightly soak — Australia added to the soak set in 6.7.
- **Docs** (`docs/DOCUMENTATION.md`, no-drift): each item updates its system doc (both layers) + changelog in the same commit.
- **Determinism** (ADR-009): all new randomness through the injected RNG; classic stays byte-identical.
- **Licensing**: every asset GPL-v2-compatible, Asset-Register recorded (source + licence).
- **Tone** (doc 19) + **ICIP** (docs 15/16): binding on all user-facing text and First Nations content.
- **ClickUp**: decompose each workstream into kanban tasks as its turn comes (rolling-wave); keep the Session Log current.

---

*This plan is a living document — revise as decisions land and items ship.*
