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
    /// settlements with ids assigned from 1, each seeded with a general goods store and a
    /// military stock; the caller (<c>Game</c>) then computes their stock-driven wanted goods.
    /// </summary>
    public static List<NativeSettlement> Place(
        Ruleset ruleset, GameMap map, IGameRandom random, IReadOnlySet<Position> excluded)
    {
        List<NativeSettlement> settlements = PlaceSettlements(ruleset, map, random, excluded);
        SeedGeneralStock(settlements, ruleset, random);
        SeedMilitaryStock(settlements);
        return settlements;
    }

    /// <summary>Muskets/horses one brave role count requires (FreeCol armed/mounted brave = 25); matches <c>Game.BraveEquipGoods</c>.</summary>
    private const int BraveEquipGoods = 25;

    /// <summary>Muskets goods id (the armed-brave equipment); matches <c>Game.MusketsId</c>.</summary>
    private const string MusketsId = "model.goods.muskets";

    /// <summary>Horses goods id (the mounted-brave equipment); matches <c>Game.HorsesId</c>.</summary>
    private const string HorsesId = "model.goods.horses";

    /// <summary>
    /// Capital / city settlement size at/above which a camp also stocks horses (so its braves can mount and, with
    /// muskets too, reach native dragoon). Smaller camps stock muskets only — they arm but do not field dragoons.
    /// </summary>
    private const int MountedStockSizeThreshold = 7;

    /// <summary>
    /// Seeds each settlement's <b>military stock</b> at game creation (Option B, faithful to FreeCol's military-goods
    /// retention <c>Settlement.getWantedGoodsAmount</c>: a settlement keeps enough muskets/horses to arm its braves).
    /// Every settlement stocks one armed-brave's worth of <b>muskets</b> (<see cref="BraveEquipGoods"/>); a
    /// <b>capital</b> — or any settlement of size ≥ <see cref="MountedStockSizeThreshold"/> (a village/city) — also
    /// stocks one mounted-brave's worth of <b>horses</b>, so the better, larger settlements field native dragoons while
    /// small frontier camps merely arm. Entirely <b>deterministic and RNG-free</b> (a pure function of each
    /// settlement's type/capital/size), so it draws no random numbers at all — never the human's stream 0 (ADR-009).
    /// This is what makes the runtime equip chain (<c>Game.EquipBravesAtThreatenedSettlements</c> → <c>TryEquipBrave</c>)
    /// fire on a real game's data rather than only on stock deposited by tests.
    /// </summary>
    private static void SeedMilitaryStock(IReadOnlyList<NativeSettlement> settlements)
    {
        foreach (NativeSettlement settlement in settlements)
        {
            settlement.AddStock(MusketsId, BraveEquipGoods);
            if (settlement.IsCapital || settlement.Size >= MountedStockSizeThreshold)
            {
                settlement.AddStock(HorsesId, BraveEquipGoods);
            }
        }
    }

    /// <summary>
    /// Seeds the general goods stock of each supplied settlement, exactly as <see cref="Place"/> does for generated
    /// ones — used to finish off settlements placed by an imported scenario map (<see cref="MapImporter"/>) rather than
    /// by this generator, so an imported settlement trades like a generated one. Draws from <paramref name="random"/>
    /// (the native stream) so the human's economy stream 0 is untouched (ADR-009). The caller (<c>Game</c>) recomputes
    /// each settlement's stock-driven wanted goods afterwards.
    /// </summary>
    public static void SeedGeneralStockTo(
        IReadOnlyList<NativeSettlement> settlements, Ruleset ruleset, IGameRandom random) =>
        SeedGeneralStock(settlements, ruleset, random);

    /// <summary>The New World raw goods native settlements gather (sugar/tobacco/cotton/furs/ore) — the candidates for the seeded general store; FreeCol seeds from tile auto-potential, which on native land is dominated by these.</summary>
    private static readonly string[] SeedGoods =
        ["model.goods.sugar", "model.goods.tobacco", "model.goods.cotton", "model.goods.furs", "model.goods.ore"];

    /// <summary>Largest seeded amount of any one good — under one full hold so a fresh store has room (FreeCol <c>GoodsContainer.CARGO_SIZE</c> = 100).</summary>
    private const int MaxSeedAmount = 60;

    /// <summary>
    /// Seeds each settlement's <b>general goods stock</b> (the wares a trader can buy and the basis of its stock-driven
    /// wanted goods, FreeCol <c>IndianSettlement.addRandomGoods</c>: "they have been here for some time"). Each
    /// settlement gets a per-settlement-varied amount (0–<see cref="MaxSeedAmount"/>) of each New World raw good
    /// (<see cref="SeedGoods"/>), drawn on the <paramref name="random"/> native stream so two settlements hold different
    /// stores and therefore <b>want different goods</b> (each is shortest of, and so most wants, what it gathered least
    /// of). A bigger settlement (more residents) holds proportionally more. Drawn on the native stream only — never the
    /// human's economy stream 0 (ADR-009); a zero draw simply leaves that good unstocked (omit-when-default save).
    /// </summary>
    private static void SeedGeneralStock(
        IReadOnlyList<NativeSettlement> settlements, Ruleset ruleset, IGameRandom random)
    {
        // Only seed goods the ruleset actually defines (the classic set always does; a stripped variant may not).
        var present = SeedGoods.Where(id => ruleset.GoodsTypes.Any(g => g.Id == id)).ToArray();
        foreach (NativeSettlement settlement in settlements)
        {
            foreach (string good in present)
            {
                // 0..MaxSeedAmount scaled lightly by size (a 4-resident camp ~⅔ of a 6-resident one), rounded down.
                int roll = random.Next(MaxSeedAmount + 1);
                int amount = roll * Math.Max(1, settlement.Size) / 6;
                if (amount > 0)
                {
                    settlement.AddGoods(good, amount);
                }
            }
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
