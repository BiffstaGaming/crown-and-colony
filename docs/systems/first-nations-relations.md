# System: First Nations relations (Australian variant)

| | |
|---|---|
| **Status** | In development — mechanics implemented (Respect / Country Pressure / Tension + the seven relationship states) and the eight peoples' encyclopedia text approved and shipped. Agreements, diplomatic units, the relationship UI and all imagery remain unbuilt. |
| **Last verified** | 2026-07-26 (WS5.3 mechanics + the approved encyclopedia text; full L1/L2 3035 green; determinism soak 6/6 green; classic byte-identical) |
| **Code** | `game/src/GameLogic/GameSession/Game.FirstNations.cs`; hooks in `Game.cs` (`ClaimLandByPaying` / `ClaimLandByStealing` / `EndTurn`); persistence in `SaveGame.cs` (v76); text in `NativeNationType.cs` + `Ruleset.cs` parse + `ColopediaPanel.cs` |
| **Tests** | `game/tests/GameLogic.Tests/GameSession/FirstNationsRelationsTests.cs` (L1), `Specification/AustralianContentTests.cs` (L1, the text), `game/presentation/tests/ColopediaPanelTests.cs` (L3, the text on screen) |
| **Design docs** | `docs/australian_federation_mode_md/15_First_Nations_Design_Principles.md`, `16_First_Nations_Cultural_Groups.md`, `18_Diplomacy_Tension_Respect_Mechanics.md` |
| **FreeCol reference** | `ServerPlayer.csNewTurn` (ambient alarm), `Tension` / `csClaimLand` — the *inherited* single-axis model this system extends, not replaces wholesale |
| **Related systems** | [natives](natives.md) (the inherited alarm engine this builds on), [federation-victory](federation-victory.md) (the Treaty grade this will unlock), [save-load](save-load.md) |

> **Scope and cultural governance.** This document covers the **mechanics layer** (§1–3) and the **encyclopedia text**
> (§3a). The text was drafted, reviewed by Chris and approved on 2026-07-26 under his standing decision that Claude
> drafts and he reviews before anything ships — the provenance record is
> [FIRST_NATIONS_TEXT_FOR_REVIEW.md](../australian_federation_mode_md/FIRST_NATIONS_TEXT_FOR_REVIEW.md).
> **Still not built, and still requiring review before they are:** imagery of any kind, the further peoples from doc 16,
> the resistance-event chains, the Agreements/Knowledge-Exchange content, and the relationship UI. Every one of those
> goes through the same draft → review → ship loop; none is written yet.

## 1. How it works (plain English)

The classic game gives you one number per tribe: **alarm**. It only ever goes up when you build near them, and you can
hold it down almost indefinitely by parking a missionary. That is a conquest-era model, and it makes the Australian
campaign read badly: you can occupy a people's Country for a century and, as long as no soldier stands next to them,
nothing registers.

This replaces it with **three separate things**, because they genuinely are separate:

**Respect** — what they think of *you*, based on how you have actually behaved. It starts at 35 out of 100 when you
first meet: wary, not hostile. Paying for land earns a little (+5). Taking land by force destroys a lot (−12). That
asymmetry is deliberate — trust is much easier to break than to build, and the game should say so.

**Country Pressure** — how heavily your presence sits on their Country, right now. It counts the population of your
colonies near their communities and the strength of your armed units nearby, out of 100. It is worked out fresh every
time it's read, which means it goes **down** if you pull back. You are never permanently condemned by a colony you
later abandoned.

**Tension** — the risk of conflict. This is the old alarm number, kept and reused (so everything already built on it
still works), but now it is *fed* by the other two: every turn, a quarter of your Country Pressure is added as tension,
plus an extra penalty if Respect has fallen to 25 or below. **This is the part that changes how the game plays** — your
footprint keeps pressing whether or not you have a soldier there, and a relationship you have wrecked keeps getting
worse until you do something about it. A missionary alone no longer holds the line.

Together those three decide the **relationship**, which is one of seven states:

| State | When |
|---|---|
| Unknown | You have not met them. |
| Cautious Contact | You have met, but trust is still low. |
| Trade Relationship | Respect 45+, Tension under 50. |
| Agreement Relationship | Respect 60+, Tension under 40 — this is the gate the future Agreements system will need. |
| Trusted Relationship | Respect 80+, Tension under 30. |
| Strained | Tension 60+. |
| Hostile | Tension 80+. |

**Tension outranks Respect.** If you push a people to Strained or Hostile, that is where the relationship sits no matter
how much goodwill you banked earlier. Two axes exist precisely so that "they used to like me" cannot paper over "I am
currently pressing on them."

