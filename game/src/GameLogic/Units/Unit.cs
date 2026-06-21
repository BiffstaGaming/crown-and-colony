using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Units;

/// <summary>Where a unit is: on the map, in transit across the high seas, or docked in Europe.</summary>
public enum UnitLocation
{
    /// <summary>On the game map at <see cref="Unit.Position"/>.</summary>
    OnMap,

    /// <summary>Sailing the high seas toward Europe.</summary>
    SailingToEurope,

    /// <summary>Docked in Europe.</summary>
    InEurope,

    /// <summary>Sailing the high seas back to the New World.</summary>
    SailingToNewWorld,
}

/// <summary>
/// A unit's standing order (FreeCol <c>Unit.UnitState</c>, the subset that affects play on the map).
/// <see cref="Active"/> is the default: free to act. <see cref="Fortifying"/> is the one-turn dig-in that
/// becomes <see cref="Fortified"/> at the next turn reset (FreeCol ages FORTIFYING → FORTIFIED), granting a
/// +50% defence bonus. <see cref="Sentry"/> rests the unit (skipped when cycling, no orders prompt) until
/// something happens. Moving or attacking clears the order back to <see cref="Active"/>.
/// </summary>
public enum UnitOrders
{
    /// <summary>Free to act (the default).</summary>
    Active,

    /// <summary>Digging in this turn; becomes <see cref="Fortified"/> at the next turn reset.</summary>
    Fortifying,

    /// <summary>Dug in: +50% defence (FreeCol <c>model.modifier.fortified</c>).</summary>
    Fortified,

    /// <summary>Resting until something happens (skipped when cycling units).</summary>
    Sentry,
}

/// <summary>A unit on the map. Its capabilities come from its ruleset <see cref="UnitType"/>.</summary>
public sealed class Unit
{
    private readonly Dictionary<string, int> _cargo = [];

    /// <summary>Creates a unit of a type at a position with full movement.</summary>
    public Unit(int id, UnitType type, Position position)
    {
        Id = id;
        Type = type;
        Position = position;
        MovementLeft = type.Movement;
    }

    /// <summary>Stable per-game identifier.</summary>
    public int Id { get; }

    /// <summary>The ruleset type (movement, sight, naval, …).</summary>
    public UnitType Type { get; }

    /// <summary>
    /// Last map position. Meaningful while <see cref="Location"/> is on the map or
    /// when returning from Europe (the tile it departed from / will re-enter at).
    /// </summary>
    public Position Position { get; internal set; }

    /// <summary>Movement points remaining this turn (spec scale: terrain costs 3/6/9).</summary>
    public int MovementLeft { get; internal set; }

    /// <summary>Where the unit currently is (map / sailing / Europe).</summary>
    public UnitLocation Location { get; internal set; } = UnitLocation.OnMap;

    /// <summary>Turns left before a sailing unit arrives (0 when not sailing).</summary>
    public int SailTurnsRemaining { get; internal set; }

    /// <summary>
    /// Turns left before a damaged ship is repaired (0 when healthy). A ship beaten in combat (but not
    /// sunk) limps to a repair location — Europe in our model — at this many turns and recovers one per
    /// turn (FreeCol's hit-point repair: <c>MaxHitPoints − 1</c> turns, 5 for every classic ship). While
    /// it is &gt; 0 the ship is under forced repair and cannot act (see <see cref="IsUnderRepair"/>).
    /// </summary>
    public int RepairTurnsRemaining { get; internal set; }

    /// <summary>True while a damaged ship is repairing (and so cannot sail or act).</summary>
    public bool IsUnderRepair => RepairTurnsRemaining > 0;

    /// <summary>
    /// The unit's standing order (active / fortifying / fortified / sentry). Only <see cref="GameSession.Game"/>
    /// mutates it: <c>Fortify</c>/<c>Sentry</c>/<c>ClearOrders</c> set it, the per-turn reset ages
    /// <see cref="UnitOrders.Fortifying"/> → <see cref="UnitOrders.Fortified"/>, and moving clears it.
    /// </summary>
    public UnitOrders Orders { get; internal set; } = UnitOrders.Active;

    /// <summary>True when dug in (FreeCol FORTIFIED): the unit defends at +50% (see <see cref="Combat.CombatModel"/>).</summary>
    public bool IsFortified => Orders == UnitOrders.Fortified;

    /// <summary>
    /// The standing "go to" map destination (FreeCol <c>Unit.destination</c>), or null when the unit has no goto.
    /// While set, the unit pathfinds toward it automatically each turn (see <see cref="GameSession.Game"/>'s
    /// <c>ProcessGotos</c>); reaching it, or any manual move, clears it. Only the destination endpoint is stored —
    /// the path is recomputed every turn so it adapts to revealed terrain and moved units.
    /// </summary>
    public Position? Destination { get; internal set; }

