# Colony Progression and Prerequisites

## Purpose

This file defines the game-state prerequisites that should drive Historical Figures, event chains, era transitions, and Federation progression.

## Global progression variables

| Variable | Type | Description |
|---|---|---|
| `year` | integer | Current campaign year. |
| `era` | enum | Survival, Wool, Separate Colonies, Gold Rush, Infrastructure, Federation. |
| `civic_voice_total` | integer | Total generated Civic Voice. |
| `civic_voice_per_turn` | integer | Current Civic Voice generation. |
| `federation_support[colony]` | percent | Federation support by colony. |
| `anti_federation[colony]` | percent | Opposition by colony. |
| `convention_points` | integer | Points for constitutional drafting. |
| `constitution_progress` | percent | Draft Constitution completion. |
| `imperial_pressure` | integer | Crown/imperial oversight pressure. |
| `frontier_legitimacy` | integer | National ethical/political legitimacy score. |
| `country_pressure[group]` | integer | First Nations Country pressure by group. |
| `respect[group]` | integer | First Nations relationship score. |
| `tension[group]` | integer | Conflict risk score. |
| `intercolonial_trade_links` | integer | Active trade links between colony regions. |
| `telegraph_connected_regions` | integer | Colony regions connected by telegraph. |
| `rail_connected_regions` | integer | Colony regions connected by rail. |

## Settlement maturity levels

### Outpost

Requirements:

- Population 1–2.
- Government Stores or basic food store.
- No Town Hall.

Typical mechanics:

- Vulnerable to starvation.
- Low Civic Voice.
- High supply dependence.

### Township

Requirements:

- Population 3+.
- Food surplus.
- Basic road or port connection.

Typical mechanics:

- Eligible for early public works perks.
- Can build Town Hall.
- Can host local events.

### Colonial Town

Requirements:

- Population 6+.
- Town Hall.
- Market or Wharf.
- At least one specialist building.

Typical mechanics:

- Generates meaningful Civic Voice.
- Can host regional administration.
- Eligible for Federation Support tracking.

### Colonial Capital

Requirements:

- Population 10+.
- Town Hall.
- Court House.
- Newspaper or School.
- Port or intercolonial road/rail link.

Typical mechanics:

- Can represent a colony region in the Federation system.
- Can host Convention events.
- Can build Federation League Hall.

## Colony region activation

A colony region becomes politically active when one of these is true:

- It has a Colonial Capital.
- It has at least two Colonial Towns.
- It has a Historical Figure or event that specifically activates it.
- Scenario setup marks it as an existing colony.

## Era transition conditions

### Survival to Wool Era

Triggers when any two are true:

- Year is at least 1797.
- Sheep or wool has been imported.
- Food crisis has been survived.
- First inland farm established.
- Elizabeth Macarthur is eligible or attained.

### Wool Era to Separate Colonies Era

Triggers when any three are true:

- Year is at least 1830.
- Two colony regions are politically active.
- At least one port outside NSW exists.
- Wool export route is active.
- Free Settler immigration has begun.

### Separate Colonies to Gold Rush Era

Triggers when any two are true:

- Year is at least 1851.
- Gold is discovered.
- Edward Hargraves attained.
- A hill/mountain region has been surveyed with high mineral chance.

### Gold Rush to Infrastructure Era

Triggers when any three are true:

- Year is at least 1872.
- At least three colony regions are politically active.
- Telegraph route surveyed.
- Rail or road network connects three regions.
- Thomas Mort or Charles Todd is eligible.

### Infrastructure to Federation Era

Triggers when any three are true:

- Year is at least 1889.
- Henry Parkes attained.
- Four colony regions are politically active.
- Civic Voice per turn exceeds threshold.
- At least two newspapers exist.
- Intercolonial trade friction exceeds threshold.

## Historical Figure prerequisite types

| Type | Example |
|---|---|
| Date gate | Lachlan Macquarie requires 1810+. |
| Resource gate | Edward Hargraves requires gold potential or 1851+. |
| Building gate | Louisa Lawson requires newspaper/printing press. |
| Regional gate | Mary Lee requires South Australia active or reform support. |
| Relationship gate | William Barak requires high respect with Kulin/Wurundjeri communities. |
| Crisis gate | Peter Lalor requires goldfield unrest. |
| Infrastructure gate | Charles Todd requires telegraph route surveyed. |
| Civic gate | Henry Parkes requires Civic Voice or 1889+. |

## Recommended event trigger formula

An event becomes eligible when:

```
all_required_conditions = true
AND current_year >= earliest_year
AND event_not_expired
AND cooldown_finished
AND random_roll <= event_weight
```

Events tied to Historical Figures should use:

```
figure_attained = true
AND earliest_year_met = true
AND relevant_game_state_exists = true
```

## Suggested global thresholds

| Mechanic | Easy | Normal | Hard |
|---|---:|---:|---:|
| Civic Voice needed to reveal Federation Support | 300 | 500 | 750 |
| Convention Points needed for Draft Constitution | 500 | 800 | 1200 |
| Towns needed before Federation era | 3 | 4 | 5 |
| Colonies needed before Convention | 3 | 4 | 5 |
| Frontier legitimacy penalty threshold | 30 | 25 | 20 |
| First Nations high tension threshold | 70 | 60 | 50 |
| Respect needed for major agreement | 60 | 70 | 80 |

## Story designer note

Prerequisites should reward players who create the conditions for history-like developments without forcing them into a rigid timeline. The mode should feel historical when played naturally, but still allow alternate development paths.
