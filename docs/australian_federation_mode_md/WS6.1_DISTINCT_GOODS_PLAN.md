# WS6.1 — Distinct Goods: scoping / ADR + incremental plan

**Created:** 2026-07-15. **Status:** scoping complete; **Slice 1 (Copper) SHIPPED 2026-07-15** — the pattern is proven end-to-end (new goods-type + market + tile production + resource amplification, australia-only, classic byte-identical, no save bump, no engine code). Slice 2+ pending.
**Goal:** promote the Australian variant's reused goods stand-ins (`silver`→**Gold**, `cotton`→**Wool**) to real distinct goods, and add the missing doc-17 goods (Coal, Copper, Hides, Tallow, Meat, Frozen Meat, Sandalwood, Pearls, Newspapers, Telegraph Wire, Rails) **where they carry distinct mechanics** — the keystone that unblocks the WS6.3 pastoral **buildings** (a Shearing Shed needs a real Wool good; a Freezing Works needs a real Meat good) and the WS6.2 economic units.

---

## 1. The decision (ADR-023, in brief)

**Add each distinct good as a new `<goods-type>` in `game/data/rules/australia/specification.xml` ONLY. Never touch `classic/specification.xml` or any shared C# constant.** This is the whole safety guarantee — it follows the ADR-018 transposability contract (the two rulesets are standalone full-copy spec files).

Consequences (all verified in code, 2026-07-15 audit):
- **Classic stays byte-identical automatically** — a good defined only in the Australian ruleset is never instantiated in a classic game, so no classic colony store / market datum / trade counter / save token can reference it. **No save-version bump** (goods stores are sparse `Dictionary<string,int>` keyed by id, omit-when-empty; market inventory is sparse + omit-when-default; prices are *derived*, never stored). Guarded by `SaveBackwardCompatTests`.
- **No enumeration breaks** — there is no goods `enum` and no `Goods[ordinal]` array. Every engine loop filters `Ruleset.GoodsTypes` by *properties* (`IsFarmed`/`IsFood`/`IsTradeable`/`IsStorable`/`IsMilitary`/`IsNewWorldGoods`). A new good slots in via its flags. Hardcoded goods-ids (`FoodId`, `MilitaryStockGoods`, native `SeedGoods`) target *specific* mechanics, never "all goods" — a new good is simply absent from them and degrades gracefully.
- **Production, market, native cravings, most UI, rival-AI are automatic** — they iterate ruleset/stored goods. (Rival-AI is moot anyway: Australia seeds 0 rival powers.)

**The only non-automatic requirements per good:**
1. **A production source** — either a terrain `<production><output goods-type=… value="N≥1"/>` (⚠ base **must be ≥ 1**; the `Game.TileYieldPotential` `baseYield<=0 → 0` guard means a `<resource>` modifier alone can *never* enable a good), or a building `<input>/<output>` chain.
2. **A `<market>` child** (`initial-amount` / `initial-price` / `price-difference`) if the good is tradeable — the per-player `Market` auto-seeds every good that has one.
3. **A goods icon** at `res://assets/australia/goods/<short>.png` — a *novel* short name has **no** FreeCol fallback → blank icon. **Solution: copy an appropriate GPL icon from `game/assets/freecol/goods/` as an interim placeholder** (GPL→GPL, licence-clean, no external sourcing, no art-approval needed); bespoke Australian goods art lands in the WS2 art pass. A slice MAY ship the mechanic first and defer the icon (the good renders name-only until then).
4. *(optional)* add the id to native `SeedGoods` (`NativeSettlementGenerator.cs`) if First Nations settlements should gather/sell it; a `DisplayOverrides` entry only if the humanized id isn't the wanted label (`gold`→"Gold" auto-humanizes fine).

**Promotions vs additive goods:**
- **Additive** (Copper, Coal, Sandalwood, Pearls, …): pure-add — new goods-type + production + market + icon. No retargeting. **Lowest risk → the pattern-setters.**
- **Promotions** (`silver`→Gold, `cotton`→Wool): also retarget the Australian *production, resource, expert unit, events, and modifiers* from the stand-in to the new good, and remove the stand-in's `DisplayOverride`. Heavier (touch `expertSilverMiner`/`masterCottonPlanter`, `model.resource.silver`/`cotton`, Hargraves/Macarthur modifiers, goldRush/woolBoom/etc. events, goldfield/stockRoute/Pasture improvements, goldfieldsOffice). Do these **after** the pattern is proven.

