namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A trade route: a named, ordered ring of stops a carrier hauls along automatically each turn (FreeCol
/// <c>TradeRoute</c>). At each stop the carrier <b>unloads</b> everything it holds that the stop does <em>not</em>
/// list to load (delivering it), then <b>loads</b> the stop's listed goods, then heads for the next stop (wrapping
/// after the last). A route is owned by a player; a carrier is attached to it by <see cref="Units.Unit.TradeRouteId"/>.
/// </summary>
/// <param name="Id">Stable per-player route id (assigned by <c>Game.CreateTradeRoute</c>).</param>
/// <param name="Name">Player-facing route name.</param>
/// <param name="Stops">The ordered stops; a route needs at least two to move goods (a 0/1-stop route is inert).</param>
public sealed record TradeRoute(int Id, string Name, IReadOnlyList<TradeRouteStop> Stops);

/// <summary>One stop on a <see cref="TradeRoute"/>: a colony to visit and the goods to load there.</summary>
/// <param name="ColonyId">The colony this stop visits.</param>
/// <param name="LoadGoodsIds">Goods ids to load at this stop; everything else the carrier holds is unloaded here (delivered).</param>
public sealed record TradeRouteStop(int ColonyId, IReadOnlyList<string> LoadGoodsIds);

/// <summary>
/// The kind of problem a trade route can have, mirroring the cases FreeCol's <c>TradeRoute.verify()</c> reports
/// (each maps to one of its <c>model.tradeRoute.*</c> message keys). A warning is advisory — like FreeCol, the
/// game <b>warns but never blocks</b>: a route with warnings still runs, it just may not behave as the player hopes.
/// </summary>
public enum TradeRouteWarningKind
{
    /// <summary>The route has fewer than two stops, so a carrier can never move goods (FreeCol <c>notEnoughStops</c>).</summary>
    NotEnoughStops,

    /// <summary>A stop names a colony the route's owner does not own or that no longer exists (FreeCol <c>invalidStop</c>).</summary>
    InvalidStop,

    /// <summary>No stop lists any goods to load, so the route would haul nothing (FreeCol <c>allEmpty</c>).</summary>
    AllEmpty,

    /// <summary>
    /// A good is listed to load at <em>every</em> stop, so it is never unloaded anywhere and can never be delivered —
    /// it just rides the carrier forever (FreeCol <c>alwaysPresent</c>). Relaxed when the ENHANCED_TRADE_ROUTES game
    /// option is on (off by default in the classic ruleset).
    /// </summary>
    GoodsAlwaysPresent,
}

/// <summary>
/// One advisory problem found by validating a <see cref="TradeRoute"/> (the C# analogue of a FreeCol
/// <c>verify()</c> <c>StringTemplate</c>). Pure data: a <see cref="Kind"/>, the <see cref="RouteId"/> it belongs to,
/// the offending stop index (or <c>null</c> for a whole-route problem), the offending goods id (or <c>null</c>), and
/// a plain-English <see cref="Message"/> for the UI. Warnings never block — they are surfaced, not enforced.
/// </summary>
/// <param name="RouteId">The <see cref="TradeRoute.Id"/> the warning applies to.</param>
/// <param name="Kind">Which validation check failed.</param>
/// <param name="StopIndex">The zero-based index of the offending stop, or <c>null</c> for a route-wide problem.</param>
/// <param name="GoodsId">The offending goods id (for <see cref="TradeRouteWarningKind.GoodsAlwaysPresent"/>), or <c>null</c>.</param>
/// <param name="Message">A human-readable description suitable for display.</param>
public sealed record TradeRouteWarning(
    int RouteId,
    TradeRouteWarningKind Kind,
    int? StopIndex,
    string? GoodsId,
    string Message);
