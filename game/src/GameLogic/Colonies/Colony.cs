using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Colonies;

/// <summary>
/// A colony on the map. Phase 2b skeleton: identity, location, and population —
/// the working economy (tile assignments, buildings, production) is Phase 3.
/// </summary>
public sealed class Colony
{
    /// <summary>Creates a colony.</summary>
    public Colony(int id, string name, Position position, int population)
    {
        Id = id;
        Name = name;
        Position = position;
        Population = population;
    }

    /// <summary>Stable per-game identifier.</summary>
    public int Id { get; }

    /// <summary>Display name (e.g. "Jamestown").</summary>
    public string Name { get; }

    /// <summary>Map tile the colony occupies.</summary>
    public Position Position { get; }

    /// <summary>Number of colonists living here (founding unit becomes the first).</summary>
    public int Population { get; internal set; }
}
