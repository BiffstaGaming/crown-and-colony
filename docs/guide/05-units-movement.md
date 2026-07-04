# Units & Movement

Everything you do in the New World, you do through **units** — the colonists, ships, soldiers, wagons and specialists that carry out your will. This chapter explains what the different units are, how they move across land and sea, and the orders you give them to keep your empire running without babysitting every figure on the map.

## The units you command

You start with a **ship** and a **colonist**, but your roster grows quickly. Units fall into a few broad families.

- **Colonists** are your people. A plain **free colonist** can do any job — work a field, staff a building, or found a colony. Weaker newcomers arrive too: **indentured servants** and **petty criminals** work less effectively until they earn their way up.
- **Experts** are colonists who have mastered a trade — an expert farmer, a master distiller, a veteran soldier and so on. An expert does its specialty far better than a free colonist. Colonists become experts by working a job long enough, by being taught in a school, or by learning a skill from a native settlement (see [Colonists, Professions & Education](09-colonists-education.md)).
- **Soldiers and dragoons** are colonists armed for war. Arm a colonist with **muskets** to make a soldier; add **horses** as well to make a faster, harder-hitting **dragoon**. See [Combat: Land & Naval](17-combat.md).
- **Scouts** are colonists mounted on horses. They see farther across the map and explore more safely — see [Exploration & Discovery](06-exploration.md).
- **Pioneers** are colonists carrying **tools**, used to improve the land (see below).
- **Missionaries** travel to native settlements to establish missions and win converts over time (see [Natives & Diplomacy](15-natives-diplomacy.md)).
- **Artillery** is a powerful attacking and defending unit, but vulnerable when caught in the open.
- **Ships** carry goods and colonists across the sea and fight naval battles. Larger ships have more cargo holds.
- **Wagon trains** haul goods overland between your colonies.

## Movement points and terrain

Each unit has a set number of **movement points** it may spend each turn. Terrain decides how far those points take you:

- Open ground such as plains is cheap to cross.
- Forests, hills and mountains cost more.
- **Roads and rivers** are fast — a unit that follows a road or river corridor covers far more ground.

A unit's movement refreshes at the start of every turn. Mounted units (dragoons, scouts) move farther than those on foot, and a few units and effects grant extra movement — for example, the Founding Father **Ferdinand Magellan** speeds up your ships.

!!! tip
    A unit always gets to make **at least one move**, even onto costly terrain it can't quite afford. A unit with any movement left can step into a forest and simply finish its turn there.

!!! warning
    Stepping onto a tile you have **never seen before** uses up the rest of a land unit's turn, however much movement it had left — exploring the wilderness is slow going. Moving across ground you have *already* explored costs only the normal terrain amount, so a fast unit can keep going over familiar land. Ships are exempt: scouting the open sea never halts them.

For exact costs and the properties of each terrain, check the map chapter — [The Map & Terrain](04-map-terrain.md) — and the in-game **Colopedia** (press `C`).

## Land, sea and the crossing between

Land units stay on land and ships stay on water — a colonist cannot swim the ocean. To move a colonist across the sea, you put it **aboard a ship**.

- A ship's **hold** is measured in slots. A caravel holds a couple, a galleon many more. Goods and passengers **share** the same hold: one colonist takes one slot, and a hundred of a trade good also takes one slot.
- **Board** a colonist when it is together with the ship — both in Europe, or the colonist standing next to the ship on the coast. Select the land unit and click the friendly ship.
- A boarded colonist **travels with the ship** and cannot move, sail or found a colony on its own while aboard.
- **Disembark** it onto a land tile next to the ship once the ship is back on the map. Select the ship and click the adjacent land tile. A colonist that boards or disembarks ends its turn where it lands.

Once ashore, a colonist is a free unit again — it can found a new colony or join an existing one (see [Founding a Colony](07-founding-colony.md)).

## Sailing to and from Europe