    /// <summary>True when the unit has a standing goto destination.</summary>
    public bool IsGoingTo => Destination is not null;

    /// <summary>
    /// The id of the <see cref="GameSession.TradeRoute"/> this carrier is assigned to (FreeCol <c>Unit.tradeRoute</c>),
    /// or null when it hauls nothing automatically. While set, the unit auto-hauls along its route's stops each turn
    /// (see <c>Game.ProcessTradeRoutes</c>); only a carrier (cargo space &gt; 0) can be assigned.
    /// </summary>
    public int? TradeRouteId { get; internal set; }

    /// <summary>The index of the route stop this carrier is currently heading for (FreeCol <c>Unit.currentStop</c>); 0 when not on a route. Wraps back to 0 after the last stop.</summary>
    public int TradeRouteStopIndex { get; internal set; }

    /// <summary>True when this carrier is assigned to a trade route.</summary>
    public bool IsOnTradeRoute => TradeRouteId is not null;

    /// <summary>
    /// The id of the ship carrying this unit, or null when not aboard. A carried
    /// unit's <see cref="Location"/>/<see cref="Position"/> mirror its carrier's.
    /// </summary>
    public int? CarrierId { get; internal set; }

    /// <summary>
    /// The owning native nation's type id (e.g. <c>model.nationType.apache</c>) when this is a
    /// native brave, or null when owned by a colonial player. Native ownership is carried here until
    /// natives become players (FP-3b); colonial ownership is <see cref="OwnerId"/>.
    /// </summary>
    public string? OwnerNationId { get; internal set; }

    /// <summary>
    /// The owning colonial player's id (FP-2; ADR-019). The human is 0; foreign colonial powers get
    /// their own ids in FP-3b. Authoritative for colonial ownership; unused (0) for a native-owned unit,
    /// whose owner is <see cref="OwnerNationId"/>. The enemy/fog/ability rules resolve the owner via <see cref="GameSession.Game"/>.
    /// </summary>
    public int OwnerId { get; internal set; }

    /// <summary>True when owned by a native nation (a brave), not a colonial player.</summary>
    public bool IsNative => OwnerNationId is not null;

    /// <summary>
    /// The unit's military status / equipment (FreeCol <see cref="Specification.RoleType"/> id);
    /// the unarmed default role unless armed (soldier/dragoon) or a brave equipped from stock.
    /// </summary>
    public string RoleId { get; internal set; } = Specification.RoleType.DefaultRoleId;

    /// <summary>The multiple of the role's required goods held (FreeCol <c>roleCount</c>); 0 for the default role.</summary>
    public int RoleCount { get; internal set; }

    /// <summary>True when the unit is in the unarmed default role.</summary>
    public bool HasDefaultRole => RoleId == Specification.RoleType.DefaultRoleId;

    /// <summary>
    /// The id of the tile improvement this unit is currently building (FreeCol <c>Unit.workImprovement</c> +
    /// <c>UnitState.IMPROVING</c>), or null when it is not improving a tile. A pioneer with a build order accrues
    /// work each turn until <see cref="WorkTurnsLeft"/> reaches zero, when the improvement lands and tools are spent.
    /// Only <see cref="GameSession.Game"/> mutates it (<c>BuildImprovement</c> sets it, the per-turn work completes it).
    /// </summary>
    public string? WorkImprovementId { get; internal set; }

    /// <summary>
    /// Turns of work left before the in-progress improvement completes (FreeCol <c>Unit.workLeft</c> /
    /// <c>TileImprovement.turnsToComplete</c>); 0 when the unit is not improving. A hardy (expert) pioneer works it
    /// down twice as fast (2 per turn vs. 1).
    /// </summary>
    public int WorkTurnsLeft { get; internal set; }

    /// <summary>True while the unit is building a tile improvement (it is busy and skipped when cycling units).</summary>
    public bool IsImproving => WorkImprovementId is not null;

    /// <summary>True when this unit is a passenger aboard a ship.</summary>
    public bool IsAboard => CarrierId is not null;

    /// <summary>True when the unit is on the game map (not sailing, in Europe, or aboard a ship).</summary>
    public bool IsOnMap => Location == UnitLocation.OnMap && !IsAboard;

    /// <summary>Goods carried in the unit's hold (naval cargo).</summary>
    public IReadOnlyDictionary<string, int> Cargo => _cargo;

    /// <summary>Amount of one good in the hold.</summary>
    public int CargoOf(string goodsId) => _cargo.GetValueOrDefault(goodsId);

