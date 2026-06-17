using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Treasure trains (<c>86d3c9ryj</c>, FreeCol <c>model.unit.treasureTrain</c> / <c>csDestroySettlement</c>): a
/// movable, capturable, non-combat land unit that carries plundered gold. Sacking a native settlement spawns one
/// (instead of crediting gold — see <c>CombatTests</c>); it is captured if beaten/undefended, and its carried
/// amount persists in saves (v27). Cashing it in is the next slice (<c>86d3c9rzu</c>).
/// </summary>
public class TreasureTrainTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string TreasureTrain = "model.unit.treasureTrain";
    private const string Artillery = "model.unit.artillery"; // captureUnits = true

    /// <summary>Forces a combat win (NextDouble 0) with no promotion / minimum draws.</summary>
    private sealed class ForceWin : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => 0.0;
        public RandomState SaveState() => new(0, 0);
    }

    // ---- Ruleset parse ----

    [Fact]
    public void TreasureTrain_ParsesItsAbilitiesAndStats()
    {
        UnitType t = Classic.Unit(TreasureTrain);
        Assert.True(t.CarryTreasure);   // model.ability.carryTreasure
        Assert.True(t.CanBeCaptured);   // model.ability.canBeCaptured
        Assert.Equal(0, t.Offence);     // non-combat
        Assert.Equal(0, t.Defence);
        Assert.Equal(6, t.SpaceTaken);  // fills a galleon's hold
    }

    [Fact]
    public void Unit_TreasureAmount_DefaultsToZero_AndFloorsNegatives()
    {
        Game game = Game.New(Classic, Seed);
        Unit train = game.SpawnUnit(Classic.Unit(TreasureTrain), game.PlayerUnits.First().Position);

        Assert.Equal(0, train.TreasureAmount);
        train.SetTreasureAmount(500);
        Assert.Equal(500, train.TreasureAmount);
        train.SetTreasureAmount(-10);
        Assert.Equal(0, train.TreasureAmount); // floored, like AddCargo
    }

    // ---- Capturability (reuses the existing CanBeCaptured combat path) ----

    [Fact]
    public void AnUndefendedTreasureTrain_IsCapturedWithItsAmount_ByAnArmedAttacker()
    {
        Game game = Game.New(Classic, Seed);
        string nation = game.NativeSettlements.First().NationTypeId;
        var occupied = game.Units.Where(u => u.IsOnMap).Select(u => u.Position)
            .Concat(game.NativeSettlements.Select(s => s.Position)).ToHashSet();
        (Position trainTile, Position adj) = (from p in game.Map.AllPositions()
            where !game.Map.TerrainAt(p).IsWater && !occupied.Contains(p) && game.ColonyAt(p) is null
            from n in p.Neighbours()
            where game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater && !occupied.Contains(n) && game.ColonyAt(n) is null
            select (p, n)).First();

        Unit train = game.SpawnUnit(Classic.Unit(TreasureTrain), trainTile, nation); // a native-owned train (enemy of the human)
        train.SetTreasureAmount(900);
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), adj);                // human, captureUnits = true
        int id = train.Id;

        game.Attack(attacker, trainTile, new ForceWin()); // artillery (7) vs defence 0 → win → capture (canBeCaptured)

        Unit captured = game.Units.Single(u => u.Id == id);
        Assert.False(captured.IsNative);          // changed side to the human captor
        Assert.Equal(0, captured.OwnerId);
        Assert.Equal(900, captured.TreasureAmount); // the gold rides along with the captured train
    }

    // ---- Persistence (v27, additive) ----

    [Fact]
    public void TreasureAmount_RoundTripsThroughSave_V27()
    {
        Game game = Game.New(Classic, Seed);
        Unit train = game.SpawnUnit(Classic.Unit(TreasureTrain), game.PlayerUnits.First().Position);
        train.SetTreasureAmount(1234);
        int id = train.Id;

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(27, SaveGame.CurrentVersion);
        Assert.Equal(1234, restored.Units.Single(u => u.Id == id).TreasureAmount);
    }

    [Fact]
    public void ATreasureFreeGame_OmitsTheTreasureToken()
    {
        Game game = Game.New(Classic, Seed);
        Assert.All(game.Units, u => Assert.Equal(0, u.TreasureAmount));
        Assert.DoesNotContain("TreasureAmount", SaveGame.From(game).ToJson()); // byte-identical to v26 with no treasure
    }
}
