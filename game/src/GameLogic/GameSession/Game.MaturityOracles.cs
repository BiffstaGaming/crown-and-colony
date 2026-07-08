using CrownAndColony.GameLogic.Colonies;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// The settlement-maturity tiers a colony climbs through (Australian Federation, 4a.2), per
/// <c>docs/australian_federation_mode_md/06_Colony_Progression_Prerequisites.md</c>. Ascending: an <see cref="Outpost"/>
/// is a fragile new claim; a <see cref="ColonialCapital"/> can represent its region in the Federation system. The tier
/// is a <b>pure derived read</b> of a colony's population and buildings (like <c>Colony.SonsOfLiberty</c>) — no new
/// state, no RNG — consumed by the later Federation progression loop; this file wires <b>no victory / era logic</b>.
/// </summary>
public enum SettlementMaturity
{
    /// <summary>A fledgling claim (population 1–2): vulnerable to starvation, minimal civic voice.</summary>
    Outpost,

    /// <summary>A settled township (population 3+ with a food surplus): can host local events and build up.</summary>
    Township,

    /// <summary>An established colonial town (population 6+ with a market/port and a specialist building): generates
    /// meaningful civic voice and is eligible for Federation-support tracking.</summary>
    ColonialTown,

    /// <summary>A colonial capital (population 10+ with the advanced civic buildings): can represent a colony region in
    /// the Federation system and host Convention events.</summary>
    ColonialCapital,
}

public sealed partial class Game
{
    // The classic building ids the maturity oracle reads. A colony always has the free base Town Hall, so the doc's
    // "has a Town Hall" gate never distinguishes tiers here; instead the higher tiers key on the presence of the
    // meaningful UPGRADE/specialist buildings a colony must deliberately construct. Ids absent from a ruleset simply
    // never match (a variant with a Court House / League Hall extends this by data), so the oracle degrades gracefully.
    private const string DocksBuildingId = "model.building.docks";               // the classic "market/wharf" analogue (a port link)
    private const string PrintingPressBuildingId = "model.building.printingPress"; // civic press (newspaper's predecessor)
    private const string NewspaperBuildingId = "model.building.newspaper";        // advanced civic press
    private const string SchoolhouseBuildingId = "model.building.schoolhouse";     // the "school" gate

    /// <summary>
    /// The <see cref="SettlementMaturity"/> tier of <paramref name="colony"/> — a <b>pure read</b> of its population and
    /// buildings (4a.2), faithful to the maturity table in the Federation progression doc:
    /// <list type="bullet">
    /// <item><b>Colonial Capital</b> — population ≥ 10 with the advanced civic buildings (a newspaper <em>or</em> printing
    /// press, and a schoolhouse) and a port link (docks);</item>
    /// <item><b>Colonial Town</b> — population ≥ 6 with a market/port link (docks) and at least one specialist upgrade building;</item>
    /// <item><b>Township</b> — population ≥ 3;</item>
    /// <item><b>Outpost</b> — otherwise (population 1–2, or an empty colony).</item>
    /// </list>
    /// No RNG, no state change, no new persisted field — derived on read exactly like <see cref="Colony.SonsOfLiberty"/>.
    /// A colony whose ruleset lacks a gate building simply cannot reach the tier that needs it (graceful degradation).
    /// </summary>
    /// <param name="colony">The colony to classify.</param>
    /// <returns>Its current maturity tier.</returns>
    public SettlementMaturity SettlementMaturityOf(Colony colony)
    {
        int population = colony.Population;
        bool hasPort = colony.Buildings.Contains(DocksBuildingId);
        bool hasCivicPress = colony.Buildings.Contains(NewspaperBuildingId) || colony.Buildings.Contains(PrintingPressBuildingId);
        bool hasSchool = colony.Buildings.Contains(SchoolhouseBuildingId);

        if (population >= 10 && hasPort && hasCivicPress && hasSchool)
        {
            return SettlementMaturity.ColonialCapital;
        }
        if (population >= 6 && hasPort && HasSpecialistBuilding(colony))
        {
            return SettlementMaturity.ColonialTown;
        }
        if (population >= 3)
        {
            return SettlementMaturity.Township;
        }
        return SettlementMaturity.Outpost;
    }

    /// <summary>
    /// Whether <paramref name="colony"/> has at least one <b>specialist building</b> — any building beyond the free base
    /// set every colony starts with (a deliberately-constructed upgrade or production building). Reuses the same
    /// base-building test the founding path uses (no build cost + not an upgrade), so the "specialist" set is exactly the
    /// buildings the colony had to build. Pure read.
    /// </summary>
    private bool HasSpecialistBuilding(Colony colony) =>
        colony.Buildings.Any(id => Ruleset.BuildingTypes
            .Where(b => b.Id == id)
            .Any(b => b.BuildCost.Count > 0 || b.UpgradesFrom is not null));

    /// <summary>
    /// Whether the map region with the given id is <b>politically active</b> (4a.2), per the Federation progression doc's
    /// colony-region activation rule: a region is active when it holds a <see cref="SettlementMaturity.ColonialCapital"/>,
    /// or at least two <see cref="SettlementMaturity.ColonialTown"/>s (a Colonial Capital counts as a town for this test).
    /// A <b>pure derived read</b> over the colonies whose tile lies in that region — no new state, no RNG. The
    /// "Historical-Figure/event-activated" and "scenario-marked" activation paths in the doc are future variant content
    /// (they would be OR-ed in by the Federation loop) and are deliberately not wired here.
    /// </summary>
    /// <param name="regionId">The map region id (see <c>GameMap.RegionIdAt</c> / <c>GameMap.Regions</c>).</param>
    /// <returns>True iff the region meets the settlement-maturity activation threshold.</returns>
    public bool IsColonyRegionActive(int regionId)
    {
        int capitals = 0;
        int towns = 0; // Colonial Towns + Colonial Capitals (a capital is a fortiori a town for the "two towns" test)
        foreach (Colony colony in _colonies)
        {
            if (Map.RegionIdAt(colony.Position) != regionId)
            {
                continue;
            }
            SettlementMaturity maturity = SettlementMaturityOf(colony);
            if (maturity == SettlementMaturity.ColonialCapital)
            {
                capitals++;
                towns++;
            }
            else if (maturity == SettlementMaturity.ColonialTown)
            {
                towns++;
            }
        }
        return capitals >= 1 || towns >= 2;
    }
}
