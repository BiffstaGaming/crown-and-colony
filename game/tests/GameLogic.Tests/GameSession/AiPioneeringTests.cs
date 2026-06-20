using System.Collections.Generic;
using System.Linq;
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
/// AI pioneering (<c>86d3c9vta</c>, FreeCol <c>PioneeringMission</c> / <c>TileImprovementPlan</c> / <c>pioneersNeeded</c>):
/// a foreign power fields a tooled pioneer that builds improvements on its colony footprints, and arms an idle
/// colonist into a pioneer from a tool-stocked colony. Every choice draws the power's OWN RNG stream, so the human's
/// stream 0 stays byte-identical (ADR-009; the soak suite proves the byte-stability).
/// </summary>
public class AiPioneeringTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Plow = TileImprovementType.PlowId;
    private const string Pioneer = "model.role.pioneer";
    private const string HardyPioneer = "model.unit.hardyPioneer";
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Tools = "model.goods.tools";

    /// <summary>Human (stream 0) + one foreign colonial power (id 1) — the roster the fixtures restore with.</summary>
    private static List<SavedPlayer> HumanPlusForeign() =>
    [
        new SavedPlayer(0, NationId: null, IsHuman: true, PlayerType: (int)PlayerType.Colonial),
        new SavedPlayer(1, "model.nation.dutch", IsHuman: false, PlayerType: (int)PlayerType.Colonial),
    ];

    /// <summary>
    /// A <paramref name="size"/>×<paramref name="size"/> all-plains map fully explored by the foreign power, with a
    /// foreign colony at the centre and the given foreign-owned units. Returns the game, the foreign colony and the
    /// power. The colony sits at <c>(size/2, size/2)</c> (so (1,1) on the default 3×3).
    /// </summary>
    private static (Game Game, Colony Colony, Player Power) ForeignFixture(
        IReadOnlyList<SavedUnit> units, IReadOnlyDictionary<string, int>? colonyStores = null, int size = 3)
    {
        int c = size / 2;
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = size,
            MapHeight = size,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", size * size)],
            Units = units,
            Explored = [.. Enumerable.Range(0, size * size)], // human-side fog is irrelevant here; the AI uses its own
            Players = HumanPlusForeign(),
            Colonies = [new SavedColony(1, "Nieuw Holland", c, c, 1, Stores: colonyStores)],
        };
        Game game = save.Restore(Classic);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        Colony colony = game.Colonies[0];
        colony.OwnerId = power.PlayerId;
        // Reveal the whole map to the power so its pioneer can see/work its colony footprint (the AI reads its own fog).
        foreach (Position p in game.Map.AllPositions())
        {
            power.ExploredSet.Add(p);
        }
        return (game, colony, power);
    }

    private static SavedUnit ForeignUnit(int id, string typeId, int x, int y, string? role = null, int? roleCount = null) =>
        new(id, typeId, x, y, 3, OwnerId: 1, Role: role, RoleCount: roleCount);

    private static Unit UnitOf(Game game, int id) => game.Units.First(u => u.Id == id);

    [Fact]
    public void ForeignPioneer_BuildsAnImprovementOnAColonyFootprintTile()
    {
        // A hardy pioneer standing on a plains footprint tile beside the foreign colony plows it (the top-priority
        // plan on farmable land), then — out of tools — reverts to a colonist, exactly like a human pioneer.
        var pioneerTile = new Position(0, 1); // a neighbour of the colony at (1,1)
        (Game game, _, _) = ForeignFixture([ForeignUnit(1, HardyPioneer, 0, 1, Pioneer, 1)]);

        game.EndTurn();
        Unit pioneer = UnitOf(game, 1);
        Assert.True(pioneer.IsImproving);             // it took up the build this turn
        Assert.Equal(Plow, pioneer.WorkImprovementId);

        // Run on until the plow lands (plains plow = 3+2 = 5 work; a hardy pioneer does 2/turn → a few turns).
        for (int i = 0; i < 6 && !game.Map.HasImprovement(pioneerTile, Plow); i++)
        {
            game.EndTurn();
        }
        Assert.True(game.Map.HasImprovement(pioneerTile, Plow)); // the improvement was built
        Assert.True(UnitOf(game, 1).HasDefaultRole);             // its one load of tools spent → back to a colonist
    }

    [Fact]
    public void ForeignPioneer_MarchesTowardAPlanWhenNotStandingOnOne()
    {
        // On a 5×5 map the colony sits at (2,2) with a 3×3 footprint (1,1)–(3,3). A pioneer at the far corner (0,0)
        // is outside it, so there is no plan underfoot — it steps toward the nearest footprint tile to go improve it,
        // rather than idling or wandering off to explore.
        (Game game, _, _) = ForeignFixture([ForeignUnit(1, HardyPioneer, 0, 0, Pioneer, 1)], size: 5);
        var start = new Position(0, 0);

        game.EndTurn();

        Unit pioneer = UnitOf(game, 1);
        Assert.NotEqual(start, pioneer.Position); // it moved
        Assert.True(Chebyshev(pioneer.Position, new Position(2, 2)) < Chebyshev(start, new Position(2, 2))); // closer to the footprint
        Assert.False(pioneer.IsImproving); // it marched this turn, not built (it wasn't on a footprint tile)
    }

    private static int Chebyshev(Position a, Position b) => System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    [Fact]
    public void ForeignPioneer_WithNoPlans_IsNotConsumedToFoundAColony()
    {
        // A tooled pioneer whose power has no colony (so no footprint, no plans) must NOT be spent founding a colony —
        // that would destroy its 20 tools. It explores instead, staying a pioneer (review finding, `86d3c9vta`).
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 5,
            MapHeight = 5,
            Terrain = [.. Enumerable.Repeat("model.tile.plains", 25)],
            Units = [ForeignUnit(1, HardyPioneer, 2, 2, Pioneer, 1)], // a lone pioneer, no colony anywhere
            Explored = [.. Enumerable.Range(0, 25)],
            Players = HumanPlusForeign(),
            Colonies = null,
        };
        Game game = save.Restore(Classic);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        foreach (Position p in game.Map.AllPositions())
        {
            power.ExploredSet.Add(p);
        }

        game.EndTurn();

        Assert.Contains(game.Units, u => u.Id == 1);              // the pioneer was NOT consumed to found
        Assert.Equal(Pioneer, UnitOf(game, 1).RoleId);           // still a tooled pioneer (tools not wasted)
        Assert.DoesNotContain(game.Colonies, c => c.OwnerId == power.PlayerId); // and it founded nothing
    }

    [Fact]
    public void ForeignPower_ArmsAnIdleColonistAsAPioneer_FromAToolStockedColony()
    {
        // A plain colonist standing in the foreign colony, which stocks a pioneer's tools, is armed as a pioneer
        // (the power wants one — there is plannable work and it owns none). The economy keeps the tools rather than
        // selling them all (the pioneer-tool reserve).
        (Game game, _, _) = ForeignFixture(
            [ForeignUnit(1, FreeColonist, 1, 1)], // a colonist standing on the colony tile, default role
            colonyStores: new Dictionary<string, int> { [Tools] = 20 });

        game.EndTurn();

        Unit colonist = UnitOf(game, 1);
        Assert.Equal(Pioneer, colonist.RoleId); // armed as a pioneer
        Assert.True(colonist.RoleCount >= 1);
    }

    [Fact]
    public void ForeignPower_DoesNotArmBeyondItsPioneerCap()
    {
        // The power already fields a pioneer (its cap with one colony is one); an idle tooled-colony colonist is NOT
        // armed into a second.
        (Game game, _, _) = ForeignFixture(
            [
                ForeignUnit(1, HardyPioneer, 0, 1, Pioneer, 1),  // an existing pioneer (counts against the cap)
                ForeignUnit(2, FreeColonist, 1, 1),              // an idle colonist in the colony
            ],
            colonyStores: new Dictionary<string, int> { [Tools] = 40 });

        game.EndTurn();

        Assert.True(UnitOf(game, 2).HasDefaultRole); // stayed a colonist — the power is at its one-pioneer cap
    }

    [Fact]
    public void AiPioneering_IsDeterministicPerSetup()
    {
        // The same fixture played the same way reaches the same improvement state — the AI draws only its own stream.
        static Game Play()
        {
            (Game g, _, _) = ForeignFixture([ForeignUnit(1, HardyPioneer, 0, 1, Pioneer, 1)]);
            for (int i = 0; i < 6; i++)
            {
                g.EndTurn();
            }
            return g;
        }

        Game a = Play();
        Game b = Play();
        Assert.Equal(
            a.Map.AllImprovements().Select(i => (i.Position, i.Improvement.Id)).OrderBy(t => (t.Position.Y, t.Position.X)),
            b.Map.AllImprovements().Select(i => (i.Position, i.Improvement.Id)).OrderBy(t => (t.Position.Y, t.Position.X)));
        Assert.Equal(a.Units.First(u => u.Id == 1).RoleId, b.Units.First(u => u.Id == 1).RoleId);
    }
}
