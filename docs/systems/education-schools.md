# Education & schools

| | |
|---|---|
| **System** | Schoolhouse / college / university teaching |
| **Status** | Implemented (GameLogic: an expert teaches the least-skilled colonist up the ladder over the spec turns; save v32). UI to *assign* a teacher to a school is a follow-up — today a teacher is any expert occupying a school building. |
| **Last verified** | 2026-06-20 @ trade tie-break (`86d3c9p7f` follow-up) |
| **Owner task** | `86d3c9p7f` |
| **Code** | `Game.RunSchoolTeaching` / `FindLeastSkilledStudent`; `Colony` school-training state + the upgrade-in-place mutators; `Ruleset.GetTeachingType` / `NeededTurnsOfTraining`; `BuildingType.MaximumSkill`/`Teaches`; `UnitType.SkillTaught` |
| **FreeCol reference** | `ServerBuilding.csTeach`/`csTrainStudent`, `Colony.findStudent`/`getTeachers`, `UnitType.getTeachingType`/`getSkillTaught`, `Specification.getNeededTurnsOfTraining`, spec `model.unitChange.education` + schoolhouse/college/university |

## 1. In plain English

Put an **expert** colonist (an expert ore miner, a master distiller, an elder statesman, …) into a **school** and they teach the colony's other colonists, lifting them up the skill ladder one step at a time:

> **petty criminal → indentured servant → free colonist → the teacher's own expertise.**

- The teacher always works on the colony's **least-skilled** colonist first — a petty criminal is taught before a plain free colonist.
- Each step takes a few turns: the criminal→servant and servant→free steps take **4** turns; learning an actual expertise takes **4, 6, or 8** turns depending on how hard it is (an expert ore miner 4, a master distiller 6, an elder statesman 8). A colony under **good government** (high Sons of Liberty) learns faster — the production bonus shaves turns off, down to a minimum of 1.
- There are three school tiers, each able to teach a higher class of expert: the **schoolhouse** teaches the simple experts (skill 1), the **college** the masters (skill 2), the **university** the most learned (skill 3+, e.g. the elder statesman). An expert too advanced for the school it sits in simply can't teach there.
- A **colonial regular** is special: in a school it teaches the **veteran soldier** skill, not "colonial regular".

**Worked example.** An expert ore miner sits in a schoolhouse; the colony also has a petty criminal. After 4 turns the criminal becomes an indentured servant; 4 more, a free colonist; 4 more, an expert ore miner — 12 turns to turn a convict into a skilled miner. Under 100% Sons of Liberty (production bonus +2) each step needs only 2 turns.

*(Classic Colonization picks the student automatically — there is no "choose who learns" prompt; we follow that.)*

## 2. Rules we implement

- **The schools** (`BuildingType`): schoolhouse (1 workplace, teaches skill ≤ **1**), college (2 workplaces, ≤ **2**), university (3 workplaces, ≤ **4**). All three carry the **teach** ability (the schoolhouse declares it; college/university inherit it up the `extends` chain).
- **Who teaches:** any **expert** (skill ≥ 1) occupying a school building whose skill is within the building's cap. An over-skilled expert (elder statesman, skill 3, in a schoolhouse) cannot teach there.
- **Who learns:** the colony's **least-skilled** teachable colonist — anywhere in the colony (a worked tile, a building, or idle). Only petty criminals, indentured servants and free colonists are teachable (only they have an education ladder); an existing expert is never a student. The teacher itself is never its own student.
- **What they become:** one rung per completed cycle, toward the teacher's taught skill — criminal→servant→free→the-teacher's-expertise. A colonial regular teaches the veteran-soldier skill (`skill-taught` indirection).
- **How long:** the spec base turns (criminal/servant rungs 4; an expertise 4/6/8 by expert), **reduced by the colony's Sons-of-Liberty production bonus**, floored at 1. Progress accrues on the school; it **resets** when the student graduates or no eligible student is present that turn.
- **Deviations / not yet modelled:**
  - A **multi-workplace** college/university teaches **one** student at a time (a single training counter per building), where FreeCol teaches one per teacher in parallel — a documented first-cut.
  - **Progress is per-school, not bound to one student** (FreeCol binds a teacher to a specific student until it graduates). So if the colony's least-skilled colonist *changes* mid-cycle (a lower-skilled colonist arrives), the accrued turns carry to the new least-skilled student rather than the original — a faithfulness edge with small gameplay impact (a late-arriving criminal can graduate a rung a turn early). Flagged as a follow-up.
  - **Tie-break among equally-least-skilled students** (`86d3c9p7f` follow-up, FreeCol `findStudent`): a student already **working the teacher's expert good** wins the tie (`getWorkType() == expertise` — e.g. an expert ore miner teaches the colonist already mining ore before an equally-skilled farmer). Remaining ties (no trade match, or several) fall back to the stable enumeration order (tiles row-major → non-school buildings → idle). Deterministic, no RNG.
  - Building **upkeep** (gold) is unmodelled project-wide. There is no *assign-a-teacher* UI yet (any in-window expert in a school is a teacher); a future placement command must apply the same min/max-skill gate. No clear-speciality command exists, so FreeCol's "can't clear a teacher's speciality" guard has no surface yet. Student selection is automatic (classic `allowStudentSelection=false`).

