# Design Principles and Scope

## High-level concept

Australian Federation Mode is a Colonization-style campaign where the player develops colonial settlements across Australia and moves from fragile settlement to self-government and Federation.

The mode should feel mechanically familiar to FreeCol players, but its story should not be a direct reskin of the United States independence narrative.

## Historical scope

**Start:** 1788, First Fleet / Sydney Cove scenario setup.  
**End:** 1901, Commonwealth of Australia proclaimed after successful Federation process.

## Core campaign fantasy

The player is not trying to defeat a Royal Expeditionary Force. Instead, the player must overcome:

- Food scarcity and unfamiliar conditions.
- Long supply lines to Britain and Europe.
- Fragmented colonies with competing interests.
- Frontier pressure and First Nations diplomacy/conflict consequences.
- Transport, communications, and intercolonial tariff barriers.
- Civic reform, migration pressure, gold-rush disruption, and constitutional politics.

## Core design pillars

### 1. Federation replaces independence war

The traditional FreeCol climax is a declaration of independence followed by war. This mode should culminate in a **Federation referendum sequence** and Commonwealth proclamation.

### 2. Historical events are conditional

Events should not simply fire because the calendar reached a year. Most historical events should have:

- A date gate.
- One or more gameplay prerequisites.
- Optional randomness or player agency.
- Alternative outcomes.
- Expiry conditions.

Example: **Macquarie Governorship** should not be a forced 1810 event. It should require:

- Year is at least 1810.
- Lachlan Macquarie Historical Figure has been attained.
- At least one established colony needs public works or administrative reform.

### 3. Historical Figures are gameplay cards

Historical Figures replace Founding Fathers. Each should have:

- Category.
- Short biography.
- Historical rationale.
- Earliest availability year.
- Prerequisites.
- Cost type.
- Gameplay perk.
- Linked event hooks.
- Balance notes.

### 4. First Nations systems must not be generic

Australia must not use one generic native faction. First Nations groups should be represented regionally and respectfully, with Country, diplomacy, knowledge exchange, and frontier tension systems.

### 5. The game should acknowledge colonial harm

The campaign can still be a strategy game, but it should not make conquest of First Nations communities into a simple profit loop. Land pressure, disease, violence, displacement, and resistance should be represented as serious systems with political and moral consequences.

## Recommended tone

Historical strategy with restrained critique. The player should have meaningful options to behave better or worse than history, while the documentation and event text should be honest about dispossession, exclusion, and frontier violence.

## Primary campaign assumptions

- The main historical campaign is British Australia.
- Other European powers can exist as alternate-history sandbox starts.
- The main map should include Australia and nearby maritime approaches.
- Six colonial regions matter politically: New South Wales, Victoria, Queensland, South Australia, Tasmania, Western Australia.
- First Nations groups are regional cultural/language groupings, not a single faction.

## Design risk notes

- Do not use the term `tribe` as the default mechanical label. Prefer `community`, `people`, `nation`, `Country`, or the specific group name.
- Do not call all First Nations peoples `Aborigines`. Use `First Nations peoples`, or specific names.
- The AIATSIS map is useful for broad guidance, but it does not define exact boundaries.
- Federation was not a universally democratic victory. Many people were excluded or restricted from voting, including many Aboriginal and Torres Strait Islander peoples and many non-European residents.
