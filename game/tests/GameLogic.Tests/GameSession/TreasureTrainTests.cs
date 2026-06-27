using System.Linq;
using CrownAndColony.GameLogic.Colonies;
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

        Assert.Equal(66, SaveGame.CurrentVersion);
        Assert.Equal(1234, restored.Units.Single(u => u.Id == id).TreasureAmount);
    }

    [Fact]
    public void ATreasureFreeGame_OmitsTheTreasureToken()
    {
        Game game = Game.New(Classic, Seed);
        Assert.All(game.Units, u => Assert.Equal(0, u.TreasureAmount));
        Assert.DoesNotContain("TreasureAmount", SaveGame.From(game).ToJson()); // byte-identical to v26 with no treasure
    }

    // ---- Cash-in (86d3c9rzu) ----

    private const string Cortes = "model.foundingFather.hernanCortes";

    /// <summary>A human colony with a human-owned treasure train standing on it, carrying <paramref name="amount"/>.</summary>
    private static (Game game, Unit train, Colony colony) TrainAtColony(int amount)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        game.HumanPlayer.TaxRate = 0; // isolate the King's transport cut from the monarch tax
        Unit train = game.SpawnUnit(Classic.Unit(TreasureTrain), colony.Position);
        train.SetTreasureAmount(amount);
        return (game, train, colony);
    }

    [Fact]
    public void CashIn_AtAColony_WithNoTax_BanksTheFullAmount()
    {
        // Col1 model (86d3fb5mj): the King's at-colony cut equals the tax rate. At 0% tax he takes nothing, so the
        // full amount is banked even via the King.
        (Game game, Unit train, _) = TrainAtColony(1000);
        int goldBefore = game.HumanPlayer.Gold;
        int id = train.Id;

        Assert.Equal(1000, game.CashInValue(train));   // 0% tax → King's cut 0 → full 1000
        Assert.Equal(0, game.TreasureKingsCut(train));
        game.CashInTreasureTrain(train);

        Assert.Equal(goldBefore + 1000, game.HumanPlayer.Gold);
        Assert.DoesNotContain(game.Units, u => u.Id == id); // the train is spent
    }

    [Theory]
    [InlineData(0, 1000)]   // 1000 × (100−0)/100
    [InlineData(25, 750)]   // 1000 × (100−25)/100
    [InlineData(50, 500)]   // 1000 × (100−50)/100
    [InlineData(75, 250)]   // 1000 × (100−75)/100
    public void CashIn_AtAColony_KingsCutEqualsTheTaxRate(int taxRate, int expectedNet)
    {
        // Col1 net formula (86d3fb5mj): a 1000-treasure train at a colony with tax T nets 1000 × (100−T)/100; the
        // King's cut is the complement (T% of the amount). No separate 60% fee, no extra tax on top.
        (Game game, Unit train, _) = TrainAtColony(1000);
        game.HumanPlayer.TaxRate = taxRate;
        int goldBefore = game.HumanPlayer.Gold;

        Assert.Equal(expectedNet, game.CashInValue(train));
        Assert.Equal(1000 - expectedNet, game.TreasureKingsCut(train)); // cut + net == amount

        game.CashInTreasureTrain(train);
        Assert.Equal(goldBefore + expectedNet, game.HumanPlayer.Gold);
    }

    [Fact]
    public void CashIn_AtAColony_WithHernanCortes_WaivesTheKingsCut()
    {
        // Cortés's treasureTransportFee −100% folds into the at-colony cut → free transport, full amount even at high tax.
        (Game game, Unit train, _) = TrainAtColony(1000);
        game.HumanPlayer.TaxRate = 25;
        game.HumanPlayer.CongressList.Add(Cortes); // treasureTransportFee −100% → King's cut waived
        int goldBefore = game.HumanPlayer.Gold;

        Assert.Equal(0, game.TreasureKingsCut(train));     // cut waived despite 25% tax
        Assert.Equal(1000, game.CashInValue(train));       // full amount
        game.CashInTreasureTrain(train);

        Assert.Equal(goldBefore + 1000, game.HumanPlayer.Gold); // full amount (no cut, no tax)
    }

    [Fact]
    public void CashIn_InEurope_KeepsTheFullAmount_FeeFreeAndTaxFree()
    {
        // Col1 model (86d3fb5mj): treasure you carry home yourself is BOTH fee-free and tax-free — even at a high tax
        // rate the full amount is banked (the tax only applies when the King ships it from a colony).
        (Game game, Unit train, _) = TrainAtColony(1000);
        game.HumanPlayer.TaxRate = 75;          // a high tax that would gut an at-colony cash-in
        train.Location = UnitLocation.InEurope; // carried home yourself → no King's cut and no tax
        int goldBefore = game.HumanPlayer.Gold;

        Assert.True(game.TreasureCashInIsFeeFree(train));
        Assert.Equal(0, game.TreasureKingsCut(train));
        Assert.Equal(1000, game.CashInValue(train)); // full amount, untaxed
        game.CashInTreasureTrain(train);

        Assert.Equal(goldBefore + 1000, game.HumanPlayer.Gold);
    }

    [Fact]
    public void CashIn_AwayFromAColony_IsRefused()
    {
        Game game = Game.New(Classic, Seed);
        Position open = game.Map.AllPositions().First(p => !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        Unit train = game.SpawnUnit(Classic.Unit(TreasureTrain), open);
        train.SetTreasureAmount(500);

        Assert.False(game.CheckCashInTreasureTrain(train).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.CashInTreasureTrain(train));
        Assert.Contains(game.Units, u => u.Id == train.Id); // not consumed
    }

    [Fact]
    public void CashIn_CannotBeRepeated_OnTheSameTrain()
    {
        (Game game, Unit train, _) = TrainAtColony(1000);
        game.CashInTreasureTrain(train);
        int goldAfter = game.HumanPlayer.Gold;

        Assert.False(game.CheckCashInTreasureTrain(train).Allowed);    // spent — its treasure is 0
        Assert.Throws<InvalidMoveException>(() => game.CashInTreasureTrain(train));
        Assert.Equal(goldAfter, game.HumanPlayer.Gold);               // no double credit
    }

    [Fact]
    public void CashIn_AtARivalColony_IsRefused()
    {
        (Game game, Unit train, Colony colony) = TrainAtColony(1000);
        colony.OwnerId = 99; // a colony the train's owner (0) does not hold

        Assert.False(game.CheckCashInTreasureTrain(train).Allowed);
        Assert.Throws<InvalidMoveException>(() => game.CashInTreasureTrain(train));
    }

    [Fact]
    public void CheckCashIn_PreviewsTheNetGold()
    {
        (Game game, Unit train, _) = TrainAtColony(1000);
        game.HumanPlayer.TaxRate = 25;
        MoveCheck check = game.CheckCashInTreasureTrain(train);
        Assert.True(check.Allowed);
        Assert.Equal(750, check.Cost); // the net the player would bank (1000 × (100−25)/100), preview
    }

    // ---- Load onto a galleon + sail home fee-free (86d3e4bcp, GAP A) ----

    private const string Galleon = "model.unit.galleon"; // space 6 — the only carrier that fits a treasure train (spaceTaken 6)

    /// <summary>A 3×1 coastal strip (plains port colony at (0,0), ocean at (1,0), high seas at (2,0)) with a human galleon on the ocean beside the colony and a treasure train standing in the colony.</summary>
    private static (Game game, Unit train, Unit galleon) TrainAndGalleonAtColony(int amount)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 1,
            Terrain = ["model.tile.plains", "model.tile.ocean", "model.tile.highSeas"],
            Units =
            [
                new SavedUnit(1, Galleon, 1, 0, 18),                              // on the ocean beside the colony
                new SavedUnit(2, TreasureTrain, 0, 0, 3, TreasureAmount: amount), // in the port colony
            ],
            Explored = [0, 1, 2],
            Colonies = [new SavedColony(1, "Port", 0, 0, 1)],
        };
        Game game = save.Restore(Classic);
        game.HumanPlayer.TaxRate = 0; // isolate the (waived) King's cut from the monarch tax
        return (game, game.Units.First(u => u.Id == 2), game.Units.First(u => u.Id == 1));
    }

    [Fact]
    public void TreasureTrain_AboardAGalleon_CannotBeCashedInWhileStillOnTheMap()
    {
        (Game game, Unit train, Unit galleon) = TrainAndGalleonAtColony(1000);
        game.Board(train, galleon); // loaded as cargo, but the galleon is still in the New World

        Assert.True(train.IsAboard);
        Assert.False(game.CheckCashInTreasureTrain(train).Allowed); // not yet home → no cash-in here
    }

    [Fact]
    public void TreasureTrain_CarriedHomeOnAGalleon_CashesInFeeFree()
    {
        (Game game, Unit train, Unit galleon) = TrainAndGalleonAtColony(1000);
        game.Board(train, galleon);
        int goldBefore = game.HumanPlayer.Gold;
        int trainId = train.Id;

        game.MoveUnit(galleon, new Position(2, 0)); // step out to the high seas (the train rides along)
        game.SailToEurope(galleon);
        for (int i = 0; i < Game.SailTurns; i++)
        {
            game.EndTurn();                         // cross the Atlantic
        }

        Assert.Equal(UnitLocation.InEurope, galleon.Location);
        Assert.Equal(UnitLocation.InEurope, train.Location);   // the aboard train arrived with the ship
        Assert.True(game.CheckCashInTreasureTrain(train).Allowed);
        Assert.Equal(1000, game.CashInValue(train));           // fee-free — you carried it yourself (no King's cut)

        game.CashInTreasureTrain(train);

        Assert.Equal(goldBefore + 1000, game.HumanPlayer.Gold); // full amount banked (no fee, no tax)
        Assert.DoesNotContain(game.Units, u => u.Id == trainId);// the train is spent
        Assert.Empty(game.Passengers(galleon));                 // and the galleon's hold is freed
    }
}
