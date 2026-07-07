using System.IO;
using System.Reflection;
using CrownAndColony.GameLogic.Specification;

namespace CrownAndColony.GameLogic.World;

/// <summary>Which map a new game is built on: a procedurally generated New World, or a fixed scenario map.</summary>
public enum MapSource
{
    /// <summary>A procedurally generated random New World — the default (see <see cref="MapGenerator"/>).</summary>
    Random,

    /// <summary>FreeCol's hand-made map of the Americas (a fixed terrain grid, 40×180).</summary>
    America,

    /// <summary>The authored Australia continent (a fixed terrain grid, 30×80) — the Australian Federation variant (P8).</summary>
    Australia,
}

/// <summary>
/// Loads a fixed scenario map from an embedded resource via the general <see cref="MapImporter"/> — FreeCol's hand-made
/// America map, extracted from <c>data/maps/M_America_Mazim.fsm</c> (GPL v2). The shipped <c>america.txt</c> is
/// <b>terrain-only</b> (it declares no overlay sections), so importing it yields a terrain-only <see cref="GameMap"/>
/// with no settlements — rivers, bonus resources, native settlements and the player's start are placed on top by the
/// usual game-start generators (so the fixed map plays with our gameplay layers — the real America landmass, our
/// systems). A scenario file that <i>does</i> declare overlays imports them directly (see <see cref="MapImporter"/>).
/// Engine-free (ADR-006); loading is deterministic (the embedded bytes never change).
/// </summary>
public static class FixedMap
{
    /// <summary>Manifest resource name of the embedded America terrain grid (see <c>GameLogic.csproj</c>).</summary>
    private const string AmericaResource = "CrownAndColony.GameLogic.Maps.america.txt";

    /// <summary>Manifest resource name of the embedded Australia terrain grid — 30×80, converted from the FreeCol community map pack (P8; see <c>GameLogic.csproj</c>).</summary>
    private const string AustraliaResource = "CrownAndColony.GameLogic.Maps.australia.txt";

    /// <summary>Manifest resource name of the embedded example overlay map (exercises every importer overlay; see <c>GameLogic.csproj</c>).</summary>
    private const string ExampleOverlaysResource = "CrownAndColony.GameLogic.Maps.example-overlays.txt";

    /// <summary>The terrain-only <see cref="GameMap"/> for a fixed map source (or null for <see cref="MapSource.Random"/>, which the caller generates instead).</summary>
    /// <param name="source">The chosen map source.</param>
    /// <param name="ruleset">The ruleset whose <see cref="TerrainType"/>s the tile ids resolve against.</param>
    public static GameMap? TryLoad(MapSource source, Ruleset ruleset) =>
        TryImport(source, ruleset)?.Map;

    /// <summary>
    /// The full <see cref="MapImportResult"/> (terrain + any declared overlays and native settlements) for a fixed map
    /// source, or null for <see cref="MapSource.Random"/> (which the caller generates instead). The shipped
    /// <c>america.txt</c> is terrain-only, so its result carries an empty <see cref="MapImportResult.Settlements"/> list
    /// and the America new game stays byte-identical; a scenario file declaring a <c>[settlements]</c> section returns
    /// those settlements here, so <c>Game.New</c> can install them in place of the procedural native generator.
    /// </summary>
    /// <param name="source">The chosen map source.</param>
    /// <param name="ruleset">The ruleset whose ids the definition resolves against.</param>
    public static MapImportResult? TryImport(MapSource source, Ruleset ruleset) =>
        source switch
        {
            MapSource.America => ImportAmerica(ruleset),
            MapSource.Australia => ImportAustralia(ruleset),
            _ => null,
        };

    /// <summary>Builds the America <see cref="GameMap"/> (FreeCol's M_America, 40×180) from the embedded terrain grid.</summary>
    /// <param name="ruleset">The ruleset whose <see cref="TerrainType"/>s the tile ids resolve against.</param>
    public static GameMap LoadAmerica(Ruleset ruleset) => ImportAmerica(ruleset).Map;

    /// <summary>
    /// Imports the full America scenario (terrain + any overlays + native settlements) via <see cref="MapImporter"/>.
    /// The shipped grid is terrain-only, so the result's settlement list is empty and only the terrain layer is set —
    /// but this is the seam through which a future overlaid America (or another scenario) would carry its bonuses,
    /// rumours and settlements into the game.
    /// </summary>
    /// <param name="ruleset">The ruleset whose ids the definition resolves against.</param>
    public static MapImportResult ImportAmerica(Ruleset ruleset) => Import(AmericaResource, ruleset);

    /// <summary>Builds the Australia <see cref="GameMap"/> (30×80) from the embedded terrain grid — the Australian Federation variant (P8).</summary>
    /// <param name="ruleset">The ruleset whose <see cref="TerrainType"/>s the tile ids resolve against.</param>
    public static GameMap LoadAustralia(Ruleset ruleset) => ImportAustralia(ruleset).Map;

    /// <summary>
    /// Imports the Australia scenario (terrain grid, converted from the FreeCol community map pack by Euzimar — the same
    /// standard FreeCol terrain ids the classic ruleset already defines, so it resolves 1:1). Terrain-only like the
    /// America grid, so rivers, resources, native settlements and the player's start are layered on at game start.
    /// </summary>
    /// <param name="ruleset">The ruleset whose ids the definition resolves against.</param>
    public static MapImportResult ImportAustralia(Ruleset ruleset) => Import(AustraliaResource, ruleset);

    /// <summary>
    /// Imports the embedded example overlay map (<c>example-overlays.txt</c>) — a tiny scenario that declares every
    /// importer overlay (resources + quantity, river/road improvements, a rumour, native settlements). Demonstrates and
    /// tests the importer end-to-end through the same embedded-resource path the America map uses.
    /// </summary>
    /// <param name="ruleset">The ruleset whose ids the definition resolves against.</param>
    internal static MapImportResult ImportExampleOverlays(Ruleset ruleset) => Import(ExampleOverlaysResource, ruleset);

    /// <summary>Imports an embedded map-definition resource into a <see cref="MapImportResult"/> via <see cref="MapImporter"/>.</summary>
    /// <param name="resourceName">The manifest resource name of the embedded definition.</param>
    /// <param name="ruleset">The ruleset whose ids the definition resolves against.</param>
    private static MapImportResult Import(string resourceName, Ruleset ruleset)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded map '{resourceName}' is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return MapImporter.Import(reader, ruleset, resourceName);
    }
}
