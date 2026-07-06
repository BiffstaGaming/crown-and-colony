# Original-Colonization (1994) Fidelity Review — online-research pass

**Date:** 2026-07-06 · **Method:** six parallel research agents studied the *original 1994 Sid Meier's Colonization*
from online sources (the game manual, community wikis, strategy forums, the FreeCol "What Would Col1 Do?" wiki), then
cross-referenced each claim against [`feature-parity.md`](feature-parity.md) and the live C# in `game/src/GameLogic/`.

## Why this pass exists (and how it differs from `feature-parity.md`)

The existing [feature-parity.md](feature-parity.md) (2026-06-26, 968 Yes / 20 Partial / 27 No across 1015 features) is
thorough but was built **entirely by reading the FreeCol source and our repo — with no online research.** This pass adds
the missing axis: **how the actual 1994 game behaved, per the manual and community record.**

### The meta-finding

> **Crown & Colony is an extremely faithful *FreeCol* port. But FreeCol's own "classic" ruleset diverges from the real
> 1994 game in specific numbers and mechanics — and wherever it does, C&C inherited FreeCol's value, not Col1's.**

A code-only audit structurally *cannot* see this: it compares us to FreeCol and finds "match ✓". Only checking against
the original game surfaces the delta. This is the payoff of the online pass. It found **~14 genuine gameplay divergences,
8 factual errors in the parity doc itself, and several "do not "fix" this" cautions** — while confirming the large
majority of mechanics are genuinely faithful.

### ⚠️ Sourcing caveat — read before acting

Several primary sources (archive.org manual, Fandom, StrategyWiki, civfanatics, GameFAQs) were intermittently
Cloudflare/403-blocked to the fetch tool. Some findings rest on **search-result snippets**, not fully-read primary text.
Confidence is marked per item. Items with **2+ independent corroborating sources** (tax cap 75%, custom-house 100/50 rule,
boycott ×500, treasure cut 50–70%) are solid; single-snippet items (Dutch recovery, exact recruit formula) are weaker.
**Verify any high-impact *code* change against the actual manual before committing.** Per licensing (ADR-007), the
original binaries/data remain off-limits — we can only ever be "faithful to the community's best reconstruction."

---

## Tier 1 — Genuine gameplay divergences (decide + act)

Ordered by player impact. "Effort" is rough: **Data** = ruleset/option value change, no logic; **Code** = new logic/UI.

