using CrownAndColony.GameLogic.Randomness;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Scatters Lost City Rumours on a generated map (FreeCol <c>SimpleMapGenerator.makeLostCityRumours</c>): a
/// target number of land tiles, kept clear of settlements, units and the player's landing, each carrying an
/// <em>undetermined</em> rumour whose reward is rolled only when a unit explores it (a later slice).
///
/// <para>Simplifications vs. FreeCol (documented in docs/systems/lost-city-rumours.md): FreeCol estimates the
/// land count from the map's <c>landMass</c> option (classic 25%); our generator has no such option and grows
/// continents to ~45% land, so we use that fraction (<see cref="LandMassPercent"/>) for the count — faithful to
/// FreeCol's <em>intent</em> (a rumour every <see cref="DefaultRumourNumber"/> land tiles, a New-Game dial) and to what the player sees,
/// not to the 25% constant. We also skip FreeCol's <c>SLOSH</c> edge-inset sampler: our maps already keep a
/// watery margin, so uniform sampling over land tiles yields the same inset effect.</para>
///
/// Deterministic for a given <see cref="IGameRandom"/> — which MUST be a stream dedicated to rumour placement
/// (never the human's stream 0), so adding rumours leaves every economy/combat/immigration draw byte-identical.
/// </summary>
public static class LostCityRumourGenerator
{
    /// <summary>Land tiles per rumour (FreeCol <c>model.option.rumourNumber</c>, classic default) — the value used when a caller omits the New-Game dial.</summary>
    public const int DefaultRumourNumber = 35;

    /// <summary>Land-fraction estimate for the count, matching our generator's ~45%-land continents (FreeCol estimates 25%).</summary>
    private const int LandMassPercent = 45;

    /// <summary>
    /// Picks the rumour tiles for a map. <paramref name="excluded"/> are tiles to avoid (settlements, units, and
    /// the player's landing area). Returns the chosen positions; the caller folds them into the map
    /// (<see cref="GameMap.AddRumour"/>). Count ≈ <c>width·height·landMass% / rumourNumber</c>, capped by the
    /// eligible-tile count (fewer on a small/over-constrained map — faithful to FreeCol's attempt cap).
    /// <paramref name="rumourNumber"/> is FreeCol's <c>model.option.rumourNumber</c> (land tiles per rumour; classic
    /// <see cref="DefaultRumourNumber"/> = 35, <b>higher = fewer</b> rumours) — a New-Game dial (86d3fq1b8); it defaults
    /// to the classic value so an omitting caller is byte-identical. A value &lt;= 0 is treated as the default (no
    /// division-by-zero), so the dial can never disable the pass entirely.
    /// </summary>
    public static IReadOnlyCollection<Position> Place(
        GameMap map, IReadOnlySet<Position> excluded, IGameRandom random, int rumourNumber = DefaultRumourNumber)
    {
        if (rumourNumber <= 0)
        {
            rumourNumber = DefaultRumourNumber;
        }
        int target = map.Width * map.Height * LandMassPercent / 100 / rumourNumber;
        if (target <= 0)
        {
            return [];
        }

        var eligible = map.AllPositions().Where(p => IsEligible(map, p, excluded)).ToList();
        Shuffle(eligible, random);
        return eligible.Take(target).ToList();
    }

    /// <summary>A tile a rumour may sit on: dry land, clear of any settlement/unit/start area, and off the polar rows.</summary>
    private static bool IsEligible(GameMap map, Position p, IReadOnlySet<Position> excluded) =>
        !map.TerrainAt(p).IsWater
        && !map.HasRumour(p)
        && !excluded.Contains(p)
        && !IsPolar(map, p);

    /// <summary>FreeCol <c>Map.isPolar</c>: the top and bottom three rows (no rumours on the ice caps).</summary>
    private static bool IsPolar(GameMap map, Position p) => p.Y <= 2 || p.Y >= map.Height - 3;

    /// <summary>In-place Fisher–Yates shuffle driven by the seeded RNG (deterministic).</summary>
    private static void Shuffle<T>(IList<T> list, IGameRandom random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
