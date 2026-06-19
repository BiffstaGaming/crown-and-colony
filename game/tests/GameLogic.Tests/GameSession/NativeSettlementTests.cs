using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Native settlement placement and persistence on a generated game. Pinned
/// invariants (every nation present, on land, spaced apart, clear of the player)
/// rather than exact tiles — exact placement is covered by the determinism test.
/// </summary>
public class NativeSettlementTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;

    private static (int, string, string, bool, Position, int, string?) Key(NativeSettlement s) =>
        (s.Id, s.NationTypeId, s.SettlementTypeId, s.IsCapital, s.Position, s.Size, s.LearnableSkill);

    [Fact]
    public void NewGame_PlacesSettlementsForEveryNativeNation()
    {
        Game game = Game.New(Classic, Seed);

        Assert.NotEmpty(game.NativeSettlements);

        // Every native nation founds at least one settlement, with exactly one capital each.
        foreach (NativeNationType nation in Classic.NativeNationTypes)
        {
            var theirs = game.NativeSettlements.Where(s => s.NationTypeId == nation.Id).ToList();
            Assert.NotEmpty(theirs);
            Assert.Equal(1, theirs.Count(s => s.IsCapital));
        }
    }

    [Fact]
    public void Settlements_SitOnSettleableLand()
    {
        Game game = Game.New(Classic, Seed);

        Assert.All(game.NativeSettlements, s =>
        {
            TerrainType terrain = game.Map.TerrainAt(s.Position);
            Assert.False(terrain.IsWater, $"{s.SettlementTypeId} on water at {s.Position}");
            Assert.True(terrain.CanSettle, $"{s.SettlementTypeId} on unsettleable {terrain.Id}");
        });
    }

    [Fact]
    public void Settlements_AreSpacedApartAndClearOfThePlayer()
    {
        Game game = Game.New(Classic, Seed);
        var settlements = game.NativeSettlements;

        // The lone starting colonist's tile — nothing should sit on or beside it.
        Position start = Assert.Single(game.PlayerUnits).Position;

        for (int i = 0; i < settlements.Count; i++)
        {
            Position a = settlements[i].Position;
            Assert.True(Chebyshev(a, start) >= 3, $"settlement at {a} too close to player start {start}");
            Assert.Null(game.ColonyAt(a));
            for (int j = i + 1; j < settlements.Count; j++)
            {
                Assert.True(
                    Chebyshev(a, settlements[j].Position) >= 3,
                    $"settlements at {a} and {settlements[j].Position} are too close");
            }
        }
    }

    [Fact]
    public void Settlements_HaveSizeWithinTheirTypeRange()
    {
        Game game = Game.New(Classic, Seed);

        Assert.All(game.NativeSettlements, s =>
        {
            SettlementType type = Classic.Settlement(s.SettlementTypeId);
            Assert.InRange(s.Size, type.MinimumSize, type.MaximumSize);
        });
    }

    [Fact]
    public void Placement_IsDeterministicForASeed()
    {
        Game a = Game.New(Classic, Seed);
        Game b = Game.New(Classic, Seed);

        Assert.Equal(
            a.NativeSettlements.Select(Key).ToList(),
            b.NativeSettlements.Select(Key).ToList());
    }

    [Fact]
    public void NativeSettlementAt_FindsTheSettlementOnATile()
    {
        Game game = Game.New(Classic, Seed);
        NativeSettlement first = game.NativeSettlements[0];

        Assert.Same(first, game.NativeSettlementAt(first.Position));
        // A tile with no settlement returns null (the player's start has none).
        Assert.Null(game.NativeSettlementAt(game.Units[0].Position));
    }

    [Fact]
    public void Settlements_SurviveASaveRoundTrip()
    {
        Game game = Game.New(Classic, Seed);
        var before = game.NativeSettlements.Select(Key).ToList();

        string json = SaveGame.From(game).ToJson();
        Game restored = SaveGame.FromJson(json).Restore(Classic);

        Assert.Equal(before, restored.NativeSettlements.Select(Key).ToList());
    }

    [Fact]
    public void SaveVersion_IsCurrent()
    {
        Assert.Equal(37, SaveGame.CurrentVersion);
        Assert.Equal(37, SaveGame.From(Game.New(Classic, Seed)).Version);
    }

    private static int Chebyshev(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
