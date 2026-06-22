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

        // The player's landing tile (the pioneer stands on the chosen start) — settlements keep clear of it.
        Position start = game.PlayerUnits.First(u => !u.Type.IsNaval && u.Type.CanFoundColony).Position;

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
    public void MilitaryStock_SurvivesASaveRoundTrip_V54()
    {
        // 86d3e49gq: the generator-seeded muskets/horses are serialized (save v54) and restored exactly.
        Game game = Game.New(Classic, Seed);
        Assert.Contains(game.NativeSettlements, s => s.StockOf("model.goods.muskets") > 0); // the seed put arms in

        string json = SaveGame.From(game).ToJson();
        Game restored = SaveGame.FromJson(json).Restore(Classic);

        foreach (NativeSettlement before in game.NativeSettlements)
        {
            NativeSettlement after = restored.NativeSettlements.Single(s => s.Id == before.Id);
            Assert.Equal(before.StockOf("model.goods.muskets"), after.StockOf("model.goods.muskets"));
            Assert.Equal(before.StockOf("model.goods.horses"), after.StockOf("model.goods.horses"));
        }
        // The whole game round-trips byte-identically with the stock present.
        Assert.Equal(json, SaveGame.From(restored).ToJson());
    }

    [Fact]
    public void AnEmptyStockSettlement_OmitsTheMilitaryStockField_ByteIdenticalToV53()
    {
        // The additive field is omit-when-empty (ADR-009): a settlement holding no stock emits NO MilitaryStock field,
        // so an empty-stock save is byte-identical to v53 but for the version. Empty every settlement's seeded stock.
        Game game = Game.New(Classic, Seed);
        foreach (NativeSettlement s in game.NativeSettlements)
        {
            s.AddStock("model.goods.muskets", -s.StockOf("model.goods.muskets"));
            s.AddStock("model.goods.horses", -s.StockOf("model.goods.horses"));
        }

        string json = SaveGame.From(game).ToJson();

        Assert.DoesNotContain("MilitaryStock", json);                 // omitted entirely when empty
        Assert.Equal(json, SaveGame.From(SaveGame.FromJson(json).Restore(Classic)).ToJson()); // round-trips
    }

    [Fact]
    public void SaveVersion_IsCurrent()
    {
        Assert.Equal(54, SaveGame.CurrentVersion);
        Assert.Equal(54, SaveGame.From(Game.New(Classic, Seed)).Version);
    }

    private static int Chebyshev(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