### Worked example

You meet the people whose Country surrounds your second colony. Respect starts at **35** → *Cautious Contact*.

- You need a tile they own. You pay for it: Respect **40**.
- Your colony grows to 12 and you station two soldiers. Country Pressure climbs to about **30**, so each turn adds
  roughly 7 tension. After a dozen turns Tension is drifting toward **50** — the relationship slips to *Strained* even
  though you never attacked anyone. The game is telling you the footprint itself is the problem.
- You pull the soldiers back. Country Pressure falls, the per-turn drag shrinks, and the existing alarm decay starts
  winning again. Tension eases and you return to *Cautious Contact*.
- Later you seize a tile rather than paying. Respect drops **40 → 28**. You are now close to the low-Respect penalty
  (25), which would add a further 10 tension a turn on top of the footprint — a spiral that is hard to stop.

## 2. Detailed rules

- **Respect** is nation-scoped (by native nation type id), 0–100, persisted (save v76). Seeded at
  `FirstNationsRespectBaseline` (35) on first movement or on recorded contact. `RecordFirstNationsContact` is
  idempotent — re-contact never resets a damaged record.
- **Contact** is true when Respect is on record **or** the human's explored fog covers one of that people's
  settlements. No separate persisted flag.
- **Country Pressure** is derived, 0–100: for each of that people's settlements, sum the population of human colonies
  and the offence of human non-naval units within `settlement claimable radius + NativeAlarmRadius` (the same reach the
  ambient-alarm pass uses), scaled against `CountryPressureFootprintForMax` (120).
- **Tension** is the existing nation-level alarm channel rescaled to 0–100 (`alarm × 100 / MaxTension`, MaxTension =
  1100). The alarm engine is unchanged.
- **Per-turn coupling** (`ApplyFirstNationsPressureTension`, called from `EndTurn` immediately after
  `ApplyAmbientNativeAlarm`): for each *contacted* people, `delta = CountryPressure × 25% (+10 if Respect ≤ 25)`,
  scaled back onto the 0–1100 alarm scale and applied to every one of that people's settlements. Uncontacted peoples
  and zero-pressure peoples are skipped, so the pass never invents free-floating tension.
- **Relationship state** is evaluated Tension-first (Hostile ≥ 80, Strained ≥ 60), then the Respect bands.

## 3. Technical design

`Game.FirstNations.cs` (a `Game` partial) holds the whole system. Public read oracles (ADR-006, all pure and RNG-free):
`FirstNationsRespectFor`, `CountryPressureFor`, `FirstNationsTensionFor`, `HasContactedFirstNations`,
`RelationshipWithFirstNations`, and `FirstNationsSummary()` — the per-nation row shape a future WS5.6 panel will draw.

**Why Respect is persisted and the other axes are not.** Respect is a record of conduct: nothing on the board says
whether you paid for that tile in 1804. Country Pressure is a property of the *current* board and is therefore derived —
which also gives the design's "withdrawal relieves pressure" behaviour for free and costs no save state. Tension already
had storage.

**Classic byte-stability (ADR-009).** Every entry point returns early unless `Ruleset.VictoryFederation` is set — the
same Australian-variant gate the Federation loop and Anti-Federation Sentiment use. In classic: no Respect accrues, the
land-claim hooks are inert, `ApplyFirstNationsPressureTension` is a no-op, `FirstNationsRespect` stays empty and is
omitted from the save. Verified by `Classic_HasNoRelationshipModel_AndOmitsTheSaveToken`,
`Classic_LandSeizure_MovesAlarmButBanksNoRespect` and the determinism soak.

**Known gaps / follow-ups (deliberate, not oversights):**
- Country Pressure does not yet count roads, rail, telegraph, pastoral runs or mines (doc 18 lists them). Each needs a
  tile-improvement *ownership* read the map model does not have — the same gap documented on the ambient-alarm
  tile-control branch in [natives](natives.md).
- Respect currently moves on two hooks (land paid / land seized). Doc 18's other sources — fair trade, gifts during
  hardship, honoured agreements, violence, livestock damage, forced movement, armed intimidation — are follow-ups;
  several depend on Agreements (WS5.4) existing at all.
- The `CommonwealthScorecard` First Nations category still reads raw tension (see
  [federation-victory](federation-victory.md)); it should move onto `RelationshipWithFirstNations` once Agreements land,
  which is also what unlocks the withheld **Treaty Commonwealth** grade.

## 3a. The encyclopedia text (content)