| # | Mechanic | Original Col1 | Crown & Colony now | Impact | Conf. | Effort | Recommendation |
|---|---|---|---|:--:|:--:|:--:|---|
| 1 | **Treasure King's-cut** | Flat, **difficulty-scaled 50% → 70%** (Discoverer→Viceroy); galleon/Cortés = free | Cut = **current tax rate** → early treasure cashes almost fee-free. Matches *neither* Col1 nor FreeCol (flat 60%) | **High** | High | Data | Replace with difficulty-driven 50→70% fee; keep galleon/Cortés free path. Fix parity row 304. |
| 2 | **Native first contact** | Chief offers **peace + small land grant**; **reject → immediate war** | One-way audience: reveal tiles + flat 10–80 gold gift. No accept/reject, no war branch, no land | **High** | Med | Code | Model Accept/Reject (reject→war) + starting land parcel; keep tales-reveal, drop auto-gold. First native interaction every game. |
| 3 | **"+50% attacking a colony"** | Bonus for **all European regular troops** assaulting a colony (on top of the universal +50%) | Gated to **REF only** (`IsRefUnit`) | Med | High | Code | Apply colony-assault +50% to any land unit attacking a settlement. Fix parity row 212 (Col1="No" is wrong). |
| 4 | **Tax cap** | King raises sales tax up to **75%** | Capped at **65%** (FreeCol `maximumTax`) | Med | High* | Data | Raise classic `maximumTax` to 75, or document 65 as a deliberate FreeCol-following choice. *Cross-corroborated by two independent agents.* |
| 5 | **Building learning-by-doing** | A free colonist working **any** job — incl. a distillery/weaver/blacksmith — can become that building's master ("learned through experience") | Only **tile** workers self-upgrade; building experts can never be self-taught | Med | High | Code | Implement building-worker experience, or accept the omission. **Parity row 152's note is factually wrong** (claims FreeCol's table covers building work — it covers 9 outdoor experts only). |
| 6 | **Custom-house export logic** | Sells a good **only when stock ≥ 100**, always **leaves 50**; then waits (hysteresis) | Sells everything above a configurable level **every turn** (trickle) | Med | Med-High | Data/Code | Add a classic "100-arm / 50-floor" mode. Chunky vs trickle sales give different income & price curves. |
| 7 | **Overcrowding penalty** | Single "bad government" tier: **−1** to all production (at 6–10 Tories by difficulty) | FreeCol's **two** tiers: −1, then **−2** at >10 Tories | Med | Med-High | Data | For strict fidelity cap the penalty at −1 (data-only limit change). Fix parity row 485 (claims Col1 had a −2 tier). |
| 8 | **End-game score values** | colonist +2, skilled +4, father +5, +1/1000 gold, +1/rebel-sentiment-point, **−(difficulty+1)** per razed native settlement | FreeCol's table: colonist **+3**, settlement penalty pinned **−5**, nation-destroy −50, + ship/soldier points Col1 never had | Med | High | Data | Add a Col1 scoring profile; keep FreeCol's for its ruleset. Score is the whole endgame. Flag parity rows 1071–1078. |
| 9 | **Continental Army muster** | Mobilized veteran fraction **scales with rebel sentiment** (100% SoL → 100% mobilize) | FreeCol per-colony cap `(units+2)·(SoL−50)/100`; **zero below 50%** | Med | Med | Data/Code | Consider Col1-faithful muster (⌈SoL% × veterans⌉) as a ruleset option. Sets your opening army in the war you built toward. |
| 10 | **Fixed end-year auto-end** | Game **auto-ends & scores** in **1800** (at peace) or **1850** (still fighting the War of Independence) | **No max-turn cap** — the calendar just advances forever; never force-scores | Med | High | Code | Implement the 1800/1850 auto-end → score screen (P7). Years are canonical, not approximate. |
| 11 | **Native buy-back volume** | You may buy back only up to what you **just sold**, capped by carrier: **wagon ≤100, ship ≤25** per 100 sold | Offers the settlement's **whole store** regardless of any prior sale; ship = wagon | Med | High | Code | Track last-sold qty per session; cap buy at min(sold, store) with ship (25) vs wagon (100) multiplier. |
| 12 | **Boycott-lift cost** | ≈ **ask price × 500** (Tools 2500, Coats 4000, Cloth 5500 observed) | **sale price × 300** ("classic factor"), keyed off *bid* not *ask* | Med | Med-High | Data | Re-derive arrears factor toward ask×500. Our boycotts are ~40% cheaper to clear than the original's. |
| 13 | **Recruit price on the docks** | **Three distinct per-slot fees**, varying by **unit type**, falling as crosses accrue | **One shared price** for all three slots; not type-dependent; flat +30 escalation | Med | Med | Code | Keep FreeCol formula as baseline; document the delta. Per-slot quote only if faithful recruitment feel matters. |
| 14 | **De Soto extended sight** | **All** units (incl. ships & scouts) get sight **2** | **+1 to land units only** (FreeCol `navalUnit=false`) — ships get nothing | Med | High | Data/Code | Lift naval sight too (or set land+naval to ≥2). Deliberate deviation from Col1 even though it matches FreeCol. |

---

## Tier 2 — Minor value-mismatches (document; low priority to change)

| Mechanic | Original Col1 | C&C now | Conf. | Note |
|---|---|---|:--:|---|
| Road bonus goods | Roads boost **ore, fur, timber** only | +2 furs/+2 lumber/+1 ore/**+1 silver** (FreeCol classic) | Med | +1 silver is a FreeCol addition beyond the manual. |
| Warehouse food cap | Stored food capped at **199**; converts at 200 | Grows at ≥200, food exempt from overflow (can transiently exceed 199) | High | Invisible in normal play. |
| Native trade pricing | **Capital** pays a price premium; single sale **≲1000 gold** soft cap | No capital premium, no cap (FreeCol formula) | Med | Economic tuning. |
| Drydock repair speed | A drydock **halves** repair time | Flat 5-turn timer regardless of drydock vs Europe | Med | Drydock already saves the Atlantic trip; speed-up is secondary. |
| Rebel-sentiment score | +1 per **point of rebel sentiment** | Σ banked **liberty bells** (different quantity) | Med | Part of the scoring-profile item (#8). |
| Price recovery model | **Per-good** rise/fall rates (silver recovers slow, ore fast) | Uniform `turn/10` drift toward baseline | Med | Relative good economics drift from the original. |
| Dutch advantage | Reduced trade impact **and faster price recovery** | Only trade impact halved (`tradeBonus −50%`) | Low | Recovery half is the smaller effect. |
| Man-o'-War availability | Appears in New-World waters **only during the War of Independence** | Verify no pre-war man-o'-war is obtainable (SUPPORT_SEA should grant a frigate) | Low | Needs a quick code check. |
| Native camp food loop | Camps **demand** food; advanced tribes **gift** up to 75 food to starving colonies | Food branch flagged "unreachable" in our doc; tier split absent | Med | Blocked partly by our abstract food model; revisit when it matures. |

---

## Tier 3 — Factual errors in `feature-parity.md` (fix regardless of gameplay decisions)

These are wrong **Col1-column claims or notes** in the parity doc — independent of whether we change any behavior.

1. **Row 41 (America map):** Col1 = "No" is **wrong**. Col1 shipped a "Start Game in AMERICA" real-geography option
   alongside "New World" and "Customize." → Col1 = **Yes** (note our shape derives from FreeCol's, a fidelity-of-shape
   deviation, not a missing feature).
2. **Row 152 (building experience):** the note claims FreeCol's experience table "covers building work
   (masterSugar/Cotton/Tobacco)." Those are **planters who work tiles.** FreeCol self-teaches 9 outdoor experts only.
   Neither FreeCol nor we self-teach building experts. → correct the note.
3. **Row 212 (colony-assault +50%):** Col1 = "No" is **wrong** — the bonus applied to all European regulars, not just
   the REF. → Col1 = **Yes**.
4. **Row 304 (treasure cut):** note says "deliberate Col1 model." It matches **neither** Col1 (difficulty 50–70%) **nor**
   FreeCol (flat 60%). → correct the note.
5. **Row 485 (overcrowding):** asserts Col1 had "an equivalent steep [−2] penalty." Col1's penalty is **−1 only.** → fix.
6. **Row 530 (custom-house smuggling):** Col1 = "No / could not smuggle boycotted goods" **contradicts row 555**
   ("Yes / matches classic") — and 555 is correct. Col1's custom house **did** auto-sell boycotted goods. → row 530 Col1 = **Yes**.
7. **Rows 541–542 (price recovery):** label Col1 = "No (single static baseline)." Col1 **had** price recovery via
   per-good rise/fall indicators. → Col1 = **Yes**; note the nuance (per-good rates vs our uniform drift).
8. **Rows 1071–1078 (scoring):** "faithful line-for-line to updateScore" is faithful to **FreeCol**, not Col1. → note the
   colonist/settlement point values differ from the original (see Tier 1 #8).

---

## Tier 4 — Confirmed faithful (do **not** "fix" toward folklore)

The online pass positively verified these against original sources — flag them so a future pass doesn't "discover" and
break them:

- **Combat:** win formula `attack/(attack+defence)`; fortify +50 / stockade +100 / fort +150 / fortress +200%;
  artillery-in-the-open −75%; artillery 7/5 & damaged 5/3; dragoon→soldier→colonist demotion; Washington auto-promote;
  naval damage-vs-sink + nearest-drydock-else-Europe repair; artillery-vs-raid +100%. **Combat stacking is
  multiplicative** (matches the disassembly) — **do not switch to additive.**
- **Economy:** expert bonus is **additive** for farmer/fisherman (+2/+3) but **×2** for other raw/manufactured — do not
  make it uniform ×2. Servant/criminal penalty is **manufactured-goods-only** (raw gathering is full-rate). Cargo caps
  (caravel 2 / merchantman 4 / galleon 6 / wagon 2), Fountain of Youth = 8, 3-recruit pool, Brewster, Boston Tea Party,
  Fugger lifts boycotts, market bounds (sell ≥1 / buy ≤19), starting price categories — all faithful.
- **Independence:** 50% Sons-of-Liberty gate is the **unweighted national average** — confirmed correct (upgrade the
  doc's "Partial" framing to confirmed).
- **Founding Fathers (numbers):** Jefferson +50% bells, Magellan +1 naval move, Hudson +100% fur, Bolívar +20% SoL,
  Adam Smith factory tier, Fugger one-time boycott clear, Minuit free land, Revere colonist-grabs-muskets,
  De las Casas converts→free, Pocahontas tension reset/half — all match.

### Cautions — mechanics that look like gaps but must NOT be imported

- ⚠️ **REF composition/growth numbers** circulating online (8 Regulars / 4 Dragoons / 4 Artillery / 4 Men-o'-War,
  navy-first growth, `revolutionEuropeUnitThreshold`) are **Civ IV: Colonization (2008)** — a *different game.* The 1994
  manual only says the REF "grows." **Do not re-tune our FreeCol-derived REF toward the Civ4Col figures.**
- ⚠️ **Lost City Rumour odds & gold formulas** were never published for Col1. Our port of FreeCol's `dx`-model is the
  best available; treat it as unverifiable, not wrong. Add a fidelity caveat to `lost-city-rumours.md` so "feels off"
  reports aren't chased as bugs.
- ⚠️ **Hidden difficulty / privateer combat modifiers** are anecdotal forum lore — do not add hidden modifiers (it would
  violate our transparent, testable combat design).
- ⚠️ **"Escape to reroll" the Founding-Father choice** is a version-specific exploit — do not implement.
- ⚠️ **Scout terrain-blocked line-of-sight** could not be confirmed for Col1 (it's a Civ IV rule) — do not act on it.

---

## Suggested next actions

### Triage decisions (2026-07-06, Chris)

- **Change — approved:** native first contact (`86d3kgbnq`), colony-assault +50% for all Europeans
  (`86d3kgbp3` — **✅ shipped 2026-07-06**), building learning-by-doing (`86d3kgbpd` — **✅ shipped 2026-07-06, save v70**).
- **Do not change — keep current/FreeCol behaviour as an accepted deviation (tasks → Cancelled):** treasure King's-cut
  (`86d3kgbna`), Col1 scoring profile (`86d3kgbq0`), 1800/1850 auto-end (`86d3kgbrc`).
- **Still to triage:** custom-house classic mode (`86d3kgbpn`), Continental muster (`86d3kgbr0`), native buy-back
  (`86d3kgbrw`), quick ruleset values (`86d3kgbtj`), document-deviations (`86d3kgbu2`).
- **All 8 Tier-3 parity-doc corrections stand regardless** — they fix factual errors in the doc, not behaviour.

1. **Doc-only (safe, no-drift):** apply the Tier 3 corrections to `feature-parity.md`; add the Tier 4 cautions to the
   relevant system docs so they're not re-litigated.
2. **Low-effort / high-fidelity-payoff (Data changes):** treasure cut (#1), tax cap (#4), overcrowding −1 (#7), score
   profile (#8), custom-house classic mode (#6). Each is a ruleset/option value with tests — the highest ROI cluster.
3. **Higher-effort (Code):** native first contact accept/reject (#2), building learning-by-doing (#5), end-year auto-end +
   score screen (#10), native buy-back model (#11). Scope individually.
4. **Fidelity-target decision (Chris):** these divergences are all "faithful to FreeCol, not to Col1." The project's
   north-star is faithful *Colonization*, which argues for the changes — but FreeCol's deviations are often deliberate
   rebalances. Decide strict-Col1 vs FreeCol-faithful-and-documented, per item or as policy.

_This is a research snapshot; behavior is unchanged. Findings feed the kanban (list `901615382059`) once triaged._
