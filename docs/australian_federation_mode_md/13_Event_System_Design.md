# Event System Design

## Goal

Historical events should support story without making the game a fixed timeline. The player creates the conditions for events; the calendar only opens windows.

## Event states

| State | Description |
|---|---|
| `locked` | Date or basic prerequisites not met. |
| `eligible` | Can enter event draw pool. |
| `offered` | Player receives choice/event popup. |
| `resolved` | Event completed. |
| `expired` | Event window passed or conditions no longer apply. |

## Event types

### Scenario setup events

These define the starting scenario and can be forced in historical mode.

Example:

- Sydney Cove Established.
- First Fleet Arrives.

### Date-gated contextual events

These require a time window but also game conditions.

Example:

- Merino Sheep Introduced requires sheep/wool trade conditions or Macarthur figure eligibility.

### Figure-linked events

These require a Historical Figure to be attained.

Example:

- Macquarie Governorship requires Lachlan Macquarie.

### Crisis events

These trigger from bad conditions.

Example:

- Starvation Crisis triggers from low food.
- Mining Licence Unrest triggers from high taxes/licence pressure.

### Opportunity events

These trigger from successful development.

Example:

- Frozen Meat Export Trial requires port, Freezing Works, and Thomas Mort.

### Regional events

These require a specific colony or Country context.

Example:

- Western Australia Referendum requires WA active.
- Coranderrk Delegation requires Kulin/Wurundjeri relationship conditions.

## Event prerequisite schema

Recommended schema:

```yaml
id: event.macquarie_governorship
name: Macquarie Governorship
type: figure_linked
earliest_year: 1810
latest_year: 1825
requires:
  historical_figure_attained:
    - historical_figure.lachlan_macquarie
  any_colony:
    population_min: 5
  any_building:
    - government_stores
    - town_hall
  public_works_need_min: 20
weight:
  base: 60
  modifiers:
    - condition: road_shortage_high
      add: 20
    - condition: emancipist_unrest_high
      add: 15
choices:
  - id: public_works
    label: Begin a public works program
  - id: fiscal_restraint
    label: Limit expenditure
```

## Event trigger components

| Component | Purpose |
|---|---|
| `earliest_year` | Prevents anachronism. |
| `latest_year` | Prevents stale events appearing too late. |
| `requires` | Hard conditions. |
| `weight` | Probability once eligible. |
| `modifiers` | Contextual chance adjustments. |
| `choices` | Player agency. |
| `effects` | Mechanical outcomes. |
| `followups` | Chains and consequences. |

## Recommended event frequency

| Era | Frequency |
|---|---|
| 1788–1797 | Frequent survival events. |
| 1797–1830 | Moderate expansion/public works events. |
| 1830–1851 | Moderate colony formation and immigration events. |
| 1851–1872 | Frequent gold/crisis/reform events. |
| 1872–1889 | Moderate infrastructure/commercial events. |
| 1889–1901 | Frequent Federation/convention/referendum events. |

## Forced vs conditional

### Forced only in scenario setup

- The starting settlement setup can be forced.
- Initial First Contact popup can be forced in historical mode.

### Conditional for almost everything else

Examples:

- Macquarie Governorship: figure + 1810+ + colony maturity.
- Gold Rush: 1851+ + gold potential/survey/Hargraves.
- Tenterfield Oration: Parkes + 1889+ + Federation Support visible.
- Overland Telegraph: Todd + route surveyed + telegraph materials.

## Player choice model

Each major event should offer at least two choices:

1. Historical-ish path.
2. Conservative/avoid-cost path.
3. Reform/higher-legitimacy path where appropriate.
4. Exploitative/high-profit path where appropriate, with long-term legitimacy costs.

## Example: Macquarie Governorship

### Trigger

- Year 1810–1825.
- Lachlan Macquarie attained.
- At least one colony population 5+.
- Road shortage or building backlog exists.

### Choices

#### Begin public works program

Effects:

- Roads/buildings cheaper.
- Administration cost increases temporarily.
- Emancipist integration improves.

#### Focus on fiscal restraint

Effects:

- Treasury preserved.
- Smaller building bonus.
- Less reform benefit.

#### Prioritise frontier expansion

Effects:

- Faster inland settlement.
- Increased Country Pressure and First Nations tension.
- Possible future legitimacy penalty.

## Example: Gold Rush Begins

### Trigger

- Year 1851+ OR Hargraves attained.
- Gold potential tile exists.
- Surveyor/scout has explored region.

### Choices

#### Encourage the rush

- Immigration surge.
- Goldfield appears.
- Inflation and unrest increase.

#### Regulate the fields

- Lower unrest.
- Slower gold output.
- More government revenue.

#### Restrict movement

- Lower chaos.
- Much lower immigration and gold.
- Reform pressure increases.

## Event design warning

Events should never reward historical suffering as a simple resource gain. Events involving disease, frontier violence, forced removal, massacres, or exclusionary policies should be handled through sombre text, legitimacy systems, and consequences.
