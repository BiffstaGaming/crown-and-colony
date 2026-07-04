# Managing a Colony

Founding a colony is only the beginning. The real work of a governor happens on the **colony screen** — the town-management view where you decide who farms, who builds, what gets constructed, and how your settlement defends itself. This chapter walks you through that screen and the systems behind it. If you have not yet founded your first town, read [Founding a Colony](07-founding-colony.md) first.

## Opening the colony screen

Click any of your colonies on the map to open its full view. It fills the screen on a brown parchment backdrop, laid out like a real colonial town ledger. From here you manage everything the colony does. You can also **rename** the colony (type a new name in the box at the top and press **Rename**) or **Abandon** it entirely — though a colony with a stockade, fort or fortress cannot be abandoned, and a colony must always keep at least one colonist.

## Anatomy of the screen

The colony screen packs a lot of information into one view. The main areas are:

- **The title and summary line** — population, idle colonists, food banked toward the next colonist, and the colony's defence bonus.
- **The production bar** (along the top) — every good the colony makes or eats this turn, shown as an icon and a net amount.
- **The Sons of Liberty band** (top left) — Rebels · Population · Royalists, the membership percentage and the current production bonus, over a gold-and-dark meter (covered below).
- **The colonist portraits and the land view** (left) — a row of your inhabitants above an isometric picture of the colony and its eight surrounding tiles, with a worker standing on each tile being farmed.
- **The buildings grid** (right) — each building drawn with the little portraits of the colonists working inside it, and **+ / −** buttons to staff or empty it.
- **The warehouse bar** — every stored good as an icon and a count.
- **The construction panel** — the build queue.
- **The production overview** — a full table of what the colony produces, consumes, and nets each turn, including food, bells, crosses and hammers.

## Putting colonists to work

Every colonist does one of two jobs: **working one of the eight surrounding tiles** (growing food or gathering a raw good like lumber, ore, cotton or furs), or **staffing a building** (turning raw goods into finished products — cloth, rum, tools, bells and so on). New colonists automatically report to the best food tile.

You move people around in whichever way suits you:

- **Click to move** — click a colonist on its tile to pick it up (it highlights), then click another tile to send it there, or click the **colony centre** to make it idle.
- **Drag and drop** — the screen has an **Idle colonists** row of portrait chips. Drag a chip onto a tile to farm there, or onto a building to staff it.
- **The buttons** — every tile keeps an **✕** to release its worker and a **Work…** picker to choose *which* good that tile produces (for example, lumber instead of food so your carpenter has something to build with). Buildings use their **+ / −** controls.

A move that makes no sense — onto an already-worked tile, a full building, or a sea tile before you have Docks — is quietly refused; nothing is lost. Dragging onto land a native nation claims raises the usual Buy / Steal / Abandon prompt.

For who does which job best — experts, servants, schooling — see [Colonists, Professions & Education](09-colonists-education.md). For what each building makes, see [Buildings](11-buildings.md) and [Goods & Production Chains](10-goods-production.md).

## Food, growth and starvation

Food is the heartbeat of a colony. Each turn:

1. The colony gathers food from worked tiles and its own centre square.
2. **Every colonist eats 2 food.** Any shortfall drains your stored food.
3. Surplus food is banked. **Once 200 food is stored, a new colonist is born** and the counter resets.

!!! warning "Do not let a colony outgrow its food"
    If a colony cannot feed everyone, one colonist starves that turn. If the shortfall is so severe that even a single colonist cannot be fed, the whole colony is **destroyed** — it vanishes from the map and the loss is recorded against your score. In a normal classic game a lone colonist always feeds itself, so this only becomes a danger under unusual conditions (such as an optional natural-disaster ruleset). Watch the food line and keep enough farmers assigned.

## The warehouse

Your warehouse holds only **100 of each storable good**. Build a **warehouse** to raise that to 200, and a **warehouse expansion** to reach 300.

!!! warning "Overflow is wasted forever"
    Anything produced past the cap each turn is **spilled and lost**. If a colony makes more cotton than it can ship, the surplus evaporates until you build more storage or trade it away. Food and the non-stored goods (bells, crosses and hammers) are exempt — they are consumed or spent, not warehoused.

