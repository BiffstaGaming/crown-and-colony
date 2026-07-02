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
    /// RNG stream id reserved for the gen-time rumour-<b>type</b> roll (the MOUNDS pre-stamp, 86d3fpxv8) — a high id
    /// like the placement stream (<c>Game.LcrStreamId</c> = 100; 105 is taken by market dynamics), so the type draws
    /// never correlate with or shift any other stream (ADR-009). Seeded off the placement stream's <b>state</b> after
    /// the position shuffle, so the positions a seed picks are byte-identical to before the stamping existed (the
    /// shuffle consumes the placement stream exactly as before; the type roll consumes only this derived stream).
    /// </summary>
    public const ulong MoundsTypeStreamId = 106;

    /// <summary>Classic medium good-rumour percentage (FreeCol <c>GameOptions.GOOD_RUMOUR</c>, <c>model.option.goodRumour</c>) — the default when a caller omits the difficulty's value.</summary>
    public const int ClassicGoodRumourPercent = 48;

    /// <summary>Classic medium bad-rumour percentage (FreeCol <c>GameOptions.BAD_RUMOUR</c>, <c>model.option.badRumour</c>) — the default when a caller omits the difficulty's value.</summary>
    public const int ClassicBadRumourPercent = 23;

    /// <summary>
    /// Picks the rumour tiles for a map and <b>pre-stamps</b> the strange-mounds ones. <paramref name="excluded"/> are
    /// tiles to avoid (settlements, units, and the player's landing area). Returns the chosen positions; the caller
    /// folds them into the map (<see cref="GameMap.AddRumour"/>). Count ≈ <c>width·height·landMass% / rumourNumber</c>,
    /// capped by the eligible-tile count (fewer on a small/over-constrained map — faithful to FreeCol's attempt cap).
    /// <paramref name="rumourNumber"/> is FreeCol's <c>model.option.rumourNumber</c> (land tiles per rumour; classic
    /// <see cref="DefaultRumourNumber"/> = 35, <b>higher = fewer</b> rumours) — a New-Game dial (86d3fq1b8); it defaults
    /// to the classic value so an omitting caller is byte-identical. A value &lt;= 0 is treated as the default (no
    /// division-by-zero), so the dial can never disable the pass entirely.
    /// <para><b>MOUNDS pre-stamp (86d3fpxv8, FreeCol <c>SimpleMapGenerator.makeLostCityRumours</c> ~L188-193):</b>
    /// after the positions are picked, each chosen tile in order draws one gen-time rumour type from FreeCol's
    /// <c>LostCityRumour.chooseType(null, random)</c> table (no exploring unit: no Fountain of Youth, no Learn — see
    /// <see cref="RollMoundsStamp"/>); a tile whose roll lands MOUNDS <b>and</b> that the natives own
    /// (<see cref="GameMap.IsNativeOwned"/> — FreeCol <c>getOwningSettlement() != null</c>) is stamped via
    /// <see cref="GameMap.StampMoundsRumour"/>, persisting the determined type exactly as FreeCol's
    /// <c>setType(MOUNDS)</c> does. The type draws come from a fresh stream-<see cref="MoundsTypeStreamId"/> generator
    /// seeded off <paramref name="random"/>'s state, so the rumour POSITIONS for a seed are unchanged (ADR-009).
    /// <paramref name="goodRumourPercent"/>/<paramref name="badRumourPercent"/> are the difficulty's
    /// <c>goodRumour</c>/<c>badRumour</c> percentages (classic medium 48/23 when omitted).</para>
    /// </summary>
    public static IReadOnlyCollection<Position> Place(
        GameMap map, IReadOnlySet<Position> excluded, IGameRandom random, int rumourNumber = DefaultRumourNumber,
        int goodRumourPercent = ClassicGoodRumourPercent, int badRumourPercent = ClassicBadRumourPercent)
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
        var chosen = eligible.Take(target).ToList();

        // Gen-time MOUNDS pre-stamp: one chooseType(null) draw per chosen tile, in the chosen (shuffled) order, on a
        // FRESH derived stream — the placement stream `random` is not advanced, so positions stay byte-identical.
        var typeRandom = new Pcg32Random(random.SaveState().State, MoundsTypeStreamId);
        foreach (Position p in chosen)
        {
            if (RollMoundsStamp(typeRandom, map.IsNativeOwned(p), goodRumourPercent, badRumourPercent)
                && map.IsNativeOwned(p))
            {
                map.StampMoundsRumour(p);
            }
        }
        return chosen;
    }

    /// <summary>
    /// One gen-time rumour-type draw (FreeCol <c>LostCityRumour.chooseType(null, random)</c>,
    /// <c>common/model/LostCityRumour.java</c> L179-271), returning whether it landed on strange MOUNDS. With no
    /// exploring unit the table is: <b>good</b> ×<paramref name="goodPercent"/> — TRIBAL_CHIEF 50, COLONIST 30,
    /// MOUNDS 8, RUINS 6, CIBOLA 4 (no Fountain of Youth — <c>allowFoY</c> needs a colonial unit; no LEARN —
    /// <c>allowLearn</c> needs a unit with a lost-city change); <b>bad</b> — BURIAL_GROUND 25·bad (native-owned land
    /// only) + EXPEDITION_VANISHES 75·bad (<c>allowVanish</c> is true with a null unit), normalised to a total of 100
    /// (<c>RandomChoice.normalize(cbad, 100)</c>); <b>neutral</b> — NOTHING 100·remainder. No unit ⇒ no De Soto / no
    /// scout modifier tilts the percentages. Exactly one <c>Next(total)</c> is drawn per call (the table is never a
    /// single entry), walking the cumulative weights like FreeCol <c>RandomChoice.getWeightedRandom</c>. Note the two
    /// bad variants normalise to the same 100 footprint, so the draw's total — and hence the MOUNDS interval — is
    /// identical on and off native land; only the stamp gate differs.
    /// </summary>
    private static bool RollMoundsStamp(IGameRandom random, bool nativeOwned, int goodPercent, int badPercent)
    {
        int neutral = Math.Max(0, 100 - badPercent - goodPercent);

        // The good sub-list in FreeCol's null-unit order; only MOUNDS' interval matters for the stamp, but the whole
        // table is built so the single draw covers the exact FreeCol distribution.
        var weights = new List<(bool IsMounds, int Weight)>();
        if (goodPercent > 0)
        {
            weights.Add((false, 50 * goodPercent)); // TRIBAL_CHIEF
            weights.Add((false, 30 * goodPercent)); // COLONIST
            weights.Add((true, 8 * goodPercent));   // MOUNDS
            weights.Add((false, 6 * goodPercent));  // RUINS
            weights.Add((false, 4 * goodPercent));  // CIBOLA
        }
        if (badPercent > 0)
        {
            // BURIAL_GROUND (native land) + EXPEDITION_VANISHES, normalised to 100 (RandomChoice.normalize):
            // {25·bad, 75·bad} → {25, 75}; vanish alone → {100}. Either way the bad side occupies 100.
            if (nativeOwned)
            {
                weights.Add((false, 25));
                weights.Add((false, 75));
            }
            else
            {
                weights.Add((false, 100));
            }
        }
        if (neutral > 0)
        {
            weights.Add((false, 100 * neutral)); // NOTHING
        }

        int total = weights.Sum(w => w.Weight);
        if (total <= 0)
        {
            return false; // degenerate percentages (good 0 / bad 0 / neutral 0) — nothing to draw
        }
        int roll = random.Next(total);
        int cumulative = 0;
        foreach ((bool isMounds, int weight) in weights)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                return isMounds;
            }
        }
        return false;
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
