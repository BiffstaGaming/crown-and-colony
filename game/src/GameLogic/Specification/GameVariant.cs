namespace CrownAndColony.GameLogic.Specification;

/// <summary>
/// A selectable game variant — a self-contained historical setting (Colonial
/// America is the only one today; Australia and others come later). A variant is
/// identified by a stable <see cref="Id"/> and knows how to load <em>its</em>
/// ruleset, which defines that world's nations, Founding Fathers (and their
/// perks), units, goods and terrain.
///
/// <para><b>The transposability contract (ADR-018):</b> selecting a variant is the
/// <em>only</em> thing that changes which data the engine plays by. The rules code
/// (`Game`, the turn loop, combat, …) is variant-agnostic — it reads whatever the
/// loaded <see cref="Ruleset"/> contains. Adding a new variant is therefore a data
/// task (author a spec, register a variant here), not a code change.</para>
/// </summary>
public sealed class GameVariant
{
    private readonly string _specResource;

    internal GameVariant(string id, string displayName, string description, string specResource)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        _specResource = specResource;
    }

    /// <summary>Stable id, persisted in saves so a game reloads under the right ruleset (e.g. <c>classic</c>).</summary>
    public string Id { get; }

    /// <summary>Player-facing name (e.g. "Colonial America (Classic)").</summary>
    public string DisplayName { get; }

    /// <summary>One-line description for the variant-select screen.</summary>
    public string Description { get; }

    /// <summary>Loads this variant's ruleset by parsing its embedded specification, applying a difficulty level.</summary>
    /// <param name="difficultyLevelId">The difficulty level to apply (default <c>model.difficulty.medium</c> → the historical balance).</param>
    public Ruleset LoadRuleset(string difficultyLevelId = DifficultyLevels.DefaultId) =>
        Ruleset.LoadEmbedded(_specResource, difficultyLevelId);
}

/// <summary>
/// The registry of game variants the build ships with. New variants (the Australia
/// scenario, etc.) are registered here and become selectable everywhere — no other
/// code changes (ADR-018).
/// </summary>
public static class GameVariants
{
    /// <summary>Resource name of the embedded classic FreeCol specification.</summary>
    internal const string ClassicSpecResource =
        "CrownAndColony.GameLogic.Specification.classic.specification.xml";

    /// <summary>The faithful 1492 New World: the four European powers, the historic Founding Fathers, and the indigenous nations.</summary>
    public static readonly GameVariant ClassicAmerica = new(
        id: "classic",
        displayName: "Colonial America (Classic)",
        description: "The 1492 New World — English, French, Spanish and Dutch colonists, the historic "
            + "Founding Fathers, and the indigenous nations. The faithful Colonization ruleset.",
        specResource: ClassicSpecResource);

    /// <summary>Every shipped variant, in menu order.</summary>
    public static IReadOnlyList<GameVariant> All { get; } = [ClassicAmerica];

    /// <summary>The variant used when none is chosen (new game / legacy saves).</summary>
    public static GameVariant Default => ClassicAmerica;

    /// <summary>Looks up a variant by its stable id.</summary>
    /// <exception cref="KeyNotFoundException">No shipped variant has that id (e.g. a save from a build that had it).</exception>
    public static GameVariant ById(string id) =>
        All.FirstOrDefault(v => v.Id == id)
            ?? throw new KeyNotFoundException(
                $"Unknown game variant '{id}'. Installed variants: {string.Join(", ", All.Select(v => v.Id))}.");

    /// <summary>Resolves a (possibly null, from a legacy save) variant id, falling back to <see cref="Default"/>.</summary>
    public static GameVariant Resolve(string? id) => id is null ? Default : ById(id);
}