## 3. Technical design

*Audience: developers / future sessions.*

- **Data (`86d3c9p7f` slice 1):** `BuildingType.MaximumSkill`/`MinimumSkill` (spec `maximum-skill`/`minimum-skill`, the latter inherited down the `extends` chain — the school window is 1..1/1..2/1..4) + `BuildingType.Teaches` (`model.ability.teach`, inherited). `UnitType.SkillTaught` (spec `skill-taught`, **own attribute, non-inherited**, defaults to the type itself) + `SkillTaughtOrSelf`. The `model.unitChange.education` table is parsed by `Ruleset.ParseEducationTurns` into `from → (to → turns)` — recovering every row past the by-`from` collapse the generic `<unit-type-change>` parse causes (the same fix as the experience table).
- **`Ruleset.GetTeachingType(teacher, student)`** (port of `UnitType.getTeachingType`): `taught = Unit(teacher).SkillTaughtOrSelf`, `taughtLevel = Unit(taught).Skill`; null if the student is already at/above that skill or has no education rows; if the student's rows reach `taught` directly, that; else it climbs **one** rung (a `to` with `Skill < taughtLevel`, recursing) — yielding a single step per cycle. **`NeededTurnsOfTraining`** returns the base turns of the rung `GetTeachingType` picks.
- **Colony state:** `_schoolTrainingTurns` (building id → accrued turns) is the overlay analogue of FreeCol's per-`Unit` `turnsOfTraining` (we have no per-colonist objects). `AddSchoolTrainingTurn`/`ResetSchoolTraining`/`RestoreSchoolTraining`/`SchoolTrainingTurnsAt`/`SchoolTrainingTurns`. Students upgrade **in place** without touching the worker count: `UpgradeTileWorker` (sets/clears the tile overlay type + clears XP), `UpgradeBuildingWorker` (swap one occupant's overlay type — add for a free→non-free promotion, remove for a non-free→free), `UpgradeIdleWorker` (same for the idle pool, free idle being implicit). `ReconcileWorkerTypes` sweeps training for a building no longer present.
- **The teach step (`Game.RunSchoolTeaching`):** runs in `RunColonyTurn` **after growth** (population + bonus settled, FreeCol's reason for deferring `csCheckTeach`), **before** the custom-house sale. For each `Teaches` building: pick the eligible teacher (skill within `[MinimumSkill, MaximumSkill]`, Ordinal-first — the single-counter teacher); `FindLeastSkilledStudent` (least-skill-first across tiles row-major → **non-school** buildings → idle; on a skill tie a student **producing the teacher's `ExpertProduction` good** wins — tile via `TileWorkers[tile]`, building via the building's attended output — else the first in stable order; **no RNG**; occupants of a `Teaches` building are skipped, since a colonist in a school is staff, never a student — FreeCol's minimum-skill keeps students out of schools); accrue a turn; `needed = max(1, NeededTurnsOfTraining − ProductionBonus)`; on `accrued >= needed` upgrade the student to `GetTeachingType(teacher, student).Id` and reset. No teacher or no student → reset (progress lapses).
- **Determinism (ADR-009):** the teach step draws **no** RNG and iterates in a fixed order, so it is twin-deterministic and the human's stream 0 is untouched. The L5 soak's AI colonies are all free colonists and never staff an expert into a school, so `RunSchoolTeaching` is a pure no-op there → byte-stable.
- **Persistence:** save **v32**, additive — `SavedColony.SchoolTraining` (omitted when no school is mid-training, so a non-teaching game is byte-identical to v31; pre-v32 loads with no training). Student *type* changes ride the existing worker-overlay save; only the per-school accrued turns are new.

## 4. Verification

