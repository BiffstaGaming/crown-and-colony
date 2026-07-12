# Australian Pioneer portraits — drop vetted images here

This folder holds the head-and-shoulders portraits for the **Australian Pioneers**
(the variant's "founding fathers"). It is wired but **deliberately empty** until
vetted public-domain images are dropped in — see the licensing rule below.

## How the loading works (no code change needed)

`ColonyArt.FatherPortrait(shortName)` routes through the WS1.3 **variant art seam**:
for the Australia variant it tries `res://assets/australia/fathers/<shortName>.jpg`
**first**, and only falls back to the FreeCol base art (which has no Australian
portrait) → **null → text-only**. So dropping a correctly-named file here makes the
portrait appear automatically in:

- the **Federation Convention** dialog (`presentation/FoundingFatherPanel.cs`), and
- the **Colopedia → Fathers** tab (`presentation/ColopediaPanel.cs`).

No recompile, no wiring — just the image file (Godot generates the `.import`
sidecar on first import).

## Naming + format

- **Filename:** `<shortName>.jpg` — exactly the ruleset short name (camelCase), e.g.
  `henryParkes.jpg`, `edmundBarton.jpg`. A mismatch simply doesn't load.
- **Format:** JPEG. Match the FreeCol portraits' **200×237** (roughly 5:6) head-and-
  shoulders crop so the thumbnail sits cleanly beside the choose button. Any
  reasonable portrait works; off-aspect images are letterboxed (KeepAspectCentered).

## The 24 filenames to supply

henryParkes · edmundBarton · johnQuick · samuelGriffith · catherineHelenSpence ·
elizabethMacarthur · thomasSutcliffeMort · georgeFifeAngas · edwardHargraves ·
sidneyKidman · matthewFlinders · charlesSturt · ludwigLeichhardt ·
johnMcDouallStuart · charlesTodd · arthurPhillip · lachlanMacquarie · jamesRuse ·
maryReibey · williamJervois · carolineChisholm · peterLalor · maryLee · louisaLawson

Recommended per-figure sources + the public-domain rationale for each are in
[`../PROVENANCE.md`](../PROVENANCE.md) (the Asset Register).

## Licensing — BINDING

Every image must be **public domain** or otherwise **GPL-v2-compatible** (CC0, CC BY,
GPL, etc.), and its source URL + licence **must** be recorded in
[`../PROVENANCE.md`](../PROVENANCE.md) before it ships. When in doubt, leave it out
and ask. Never use an image derived from the 1994/2008 Sid Meier game.

## `williamBarak.jpg` — SUPPLIED (signed off 2026-07-12)

William Barak (Wurundjeri *ngurungaeta*) was **held** pending a First Nations
cultural-protocol / ICIP decision (doc 15); **Chris signed off on 2026-07-12**. The
supplied file is Carl Walter's **public-domain (PD-Australia) 1866 photograph**, from
Wikimedia Commons — full provenance + the cultural note are in
[`../PROVENANCE.md`](../PROVENANCE.md). He now renders like any other Pioneer.
