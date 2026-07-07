# First Nations Cultural Groups

## Important note

This list is for game-design scaffolding only. Boundaries should be treated as approximate. The AIATSIS map and similar resources show broad language, social, and nation groupings; they should not be treated as exact borders or as native title determinations.

Where possible, consult Traditional Owners and Indigenous-authored sources before finalising names, territories, event text, or cultural mechanics.

## Proposed regional groups

| Group / regional label | Broad game region | Gameplay identity |
|---|---|---|
| Eora / Dharug / Dharawal peoples | Sydney, Cumberland Plain, Illawarra | Early contact, coastal food, fishing, shellfish, Pemulwuy resistance chain. |
| Wiradjuri peoples | Central NSW | Riverine/inland trade, food knowledge, strong regional networks. |
| Gamilaraay / Kamilaroi peoples | Northern NSW / southern Queensland | Plains movement, trade routes, astronomy/travel flavour. |
| Turrbal / Jagera / Quandamooka peoples | Meanjin/Brisbane and Moreton Bay | River/coastal trade, bay navigation, fishing. |
| Bundjalung / Yugambeh peoples | Northern NSW / Gold Coast hinterland | Rainforest/coastal resources, timber, bush foods. |
| Kulin Nation | Port Phillip / central Victoria | Diplomacy, Wurundjeri relations, William Barak/Coranderrk chain. |
| Gunditjmara peoples | Western Victoria | Aquaculture/eel trap food economy, wetland management. |
| Yorta Yorta peoples | Murray-Goulburn region | River/wetland movement, canoe travel, food systems. |
| Kaurna peoples | Adelaide Plains | South Australian settlement contact and diplomacy. |
| Ngarrindjeri peoples | Lower Murray, Lakes, Coorong | River/lake navigation, reeds, fishing, canoe travel. |
| Palawa peoples | Tasmania | Island resources, seal/fish economy, severe frontier-history sensitivity. |
| Noongar peoples | South-west Western Australia | Six-season ecological knowledge, sandalwood/timber/food trade. |
| Yawuru / Nyikina / Kimberley groups | Kimberley / north-west | Coastal trade, mangroves, pearling-region flavour. |
| Larrakia peoples | Darwin region | Northern port diplomacy, maritime gateway. |
| Yolŋu peoples | North-east Arnhem Land | Maritime trade, Macassan-contact flavour, high cultural autonomy. |
| Arrernte peoples | Central Australia / Mparntwe | Desert water, central route knowledge, overland passage. |
| Warlpiri / Warumungu peoples | Tanami / Barkly / Central NT | Tracking, desert movement, long-distance routes. |
| Anangu Pitjantjatjara Yankunytjatjara peoples | Western Desert / Uluru region | Desert survival, water sites, high respect requirements for passage. |
| Torres Strait Islander peoples | Torres Strait Islands | Maritime travel, island trade, languages including Kala Lagaw Ya, Meriam Mir, Yumplatok. |

## Example group card schema

```yaml
id: first_nations.kulin
name: Kulin Nation
broad_region: Port Phillip / central Victoria
subgroups_or_related_names:
  - Wurundjeri
  - Boonwurrung
  - Taungurung
  - Dja Dja Wurrung
  - Wathaurong / Wadawurrung
primary_mechanics:
  - diplomacy
  - wetland_food
  - agreement_council
linked_figures:
  - historical_figure.william_barak
linked_events:
  - event.coranderrk_delegation
  - event.treaty_council
```

## Mechanical roles by environment

### Coastal peoples

Potential bonuses from high respect:

- Coastal fishing.
- Shellfish gathering.
- Canoe travel.
- Storm/reef navigation.

### River peoples

Potential bonuses:

- River movement.
- Wetland food.
- Canoe trade.
- Drought resilience near rivers.

### Desert peoples

Potential bonuses:

- Desert water knowledge.
- Reduced expedition attrition.
- Safe passage across arid regions.
- Bush food knowledge.

### Rainforest / tropical peoples

Potential bonuses:

- Bush foods.
- Timber knowledge.
- Tropical disease/supply mitigation.
- Rainforest movement.

### Island / maritime peoples

Potential bonuses:

- Island navigation.
- Maritime trade.
- Fishing yield.
- Storm movement mitigation.

## Resistance event examples

### Pemulwuy resistance chain

Region:

- Eora/Dharug/Dharawal context.

Prerequisites:

- Sydney region settlement expansion.
- Country Pressure high.
- Respect low or violence occurred.
- Year window roughly 1790s–1802.

Effects:

- Raids or disruption near high-pressure settlements.
- Military response option increases violence/legitimacy penalty.
- Negotiation/compensation option can reduce future tension.

### Frontier pressure chain

Generic regional chain for many groups.

Prerequisites:

- Pastoral or mining expansion without agreement.
- Livestock/property conflict.
- Tension above threshold.

Effects:

- Trade closes.
- Knowledge exchange pauses.
- Resistance events become possible.
- Reform backlash in civic towns if frontier violence becomes known.

## Naming caution

Many group names have spelling variants, contested boundaries, or local preferences. Use broad region labels only as placeholders until reviewed.