| Layer | Required? | Tests / goldens | Status |
|---|---|---|---|
| L1 Unit | Always | `EducationDataTests` (school skill caps + teach ability incl. inheritance; `skill-taught` colonial-regular→veteran; `GetTeachingType` one-rung ladder + skill-taught indirection + at/above-skill null; `NeededTurnsOfTraining` 4/6/8; every expert education row recoverable). `SchoolTeachingTests` (training-turn accrue/reset/restore; `UpgradeIdleWorker` free↔expert; reconcile sweep; the 4/8/12 criminal→servant→free→expert ladder; least-skilled-first; over-skilled teacher can't teach a schoolhouse but can a university; a colonist sitting *in* a school is not taught; tile student taught in place; SoL bonus reduces needed turns; no-school no-op; **trade tie-break** — given two equal-skill free colonists, the one working the teacher's expert good (ore) is taught before the one farming grain even though grain is earlier in stable order) | ✅ |
| L2 Scenario | Always | `SchoolTeachingTests` save round-trip (mid-training v32 progress survives + completes at the same turn; omitted-when-empty); the L5 soak round-trips byte-identically with the teach step inert (no AI-staffed schools) | ✅ |
| L3 Interaction | No new UI | — (no assign-a-teacher screen yet) | — |
| L4 Visual | No new screen | — | — |
| L5 Soak | Always | covered by L2 (byte-identical round-trip; teach step a no-op in the all-free soak colonies) | ✅ |

- **FreeCol cross-check:** the ladder turns (4/4/4 for a criminal under a skill-1 expert; 6/8 for masters/elders) and least-skill-first selection match `model.unitChange.education` + `Colony.findStudent`; the skill-taught indirection matches `UnitType.getSkillTaught` (colonial regular).

## 5. Open issues / TODO

- [ ] **Assign-a-teacher UI** — the teach step already enforces the min/max-skill window (`getNoAddReason`) when picking a teacher and never teaches a colonist sitting in a school; a future "put this colonist in the school" *command* should reject an out-of-window placement up front (today nothing stops a player assigning a non-expert into a school — it just won't teach or be taught).
- [ ] **Bind training to one student** until it graduates (FreeCol) — today progress is per-school, so a mid-cycle change of the least-skilled student carries the accrued turns.
- [ ] **Per-teacher parallel training** in a college/university (currently one student at a time per building).
- [x] **Trade tie-break** (`86d3c9p7f` follow-up) — a tied student already working the teacher's expert good is taught first (FreeCol `findStudent`'s `getWorkType() == expertise`); remaining ties keep the stable enumeration order. See §2/§3.
- [ ] **clear-speciality guard** — when a clear-speciality command exists, refuse to demote an expert teaching in a school (FreeCol `InGameController` guard).
- [x] **Unblocks** the criminal/servant→colonist **education rung** of the promotion ladder (`86d3c9q1z`) — that path is this system's criminal→servant→free steps.

## Changelog

| Date | Change | Commit |
|---|---|---|
| 2026-06-20 | **Trade tie-break** (`86d3c9p7f` follow-up, FreeCol `Colony.findStudent`): `FindLeastSkilledStudent` now breaks a skill tie in favour of a student already **producing the teacher's `ExpertProduction` good** — a tile student via `TileWorkers[tile]`, a building student via the building's attended output good — before falling back to the stable enumeration order. So an expert ore miner teaches the colonist already mining ore ahead of an equally-skilled farmer. Closes the documented trade-tie-break deviation. Deterministic (no RNG), no save change, soak-inert (no AI-staffed schools). +1 L1 (`SchoolTeachingTests.TieBreak_…`, proven against the old stable-order rule); 1237 L1/L2 + soak green | Phase 5 (`86d3c9p7f`) |
| 2026-06-18 | **Teaching system** (`86d3c9p7f`): schools teach the least-skilled colonist up the ladder (criminal→servant→free→expertise) over 4/6/8 turns (reduced by the SoL bonus, floor 1), built on the per-colonist worker overlay. Slice 1 = data (`BuildingType.MaximumSkill`/`Teaches`, `UnitType.SkillTaught`, the education-turns table parse, `GetTeachingType`/`NeededTurnsOfTraining`). Slice 2 = the colony-turn teach step (`RunSchoolTeaching`/`FindLeastSkilledStudent`) + the `_schoolTrainingTurns` state + in-place upgrade mutators + save **v32** (additive). Deterministic (no RNG); soak inert (no AI-staffed schools) → byte-stable. A 14-agent adversarial review drove three fixes folded in: `minimum-skill` parsed + used for teacher eligibility (was a hardcoded `Skill>0`); a colonist sitting *in* a school is skipped as a student (no free in-school graduation); and the per-school-progress + trade-tie-break deviations documented. +32 L1/L2 (`EducationDataTests`, `SchoolTeachingTests`). | Phase 5 (`86d3c9p7f`) |
