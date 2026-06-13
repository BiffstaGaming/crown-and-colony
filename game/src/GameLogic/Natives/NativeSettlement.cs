using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Natives;

/// <summary>
/// How alarmed a native settlement is toward the player (FreeCol <c>Tension.Level</c>):
/// its hostility band, derived from the numeric alarm. Higher bands gate interaction —
/// an angry settlement won't teach its skill, a hateful one is dangerous to approach.
/// </summary>
public enum AlarmLevel
{
    /// <summary>At peace (alarm 0–100).</summary>
    Happy,

    /// <summary>Tolerant (101–600).</summary>
    Content,

    /// <summary>Wary (601–700).</summary>
    Displeased,

    /// <summary>Hostile (701–800).</summary>
    Angry,

    /// <summary>Ready to attack (801+).</summary>
    Hateful,
}

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

    /// <summary>Maximum alarm value (FreeCol <c>Tension</c> caps the Hateful band at 1000).</summary>
    public const int MaxAlarm = 1000;

    // FreeCol Tension.Level upper limits (Tension.java).
    internal const int AlarmHappyMax = 100;
    internal const int AlarmContentMax = 600;
    internal const int AlarmDispleasedMax = 700;
    internal const int AlarmAngryMax = 800;

    /// <summary>
    /// Alarm toward the player (0–<see cref="MaxAlarm"/>); starts peaceful at 0, raised
    /// by hostile acts (combat/land-taking, later slices) and cools each turn.
    /// </summary>
    public int Alarm { get; internal set; }

    /// <summary>The hostility band (FreeCol <c>Tension.Level</c>) derived from <see cref="Alarm"/>.</summary>
    public AlarmLevel AlarmLevel =>
        Alarm <= AlarmHappyMax ? AlarmLevel.Happy
        : Alarm <= AlarmContentMax ? AlarmLevel.Content
        : Alarm <= AlarmDispleasedMax ? AlarmLevel.Displeased
        : Alarm <= AlarmAngryMax ? AlarmLevel.Angry
        : AlarmLevel.Hateful;

    /// <summary>True once a colonist has spoken with the chief (the first-contact gift is given once).</summary>
    public bool HasBeenVisited { get; internal set; }

    /// <summary>True once this settlement's <see cref="LearnableSkill"/> has been taught (capitals never consume theirs).</summary>
    public bool SkillConsumed { get; internal set; }
}
