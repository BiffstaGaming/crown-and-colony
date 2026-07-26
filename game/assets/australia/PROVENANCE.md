# Australian art — provenance & Asset Register

All files under `game/assets/australia/` are the **Australian Federation variant**'s
art. They override the FreeCol base art per-file via the WS1.3 variant art seam
(`ColonyArt.Load` tries `res://assets/australia/<path>` first, else
`res://assets/freecol/<path>`, else null → text/placeholder). This file is the
authoritative provenance record; keep it and the ClickUp Asset Register (doc 05) in
sync whenever a file is added or replaced.

**Licensing rule (BINDING).** Every image must be **public domain** or otherwise
**GPL-v2-compatible** (CC0, CC BY, GPL, OFL). Record each supplied file's exact
source URL + licence in the table below **before it ships**. Never use anything
derived from the 1994/2008 Sid Meier game. When in doubt, leave it out and ask.

---

## Goods icons (`goods/<shortName>.png`)

The icon shown beside each good in the colony warehouse, the Europe market screen and the
reports, loaded by `ColonyArt.GoodsIcon(shortName)` through the WS1.3 variant art seam. A
**novel** short name has **no FreeCol fallback** (there is no `freecol/goods/gold.png`), so
without a file here the good renders **name-only**.

**These are all interim placeholders reused from our own committed FreeCol base art**
(`game/assets/freecol/goods/`, **GPL v2** — see [that folder's PROVENANCE](../freecol/PROVENANCE.md)).
Copying GPL art within this GPL-v2 project is licence-clean and needs no new sourcing.
Bespoke Australian goods art is a WS2 art-pass follow-up.

| File | Source (all GPL v2, from `assets/freecol/goods/`) | Why this placeholder |
|---|---|---|
| `gold.png` | `silver.png` | **Exact, not a compromise** — until WS6.1 the game *showed* the classic `silver` good renamed "Gold", so this is already the icon players associate with Gold. |
| `wool.png` | `cotton.png` | **Exact, same reason** — `cotton` was displayed as "Wool" before WS6.1. |
| `copper.png` | `ore.png` | A generic mineral/ore icon stands in for the copper ore export. |
| `coal.png` | `ore.png` | Ditto — a mineral icon for the bulk coal measures. |
| `sandalwood.png` | `lumber.png` | A timber icon for the aromatic-timber export. |
| `cattle.png` | `horses.png` | The only livestock icon available — Cattle breed like horses, so it reads sensibly. |
| `meat.png` | `food.png` | A foodstuff icon for the worked-up meat. |
| `frozenMeat.png` | `fish.png` | Chosen over `food.png` purely so Meat and Frozen Meat are **not identical on screen**. |

> **Follow-up (WS2 art pass):** `copper`/`coal` share one image (both `ore.png`) and are visually
> indistinct; `cattle`/`meat`/`frozenMeat` are stand-ins from unrelated goods. Bespoke art should
> replace all of those. **`gold` and `wool` are already correct** (they are the exact icons the
> game showed for Gold/Wool before WS6.1) and need no change.

---




## Top-down square terrain tiles (`terrain/<name>/top{0,1}.png`) — CC0 base + Australian re-tone

**Two-stage, and the first stage is genuinely sourced art.** Top-down is expected to become the game's main view, and it
had no art of its own: `MapView` warped the inscribed diamond of each 128×64 isometric tile onto a 64px square. Art drawn
as diamonds *cannot* tile as squares, so that de-skew showed **visible seams and repeating diagonal artefacts**, and only
part of each source image was ever sampled.

1. **Base — [ambientCG](https://ambientcg.com), CC0 (public domain).** Seamless 1K ground scans; no attribution required,
   and CC0 is unambiguously GPL-v2-compatible. Each game tile is a **256px crop scaled 4:1 to 64px** — a straight
   1024→64 downscale averages away all grain and the tile reads as a flat colour block on screen. **Two variants per
   terrain** from different crops, because a single tile repeated across a biome shows an obvious grid of identical clumps.
2. **Australian re-tone** — the same hue/saturation transform as the isometric art, so both projections read as one country.

| Terrain | ambientCG source | Reads as |
|---|---|---|
| desert | `Ground054` | red centre |
| plains / prairie / savannah | `Ground078` | straw / bleached / ochre dry grass |
| grassland | `Grass004` | dry gold pasture |
| marsh / swamp | `Ground048` | olive wetland |
| hills / mountains | `Rock061` | red-brown rock |
| ocean, high seas, arctic, tundra | *(none)* | fall back to the de-skewed FreeCol tile |

Pipeline: [`tools/build_topdown_terrain.py`](../../../tools/build_topdown_terrain.py), with the CC0 inputs committed under
`tools/cc0-ground/` so it runs offline and reproducibly.

**Licences rejected along the way, recorded so the search is not repeated:** an OpenGameArt seasonal isometric tileset
was **CC BY-SA 4.0** and an LPC terrain set is **CC BY-SA 3.0 / GPL 3.0** — both are one-way compatible with GPL **v3**
only, and this project is **GPL v2** (see `LICENSE`). Neither is usable however good it looks.

## Australian terrain + forest art (`terrain/`, `forest/`)

**Derivative of our own FreeCol art — GPL v2 → GPL v2.** Every file here is `game/assets/freecol/<same path>` put through a
**hue-rotation + saturation/value transform** in HSV; no pixel comes from anywhere else, so there is no new licence
obligation and no attribution beyond the FreeCol credit the project already carries (© The FreeCol Team, GPL v2+).

The transform is reproducible and is checked in as [`tools/retone_australian_terrain.py`](../../../tools/retone_australian_terrain.py)
— **the script is the provenance**: re-running it regenerates these files byte-for-byte from the FreeCol originals, and
the per-terrain numbers below are its tuning table. FreeCol's originals are never modified.

| Terrain | Reads as | Why |
|---|---|---|
| desert | burnt red ochre | the red centre — the single most recognisable Australian landscape, and the FreeCol original is a pale beige, so it needs a large saturation lift |
| grassland / plains / prairie | dry gold, straw | Australian pasture is bleached, not emerald |
| savannah | ochre grass | the tropical north's dry season |
| marsh / swamp | muted olive | |
| hills / mountains | red-brown rock | |
| broadleaf / mixed / conifer / boreal forest | dusty grey-green | **eucalypt**. The FreeCol originals already sit near 82° hue, so a hue change alone does nothing visible — what reads is dropping saturation |
| scrub forest | driest grey-green | mallee |
| tropical / rain forest | still genuinely green | the far north really is |
| ocean, high seas, arctic, tundra, unexplored | **untouched** | the sea is the sea, and the polar tiles never appear on the Australia map |

**Honest limitation:** this changes *colour*, not *silhouette*. A re-toned pine is still a pine. Distinctly Australian
shapes are a separate job.

## WS6.2 expert-unit icons (`units/<shortName>.png`)

Interim placeholders for the five Australian expert workers added with WS6.2, copied unmodified from our own FreeCol
art (`game/assets/freecol/units/`) — **GPL v2 → GPL v2**, © The FreeCol Team, so no new licence obligation. Each is a
stand-in with the right *silhouette* for the trade, not a bespoke sprite; they are replaced by the WS2.4 unit-art pass.

| File | Copied from | Why that source |
|---|---|---|
| `expertCopperMiner.png` | `freecol/units/expertOreMiner.png` | a miner reads as a miner |
| `expertCoalMiner.png` | `freecol/units/expertOreMiner.png` | same trade, same silhouette |
| `expertSandalwoodCutter.png` | `freecol/units/expertLumberJack.png` | a timber-getter with an axe |
| `masterButcher.png` | `freecol/units/masterDistiller.png` | an apron-and-barrel processing worker |
| `masterFreezingWorker.png` | `freecol/units/masterDistiller.png` | as above |

Copper and Coal deliberately share one source and Butcher/Freezing-Works Hand share another: the two pairs are the same
trade at different tiers, and inventing a spurious visual difference between them would be worse than the honest reuse.

## Pioneer portraits (`fathers/<shortName>.jpg`, target 200×237)

The head-and-shoulders portrait shown beside each Australian Pioneer in the
**Federation Convention** dialog (`presentation/FoundingFatherPanel.cs`) and the
**Colopedia → Fathers** tab (`presentation/ColopediaPanel.cs`), loaded by
`ColonyArt.FatherPortrait`. Filenames are the ruleset **short name** so the loader is
a direct lookup. Absent → the Pioneer renders text-only (graceful).

**Status: 24 / 25 supplied** (added 2026-07-26). Every Pioneer but **James Ruse** now has a vetted portrait —
sourced from Wikimedia Commons, licence read from each file's own Commons metadata, and recorded in the
**Supplied portraits** table below. **`jamesRuse` is deliberately text-only**: no authenticated likeness of him
exists (the sourcing note below flagged this in advance, and a Commons sweep confirmed it — the only "James Ruse"
images are of the road, bridge and high school named after him). Inventing or mislabelling one would be worse than
the graceful text-only fallback, so he stays without a portrait unless an authenticated likeness surfaces.

Images were fetched at 480px width (Commons thumbnail renditions, 14–117 KB each) rather than full resolution:
the panel draws them at roughly 200×237, so the larger originals would be pure repository weight.

### Supplied portraits (added 2026-07-26)

All files are `fathers/<shortName>.jpg`. "Licence" is the licence Wikimedia Commons states on the file page.

| Filename | Commons file | Date | Author / holder | Licence | Source (file page) |
|---|---|---|---|---|---|
| `arthurPhillip.jpg` | Arthur_Phillip_-_Wheatley_ML124_(cropped).jpg | 1786 | Francis Wheatley | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Arthur_Phillip_-_Wheatley_ML124_(cropped).jpg) |
| `carolineChisholm.jpg` | Caroline_Chisholm,_1852_SLNSW_FL3259987.jpg | 1852 | Angelo Collen Hayter | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Caroline_Chisholm,_1852_SLNSW_FL3259987.jpg) |
| `catherineHelenSpence.jpg` | Catherine_Helen_Spence_c_1900_side_portrait.jpg | circa 1900 | Unknown author | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Catherine_Helen_Spence_c_1900_side_portrait.jpg) |
| `charlesSturt.jpg` | Charles_Sturt_by_John_Michael_Crossland_lowres_color.jpg | 1853 | anonymous | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Charles_Sturt_by_John_Michael_Crossland_lowres_color.jpg) |
| `charlesTodd.jpg` | Charles Todd.jpeg | Dated 1872. | — | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Charles_Todd.jpeg) |
| `edmundBarton.jpg` | Edmund_Barton_-_Swiss_Studios_(b&w).jpg | 1902 | Swiss Studios | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Edmund_Barton_-_Swiss_Studios_(b%26w).jpg) |
| `edwardHargraves.jpg` | Hargraves_by_Thomas_Balcombe_1851.jpg | 1851-06 | Thomas Tyrwitt Balcombe | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Hargraves_by_Thomas_Balcombe_1851.jpg) |
| `elizabethMacarthur.jpg` | ElizabethMacarthur.jpg | undated - 19th century | Unidentified painter | Public domain | [file page](https://commons.wikimedia.org/wiki/File:ElizabethMacarthur.jpg) |
| `georgeFifeAngas.jpg` | George_Fife_Angas.jpg | 15 March 2005 (original upload date) | The original uploader was Diceman at English Wikipedia. | Public domain | [file page](https://commons.wikimedia.org/wiki/File:George_Fife_Angas.jpg) |
| `henryParkes.jpg` | Henryparkes.jpg | — | Unknown author | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Henryparkes.jpg) |
| `johnMcDouallStuart.jpg` | John_McDouall_Stuart_(Portrait).jpg | circa 1860 | Unknown author | Public domain | [file page](https://commons.wikimedia.org/wiki/File:John_McDouall_Stuart_(Portrait).jpg) |
| `johnQuick.jpg` | John_Quick_-_W._Vincent_Kelly_(cropped).jpg | 1910s | W. Vincent Kelly | Public domain | [file page](https://commons.wikimedia.org/wiki/File:John_Quick_-_W._Vincent_Kelly_(cropped).jpg) |
| `lachlanMacquarie.jpg` | Ln-Governor-Lachlan_macquarie.jpg | between circa 1805 and circa 1824 | John Opie? | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Ln-Governor-Lachlan_macquarie.jpg) |
| `louisaLawson.jpg` | Louisa_Lawson_V1-FL3303627.jpg | ca. 1885 | Unknown | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Louisa_Lawson_V1-FL3303627.jpg) |
| `ludwigLeichhardt.jpg` | Ludwig_Leichhardt.jpg | — | Friedrich August Schmalfuß (1791–1876), (Leichhardt-Museum, Trebatsch) | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Ludwig_Leichhardt.jpg) |
| `maryLee.jpg` | Mary_Lee.jpg | 1885 | State Library of South Australia | CC BY 2.0 | [file page](https://commons.wikimedia.org/wiki/File:Mary_Lee.jpg) |
| `maryReibey.jpg` | Mary_Reibey_State_Library_of_NSW_Min_76.jpg | circa 1835 | Unknown author | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Mary_Reibey_State_Library_of_NSW_Min_76.jpg) |
| `matthewFlinders.jpg` | Toussaint_Antoine_DE_CHAZAL_DE_Chamerel_-_Portrait_of_Captain_Matthew_Flinders,_RN,_1774-1814_-_Google_Art_Project.jpg | from 1806 until 1807 | Antoine Toussaint de Chazal | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Toussaint_Antoine_DE_CHAZAL_DE_Chamerel_-_Portrait_of_Captain_Matthew_Flinders,_RN,_1774-1814_-_Google_Art_Project.jpg) |
| `peterLalor.jpg` | Peter_Lalor_(cropped).jpg | before 1889 (subject's death) | not stated | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Peter_Lalor_(cropped).jpg) |
| `samuelGriffith.jpg` | Sir_Samuel_Walker_Griffith.jpg | uncertain (subject died 1920) | State Library of Queensland | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Sir_Samuel_Walker_Griffith.jpg) |
| `sidneyKidman.jpg` | Sidney_Kidman.jpg | 1927 | Unknown author | Public domain | [file page](https://commons.wikimedia.org/wiki/File:Sidney_Kidman.jpg) |
| `thomasSutcliffeMort.jpg` | TSMort&TheresaAbt1847.jpg | 1846 | Unknown author | Public domain | [file page](https://commons.wikimedia.org/wiki/File:TSMort%26TheresaAbt1847.jpg) |
| `williamJervois.jpg` | William_Jervois.jpg | — | — | Public domain | [file page](https://commons.wikimedia.org/wiki/File:William_Jervois.jpg) |


**Vetting outcomes worth recording.**
- The six figures the sourcing notes flagged ⚠ ("must be a **photograph**, not a painting, because the sitter died
  1920–1935 but a painting runs life-of-artist + 70y") all resolved to genuine studio **photographs**: Edmund Barton
  (Swiss Studios, 1902), John Quick (W. Vincent Kelly, 1910s), Samuel Griffith (State Library of Queensland),
  William Jervois, Louisa Lawson (c. 1885) and **Sidney Kidman (1927 — the ⚠⚠ riskiest entry, and comfortably
  pre-1929 as required)**. The PD basis therefore holds for each.
- **`maryLee.jpg` is CC BY 2.0, not public domain** (State Library of South Australia, 1885). CC BY is
  GPL-v2-compatible for distribution but **carries an attribution obligation** — it is credited in `CREDITS.md`.
  It is the only non-PD portrait in the set.

> **Public-domain basis, in brief.** In Australia a photograph *taken before
> 1 Jan 1955* is PD regardless of photographer; a *painted* portrait runs
> life-of-artist + 70 years. As of 2026, anything *published before 1931* is PD in the
> US. Every row below has a valid pre-1955 / pre-1931 basis — but for the five figures
> flagged ⚠ you must pick the **right image type** (a photograph, not a modern
> painting) for the basis to actually hold. See "Vetting caveats" after the table.

| Filename (`.jpg`) | Figure (life) | PD basis | Recommended source (search term) | Source URL | Licence |
|---|---|---|---|---|---|
| `arthurPhillip` | Arthur Phillip (1738–1814) | died 1814; Wheatley 1786 oil, artist d.1801 | Wikimedia Commons cat. "Arthur Phillip" (Wheatley 1786); NPG item 2010.54 | | |
| `lachlanMacquarie` | Lachlan Macquarie (1762–1824) | died 1824; Richard Read Sr watercolour c.1822 | State Library of NSW (Mitchell); Commons cat. "Lachlan Macquarie" | | |
| `jamesRuse` ⚠ | James Ruse (1759–1837) | died 1837 — **no authenticated likeness exists** | SLNSW "First farms"; headstone photo (St John's, Campbelltown) — see caveat | | |
| `maryReibey` | Mary Reibey (1777–1855) | died 1855; c.1835 miniature (artist unknown → 70y from creation) | SLNSW (Mitchell); Commons cat. "Mary Reibey" (the $20-note miniature) | | |
| `williamJervois` ⚠ | Sir William Jervois (1821–1897) | died 1897; use a pre-1931 **photograph** (Solomon Studios cabinet card) | NPG item 2015.56; Commons cat. "William Jervois"; search "Jervois Governor SA" | | |
| `matthewFlinders` | Matthew Flinders (1774–1814) | died 1814; de Chazal **oil c.1806–07** (artist d.1822), not an "1801 miniature" | Art Gallery of SA ("Portrait of Captain Matthew Flinders, RN"); Commons "Flinders de Chazal" | | |
| `charlesSturt` | Charles Sturt (1795–1869) | died 1869; Crossland oil / Koberwein portrait | State Library of SA; Art Gallery of SA; Commons "Charles Sturt portrait" | | |
| `ludwigLeichhardt` | Ludwig Leichhardt (1813–disap. 1848) | presumed died 1848; William Nicholas portrait c.1846 | NPG (portrait.gov.au); SLNSW (Mitchell); Commons "Ludwig Leichhardt portrait" | | |
| `johnMcDouallStuart` | John McDouall Stuart (1815–1866) | died 1866; 1860s cartes-de-visite | State Library of SA (SLSA Stuart LibGuide); Commons "John McDouall Stuart" | | |
| `charlesTodd` | Sir Charles Todd (1826–1910) | died 1910; c.1900 SLSA portrait (catalogued PD) | State Library of SA (collections.slsa.sa.gov.au "Charles Todd"); Commons | | |
| `elizabethMacarthur` | Elizabeth Macarthur (1766–1850) | died 1850; c.1785–90 miniature | SLNSW (Mitchell, "likely to be of…"); Commons "Elizabeth Macarthur" | | |
| `thomasSutcliffeMort` | Thomas Sutcliffe Mort (1816–1878) | died 1878; 19th-c. engraving/carte-de-visite | SLNSW; Commons "Thomas Sutcliffe Mort" | | |
| `georgeFifeAngas` | George Fife Angas (1789–1879) | died 1879; 19th-c. oil/engraving | State Library of SA; History Trust of SA (Adelaidia); Commons "George Fife Angas" | | |
| `edwardHargraves` | Edward H. Hargraves (1816–1891) | died 1891; Thomas Balcombe 1851 portrait | SLNSW (Balcombe 1851); Commons "Edward Hargraves" | | |
| `sidneyKidman` ⚠⚠ | Sir Sidney Kidman (1857–1935) | died 1935 — **use a demonstrably pre-1929 photograph** (riskiest entry) | State Library of SA / NLA Trove — pick a clearly pre-1929 photo; verify catalogue date | | |
| `henryParkes` | Sir Henry Parkes (1815–1896) | died 1896; any 19th-c. portrait/photo | SLNSW (Mitchell, Parkes papers); NPG; Commons "Henry Parkes" | | |
| `edmundBarton` ⚠ | Sir Edmund Barton (1849–1920) | died 1920; use a pre-1931 **photograph** | NLA/Trove; NPG (edmund-barton-1849); Commons "Edmund Barton" | | |
| `johnQuick` ⚠ | Sir John Quick (1852–1932) | died 1932; use a pre-1931 **photograph** | NLA/Trove; firstparliament.senate.gov.au; Commons "John Quick (politician)" | | |
| `samuelGriffith` ⚠ | Sir Samuel Griffith (1845–1920) | died 1920; use a pre-1931 **photograph** | State Library of Qld; NPG (samuel-**walter**-griffith-1845 — sic); High Court coll. | | |
| `catherineHelenSpence` | Catherine Helen Spence (1825–1910) | died 1910; 19th/early-20th-c. portrait | State Library of SA (B+11192, B+36575); NLA/Trove; Commons | | |
| `carolineChisholm` | Caroline Chisholm (1808–1877) | died 1877; Hayter 1852 oil / Hunt 1853 engraving | SLNSW (Mitchell/Dixson, Hayter 1852); NPG; Commons (Hunt 1853 engraving) | | |
| `peterLalor` | Peter Lalor (1827–1889) | died 1889; Becker 1856 lithograph (SLV H5601) | State Library of Victoria (Pictures, H5601); Commons "Peter Lalor" | | |
| `maryLee` | Mary Lee (1821–1909) | died 1909; c.1895 studio photo | State Library of SA (B 58378, B 57233); Commons "Mary Lee suffragist" | | |
| `louisaLawson` ⚠ | Louisa Lawson (1848–1920) | died 1920; use a pre-1931 **photograph** (c.1880 onward) | NLA/Trove (c.1880 portrait); SLNSW (Mitchell/Dixson, Holtermann); Commons | | |

### Vetting caveats (read before sourcing the ⚠ rows)

- **Photograph, not a painting, for the five who died 1920–1935** — Edmund Barton
  (d.1920), Samuel Griffith (d.1920), John Quick (d.1932), Louisa Lawson (d.1920),
  William Jervois (d.1897, borderline), and especially **Sidney Kidman (d.1935)**. A
  painted portrait runs *life-of-artist + 70y*, so a portrait *painting* by an artist
  who lived past ~1955 can still be in copyright even though the sitter died long ago.
  The PD basis only holds if you pick a **photograph** published pre-1931 / taken
  pre-1955. Confirm the image type and date from the catalogue record.
- **Sidney Kidman (⚠⚠) is the single riskiest entry.** Confirm the chosen photograph is
  demonstrably **pre-1929** from its catalogue metadata before use; avoid any 1931–1935
  or posthumous image. If nothing clean can be confirmed, leave him text-only.
- **James Ruse has no authenticated contemporary likeness.** Do **not** let any AI/stock
  "portrait of James Ruse" slip in as authentic. Options: a photo of his self-carved
  1837 headstone (image itself PD), or a clearly-labelled commemorative depiction — or
  leave him text-only. Flag the choice here when made.
- **Mary Reibey** — the miniature's artist is *unknown*, so the PD basis is the
  **anonymous-work** rule (70 years from creation/publication, satisfied for c.1835),
  **not** an author-death calculation. Record that basis explicitly.
- **Samuel Griffith** — the NPG URL spells the middle name **"Walter"** while his legal
  middle name is **"Walker"** (used above). The mismatch is expected, not a typo to fix.

### `williamBarak.jpg` — SUPPLIED (signed off 2026-07-12)

William Barak (Wurundjeri *ngurungaeta*, c.1824–1903), the 25th Pioneer. His portrait was **held** pending a
First Nations cultural-protocol / ICIP decision (doc 15); **Chris signed off on 2026-07-12** and chose this image.

| Field | Value |
|---|---|
| **Image** | Studio portrait photograph of William Barak (bearded, dark buttoned jacket), Coranderrk. |
| **Photographer / date** | **Carl Walter, 1866** (his Coranderrk series, exhibited at the 1866–67 Intercolonial Exhibition). |
| **Original held by** | State Library of Victoria (the physical albumen print). |
| **File source** | Wikimedia Commons — [`File:William Barak 1866.jpg`](https://commons.wikimedia.org/wiki/File:William_Barak_1866.jpg) (downloaded 2026-07-12, 622×830). |
| **Licence** | **Public domain — PD-Australia**: an Australian photograph taken before 1 Jan 1956, copyright expired. GPL-v2-compatible for distribution. |

**Cultural note (informational, recorded for transparency).** Copyright has expired, but this is an image of a
First Nations ancestor: the SLV's own catalogue marks its copy *"all rights reserved"* (an institutional
reproduction claim, not a subsisting copyright), and the living cultural authority for Barak's people is the
[Wurundjeri Woi Wurrung Cultural Heritage Aboriginal Corporation](https://www.wurundjeri.com.au/our-story/ancestors-past/).
The ICIP layer sits beyond copyright; this was a deliberate, signed-off decision — see task `86d3n855a`.
