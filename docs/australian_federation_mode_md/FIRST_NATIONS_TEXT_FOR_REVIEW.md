# First Nations text — draft for Chris to read

**Status: APPROVED by Chris on 2026-07-26 and now IN THE GAME.**
This document is retained as the provenance record for the wording — what was written, why, and what was deliberately
left out. The live text is in `game/data/rules/australia/specification.xml` on each `<indian-nation-type>`.
Written and approved 2026-07-26, under Chris's decision that same day: *"I write it, you read it."* The sections below
are preserved as written for review — see **Outcome** at the end for what shipped.

---

## What this is, in one paragraph

Your game already has eight First Nations peoples in it as playable-world nations — Eora, Kulin, Noongar, Wangkatja,
Larrakia, Yolŋu, Yawuru and Arrernte. *(At the time of writing)* a player who clicks on one sees **a name and nothing else**. Every
other thing in the game (every building, unit, good, historical figure) has an encyclopedia entry explaining what it
is. These don't. That's the gap this fills.

## How I wrote it

Rules I set myself, because you're the only reviewer:

1. **Only well-documented, publicly-stated facts** — who the Country belongs to, where it is, what the historical
   record widely records about living and working there. Nothing invented for flavour.
2. **Present tense for continuing existence.** These are peoples who exist now, not historical curiosities. Every entry
   says so.
3. **No sacred, ceremonial, or restricted material.** Nothing about law, ceremony, kinship, story or belief. That is
   not mine to write, it is frequently restricted knowledge, and a strategy game is the wrong place for it.
4. **No invented individuals, dialogue or speech.** The only named people are ones already in your historical-figures
   list (e.g. William Barak, who you signed off on in July).
5. **Colonisation stated plainly, not dwelt on.** Where dispossession is the central fact of the contact period, the
   entry says so in a sentence. It does not editorialise and it does not soften.
6. **Spelling** follows the forms most commonly used by the peoples themselves where that is clear (e.g. Yolŋu with the
   ŋ, Noongar). Your design doc 16 already flags that variants exist.

## What I deliberately did NOT write

- Any artwork or imagery (that's a separate job, and a harder one).
- Any description of ceremony, law, story, or belief.
- Descriptions of the ~11 further peoples in doc 16 who aren't yet in the game — no point writing content for groups
  that don't exist in the data until we decide to add them.
- Anything for the "resistance event" chains in doc 16 (e.g. Pemulwuy). Those are dramatised historical episodes
  involving killing, and I'd want your explicit yes on each one separately rather than bundled in here.

---

# The eight entries

Each is what a player would read in the encyclopedia. Roughly 45–70 words each — the same length as your existing
entries.

### Eora
**Country:** Sydney basin and coastal New South Wales.

> The Eora are the coastal people of the Sydney basin, on whose Country the First Fleet anchored in January 1788.
> Theirs was a marine economy — fishing from bark canoes, gathering shellfish along the harbour and its inlets. The
> arrival of the colony brought dispossession and a catastrophic smallpox epidemic within two years. Eora people
> remain in Sydney today.

### Kulin
**Country:** Port Phillip and central Victoria.

> The Kulin are an alliance of neighbouring peoples — among them the Wurundjeri, Boonwurrung, Taungurung, Dja Dja
> Wurrung and Wadawurrung — whose Country covers Port Phillip and central Victoria. The 1835 arrival of settlers at
> the Yarra began a rapid dispossession. Kulin leaders, William Barak foremost among them, spent the following decades
> petitioning colonial governments for land and rights.

### Noongar
**Country:** South-west Western Australia.

> The Noongar are the people of the entire south-west corner of Western Australia, one of the largest Aboriginal
> cultural blocs on the continent. Their calendar divides the year into six seasons rather than four, tracking the
> south-west's distinct cycles of weather, plant and animal life. Noongar Country was taken up for farming and timber
> from the 1830s onward.

### Wangkatja
**Country:** Western Australian goldfields and interior.

> The Wangkatja peoples hold Country across the arid Western Australian interior and goldfields. Living in that
> country depends on precise knowledge of water — soaks, rock holes and springs — and of the routes between them. The
> gold rushes of the 1890s drove thousands of prospectors across that Country in a matter of years.

### Larrakia
**Country:** Darwin region, Northern Territory.

> The Larrakia are the saltwater people of the Darwin region, with Country covering the harbour, coast and nearby
> islands. Their long-standing trade and travel connections run along the northern coast. The founding of Palmerston,
> later Darwin, in 1869 placed a colonial port directly on Larrakia Country. They are known today as the traditional
> owners of Darwin.

### Yolŋu
**Country:** North-east Arnhem Land.

> The Yolŋu of north-east Arnhem Land traded with Macassan mariners from Sulawesi for centuries before British
> settlement — an established international trade in trepang that predates the colony by generations. Arnhem Land's
> remoteness meant colonial settlement reached Yolŋu Country later and less completely than the south-east, and Yolŋu
> communities retain a high degree of autonomy.

### Yawuru
**Country:** Broome and the west Kimberley coast.

> The Yawuru are the coastal people of the Broome region on the west Kimberley coast, their Country taking in
> mangroves, mudflats and the waters beyond. From the 1880s the pearling industry transformed the region, drawing
> workers from across Asia and the Pacific and relying heavily — and often coercively — on Aboriginal labour.

### Arrernte
**Country:** Central Australia, around Mparntwe (Alice Springs).

> The Arrernte hold Country in the centre of the continent, around Mparntwe — the place the colonists called Alice
> Springs. Survival in the arid centre rests on detailed knowledge of water sources and the routes between them.
> Colonial expeditions and the Overland Telegraph Line of the 1870s cut directly through Arrernte Country, and the
> telegraph station became the town.

---

## The one entry I want you to look at hardest

**Eora.** It is the only entry that states a death toll event (the 1789 smallpox epidemic). I included it because
leaving it out would misrepresent what happened in the very place and decade your game opens, and because your design
principles explicitly call for truth-telling rather than a sanitised frontier. But it is the sharpest sentence in the
set, and if you want it softened or removed, say so — it's a one-line change.

## Outcome

Chris approved the text unchanged on 2026-07-26, including the Eora smallpox sentence. All eight entries are now in
`game/data/rules/australia/specification.xml` and appear under a **First Nations** heading on the Colopedia's Nations
tab. Guarded by `AustralianContentTests` (every people carries Country + description; present tense; classic authors
none) and by `ColopediaPanelTests` (the entries reach the screen; classic grows no section).

One correction made while wiring it in: the game now spells **Yolŋu** with the ŋ, via a new authored `display-name`
attribute — ruleset ids have to stay ASCII, but a people's name does not.

Still outstanding, unchanged: imagery, the further peoples from doc 16, and the resistance-event chains.