The game helps you catch this before it bites. The warehouse bar flags a good that is at or near the cap with an amber **▲** and a *will overflow* tooltip, and a good that is almost gone with a red **▼** and a *running low* tooltip — so the tools your blacksmith is about to run dry of catch your eye in time. After End Turn you also get a notice telling you which colony wasted what.

## The build queue

The construction panel is a **queue**, not a single job. Open the **Add to queue** menu to see everything the colony can construct — every eligible **building** (warehouses, churches, schools, fortifications, workshops) *and* every eligible **unit** (a wagon train anywhere, artillery once you have an armory, ships at a coastal shipyard), each listed with its hammer (and sometimes tool) cost.

The colony finishes one item per turn as its hammers accumulate, then moves to the next, so you can plan several builds ahead and walk away. Each line has **▲/▼** to reorder it and **✕** to remove it, and a **Clear queue** button wipes the list. Finished buildings join the colony; finished units appear on the colony's tile (ships launch onto the water beside it).

## The custom house and setting exports

Once you have elected the right Founding Father, you can build a **custom house** — a building that quietly sells your surplus to Europe **every turn, with no ship required**, even through a blockade. When a colony has one, the colony screen grows a **Custom house — exports** section listing every good you can trade, each with:

- a **tick-box** — flag this good for export;
- a **keep-amount** box — how much to retain in the warehouse before the rest is auto-sold;
- a **"max" box** — an import ceiling that stops an automatic trade-route delivery from piling this good up past a set level.

Nothing exports until you tick it — the default is "export nothing." A flagged good gets a small green **→** on the warehouse bar so you can see at a glance what is shipping. Each sale pays the going market price minus the King's tax, and after End Turn the game reports what each custom house sold. In the classic ruleset a custom house even **smuggles boycotted goods** past a royal boycott. For the wider market, see [Trade & the Europe Screen](12-trade-europe.md) and [The King, Taxes & Your Monarch](13-king-taxes.md).

## Defending the colony

A colony is only as safe as its walls and its garrison. Three things matter:

- **Fortifications** — build a **stockade**, then a **fort**, then a **fortress**, each making the colony progressively harder to capture and granting a growing defence bonus (shown on the summary line). Remember that a fortified colony cannot be abandoned.
- **A garrison** — station armed units on the colony tile. Send a colonist out onto the tile and **arm** it from the colony's own stores: 50 muskets for a soldier, muskets and horses for a dragoon, and so on. Fortified defenders dig in for a bonus.
- **Artillery** — powerful behind walls, but vulnerable caught in the open.

For how attacks are resolved, see [Combat: Land & Naval](17-combat.md).

## Sons of Liberty and the production bonus

Every colony has a mood. As its town hall rings **liberty bells**, colonists come over to the rebel cause and become **Sons of Liberty**; the rest stay loyal to the Crown (royalists). The Rebels · Population · Royalists band shows where your colony stands.

Membership directly changes how hard the colony works:

| Sons of Liberty / royalists | Effect on every worker |
|---|---|
| 100% membership | **+2** production |
| 50% or more membership | **+1** production |
| More than 6 royalists ("bad government") | **−1** production |
| More than 10 royalists ("very bad government") | **−2** production |

A small colony (six or fewer royalists) never suffers a penalty no matter how low its membership. The bonus (or penalty) applies to every colonist working a tile or a building, and it never pushes anyone below zero output.

Under the population count, a **preferred-size hint** advises whether the colony has room to grow: "**+N room to grow**", "**−N overcrowded**", or "**at ideal size**". A colony that grows faster than its bell output will see its membership fall, so a large colony must keep its town hall staffed. The exact numbers and how to raise them are covered in [Founding Fathers & the Continental Congress](14-founding-fathers.md) and, for high-level tactics, [Strategy & Tips](21-strategy.md).

!!! tip "Read the Colopedia for the fine print"
    Exact yields, building costs and market prices vary by good and terrain. Rather than memorise them, press `C` in-game to open the **Colopedia** — the built-in reference for every unit, good and building.
