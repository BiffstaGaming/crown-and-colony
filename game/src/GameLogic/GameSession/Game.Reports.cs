using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// Read-only report oracles (ADR-006): the pure, RNG-free reads the empire-report panels
/// (the presentation layer's <c>ColonyReportPanel</c>) surface but do not compute — the per-type Royal
/// Expeditionary Force composition (the REF intelligence report), the colonial nation ranking by final score (the
/// score / nation report), and the named tension band a colonial pair sits in (the Foreign Affairs report's
/// tension/attitude column). Each mutates nothing and draws no randomness, so a report read leaves the game
/// byte-identical (ADR-009); none is persisted. FreeCol surfaces the same figures in its <c>Report*Panel</c> family
/// (<c>ReportRequirementsPanel</c> siblings) and <c>NationSummary</c>.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// One block of like units in the Royal Expeditionary Force's current composition (a display projection of a
    /// <see cref="ForceEntry"/>): the unit type, the role it carries (e.g. infantry / cavalry, null for artillery and
    /// ships), how many there are, and whether the block is naval. Used by the REF intelligence report to list the
    /// force as "N × King's Regular (cavalry)" rows rather than a bare land/naval count.
    /// </summary>
    /// <param name="UnitTypeId">The ruleset unit-type id of the block (e.g. <c>model.unit.kingsRegular</c>).</param>
    /// <param name="RoleId">The ruleset role id the units carry, or <c>null</c> when the type has no distinguishing role (artillery, man-o-war).</param>
    /// <param name="Count">How many units the block holds.</param>
    /// <param name="IsNaval">Whether this is a naval block (a transport/escort) rather than a land block.</param>
    public readonly record struct RefForceBlock(string UnitTypeId, string? RoleId, int Count, bool IsNaval);

    /// <summary>
    /// One colonial power's standing in the nation ranking (the score / nation report): the player, its final
    /// <see cref="PlayerScore"/>, and its colony / unit counts. A pure value projection for display; ordering and
    /// rank are the panel's concern (it sorts by <see cref="Score"/> descending).
    /// </summary>
    /// <param name="Player">The ranked player (the human or a foreign colonial power, including an independent nation).</param>
    /// <param name="Score">The player's final score (<see cref="PlayerScore"/>).</param>
    /// <param name="ColonyCount">How many colonies the player owns.</param>
    /// <param name="UnitCount">How many (non-native) units the player owns.</param>
    public readonly record struct NationStanding(Player Player, int Score, int ColonyCount, int UnitCount);

    /// <summary>
    /// The Royal Expeditionary Force's <b>current composition</b>, block by block, for the REF intelligence report (a
    /// read-only oracle, ADR-006; FreeCol <c>Monarch.getExpeditionaryForce</c> projected to per-type counts). Returns
    /// the land blocks first (King's Regulars in their infantry/cavalry roles, then artillery), then the naval blocks
    /// (men-o-war), each a <see cref="RefForceBlock"/>. The force materialises its base composition for the read
    /// without storing it (no save change — the persisted REF is whatever the saved force holds), exactly as
    /// <see cref="ExpeditionaryForceStrength"/> does. The presentation surfaces these rows so the player can watch the
    /// King's army grow (via ADD_TO_REF) before deciding whether to break away. RNG-free; never mutates.
    /// </summary>
    /// <returns>The REF's land blocks (in force order) followed by its naval blocks.</returns>
    public IReadOnlyList<RefForceBlock> ExpeditionaryForceComposition()
    {
        Force ref_ = _refForce ?? BuildBaseRef(); // read-only: don't store a lazily-built base (the saved force stays the truth)
        var blocks = new List<RefForceBlock>(ref_.LandUnits.Count + ref_.NavalUnits.Count);
        foreach (ForceEntry e in ref_.LandUnits)
        {
            blocks.Add(new RefForceBlock(e.UnitTypeId, e.RoleId, e.Count, IsNaval: false));
        }
        foreach (ForceEntry e in ref_.NavalUnits)
        {
            blocks.Add(new RefForceBlock(e.UnitTypeId, e.RoleId, e.Count, IsNaval: true));
        }
        return blocks;
    }

    /// <summary>
    /// The colonial nation ranking by final score, highest first (the score / nation report; FreeCol surfaces the
    /// score in its report/high-score screens). A read-only oracle (ADR-006): every colonial-or-independent power —
    /// the human and the foreign powers, but not the natives or the Royal Expeditionary Force — with its
    /// <see cref="PlayerScore"/> and its colony / unit counts, ordered by score descending (ties broken by
    /// <see cref="Player.PlayerId"/> for stability). Pure and RNG-free; never mutates, never persisted.
    /// </summary>
    /// <returns>The ranked standings, best score first.</returns>
    public IReadOnlyList<NationStanding> NationRanking() =>
        _players
            .Where(p => p.PlayerType is PlayerType.Colonial or PlayerType.Rebel or PlayerType.Independent)
            .Select(p => new NationStanding(
                p,
                PlayerScore(p),
                _colonies.Count(c => c.OwnerId == p.PlayerId),
                _units.Count(u => u.OwnerId == p.PlayerId && !u.IsNative)))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Player.PlayerId)
            .ToList();

    /// <summary>
    /// The named tension band <paramref name="a"/> holds toward <paramref name="b"/> (the Foreign Affairs report's
    /// tension/attitude column; FreeCol <c>Tension.getLevel</c>). A read-only oracle (ADR-006) banding the raw
    /// <see cref="TensionBetween"/> scalar through FreeCol's <c>Tension.Level</c> thresholds — Happy ≤ 100, Content ≤
    /// 600, Displeased ≤ 700, Angry ≤ 800, else Hateful — reusing the <see cref="AlarmLevel"/> enum (whose bands are
    /// the same FreeCol <c>Tension.Level</c> figures the natives use), so the banding lives in the engine rather than
    /// the panel. Pure and RNG-free.
    /// </summary>
    /// <param name="a">The player whose attitude is read (their <see cref="Player.PlayerId"/>).</param>
    /// <param name="b">The player the attitude is toward (their <see cref="Player.PlayerId"/>).</param>
    /// <returns>The tension band, from <see cref="AlarmLevel.Happy"/> (calm) to <see cref="AlarmLevel.Hateful"/>.</returns>
    public AlarmLevel TensionLevelBetween(int a, int b)
    {
        int tension = TensionBetween(a, b);
        // FreeCol Tension.Level thresholds (HAPPY 100, CONTENT 600, DISPLEASED 700, ANGRY 800, HATEFUL 1000) — the
        // same bands the native AlarmLevel uses, so we reuse that enum rather than declare a parallel one.
        return tension <= 100 ? AlarmLevel.Happy
            : tension <= 600 ? AlarmLevel.Content
            : tension <= 700 ? AlarmLevel.Displeased
            : tension <= 800 ? AlarmLevel.Angry
            : AlarmLevel.Hateful;
    }

    // ── Labour-detail drill-down (FreeCol ReportLabourDetailPanel / CompactLabourReport) ────────────────────

    /// <summary>
    /// One location's count of colonists of a given unit type in the labour-detail drill-down (a row in FreeCol's
    /// <c>ReportLabourDetailPanel</c>, whose model is <c>Map&lt;UnitType, Map&lt;Location, Integer&gt;&gt;</c>): the place
    /// (a colony name, or "in the field" for on-map person units) and how many of that type are there. A pure value
    /// projection for display.
    /// </summary>
    /// <param name="Location">The place the colonists are — a human colony's name, or the field marker for on-map units.</param>
    /// <param name="Count">How many colonists of the drilled-into unit type are at this location.</param>
    public readonly record struct LabourLocation(string Location, int Count);

    /// <summary>The location marker the labour-detail oracle uses for the human's on-map person units (not in any colony).</summary>
    public const string LabourFieldLocation = "In the field";

    /// <summary>
    /// The labour-detail drill-down for one unit type (a read-only oracle, ADR-006; FreeCol's
    /// <c>ReportLabourDetailPanel</c> data for a chosen <c>UnitType</c>): <b>where the human's colonists of
    /// <paramref name="unitTypeId"/> are</b>, location by location, each with its head-count. Tallies the same colonists
    /// the flat Labour report does — colony residents from the worker overlays (tile workers, building occupants, idle
    /// pool) plus the on-map person units (under <see cref="LabourFieldLocation"/>) — but bucketed by location for the one
    /// chosen type rather than listed flat. Colonies are returned in id order, the field marker last; a location with no
    /// colonists of that type is omitted. Pure and RNG-free; never mutates, never persisted.
    /// </summary>
    /// <param name="unitTypeId">The ruleset unit-type id to drill into (e.g. <c>model.unit.expertFarmer</c>).</param>
    /// <returns>The per-location counts (colonies in id order, then the field), empty when the human has none of that type.</returns>
    public IReadOnlyList<LabourLocation> LabourDetail(string unitTypeId)
    {
        var result = new List<LabourLocation>();

        foreach (Colony c in _colonies.Where(c => c.OwnerId == _human.PlayerId).OrderBy(c => c.Id))
        {
            int count = 0;
            foreach (Position tile in c.TileWorkers.Keys)
            {
                if (c.WorkerTypeAt(tile) == unitTypeId)
                {
                    count++;
                }
            }
            foreach (string buildingId in c.Buildings)
            {
                foreach (string occupant in c.BuildingOccupants(buildingId))
                {
                    if (occupant == unitTypeId)
                    {
                        count++;
                    }
                }
            }
            foreach (string idle in c.IdleWorkerTypes)
            {
                if (idle == unitTypeId)
                {
                    count++;
                }
            }
            // Free-colonist idle remainder (the idle pool beyond the named non-free types is all free colonists).
            if (unitTypeId == Colony.FreeColonistTypeId)
            {
                count += c.IdleColonists - c.IdleWorkerTypes.Count;
            }
            if (count > 0)
            {
                result.Add(new LabourLocation(c.Name, count));
            }
        }

        int field = PlayerUnits.Count(u => u.Type.IsPerson && u.Type.Id == unitTypeId);
        if (field > 0)
        {
            result.Add(new LabourLocation(LabourFieldLocation, field));
        }

        return result;
    }

    // ── Colony requirements advisor (FreeCol ReportRequirementsPanel.checkColony extras) ─────────────────────

    /// <summary>
    /// One production-shortage warning for a colony (a row of FreeCol's <c>ReportRequirementsPanel</c>
    /// production/missing-goods warnings): a manned building making <see cref="OutputGoodsId"/> is below its potential
    /// because the colony is net-short of the input <see cref="InputGoodsId"/> it consumes. A pure value projection for
    /// the Requirements report.
    /// </summary>
    /// <param name="OutputGoodsId">The good the starved building makes (e.g. <c>model.goods.cloth</c>).</param>
    /// <param name="InputGoodsId">The input good the colony is net-negative on, throttling that output (e.g. <c>model.goods.cotton</c>).</param>
    public readonly record struct ColonyProductionWarning(string OutputGoodsId, string InputGoodsId);

    /// <summary>
    /// The <b>production-shortage warnings</b> for a colony (a read-only oracle, ADR-006; FreeCol
    /// <c>ReportRequirementsPanel.checkColony</c>'s "not enough input" branch — a building whose
    /// <c>ProductionInfo</c> is <c>!atMaximumProduction()</c> warns for each of its input goods). A manned,
    /// non-teaching building making a good is flagged when the colony's whole-colony <b>net</b> production of one of
    /// that building's input goods is <b>negative</b> (it burns more than it makes, so the building runs below its
    /// potential output). Reuses <see cref="ColonyProductionSummary"/> (the same tested figure the production overview
    /// shows) for the net; the panel stays rules-free. One warning per distinct (output, input) pair, in building /
    /// production order. Pure and RNG-free; never mutates, never persisted.
    /// </summary>
    /// <param name="colony">The colony to check.</param>
    /// <returns>The production-shortage warnings (empty when every manned building has its inputs covered).</returns>
    public IReadOnlyList<ColonyProductionWarning> ColonyProductionWarnings(Colony colony)
    {
        IReadOnlyDictionary<string, ColonyGoodFlow> flows = ColonyProductionSummary(colony);
        var result = new List<ColonyProductionWarning>();
        var seen = new HashSet<(string, string)>();

        foreach (string buildingId in colony.Buildings)
        {
            BuildingType building = Ruleset.Building(buildingId);
            if (building.Teaches || colony.BuildingWorkers.GetValueOrDefault(buildingId) == 0)
            {
                continue; // teachers aren't producers; an unmanned building makes nothing to be short for
            }
            foreach (ProductionEntry entry in building.Productions)
            {
                if (entry.Inputs.Count == 0)
                {
                    continue; // no input good to be short of (an unattended bell/cross line)
                }
                foreach (GoodsOutput output in entry.Outputs)
                {
                    foreach (GoodsOutput input in entry.Inputs)
                    {
                        // Net-negative colony-wide production of the input good ⇒ the building is starved (below max).
                        string inputStored = Ruleset.StorageIdOf(input.GoodsId);
                        if (flows.TryGetValue(inputStored, out ColonyGoodFlow flow) && flow.Net < 0
                            && seen.Add((output.GoodsId, input.GoodsId)))
                        {
                            result.Add(new ColonyProductionWarning(output.GoodsId, input.GoodsId));
                        }
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// One tile-improvement suggestion for a colony (a row of FreeCol's <c>ReportRequirementsPanel</c> tile warnings,
    /// from <c>Colony.getTileImprovementSuggestions</c>): the worked colony tile that would benefit, and — when
    /// <see cref="ImprovementId"/> is set — the improvement (plow/road) that would raise its worked good's yield;
    /// a <c>null</c> <see cref="ImprovementId"/> marks an <b>unexplored</b> worked tile to send a scout to. A pure
    /// value projection for the Requirements report.
    /// </summary>
    /// <param name="Tile">The worked colony tile the suggestion is about.</param>
    /// <param name="ImprovementId">The improvement ruleset id (<c>model.improvement.plow</c> / <c>road</c>) that would help, or <c>null</c> for an "explore this tile" suggestion.</param>
    public readonly record struct TileImprovementSuggestion(Position Tile, string? ImprovementId);

    /// <summary>
    /// The <b>tile-improvement suggestions</b> for a colony (a read-only oracle, ADR-006; mirrors FreeCol
    /// <c>Colony.getTileImprovementSuggestions</c> restricted to the tiles the colony actually works). For each of the
    /// colony's <b>worked</b> tiles: if the tile is <b>unexplored</b> by the human it yields an explore suggestion
    /// (<see cref="TileImprovementSuggestion.ImprovementId"/> <c>null</c>); otherwise, for each non-natural improvement
    /// the human can build (plow / road) that the tile does not already carry, applies to the tile's terrain, and would
    /// <b>raise the worked good's yield</b> (<see cref="World.Improvements.ImprovementProduction.YieldDelta(World.Improvements.TileImprovementType, string)"/> &gt; 0 —
    /// FreeCol's <c>ct.improvedBy(ti) &gt; 0</c>), it yields a plow/road suggestion. Tiles are visited in row-major
    /// order; a river (a natural improvement) is never suggested. Reuses the pure improvement-yield rule, so the panel
    /// stays rules-free. Pure and RNG-free; never mutates, never persisted.
    /// </summary>
    /// <param name="colony">The colony whose worked tiles to check.</param>
    /// <returns>The tile-improvement / explore suggestions (empty when every worked tile is explored and already optimal).</returns>
    public IReadOnlyList<TileImprovementSuggestion> ColonyTileImprovementSuggestions(Colony colony)
    {
        var result = new List<TileImprovementSuggestion>();
        foreach ((Position tile, string good) in colony.TileWorkers.OrderBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X))
        {
            if (!IsExplored(tile))
            {
                result.Add(new TileImprovementSuggestion(tile, null)); // send a scout — the tile is still fogged
                continue;
            }
            TerrainType terrain = Map.TerrainAt(tile);
            foreach (World.Improvements.TileImprovementType imp in Ruleset.ImprovementTypes)
            {
                if (imp.Id == World.Improvements.TileImprovementType.RiverId // natural — not player-buildable
                    || Map.HasImprovement(tile, imp.Id)                      // already there
                    || !imp.AppliesTo(terrain))                             // illegal on this terrain
                {
                    continue;
                }
                if (World.Improvements.ImprovementProduction.YieldDelta(imp, good) > 0) // FreeCol improvedBy > 0
                {
                    result.Add(new TileImprovementSuggestion(tile, imp.Id));
                }
            }
        }
        return result;
    }
}
