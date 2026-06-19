using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// A military force as a list of unit blocks (FreeCol <c>Monarch.Force</c>): the land units that must be carried and
/// the naval units that carry them. Used for the Royal Expeditionary Force (grown by ADD_TO_REF, realised into real
/// units at the Declaration of Independence) and the foreign-intervention force. <see cref="SpaceRequired"/> /
/// <see cref="NavalCapacity"/> drive the "keep the navy at +10% of the land it must ferry" growth rule.
/// </summary>
public sealed class Force
{
    private readonly List<ForceEntry> _land = [];
    private readonly List<ForceEntry> _naval = [];

    /// <summary>Creates an empty force.</summary>
    public Force() { }

    /// <summary>Creates a force from existing entry lists (save restore).</summary>
    public Force(IEnumerable<ForceEntry> land, IEnumerable<ForceEntry> naval)
    {
        _land.AddRange(land);
        _naval.AddRange(naval);
    }

    /// <summary>The land-unit blocks (units that need transporting).</summary>
    public IReadOnlyList<ForceEntry> LandUnits => _land;

    /// <summary>The naval-unit blocks (the transports/escorts).</summary>
    public IReadOnlyList<ForceEntry> NavalUnits => _naval;

    /// <summary>Total land units across all blocks.</summary>
    public int LandUnitCount => _land.Sum(e => e.Count);

    /// <summary>Total naval units across all blocks.</summary>
    public int NavalUnitCount => _naval.Sum(e => e.Count);

    /// <summary>Adds land units, merging into a matching (type, role) block.</summary>
    public void AddLand(string unitTypeId, string? roleId, int count) => Add(_land, unitTypeId, roleId, count);

    /// <summary>Adds naval units, merging into a matching (type, role) block.</summary>
    public void AddNaval(string unitTypeId, string? roleId, int count) => Add(_naval, unitTypeId, roleId, count);

    private static void Add(List<ForceEntry> list, string unitTypeId, string? roleId, int count)
    {
        if (count <= 0)
        {
            return;
        }
        int i = list.FindIndex(e => e.UnitTypeId == unitTypeId && e.RoleId == roleId);
        if (i >= 0)
        {
            list[i] = list[i] with { Count = list[i].Count + count };
        }
        else
        {
            list.Add(new ForceEntry(unitTypeId, roleId, count));
        }
    }

    /// <summary>Cargo slots the land units occupy when carried (FreeCol <c>Force.spaceRequired</c>).</summary>
    public int SpaceRequired(Ruleset ruleset) => _land.Sum(e => ruleset.Unit(e.UnitTypeId).SpaceTaken * e.Count);

    /// <summary>Cargo capacity of the naval units (FreeCol <c>Force.capacity</c>).</summary>
    public int NavalCapacity(Ruleset ruleset) => _naval.Sum(e => ruleset.Unit(e.UnitTypeId).Space * e.Count);
}
