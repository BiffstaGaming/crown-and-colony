using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Derives which land tiles each native settlement owns (FreeCol <c>Tile.owner</c> / <c>Settlement</c> claim):
/// the tiles within a settlement's <see cref="SettlementType.ClaimableRadius"/> (Chebyshev distance), on dry
/// land and in bounds, including the settlement's own tile. Where two settlements' claims overlap the tile goes
/// to the <em>nearest</em> settlement (ties broken by the lower settlement id).
///
/// <para>The claim is a pure, deterministic function of the settlements' positions and their ruleset claim radii
/// — it draws no randomness — so it recomputes byte-identically at game start and on load. Native ownership is
/// therefore <b>derived</b> (rebuilt from the saved settlements), never serialized; this mirrors how Lost City
/// Rumour placement is gen-time only. FreeCol's wider <c>extraClaimableRadius</c> (tiles claimable only under
/// pressure / by purchase) is not modelled here; only the base owned radius is.</para>
/// </summary>
public static class NativeLandClaimGenerator
{
    /// <summary>
    /// Computes the tile → owning-native-nation map for <paramref name="settlements"/> on <paramref name="map"/>.
    /// The caller folds it into the map (<see cref="GameMap.SetNativeOwner"/>).
    /// </summary>
    public static IReadOnlyDictionary<Position, string> Claim(
        GameMap map, IEnumerable<NativeSettlement> settlements, Ruleset ruleset)
    {
        // Track the best (nearest, then lowest-id) claimant per tile so overlaps resolve deterministically.
        var best = new Dictionary<Position, (int Distance, int SettlementId, string Nation)>();
        foreach (NativeSettlement settlement in settlements.OrderBy(s => s.Id))
        {
            int radius = ruleset.Settlement(settlement.SettlementTypeId).ClaimableRadius;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var p = new Position(settlement.Position.X + dx, settlement.Position.Y + dy);
                    if (!map.InBounds(p) || map.TerrainAt(p).IsWater)
                    {
                        continue; // natives claim dry land only; off-map tiles do not exist
                    }
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy)); // Chebyshev (square-tile adjacency)
                    if (!best.TryGetValue(p, out var current)
                        || distance < current.Distance
                        || (distance == current.Distance && settlement.Id < current.SettlementId))
                    {
                        best[p] = (distance, settlement.Id, settlement.NationTypeId);
                    }
                }
            }
        }
        return best.ToDictionary(kv => kv.Key, kv => kv.Value.Nation);
    }
}