    /// <summary>Adds goods to the hold (negative removes; floor at 0).</summary>
    internal void AddCargo(string goodsId, int amount)
    {
        int next = Math.Max(0, CargoOf(goodsId) + amount);
        if (next == 0)
        {
            _cargo.Remove(goodsId);
        }
        else
        {
            _cargo[goodsId] = next;
        }
    }

    /// <summary>Empties the hold (a sunk or damaged ship loses its goods — FreeCol jettisons cargo on either).</summary>
    internal void ClearCargo() => _cargo.Clear();

    private int _treasureAmount;

    /// <summary>
    /// The gold this unit carries as a <b>treasure train</b> (FreeCol <c>Unit.treasureAmount</c>) — the plunder
    /// from a sacked native settlement or a lost-city find, cashed in at a colony or in Europe. 0 for every other
    /// unit. Only a unit whose type has <see cref="Specification.UnitType.CarryTreasure"/> ever carries a positive
    /// amount; capturing the train carries the amount to the captor (the same <see cref="Unit"/> object changes side).
    /// </summary>
    public int TreasureAmount => _treasureAmount;

    /// <summary>Sets the carried treasure (negative floors to 0); only <see cref="GameSession.Game"/> mutates it.</summary>
    internal void SetTreasureAmount(int amount) => _treasureAmount = Math.Max(0, amount);

    /// <summary>
    /// Turns this unit has spent standing in the open wilderness — its accrued <b>attrition</b> (FreeCol
    /// <c>Unit.attrition</c>, "the number of turns it has spent in the open"). A unit whose <see cref="UnitType"/>
    /// has a finite maximum attrition gains +1 each world turn it ends on a settlement-less map tile; when the total
    /// exceeds that maximum the unit wastes away and is removed. Entering a settlement (or sailing to Europe) resets
    /// it to 0. In the classic ruleset only the Indian Convert (<c>maximum-attrition="8"</c>) is subject to this — every
    /// other unit type's maximum attrition is infinite, so it never accrues and stays at 0. Only
    /// <see cref="GameSession.Game"/>'s per-turn attrition step mutates it; persisted omit-when-0 (save v50).
    /// </summary>
    public int Attrition { get; internal set; }

    /// <summary>
    /// The unit's original <b>nationality</b> — its personal allegiance to a nation (FreeCol <c>Unit.nationality</c>):
    /// the nation id of the player that <em>created</em> the unit. Set once on spawn from the owner's nation
    /// (FreeCol <c>Unit.initialize</c> → <c>setNationality(owner.getNationId())</c>) and <b>not</b> changed when the
    /// unit merely switches owners in combat — so a captured colonist keeps the nationality of the side that raised it
    /// (FreeCol changes it only when a unit switches owners <em>willingly</em>, e.g. a convert). This can differ from
    /// the current <see cref="OwnerId"/> owner. Null for a unit raised without a nation (the classic nation-less human)
    /// and for a non-person (a ship/wagon never gets one — FreeCol gates on <c>isPerson()</c>). Persisted only when it
    /// has <em>diverged</em> from the owner's nation (a captured colonist); an origin that still equals the owner is
    /// omitted and re-derived on load, so a default game stays byte-identical (save v52). Only <see cref="GameSession.Game"/> sets it.
    /// </summary>
    public string? Nationality { get; internal set; }

    /// <summary>
    /// The unit's <b>ethnicity</b> — the look of the people it was born among (FreeCol <c>Unit.ethnicity</c>): the
    /// nation id whose inhabitants the unit resembles. Set once on spawn from the owner's nation (FreeCol
    /// <c>Unit.initialize</c> → <c>setEthnicity(owner.getNationId())</c>) and inherited through capture, so a former
    /// convert can keep a native look as a colonist (FreeCol <c>hasNativeEthnicity</c>). Differs from
    /// <see cref="Nationality"/> only when set deliberately (e.g. a native convert keeps a native ethnicity but takes
    /// the colony owner's nationality). Null for a nation-less raise and for a non-person. Persisted like
    /// <see cref="Nationality"/> — only when it has diverged from the owner's nation (save v52). Only <see cref="GameSession.Game"/> sets it.
    /// </summary>
    public string? Ethnicity { get; internal set; }

    /// <summary>
    /// The unit's individual <b>custom name</b> (FreeCol <c>Unit.name</c> via the <c>Nameable</c> interface): a name a
    /// player has given this one unit (e.g. christening a famous ship). Null by default — the unit shows its type's
    /// generic name. Set/cleared only through <see cref="GameSession.Game"/>'s <c>NameUnit</c> command. Persisted
    /// omit-when-null (save v52), so an unnamed unit serialises byte-identically.
    /// </summary>
    public string? Name { get; internal set; }
}