---

## 2. Risk surface

| Area | Risk | Mitigation |
|---|---|---|
| Classic byte-stability | **None** if the good is australia-only | Never edit `classic/` or shared C# constants; the soak + `SaveBackwardCompatTests` guard it |
| Save format | **None** — sparse id-keyed maps, no bump | Author omit-when-default (already the model) |
| Production wiring | A good with base-0 terrain silently yields nothing | Give the source terrain a base `<production>` value ≥ 1 |
| Market/balance | A mis-priced good distorts the economy | First-pass prices from doc-17 value hints; flag as tunable (playtest) |
| Icon | Blank icon for a novel short name | GPL placeholder from `assets/freecol/goods/`, or defer name-only |
| Promotions | Retargeting misses a reference → the stand-in and the new good both live | A "no stray `silver`/`cotton` production refs remain in the Australian spec" test per promotion |

Nothing here forces a classic change, a save bump, or an engine change for the additive goods. **This is a data-first workstream.**

---

## 3. Incremental slice plan (ordered)

Each slice lands with L1/L2 tests + same-commit docs + CI-green; classic byte-identical throughout.

1. ~~**Slice 1 — Copper (pattern-setter).**~~ ✅ **SHIPPED 2026-07-15.** `model.goods.copper` (`is-farmed`, not new-world-goods) + `<market initial-amount=1000 initial-price=8 price-difference=2>` — a mid-value mineral between bulk Ore (4) and Gold (16). Mined on **hills** at base **2** (satisfying the `baseYield≥1` guard); the **existing** `model.resource.ore` gained a `+2 copper` modifier so a copper-bearing ore deposit amplifies it — *reusing* that resource rather than adding a new one to the terrain **left the map generator's resource placement untouched**. Icon deferred (renders name-only until the art pass). Faithful: the SA copper boom (Burra/Kapunda/Moonta), a great raw export. +1 L1 (`AustralianContentTests.Copper_IsADistinctAustralianGood_…`: farmed/tradeable/price/not-NW + hills base ≥1 + the ore-resource lift + absent from classic's goods/terrain/resource). Full L1/L2 **2998** green; classic byte-identical; **no engine code, no save bump** — the plan's core claim is now proven in practice.
2. **Slice 2 — Coal + Sandalwood + Pearls** (more additive export goods, same pattern; Coal seeds a future industry/rail-input use, Sandalwood=WA, Pearls=NW — each purely additive).
3. **Slice 3 — Gold** (promotion): add `model.goods.gold` + `model.resource.gold`; retarget the gold economy (Hargraves' +100% + deposit-reveal handler, goldfieldsOffice, goldfield improvement, goldRush/payableField/brokenHill events, the Digger expert) from `silver`→`gold`; remove the `silver`→"Gold" override; copy the silver icon → `gold.png`. Add a "no stray silver production refs" guard.
4. **Slice 4 — Wool** (promotion): `model.goods.wool` + retarget the cotton→wool economy (Macarthur, squattingRun/woolBoom, stockRoute/Pasture, the weaver→cloth chain = the Wool Shed/Shearing Shed, the Shepherd expert); remove the `cotton`→"Wool" override. **This unblocks a *distinct* Shearing Shed (WS6.3) — wool processing on a real Wool good.**
5. **Slice 5 — the pastoral chain**: Cattle/Sheep as farmed goods → Hides/Tallow/Meat by-products → **Frozen Meat** (requires the Freezing Works, doc 17). Enables the deferred WS6.3 pastoral buildings (Cattle Station, Freezing-Works chain). Highest complexity (production chains + buildings) — last.
6. **Cross-cutting:** goods icons (GPL placeholders now; bespoke in WS2 art), and a doc-20 balance pass on the new prices (needs playtest).

**Recommended first action:** Slice 1 (Copper) — the safe, self-contained pattern-setter.

---

## 4. Cross-cutting rules (every slice)

- **Byte-identical classic** (ADR-009/018): australia-spec-only; soak stays green.
- **Tests**: L1 parse/availability + market/production presence; a promotion adds a "no stray stand-in refs" guard. L4 goldens once icons land.
- **Docs** (no-drift): update `docs/systems/market.md` / `ruleset-data.md` + this plan's status, same commit.
- **Licensing**: goods icons GPL-v2-compatible (FreeCol placeholders now; recorded in the Asset Register when bespoke art lands).

*Living document — update status as slices land.*