Each of the eight First Nations peoples in the Australian ruleset carries player-facing text — a `country` (where their
Country is, in plain words) and a `description` (a ~50-word encyclopedia entry). Before this they had **no entry at
all**: the Colopedia's Nations tab walked only the European powers, so a people appeared in the world with a bare label
while every building, unit and good in the game had a proper entry.

`NativeNationType` gained three optional fields — `DisplayName`, `Country`, `Description` — parsed from optional
`display-name` / `country` / `description` attributes on `<indian-nation-type>`. **Classic authors none**, so classic
nation types carry empty strings and the Colopedia skips the whole section (no empty heading); this is guarded by
`ClassicTribes_CarryNoEncyclopediaText` and by an L3 test.

`DisplayName` exists because ruleset **ids must stay ASCII** while a people's own spelling need not: the id is `yolngu`
and the name shown is **Yolŋu**. `PlayerFacingName` returns the authored name when present, else the title-cased short
name (what every classic tribe does).

**Provenance of the wording.** The text was drafted, put to Chris for review, and approved on 2026-07-26 — see
[FIRST_NATIONS_TEXT_FOR_REVIEW.md](../australian_federation_mode_md/FIRST_NATIONS_TEXT_FOR_REVIEW.md), which records
both the entries and the rules the draft held to: documented public facts only; present tense for continuing existence;
**no sacred, ceremonial or restricted material**; no invented individuals or dialogue; colonisation stated plainly but
not dwelt on. Deliberately **not** included: imagery; the further peoples from doc 16 who are not in the game data; and
doc 16's resistance-event chains, which dramatise frontier killing and need per-item sign-off rather than bundling.

## 4. Verification

| Layer | Required? | Tests | Status |
|---|---|---|---|
| L1 Unit | Always | `FirstNationsRelationsTests` — classic has no model and omits the save token; classic land seizure still moves alarm but banks no Respect; Respect seeds at the baseline, clamps 0–100, and re-contact never launders damaged trust; seizing costs more than paying earns; the land hooks move Respect by the documented amounts; Country Pressure rises with the footprint and **falls when it withdraws**; the doc-18 Respect bands resolve to their states; Tension outranks Respect (Strained → Hostile at full Respect); Country Pressure raises Tension per turn; an unpressed people gains none; Respect round-trips a v76 save; an Australia game with no contact omits the token. | ✅ 18 green |
| L2 Scenario | Yes | Covered by the determinism soak (classic byte-identical + the Australia soak seeds exercise the per-turn pass). | ✅ 6/6 green |
| L3 Interaction | Not yet | No UI — the relationship panel is WS5.6, consultation-gated. | n/a |
| L4 Visual | Not yet | As above. | n/a |

## 5. Open issues / TODO

- [ ] **ADR-022 sign-off (Chris).** The approach, and the consultation/ICIP review process for all WS5 *content*.
- [ ] WS5.4 Agreements (7 types) + Knowledge Exchange — the payoff this mechanics layer exists to support.
- [ ] WS5.2 expand from the 8 named cultural groups to the designed ~19 (content — gated).
- [ ] WS5.5 Interpreter / Mediator / Federation Commissioner units.
- [ ] WS5.6 relationship UI (gated), WS5.7 frontier legitimacy ↔ Federation, WS5.8 replace conversion/missionary
      mechanics with Agreement Council / Mediation, WS5.9 Country visualisation.
- [ ] Broaden the Respect hooks and the Country Pressure sources (see §3).

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-07-26 | **First Nations encyclopedia text (content, approved by Chris).** The eight peoples in the Australian ruleset gained `country` + `description` text and, where needed, an authored `display-name` (id `yolngu` → **Yolŋu**), surfaced as a new "First Nations" section on the Colopedia Nations tab — they previously were not listed there at all. `NativeNationType` gained `DisplayName`/`Country`/`Description` (all optional, empty in classic) and `PlayerFacingName`. Classic renders exactly as before: no text authored, section skipped. Wording drafted → reviewed → approved (see the review doc); no imagery, no restricted material, no resistance-event content. | (this commit) |
| 2026-07-26 | **Initial implementation — the WS5.3 three-axis relationship model.** Respect (persisted, save v76, nation-scoped, 0–100, seeded at 35 on contact, +5 paying for land / −12 seizing it), Country Pressure (derived 0–100 from the live colonial footprint, so withdrawal relieves it), and the existing alarm reused as Tension on a 0–100 read — plus a per-turn pass in which Country Pressure and low Respect keep feeding Tension, and doc 18's seven relationship states derived Tension-first. Mechanics only: no new cultural content, naming or framing (ADR-022 / ICIP gated). Classic is entirely unaffected and byte-identical (ADR-009). | (this commit) |