To reach the New World from Europe — and to return — a ship sails to the **high seas**, the deep water at the edges of the map. A voyage in either direction takes a few turns. Aim a ship at the map's edge with a *go to Europe* order and it will sail out on its own and set off across the ocean; you don't have to nurse it to the edge by hand. Everything you buy, sell and recruit on the far side is handled on the Europe screen — see [Trade & the Europe Screen](12-trade-europe.md).

## Wagon trains overland

Where ships move goods by sea, a **wagon train** moves them by land, between your colonies. It loads goods from a colony it stands on or beside, trundles overland to another colony, and unloads them into that colony's warehouse. This lets an inland colony send tools or ore to a coastal port for export without a road, and lets a port feed muskets to a frontier settlement. You load and unload a carrier by hand from the **Cargo** section of the colony screen; for hands-off hauling, set up a **trade route** (see [Managing a Colony](08-managing-colony.md) and [Trade & the Europe Screen](12-trade-europe.md)).

## Pioneers: improving the land

A **pioneer** is a colonist equipped with **tools**, and it is how you reshape the wilderness:

- **Build roads** to speed movement between colonies.
- **Plow** cleared ground to raise its farm output.
- **Clear forest** to open new land (and yield a one-off supply of lumber).

Each task consumes tools and takes time — the pioneer works several turns on a tile while you leave it to it. When its tools run out, re-equip it and send it back to work. Terrain improvements and their exact effects are covered in [The Map & Terrain](04-map-terrain.md).

## Scouts: seeing far, staying safe

A **scout** is a colonist on a horse. It reveals more of the map than a colonist on foot and is the safe choice for investigating **lost-city rumours** — a scout never simply vanishes when it explores one. Scouts also speak with native chiefs and, with the right Founding Father, become even keener-sighted. See [Exploration & Discovery](06-exploration.md).

## Orders and automation

You rarely need to walk every unit by hand. A unit can be given a **standing order** or told to travel on its own.

| Order | Key | What it does |
|---|---|---|
| **Fortify** | — | The unit digs in for a turn, then defends **half again as hard** (+50%). Perfect for a soldier guarding a colony or a chokepoint. Moving the unit wakes it and gives up the bonus. |
| **Sentry** | — | Rests the unit so it stops asking for orders — but it **auto-wakes** the moment an enemy steps next to it, so it genuinely guards ground. |
| **Skip** | `Space` | Passes over the selected unit for the rest of this turn; it keeps its movement but the game stops prompting you for it. |
| **Wait / next unit** | `W` | Jumps to the next unit that still needs orders this turn. |
| **Go to** | `G` | Point the unit at a distant tile and it walks itself there over future turns. |
| **Disband** | `D` | Removes a unit you no longer want, for good. You are asked to confirm first. |

### Go-to, in detail

Press `G`, then click a destination. The unit finds the cheapest route through ground it has **already explored** and walks itself toward the target a little each turn, all on its own, until it arrives — then the order clears. Before you commit, the game can draw a glowing **route line** so you can see exactly where the unit will go and how far it is.

You can send a unit to more than empty ground. Aim it at **one of your own colonies** and it walks right in; aim it at a **native village or an enemy town** and it stops on the tile right beside it, ready to trade, scout or attack. Aim a **ship** across open sea and it sails the water route; aim a ship at the map's edge and it heads for Europe.

!!! tip
    A go-to never routes through unexplored land or straight into an enemy — if the way is blocked it simply waits and tries again next turn. Moving a unit by hand cancels its trip. While a unit is travelling, the game skips it and cycles straight to the next unit that needs you, then tells you when the turn can be ended.

### Ending the turn

When every unit has orders and nothing more needs your attention, press `Enter` to **end the turn**. The world then resolves — other powers move, colonies produce and grow, ships at sea advance — and your units are refreshed for the next turn. Press `C` at any time to open the Colopedia for the full details of any unit type.
