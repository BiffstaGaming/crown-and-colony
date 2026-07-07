# Historical Figures System

## Overview

Historical Figures replace FreeCol's Founding Fathers. They are not just flavour: each figure acts as a permanent policy, event unlock, unit unlock, building unlock, or economic modifier.

## Categories

| Category | Point source | Role |
|---|---|---|
| Industry & Commerce | Trade, exports, markets, banks, ports | Economy, goods, trade, gold, wool. |
| Exploration & Infrastructure | Scouts, maps, roads, telegraph, rail, ports | Mobility, discovery, continental connection. |
| Settlement, Administration & Defence | Food, public works, governance, coastal defence | Survival, town growth, order. |
| Democracy & Federation | Civic Voice, conventions, newspapers | Federation victory and constitutional mechanics. |
| Social Reform & First Nations | Immigration, reform, diplomacy, newspapers, agreements | Civic reform, legitimacy, relationship systems. |

## Figure card schema

Recommended schema:

```yaml
id: historical_figure.lachlan_macquarie
name: Lachlan Macquarie
category: Settlement, Administration & Defence
earliest_year: 1810
cost_type: administration_points
cost: 450
requires:
  any_colony:
    population_min: 5
  buildings_any:
    - town_hall
    - government_stores
perk:
  name: Public Works Governor
  effects:
    - public_buildings_cost_modifier: -20
    - free_roads_adjacent_to_pop3_colonies: true
linked_events:
  - event.macquarie_governorship
```

## Availability model

Each figure should have three states:

### Locked

The player has not reached date or prerequisite gates.

### Eligible

The figure can appear in the selection pool.

### Offered

The figure is actively selectable when enough points have been accumulated.

### Attained

The figure's perk is active and linked events may become eligible.

## Prerequisite design rules

1. **Use date gates for historical plausibility.**  
   Example: Macquarie should not appear before 1810.

2. **Use game-state gates for agency.**  
   Example: Charles Todd should require surveyed inland route or telegraph progress.

3. **Use regional gates where relevant.**  
   Example: Mary Lee should require South Australia or suffrage reform conditions.

4. **Use crisis gates for reform figures.**  
   Example: Peter Lalor should be more likely after mining licence unrest.

5. **Use relationship gates for First Nations diplomacy figures.**  
   Example: William Barak should require strong Kulin/Wurundjeri respect, not just points.

## Recommended figure roster

### Industry & Commerce

1. Elizabeth Macarthur.
2. Thomas Sutcliffe Mort.
3. George Fife Angas.
4. Edward Hargraves.
5. Sidney Kidman.

### Exploration & Infrastructure

1. Matthew Flinders.
2. Charles Sturt.
3. Ludwig Leichhardt.
4. John McDouall Stuart.
5. Charles Todd.

### Settlement, Administration & Defence

1. Arthur Phillip.
2. Lachlan Macquarie.
3. James Ruse.
4. Mary Reibey.
5. William Jervois.

### Democracy & Federation

1. Henry Parkes.
2. Edmund Barton.
3. John Quick.
4. Samuel Griffith.
5. Catherine Helen Spence.

### Social Reform & First Nations

1. Caroline Chisholm.
2. Peter Lalor.
3. Mary Lee.
4. Louisa Lawson.
5. William Barak.

## Figure balance model

Each category should include:

- One early-game figure.
- One mid-game economic or expansion figure.
- One late-game power spike.
- One figure that helps recover from a crisis.
- One figure that unlocks a new mechanic.

## Figure selection approach

Recommended: keep FreeCol's point-purchase style, but apply eligibility filters.

Example:

- Player earns enough Industry & Commerce points.
- Game checks eligible Industry figures.
- Elizabeth Macarthur appears only if sheep/wool era conditions exist.
- Thomas Mort appears only if ports/export economy exist and year gate is met.
- Edward Hargraves appears only after 1851 or mineral survey conditions.

## Avoiding anachronism

Do not allow players to recruit:

- Federation figures in 1790.
- Telegraph figures before inland infrastructure is plausible.
- Gold-rush figures before gold is discoverable.
- Suffrage figures before civic reform and newspapers exist.

## Linked events

Historical Figures can unlock event chains. These should be optional or conditional.

Example:

- Attaining Lachlan Macquarie unlocks `Macquarie Governorship`.
- Attaining Peter Lalor unlocks `Eureka Reform Settlement` if goldfield unrest exists.
- Attaining Henry Parkes unlocks `Tenterfield Oration` if Federation Support is not already high.
- Attaining William Barak unlocks `Coranderrk Delegation` if Kulin relations are strong.
