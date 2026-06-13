using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Natives;

/// <summary>
/// A native settlement on the map (FreeCol <c>IndianSettlement</c>): a camp, village
/// or city belonging to one of the indigenous nations. Each sits on a single tile,
/// has a size (its resident population), and — until a visitor learns it — a skill it
/// can teach. Interaction (visiting, trade, tension, combat) arrives in later slices;
/// this slice models their placement and existence.
/// </summary>
public sealed class NativeSettlement
{
    /// <summary>Creates a native settlement. Used by the generator and on load.</summary>
    public NativeSettlement(
        int id, string nationTypeId, string settlementTypeId, bool isCapital,
        Position position, int size, string? learnableSkill)
    {
        Id = id;
        NationTypeId = nationTypeId;
        SettlementTypeId = settlementTypeId;
        IsCapital = isCapital;
        Position = position;
        Size = size;
        LearnableSkill = learnableSkill;
    }

    /// <summary>Stable per-game identifier.</summary>
    public int Id { get; }

    /// <summary>The owning native nation's type id (e.g. <c>model.nationType.apache</c>).</summary>
    public string NationTypeId { get; }

    /// <summary>The settlement template id (e.g. <c>model.settlement.camp</c> or its capital variant).</summary>
    public string SettlementTypeId { get; }

    /// <summary>Whether this is the nation's capital (bigger, better defended).</summary>
    public bool IsCapital { get; }

    /// <summary>The tile the settlement occupies.</summary>
    public Position Position { get; }

    /// <summary>Resident population (FreeCol settlement "units"); set at creation, grows in later slices.</summary>
    public int Size { get; internal set; }

    /// <summary>
    /// The expert unit type this settlement can teach a visiting colonist
    /// (e.g. <c>model.unit.expertFarmer</c>), or null if it teaches nothing.
    /// </summary>
    public string? LearnableSkill { get; }
}
