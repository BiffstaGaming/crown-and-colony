using System.IO;
using System.Linq;
using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using CrownAndColony.GameLogic.World.Improvements;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Clear-forest's two FreeCol effects beyond delivering lumber:
/// <list type="bullet">
/// <item>a chance (classic 5%) to expose a hidden bonus resource on the cleared tile, rolled through the injected
/// seeded RNG — picking weighted-random from the cleared terrain type's resource table (task 86d3fpx99); and</item>
/// <item>a lumber mill in the receiving colony triples the one-off lumber delivered (task 86d3fpy8x).</item>
/// </list>
/// Cross-checked against FreeCol <c>ServerUnit.csImproveTile</c> (the <c>expose-resource-percent</c> roll +
/// <c>Settlement.apply(TILE_TYPE_CHANGE_PRODUCTION, lumber)</c> ×3 modifier).
/// </summary>
public class ClearForestRevealTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Clear = TileImprovementType.ClearForestId;
    private const string Pioneer = "model.role.pioneer";
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Lumber = "model.goods.lumber";
    private const string LumberMill = "model.building.lumberMill";

    /// <summary>
    /// A 3×3 map of mixed forest (clears to plains; plains hosts only the prime-grain resource) with a pioneer on the
    /// centre, an optional owning colony at (0,0), a chosen RNG seed, and an <paramref name="exposePercent"/> override
    /// of clear-forest's expose-resource chance (null = the classic 5%).
    /// </summary>
    private static Game ForestPioneer(ulong seed, bool withColony = false, int? exposePercent = null,
        IReadOnlyList<string>? buildings = null)
    {
        Ruleset ruleset = exposePercent is { } p ? ClassicWithExposePercent(p) : Classic;
        string[] terrain = [.. Enumerable.Repeat("model.tile.mixedForest", 9)];
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = seed,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = terrain,
            Units = [new SavedUnit(1, FreeColonist, 1, 1, 3, Role: Pioneer, RoleCount: 1)],
            Explored = [],
            Colonies = withColony ? [new SavedColony(2, "Millville", 0, 0, 1, Buildings: buildings)] : null,
        };
        return save.Restore(ruleset);
    }

    private static Unit ThePioneer(Game game) => game.Units.First(u => u.Id == 1);
    private static readonly Position Centre = new(1, 1);

    // Clears the forest on the pioneer's tile (mixedForest → plains: 4 + 2 = 6 work turns).
    private static void ClearForest(Game game)
    {
        Unit u = ThePioneer(game);
        game.BuildImprovement(u, Clear);
        for (int i = 0; i < 6; i++)
        {
            game.ProcessImprovements(game.HumanPlayer);
        }
    }

    private static Ruleset ClassicWithExposePercent(int percent)
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        XElement clear = doc.Descendants("tile-improvement-type")
            .Single(t => (string?)t.Attribute("id") == Clear);
        clear.SetAttributeValue("expose-resource-percent", percent);
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

    // ───────────────────────── resource exposure ─────────────────────────

    [Fact]
    public void ClearForest_WithAlwaysExpose_RevealsAResourceFromTheClearedTerrain()
    {
        // 100% expose → the cleared plains tile gains its only hostable resource (prime grain).
        Game game = ForestPioneer(seed: 1, exposePercent: 100);
        ClearForest(game);

        Assert.Equal("model.tile.plains", game.Map.TerrainAt(Centre).Id);
        Assert.Equal("model.resource.grain", game.Map.ResourceAt(Centre)); // plains' resource table
    }

    [Fact]
    public void ClearForest_WithNeverExpose_RevealsNothing()
    {
        // 0% expose → the cleared tile carries no resource, whatever the seed.
        Game game = ForestPioneer(seed: 1, exposePercent: 0);
        ClearForest(game);

        Assert.Null(game.Map.ResourceAt(Centre));
    }

    [Fact]
    public void ExposeRoll_IsDeterministicForAGivenSeed()
    {
        // Same seed → identical outcome (the roll is a pure function of the seeded stream — ADR-009).
        Game a = ForestPioneer(seed: 12345);
        Game b = ForestPioneer(seed: 12345);
        ClearForest(a);
        ClearForest(b);

        Assert.Equal(a.Map.ResourceAt(Centre), b.Map.ResourceAt(Centre));
    }

    [Fact]
    public void ClassicExposeRate_IsRoughlyFivePercent()
    {
        // Over many seeds the classic 5% gate exposes a resource on roughly 5% of clears — pins the real probability
        // (and proves the RNG path actually runs on the classic ruleset, not just the overrides above).
        int revealed = 0;
        const int trials = 600;
        for (ulong seed = 1; seed <= trials; seed++)
        {
            Game game = ForestPioneer(seed);
            ClearForest(game);
            if (game.Map.ResourceAt(Centre) is not null)
            {
                revealed++;
            }
        }
        double rate = revealed / (double)trials;
        Assert.InRange(rate, 0.02, 0.09); // ≈ 5%, with sampling slack
    }

    // ───────────────────────── lumber mill ×3 ─────────────────────────

    [Fact]
    public void ClearForest_WithoutLumberMill_DeliversBaseLumber()
    {
        // A plain colony (carpenter's house only, no lumber mill) gets the base 20 lumber.
        Game game = ForestPioneer(seed: 1, withColony: true, exposePercent: 0);
        Colony colony = game.Colonies.First(c => c.Id == 2);
        int before = colony.StoreOf(Lumber);

        ClearForest(game);

        Assert.Equal(before + 20, colony.StoreOf(Lumber));
    }

    [Fact]
    public void ClearForest_WithLumberMill_TriplesTheLumber()
    {
        // A colony with a lumber mill (model.modifier.tileTypeChangeProduction ×3 on lumber) gets 60.
        Game game = ForestPioneer(seed: 1, withColony: true, exposePercent: 0, buildings: [LumberMill]);
        Colony colony = game.Colonies.First(c => c.Id == 2);
        int before = colony.StoreOf(Lumber);

        ClearForest(game);

        Assert.Equal(before + 60, colony.StoreOf(Lumber)); // 20 × 3
    }

    [Fact]
    public void LumberMill_CarriesTheTileTypeChangeFactor()
    {
        // The data wiring: the lumber mill's ×3 lumber tile-type-change modifier is parsed; the base carpenter's house
        // (and an unrelated building) carry the ×1 identity.
        Assert.Equal(3.0, Classic.Building(LumberMill).LumberTileTypeChangeFactor);
        Assert.Equal(1.0, Classic.Building("model.building.carpenterHouse").LumberTileTypeChangeFactor);
    }
}
