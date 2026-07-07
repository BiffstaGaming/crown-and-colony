# Diplomacy, Tension, and Respect Mechanics

## Purpose

This system replaces simple native alarm with a more nuanced relationship model.

## Core variables

### Respect

Represents trust and positive relationship.

Increases from:

- Fair trade.
- Gifts during hardship.
- Honoured agreements.
- Interpreters/mediators.
- Low-pressure settlement.
- Compensation for damage.
- Shared water/route agreements.

Decreases from:

- Broken agreements.
- Violence.
- Seizure of land.
- Livestock damage.
- Forced movement.
- Armed intimidation.

### Tension

Represents conflict risk.

Increases from:

- Country Pressure.
- Low Respect.
- Armed units nearby.
- Pastoral/mining expansion.
- Disease events.
- Retaliatory violence.

Decreases from:

- Time without new pressure.
- Agreements.
- Mediator presence.
- Compensation.
- Withdrawal from sensitive sites.

### Country Pressure

Represents cumulative colonial pressure on Country.

Sources:

- Settlement footprint.
- Population density.
- Roads/rail/telegraph.
- Pastoral runs.
- Mines/goldfields.
- Timber extraction.
- Armed units.
- Mission/protection policies.

## Relationship states

| State | Conditions | Gameplay |
|---|---|---|
| Unknown | No contact | No diplomacy. |
| Cautious Contact | Contact made, Respect 20–50 | Limited trade. |
| Trade Relationship | Respect 45+, Tension <50 | Trade and minor knowledge. |
| Agreement Relationship | Respect 60+, Tension <40 | Formal agreements. |
| Trusted Relationship | Respect 80+, Tension <30 | Major knowledge exchange and legitimacy bonuses. |
| Strained | Tension 60+ | Trade reduced, warnings. |
| Hostile | Tension 80+ | Resistance likely, agreements suspended. |

## Agreement negotiation

Agreements should cost time and resources.

Possible costs:

- Goods.
- Diplomatic points.
- Interpreter/Mediator turns.
- Limits on settlement expansion.
- Compensation obligations.
- Shared access rules.

## Agreement examples

### Passage agreement

Prerequisites:

- Respect 40+.
- Tension below 60.

Effects:

- Scouts/surveyors can pass through Country with lower tension.

### Trade agreement

Prerequisites:

- Respect 45+.
- No recent violence.

Effects:

- Trade opens.
- Knowledge exchange chance increases.

### Water agreement

Prerequisites:

- Respect 60+.
- Desert/arid route or drought region.

Effects:

- Expedition attrition reduced.
- Drought penalty reduced.

### Infrastructure corridor agreement

Prerequisites:

- Respect 65+.
- Telegraph/road/rail project proposed.
- Mediator assigned.

Effects:

- Infrastructure Country Pressure reduced by 50%.
- Agreement maintenance required.

### Town boundary agreement

Prerequisites:

- Respect 70+.
- Existing town pressure stable.

Effects:

- Settlement expansion causes lower tension within agreed boundary.
- Breaking boundary causes major Respect loss.

## Conflict model

Conflict should be costly and destabilising.

Possible effects:

- Trade routes close.
- Knowledge exchange ends.
- Federation legitimacy falls.
- Reform backlash appears in newspapers.
- Military spending rises.
- Nearby settlements lose productivity.
- Resistance events intensify.

## Legitimacy interaction

`Federation Legitimacy` should be affected by First Nations relations.

| Condition | Effect |
|---|---|
| Multiple hostile regions | Federation Support from reform towns reduced. |
| Broken agreements publicised | Newspapers create reform pressure and anti-government unrest. |
| Strong agreement network | Treaty Commonwealth victory grade possible. |
| Coercive frontier policy | Faster land access but lower final score. |

## Diplomatic unit roles

### Interpreter

- Reduces misunderstanding event chance.
- Improves trade negotiation.
- Required for advanced agreements.

### Mediator

- Reduces tension over time.
- Can negotiate compensation.
- Required for corridor/town boundary agreements.

### Federation Commissioner

- Late-game unit.
- Can investigate frontier incidents.
- Converts reform pressure into legitimacy recovery.

## Design note

This system makes peaceful diplomacy mechanically valuable without pretending colonial settlement was neutral or harmless. Expansion can still happen, but it should have costs, choices, and consequences.
