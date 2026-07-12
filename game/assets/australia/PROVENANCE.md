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

## Pioneer portraits (`fathers/<shortName>.jpg`, target 200×237)

The head-and-shoulders portrait shown beside each Australian Pioneer in the
**Federation Convention** dialog (`presentation/FoundingFatherPanel.cs`) and the
**Colopedia → Fathers** tab (`presentation/ColopediaPanel.cs`), loaded by
`ColonyArt.FatherPortrait`. Filenames are the ruleset **short name** so the loader is
a direct lookup. Absent → the Pioneer renders text-only (graceful).

**Status: 1 / 25 supplied** — **`williamBarak.jpg`** (signed off + added 2026-07-12; see the bottom of this
section). The other 24 remain to source: the "recommended source" column is a sourcing guide (researched +
PD-verified 2026-07-10); Chris sources and vets the actual files, then fills **Source URL** and **Licence**.

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
