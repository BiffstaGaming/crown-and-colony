# Asset Research — Australian Federation Variant

**Status:** RESEARCH ONLY. This is a documented **candidate list** for Chris to approve. **Nothing has been downloaded, extracted, or committed** — no binary asset of any kind is in this branch. Every entry below is a source to *evaluate*, and each individual file must still be licence-verified per-file at download time (a pack's headline licence does not guarantee every file inside it — see "Per-file verification" below).

Author: research agent · Date: 2026-07-08 · Branch: `worktree-agent-a7fd70572e6fc15c3` (off `main`)

---

## The licensing rule (binding — read first)

Crown & Colony is **GPL v2** (the whole project, because it derives from FreeCol's GPL-v2 code and data — see `CLAUDE.md` → Licensing). Any art, audio, or font we distribute must therefore be under a licence that is **GPL-v2-compatible for distribution**. That means:

| Licence | Verdict | Notes |
|---|---|---|
| **CC0** (public domain dedication) | ✅ Best choice | No conditions, no attribution required. Still record provenance for our own audit trail. |
| **Public domain** (expired copyright) | ✅ | e.g. author died >70 yrs ago, or AU photos taken **before 1 Jan 1955**. Attribute as courtesy + record source. |
| **CC-BY 3.0 / 4.0** | ✅ | Attribution is **mandatory** — must go in `CREDITS.md` / per-folder `PROVENANCE.md`. |
| **GPL v2 / GPL v2+** | ✅ | Native match (this is FreeCol's licence). |
| **OFL** (SIL Open Font License) | ✅ (fonts only) | GPL-compatible for bundling/embedding. Cannot sell the font *by itself*; that's irrelevant to us. Don't reuse the font's "Reserved Font Name" on a modified copy. |
| **CC-BY-SA 4.0** | ⚠️ One-way only | Officially **one-way compatible with GPLv3** (CC + FSF, 2015-10-08). It is **NOT** listed as GPLv2-compatible. Because our base is GPL **v2**, CC-BY-SA assets are usable **only if** we are prepared to treat/relicense the combined work as **GPL v2-or-later → GPLv3**. Treat as **"verify — needs a licensing decision from Chris"**, not a free grab. |
| **CC-BY-SA 3.0 / 2.x / 1.0** | ⚠️/❌ | Older CC-BY-SA versions are **not** GPL-compatible at all (no compatibility clause). Avoid unless Chris explicitly accepts the ShareAlike constraint on our asset folder. |
| **CC-BY-NC** (non-commercial) | ❌ | GPL guarantees the freedom to sell/redistribute — NC forbids it. **Never usable.** |
| **CC-BY-ND** (no-derivatives) | ❌ | We modify/repackage assets; ND forbids derivatives. **Never usable.** |
| **"Pixabay Content License", "Freepik Free", custom "free for games" EULAs** | ❌ / verify | These usually ban **standalone redistribution** of the asset. GPL *requires* that anyone can redistribute every part of the work, including standalone. That clause is a **direct GPL conflict.** Treat all custom platform licences as ❌ until proven to be true CC0/CC-BY. |

**When unsure, the entry is marked `verify` and must not be used until resolved.**

### Per-file verification (do not skip)
A pack advertised as "CC0" or "CC-BY" can still contain individual files under a different licence (OpenGameArt "collections" are the classic trap — they aggregate mixed-licence art under one page). The asset pipeline must:
1. Read the licence stated **on the specific file/asset page**, not the collection or the site's front page.
2. Record it in the per-folder `PROVENANCE.md` and aggregate into root `CREDITS.md` (per `docs/DOCUMENTATION.md`).
3. Reject anything NC / ND / custom-EULA / "verify-unresolved".

---

## 0. The default base — reuse FreeCol's own art (already GPL v2)

Before sourcing anything new: FreeCol's art and audio are **GPL v2** (with some assets CC-BY — see `CLAUDE.md`). They are already the visual base of this project, and the isometric look reuses them. For the Australia variant, **most tiles, UI chrome, and generic unit/building frames can stay FreeCol art** — the variant is a *data* change first. New art is only needed where the Australian roster has no FreeCol equivalent (convict, shepherd/shearer, drover, digger, telegraph worker, federation campaigner; sheep station, wool shed, goldfields office, freezing works, telegraph office, federation hall).

| Source | Covers | Licence | GPL-v2 verdict | Attribution | Notes |
|---|---|---|---|---|---|
| FreeCol `freecol/data/` (local read-only clone) | Terrain tiles, unit/colonist frames, building icons, UI, sounds | **GPL v2** (some assets CC-BY) | ✅ | Retain FreeCol/artist credit (already in FreeCol's credits) | The lowest-risk source by far — same licence, same art style, already integrated. Recolours/reskins of these are the cleanest path for new Australian units/buildings. `srcdata/graphics/README` + `srcdata/audio/README` name the original authors (Stephen, Steven Melenchuk). |

---

## 1. Unit / colonist / soldier sprites (convicts, settlers, troopers, shepherds, miners, diggers)

| Source (name + URL) | Covers | Licence | GPL-v2 verdict | Attribution | Suitability notes |
|---|---|---|---|---|---|
| **Kenney — Medieval RTS** https://kenney.nl/assets/medieval-rts | 120 assets: units, structures, tiles for top-down/RTS | **CC0** | ✅ | None required | Clean, consistent top-down set. Units are generic-medieval, not colonial, but recolourable into troopers/settlers. Strong CC0 anchor. |
| **Kenney — Tiny Town / Tiny Dungeon / Toon Characters** https://kenney.nl/assets (browse "Characters", "Tiny") | Small modular character sprites | **CC0** | ✅ | None | Whole Kenney catalogue is CC0 — safest bulk source. Good for placeholder colonists; style is cartoonish, may clash with FreeCol's painterly look. |
| **OpenGameArt — "Tiny Soldiers of The Old World Wars"** https://opengameart.org/content/tiny-soldiers-of-the-old-world-wars | Historical (musket-era) soldier sprites | **CC0** (verify on asset page) | ✅ if CC0 confirmed | None if CC0 | Musket-era troopers are the closest match to colonial militia / mounted police / redcoats. **Verify the per-asset licence line** before use. |
| **OpenGameArt — "pixel art top down soldiers"** https://opengameart.org/content/pixel-art-top-down-soldiers | 32×32 top-down soldier + officer spritesheet, spear/musket variants | **CC0** (verify) | ✅ if confirmed | None | Good top-down orientation; small canvas. Verify licence on page. |
| **OpenGameArt — "Top-Down Assets" curated list** https://opengameart.org/content/top-down-assets-1 | Curated index of top-down/colony-sim CC0 art | **Mixed** (index) | verify each | per-item | An index, not one licence — use only to *discover*; verify every linked item individually. |
| itch.io CC0 top-down character packs https://itch.io/game-assets/assets-cc0/tag-top-down | Many small CC0 character/tileset packs | **CC0** (per pack — verify) | ✅ if CC0 | None | Large pool; quality/consistency varies. Filter to CC0, verify each pack's licence file. |

**Gap:** No pack contains *distinctly Australian-colonial* units (convict in slops, swagman/shepherd, gold digger with cradle, drover on horseback). These will almost certainly need **custom reskins of FreeCol/Kenney bases** or commissioned/AI-free pixel work approved by Chris. There is no ready-made "Australian convict sprite" pack in the open ecosystem.

---

## 2. Building sprites (stores, sheds, town halls, wharves, goldfields, Victorian-era)

| Source (name + URL) | Covers | Licence | GPL-v2 verdict | Attribution | Suitability notes |
|---|---|---|---|---|---|
| **Kenney — Medieval Town (Base)** https://kenney.nl/assets/medieval-town-base | 65 modular town-building assets | **CC0** | ✅ | None | Modular houses/stores/walls; pre-industrial look fits early-colony (stores, granary, town hall). No Victorian industrial buildings (freezing works, rail depot). |
| **Kenney — Fantasy Town Kit** https://kenney.nl/assets/fantasy-town-kit | 160 town-building assets | **CC0** | ✅ | None | 3D kit; more for reference/3D. Fantasy trim may need stripping. |
| **Kenney — Medieval RTS** (as above) | Structures within the RTS set | **CC0** | ✅ | None | Overlaps §1; buildings usable for wharf/store/town-hall stand-ins. |
| **OpenGameArt building/isometric packs** (search "colonial", "victorian", "isometric building") https://opengameart.org/ | Scattered building sprites | **Mixed (CC0 / CC-BY / CC-BY-SA)** | verify each | per-item | No single colonial-Australian pack found. Individual CC0/CC-BY buildings can be assembled; verify each. |

**Gap (significant):** The distinctly **industrial-Victorian and gold-rush** buildings the roster needs — **Goldfields Office, Freezing Works, Wool Shed / Shearing Shed, Telegraph Office, Rail Depot, Customs House, Harbour Battery, Federation League Hall, Convention Hall** — have **no open-asset equivalent**. Expect these to be reskins of FreeCol building frames or custom work. This is the biggest art gap in the variant.

---

## 3. Terrain / tile art (arid interior, bush, coast)

*The current isometric look reuses FreeCol terrain — new terrain is low priority.*

| Source (name + URL) | Covers | Licence | GPL-v2 verdict | Attribution | Suitability notes |
|---|---|---|---|---|---|
| **FreeCol terrain tiles** (`freecol/data/`) | Full isometric terrain set already in use | **GPL v2 / CC-BY** | ✅ | FreeCol credit | Default. Australia needs an **arid/desert/spinifex** palette — likely a **recolour** of FreeCol's existing arid/plains tiles rather than new art. |
| **OpenGameArt — "Desert isometric tiles"** https://opengameart.org/content/desert-isometric-tiles | Isometric desert/oasis tiles | **CC0** (verify) | ✅ if confirmed | None | Closest match for the arid interior if a distinct look is wanted. Verify licence + confirm it tiles with FreeCol's grid geometry (FreeCol uses a specific diamond size). |
| **OpenGameArt — Flare "Grassland Tileset"** https://opengameart.org/content/grassland-tileset | 200+ isometric grassland tiles (64×32) | **CC-BY / CC-BY-SA** (mixed — built from many sources) | ⚠️ verify | Attribution (and possibly ShareAlike) | Because it's assembled from CC-BY **and** CC-BY-SA pieces, ShareAlike may attach → the GPLv3 caveat applies. Use only if Chris accepts the SA/GPLv3 path. |
| **OpenGameArt CC0 Textures** https://opengameart.org/content/cc0-textures-0 | Raw ground/rock/water textures | **CC0** (verify) | ✅ if confirmed | None | Source material for hand-building arid tiles, not drop-in isometric tiles. |

**Gap:** No open pack is specifically "Australian bush/outback isometric". Recolouring FreeCol's tiles to an ochre/arid palette is the pragmatic answer; a distinct desert set is a nice-to-have.

---

## 4. Australian colonial imagery / portraits (Pioneers, event art, backgrounds)

The existing plan already sourced public-domain **portraits** for the Australian Pioneers (ClickUp task `86d3mmdh7`, and the ADB/NMA source URLs in `docs/australian_federation_mode_md/21_Research_Sources.md`). Most named figures (Parkes, Barton, Chisholm, Macarthur, Flinders, etc.) died well before 1955, so their portraits are **public domain**. This section targets the **gaps**: scene/event art, backgrounds, goldfields genre scenes, and any figure without a found portrait.

| Source (name + URL) | Covers | Licence | GPL-v2 verdict | Attribution | Suitability notes |
|---|---|---|---|---|---|
| **Wikimedia Commons — 19th-c. Australian paintings** https://commons.wikimedia.org/wiki/Category:19th-century_landscape_paintings_in_Australia | Colonial landscapes, town scenes, First-Fleet-era art | **Public domain** (pre-1955 / author long dead) — verify each file's tag | ✅ (PD) | Courtesy credit + record source | Rich source of **event/background art** (settlement founding, harbour views). Each Commons file states its PD rationale — record it. Prefer files tagged PD-old / PD-Australia. |
| **S. T. Gill goldfields watercolours** (State Library Victoria; digitised on Commons/SLV) https://www.slv.vic.gov.au/search-discover/galleries/australian-sketchbook-st-gill · https://en.wikipedia.org/wiki/S._T._Gill | ~40 commissioned goldfields scenes + ~3000 works: diggers, tents, gold cradling, colonial daily life | **Public domain** (Gill d. 1880) | ✅ | Courtesy: "S. T. Gill, State Library Victoria" | **The single best fit** for gold-rush event/loading-screen art. A digitised PD work is itself PD (Trove/NLA confirm digitisation doesn't create new copyright). |
| **Trove / National Library of Australia** https://trove.nla.gov.au/help/copyright/copyright-and-re-use | Colonial photographs, illustrations, newspaper engravings | **Public domain if taken/published before 1 Jan 1955** (AU rule) — else verify | ✅ (pre-1955) | Cite item + holding institution | NLA states pre-1955 AU photos are out of copyright and digitised PD stays PD. **Partner-held items:** confirm terms with the holding institution (Trove aggregates many libraries). |
| **State Library of NSW / SLV / NGV / NGA digital collections** | High-res colonial paintings, portraits, maps | **Mostly PD for pre-1955 works** — but each institution has its own **reuse/terms page**; some assert digitisation/reproduction terms | verify per institution | Cite institution | PD *artwork* can't be re-copyrighted, but some galleries impose contractual reuse terms on *their scans*. Prefer Commons copies where the PD tag is explicit; otherwise read the institution's reuse page. |
| **LibreShot** https://libreshot.com | Free stock photography (modern) | **CC0-style / verify current terms** | verify | check site | Modern photos only — useful for reference or modern-outback texture, **not** period imagery. Verify its current licence (it has shifted terms over time). |
| **PICRYL / Public Domain Review** https://picryl.com | Aggregated public-domain historical images | **Public domain** (verify each) | ✅ (PD) | Cite original holder | Convenient PD aggregator; always trace back to the original holding institution's PD statement. |

**Gaps:**
- **Event/scene art** (First Fleet landing, Eureka Stockade, Federation ceremony, telegraph completion) — PD paintings/engravings exist on Commons/Trove but must be found and PD-verified one by one.
- **Any Pioneer without a portrait** — cross-check the roster in docs 07–12 against task `86d3mmdh7`'s found set; likely gaps are minor/regional figures. First Nations figures (Pemulwuy, Barak) are **especially sensitive** — see §6.

---

## 5. Music / ambient audio (colonial / folk / period)

| Source (name + URL) | Covers | Licence | GPL-v2 verdict | Attribution | Suitability notes |
|---|---|---|---|---|---|
| **Freesound.org** (filter: Creative Commons 0 / Attribution) https://freesound.org | Ambient beds, foley (harbour, bush, tools, crowd, sheep), instrument samples | **CC0** or **CC-BY** (per sound — filterable) | ✅ | CC0 none / CC-BY mandatory | Use the on-site licence filter → "Creative Commons 0" (safest) or "Attribution". Filter to **Free Cultural Works** (CC0/CC-BY). Ideal for **ambience + SFX**; less for full music tracks. |
| **OpenGameArt — CC0 Music** https://opengameart.org/content/cc0-music-0 | Loopable game music tracks | **CC0** (verify each) | ✅ if confirmed | None | General game music; little is period-folk. Good for menu/ambient loops. Verify each track. |
| **OpenGameArt — music (CC-BY)** https://opengameart.org/ (search "folk", "celtic", "shanty", "acoustic") | Folk/acoustic/period-ish tracks | **CC-BY / CC-BY-SA** (verify) | ✅ (CC-BY) / ⚠️ (SA) | CC-BY mandatory | Celtic/folk tracks suit a British-colonial tone. Watch for CC-BY-SA (GPLv3 caveat). |
| **Musopen** https://musopen.org | Public-domain classical recordings + PD sheet music | **PD / CC0** (recording licence varies — verify) | ✅ if PD/CC0 | Cite performer if required | Period-appropriate classical (colonial parlour music). **The *composition* may be PD but a specific *recording* may not** — verify the recording's licence, not just the score. |
| **Traditional Australian/British folk tunes** (e.g. pre-1900 bush ballads, sea shanties) — sheet music PD | Melodies (Click Go the Shears, bush ballads, shanties) | **PD** (composition, if pre-1900s & author long dead) | ✅ (composition) | Cite | The **tune** is PD; you still need a **freely-licensed recording/arrangement** (commission or CC0/CC-BY performance). Don't grab a modern copyrighted recording of a PD tune. |
| ❌ **Pixabay Music / "no-copyright" YouTube music** | — | **"Pixabay Content License"** (NOT CC0 since 2019) | ❌ | — | **Do NOT use.** Bans standalone redistribution → conflicts with GPL. Same for most "royalty-free"/"no-copyright" platform music. Only pre-2019 Pixabay uploads are true CC0, and that's not worth the per-file risk. |

**Gap:** No ready pack of *colonial-Australian* music. Best path: **CC0 ambient + SFX from Freesound**, plus **PD folk/shanty melodies performed under a free licence** (a small commission or a CC0/CC-BY community recording). Full original scoring is out of scope for now.

---

## 6. First Nations representation — HANDLE WITH CARE (Chris decision, not an asset grab)

**This is a protocol issue, not a sourcing issue.** Do **NOT** treat First Nations visual representation as an open-asset problem to solve by downloading art. Per `docs/australian_federation_mode_md/15_First_Nations_Design_Principles.md` and `16_First_Nations_Cultural_Groups.md`, and the project's sensitivity rules:

- **Do NOT** recommend, source, or use **Aboriginal or Torres Strait Islander artworks, dot-painting styles, sacred/ceremonial motifs, rock-art imagery, totemic designs, or the Aboriginal / Torres Strait Islander flags** as game art, UI, or decoration.
- **Do NOT** use **"Aboriginal-style" stock art, AI-generated "Indigenous" art, or appropriated motifs** — these are culturally harmful and off-limits regardless of their nominal licence. A permissive licence does **not** confer cultural permission; the two are independent. Copyright expiry does **not** extinguish cultural rights or Indigenous Cultural and Intellectual Property (ICIP) interests.
- **Portraits of specific First Nations figures** (e.g. Pemulwuy, William Barak): even where a depiction is technically public domain, using it requires **cultural sensitivity review** — some communities restrict images of deceased persons, and many colonial-era depictions are inaccurate or demeaning. Flag each such image for consultation; do not auto-include on PD grounds alone.
- **Correct path:** First Nations visual representation requires **cultural consultation and permission** — engaging Traditional Owners / a relevant Aboriginal organisation, following ICIP protocols (e.g. AIATSIS, the Australia Council's Indigenous cultural protocols). This is a **Chris decision requiring proper protocol and likely paid/authorised consultation**, scheduled as a design/ethics task — **not** an asset to grab now.
- Until that protocol is in place, First Nations peoples in-game should be represented via **respectful non-appropriative means** (text, neutral map/territory overlays, generic non-sacred iconography) as the design docs already lean toward — and the *art* question stays parked.

**Recommendation:** raise a dedicated ClickUp task under the 4b First Nations stream: "Cultural consultation protocol for any First Nations visual representation" — blocking on all First Nations art, marked as needing Chris + external consultation. No asset in this document is offered for First Nations depiction.

---

## Recommended shortlist (approve these first)

1. **Reuse FreeCol art (GPL v2)** — the default base for tiles, UI, and most unit/building frames. Zero new licensing risk; already integrated. New Australian units/buildings = **reskins of these**. *(Source: local `freecol/data/`.)*
2. **Kenney CC0 packs** — *Medieval RTS* (120), *Medieval Town Base* (65), plus Kenney character kits. **CC0, no attribution, no risk.** Best drop-in supplement for buildings and placeholder units. https://kenney.nl/assets
3. **S. T. Gill goldfields watercolours (public domain)** — the ideal source for **gold-rush event/loading-screen imagery**; Gill d. 1880 → firmly PD. https://en.wikipedia.org/wiki/S._T._Gill
4. **Wikimedia Commons + Trove/NLA (public domain, pre-1955)** — colonial paintings, engravings, and photos for **event/background art and any missing Pioneer portraits**. Verify each file's PD tag; digitised PD stays PD. https://commons.wikimedia.org/ · https://trove.nla.gov.au
5. **Freesound.org (CC0/CC-BY, filtered)** — **ambience + SFX** (harbour, bush, sheep, tools, crowd). Use the Free-Cultural-Works licence filter. https://freesound.org

*Runner-up (conditional):* **OpenGameArt CC0 items** (Tiny Soldiers, desert isometric tiles, CC0 music) — good, but each must be **per-file licence-verified** because OGA mixes licences.

---

## Do NOT use / needs consultation

- ❌ **Pixabay / Freepik-free / "no-copyright" / "royalty-free" platform licences** — ban standalone redistribution → conflict with GPL v2. Not usable. (Only *pre-2019* Pixabay CC0 uploads are clean, and not worth the per-file risk.)
- ❌ **CC-BY-NC and CC-BY-ND** anything — non-commercial / no-derivatives are GPL-incompatible. Never usable.
- ⚠️ **CC-BY-SA (any version), and mixed CC-BY-SA packs (e.g. the Flare grassland tileset)** — CC-BY-SA **4.0** is only **one-way compatible with GPLv3**; older CC-BY-SA versions aren't compatible at all. Using any requires Chris to accept relicensing our asset set toward **GPL v2-or-later/GPLv3**. Parked pending that decision.
- ⚠️ **Original 1994/2008 Sid Meier *Colonization* assets** — off-limits entirely (never copy/extract/decompile). Not in scope, restated for safety.
- 🛑 **First Nations art / motifs / flags / "Aboriginal-style" or AI-"Indigenous" art** — **needs cultural consultation and permission (ICIP protocol)**, a Chris decision, not an asset to source. See §6. Licence status is irrelevant to this — it is a cultural-rights and ethics gate.
- ⚠️ **Institution scans with contractual reuse terms** (some SLV/SLNSW/NGA/NGV pages) — the PD *artwork* is fine, but read each institution's reuse-terms page for any contract on *their scan*; prefer the Wikimedia Commons copy with an explicit PD tag.

---

## What needs Chris

1. **Approve the shortlist** (the 5 sources above) as the sourcing basis before the pipeline touches any file.
2. **CC-BY-SA decision:** are we willing to treat our **asset folder** as GPL v2-or-later → GPLv3 to unlock CC-BY-SA 4.0 assets? If "no", CC-BY-SA is entirely off the table (simpler, recommended default: **stick to CC0 / PD / CC-BY / GPL only**).
3. **First Nations protocol (blocking):** commission/arrange cultural consultation before **any** First Nations visual representation. Raise as a dedicated 4b task. Nothing proceeds here without it.
4. **Accept the two big art gaps** as reskin/custom work, not sourcing: (a) distinctly Australian-colonial **units** (convict, shepherd, digger, drover, telegraph worker), (b) industrial-Victorian **buildings** (goldfields office, freezing works, wool/shearing shed, telegraph office, rail depot, federation/convention halls). No open pack covers these.

---

## Provenance / pipeline reminder

When any of these are actually adopted, the pipeline **must** (per `docs/DOCUMENTATION.md`): record each file's source URL + exact licence + attribution in a per-folder `PROVENANCE.md`, aggregate into root `CREDITS.md`, mirror into the **ClickUp Asset Register (doc 05)**, and reject anything failing the per-file licence check. This research document is the candidate list only — it is **not** approval to import.
