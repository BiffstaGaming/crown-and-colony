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
