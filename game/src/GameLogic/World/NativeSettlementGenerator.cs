using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World;

/// <summary>
/// Places native settlements on a generated map (FreeCol
/// <c>SimpleMapGenerator.makeNativeSettlements</c>). Capitals are placed first,
/// then regular settlements round-robin so every nation appears; each settlement
/// sits on a suitable land tile kept a minimum distance from the others and from
/// the player's landing site.
///
/// <para>Simplifications vs. FreeCol (documented in docs/systems/natives.md): our
/// map has no named regions or difficulty-scaled settlement counts, so per-nation
/// counts come from the <see cref="SettlementNumber"/> band via
/// <see cref="TargetCount"/> rather than FreeCol's region/landmass formula. The
/// suitability and capital-first rules are kept.</para>
///
/// Deterministic for a given <see cref="IGameRandom"/>.
/// </summary>
public static class NativeSettlementGenerator
{
    /// <summary>Minimum Chebyshev distance between settlement tiles, and from excluded tiles.</summary>
    private const int MinSettlementDistance = 3;

    /// <summary>Settlements a nation founds for each <see cref="SettlementNumber"/> band (a tuned simplification).</summary>
    private static int TargetCount(SettlementNumber band) => band switch
    {
        SettlementNumber.Low => 1,
        SettlementNumber.High => 3,
        _ => 2,
    };

    /// <summary>
    /// Generates the native settlements for a map. <paramref name="excluded"/> are
    /// tiles to stay clear of (the player's landing site and its surrounds). Returns
    /// settlements with ids assigned from 1, each with its wanted goods.
    /// </summary>
    public static List<NativeSettlement> Place(
        Ruleset ruleset, GameMap map, IGameRandom random, IReadOnlySet<Position> excluded)
    {
        List<NativeSettlement> settlements = PlaceSettlements(ruleset, map, random, excluded);
        AssignWantedGoods(settlements, ruleset, random);
        return settlements;
    }

    /// <summary>
    /// Gives each settlement up to 3 wanted goods (FreeCol <c>wantedGoods</c>) — drawn
    /// <em>after</em> placement so the placement RNG sequence (positions/sizes/skills) is
    /// unchanged. FreeCol picks the top-3 by buy-price (settlement-stock-dependent); we
    /// don't model settlement stock yet, so we draw 3 distinct tradeable goods.
    /// </summary>
    private static void AssignWantedGoods(
        List<NativeSettlement> settlements, Ruleset ruleset, IGameRandom random)
    {
        var tradeable = ruleset.GoodsTypes.Where(g => g.Market is not null).Select(g => g.Id).ToList();
        if (tradeable.Count == 0)
        {
            return;
        }
        foreach (NativeSettlement settlement in settlements)
        {
            var pool = new List<string>(tradeable);
            Shuffle(pool, random);
            settlement.WantedGoods = pool.Take(Math.Min(3, pool.Count)).ToList();
        }
    }

    private static List<NativeSettlement> PlaceSettlements(
        Ruleset ruleset, GameMap map, IGameRandom random, IReadOnlySet<Position> excluded)
    {
        var settlements = new List<NativeSettlement>();
        if (ruleset.NativeNationTypes.Count == 0)
        {
            return settlements;
        }

        // Suitable land tiles, in an unbiased (shuffled) but seed-deterministic order.
        var available = map.AllPositions().Where(p => IsSuitable(map, p)).ToList();
        Shuffle(available, random);

        int nextId = 1;

        // A tile is usable if it is at least MinSettlementDistance from every excluded
        // tile and every settlement placed so far. Removes and returns the first such tile.
        Position? TakeTile()
        {
            for (int i = 0; i < available.Count; i++)
            {
                Position p = available[i];
                if (excluded.All(e => Chebyshev(p, e) >= MinSettlementDistance)
                    && settlements.All(s => Chebyshev(p, s.Position) >= MinSettlementDistance))
                {
                    available.RemoveAt(i);
                    return p;
                }
            }
            return null;
        }

        // Phase 1 — a capital for each nation (placed first, as in FreeCol).
        foreach (NativeNationType nation in ruleset.NativeNationTypes)
        {
            if (TakeTile() is not { } tile)
            {
                return settlements; // no room anywhere
            }
            settlements.Add(MakeSettlement(ref nextId, nation, capital: true, tile, ruleset, random));
        }

        // Phase 2 — the remaining settlements, round-robin so nations stay balanced.
        var remaining = ruleset.NativeNationTypes
            .ToDictionary(n => n.Id, n => TargetCount(n.NumberOfSettlements) - 1);
        while (remaining.Values.Any(count => count > 0))
        {
            bool placedThisRound = false;
            foreach (NativeNationType nation in ruleset.NativeNationTypes)
            {
                if (remaining[nation.Id] <= 0)
                {
                    continue;
                }
                if (TakeTile() is not { } tile)
                {
                    return settlements; // room exhausted
                }
                settlements.Add(MakeSettlement(ref nextId, nation, capital: false, tile, ruleset, random));
                remaining[nation.Id]--;
                placedThisRound = true;
            }
            if (!placedThisRound)
            {
                break;
            }
        }

        return settlements;
    }

    private static NativeSettlement MakeSettlement(
        ref int nextId, NativeNationType nation, bool capital, Position tile,
        Ruleset ruleset, IGameRandom random)
    {
        SettlementType type = ruleset.Settlement(
            capital ? nation.CapitalSettlementTypeId : nation.SettlementTypeId);
        int size = type.MaximumSize > type.MinimumSize
            ? random.Next(type.MinimumSize, type.MaximumSize + 1)
            : type.MinimumSize;
        return new NativeSettlement(
            nextId++, nation.Id, type.Id, capital, tile, size, DrawSkill(nation.Skills, random));
    }

    /// <summary>A weighted-random taught skill, or null when the nation teaches nothing.</summary>
    private static string? DrawSkill(IReadOnlyList<NativeSkill> skills, IGameRandom random)
    {
        int total = skills.Sum(s => s.Probability);
        if (total <= 0)
        {
            return null;
        }
        int roll = random.Next(total);
        foreach (NativeSkill skill in skills)
        {
            roll -= skill.Probability;
            if (roll < 0)
            {
                return skill.UnitTypeId;
            }
        }
        return skills[^1].UnitTypeId; // unreachable: roll < total
    }

    /// <summary>
    /// A tile a native settlement may occupy: settleable land whose neighbourhood is
    /// at least half land (FreeCol's anti-islet rule — no settlements on thin spits).
    /// </summary>
    private static bool IsSuitable(GameMap map, Position p)
    {
        TerrainType terrain = map.TerrainAt(p);
        if (terrain.IsWater || !terrain.CanSettle)
        {
            return false;
        }
        var neighbours = p.Neighbours().Where(map.InBounds).ToList();
        int land = neighbours.Count(n => !map.TerrainAt(n).IsWater);
        return neighbours.Count > 0 && land * 2 >= neighbours.Count;
    }

    private static int Chebyshev(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

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
