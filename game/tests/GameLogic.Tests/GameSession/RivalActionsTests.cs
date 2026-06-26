using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Player-initiated diplomacy actions against a rival European power (86d3fq0bf / 86d3fq0dj / 86d3fq0ev):
/// <list type="bullet">
/// <item><b>Declare war</b> — an explicit command setting a contacted at-peace rival to <see cref="Stance.War"/> with
/// the FreeCol stance-change tension consequence (<c>csChangeStance(WAR)</c>).</item>
/// <item><b>Trade at a rival colony</b> — a carrier (gated on de Witt's <c>tradeWithForeignColonies</c>) sells/buys
/// goods at a non-war rival colony for gold (FreeCol <c>moveTrade</c>).</item>
/// <item><b>Demand tribute from a European colony</b> — an armed unit shakes down a rival colony; a badly-outmatched
/// owner pays gold out of its treasury, an even-or-stronger one refuses; either way the demand sours relations
/// (FreeCol <c>moveTribute</c>'s European branch). The roll is on the demander's own stream (ADR-009).</item>
/// </list>
/// </summary>
public class RivalActionsTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string Tobacco = "model.goods.tobacco";
    private const string Artillery = "model.unit.artillery"; // innate offence 7 — "armed" without a role
    private const string Caravel = "model.unit.caravel";
    private const string DeWitt = "model.foundingFather.janDeWitt";
    private const string VeteranSoldier = "model.unit.veteranSoldier";
    private const string SoldierRole = "model.role.soldier";

    /// <summary>A fixed RNG: <see cref="Next(int)"/> returns min(value, max-1); doubles unused here.</summary>
    private sealed class TestRandom(int next) : IGameRandom
    {
        public int Next(int maxExclusive) => System.Math.Min(next, maxExclusive - 1);
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => 0.0;
        public RandomState SaveState() => new(0, 0);
    }

    private static int ForeignPowerId(Game game) =>
        game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater && g.Map.TerrainAt(p).CanSettle
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null && !g.Map.IsNativeOwned(p)
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    private static int Chebyshev(Position a, Position b) =>
        System.Math.Max(System.Math.Abs(a.X - b.X), System.Math.Abs(a.Y - b.Y));

    private static Position FoundableSite(Game g, Position? avoid = null) =>
        g.Map.AllPositions().First(p =>
            FreeLand(g, p)
            && (avoid is null || Chebyshev(p, avoid.Value) > 2)
            && p.Neighbours().All(n => !g.Map.InBounds(n) || g.ColonyAt(n) is null));

    /// <summary>A coastal foundable land tile (adjacent to a water tile), so a ship can sit beside the colony.</summary>
    private static Position CoastalFoundableSite(Game g, Position? avoid = null) =>
        g.Map.AllPositions().First(p =>
            FreeLand(g, p)
            && (avoid is null || Chebyshev(p, avoid.Value) > 2)
            && p.Neighbours().Any(n => g.Map.InBounds(n) && g.Map.TerrainAt(n).IsWater)
            && p.Neighbours().All(n => !g.Map.InBounds(n) || g.ColonyAt(n) is null));

    private static Colony FoundColonyAt(Game g, int ownerId, Position site)
    {
        Unit founder = g.SpawnUnit(Classic.Unit(Colony.FreeColonistTypeId), site);
        Colony colony = g.FoundColony(founder);
        colony.OwnerId = ownerId;
        return colony;
    }

    private static void Elect(Game game, int playerId, string fatherId) =>
        game.Players.First(p => p.PlayerId == playerId).CongressList.Add(fatherId);

    // ───────────────────────── Declare war on a rival (86d3fq0bf) ─────────────────────────

    [Fact]
    public void CanDeclareWar_OnlyOnAContactedAtPeaceRival()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);

        Assert.False(game.CanDeclareWar(0, fid)); // uncontacted — war comes via the first attack, not a declaration

        game.SetStance(0, fid, Stance.Peace);
        Assert.True(game.CanDeclareWar(0, fid)); // met and at peace → may declare

        game.SetStance(0, fid, Stance.War);
        Assert.False(game.CanDeclareWar(0, fid)); // already at war → nothing to declare

        Assert.False(game.CanDeclareWar(0, 0)); // not against oneself
    }

    [Fact]
    public void DeclareWar_SetsMutualWar_AndSpikesTension()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.Peace);

        Assert.True(game.DeclareWar(0, fid));

        Assert.Equal(Stance.War, game.StanceBetween(0, fid));
        Assert.Equal(Stance.War, game.StanceBetween(fid, 0));            // symmetric
        Assert.Equal(Game.TensionWar, game.TensionBetween(0, fid));      // Peace→War spikes by TensionWar (1000)
    }

    [Fact]
    public void DeclareWar_FromCeaseFire_AddsTheResumeWarModifier()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.CeaseFire);
        int before = game.TensionBetween(0, fid);

        Assert.True(game.DeclareWar(0, fid));

        Assert.Equal(Stance.War, game.StanceBetween(0, fid));
        Assert.Equal(System.Math.Min(before + Game.TensionResumeWarModifier, Game.MaxTension), game.TensionBetween(0, fid));
    }

    [Fact]
    public void DeclareWar_OnAnIllegalTarget_IsAHarmlessNoOp()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        // Uncontacted: not declarable.
        Assert.False(game.DeclareWar(0, fid));
        Assert.Equal(Stance.Uncontacted, game.StanceBetween(0, fid));
    }

    [Fact]
    public void DeclareWar_DrawsNoRng_LeavesTheSavedStreamByteIdentical()
    {
        var game = Game.New(Classic, seed: 7);
        int fid = ForeignPowerId(game);
        game.SetStance(0, fid, Stance.Peace);
        RandomState before = game.RandomState;
        game.DeclareWar(0, fid);
        Assert.Equal(before, game.RandomState);
    }

    // ───────────────────────── Trade at a rival colony (86d3fq0dj) ─────────────────────────

    /// <summary>Human ship beside a rival coastal colony, de Witt elected, at peace; the rival owner funded.</summary>
    private static (Game game, Colony rival, Unit ship, int fid) StageForeignTrade(ulong seed = 7, bool deWitt = true, int rivalGold = 5000)
    {
        Game game = Game.New(Classic, seed);
        int fid = ForeignPowerId(game);
        Position site = CoastalFoundableSite(game);
        Colony rival = FoundColonyAt(game, fid, site);
        game.SetStance(0, fid, Stance.Peace);
        if (deWitt)
        {
            Elect(game, 0, DeWitt);
        }
        game.Players.First(p => p.PlayerId == fid).Gold = rivalGold;
        Position water = rival.Position.Neighbours().First(n => game.Map.InBounds(n) && game.Map.TerrainAt(n).IsWater);
        Unit ship = game.SpawnUnit(Classic.Unit(Caravel), water); // human-owned carrier
        return (game, rival, ship, fid);
    }

    [Fact]
    public void CheckSellToForeignColony_RequiresTheDeWittAbility()
    {
        (Game game, Colony rival, Unit ship, _) = StageForeignTrade(deWitt: false);
        ship.AddCargo(Tobacco, 100);
        Assert.False(game.CheckSellToForeignColony(ship, rival.Position, Tobacco, 50).Allowed); // no de Witt → refused
    }

    [Fact]
    public void CheckSellToForeignColony_RefusesAtWar()
    {
        (Game game, Colony rival, Unit ship, int fid) = StageForeignTrade();
        game.SetStance(0, fid, Stance.War);
        ship.AddCargo(Tobacco, 100);
        Assert.False(game.CheckSellToForeignColony(ship, rival.Position, Tobacco, 50).Allowed);
    }

    [Fact]
    public void CheckSellToForeignColony_RefusesOwnColony()
    {
        (Game game, _, Unit ship, _) = StageForeignTrade();
        Position site = CoastalFoundableSite(game, avoid: ship.Position);
        Colony ours = FoundColonyAt(game, ownerId: 0, site);
        ship.AddCargo(Tobacco, 100);
        Assert.False(game.CheckSellToForeignColony(ship, ours.Position, Tobacco, 50).Allowed); // not a rival colony
    }

    [Fact]
    public void SellToForeignColony_MovesGoodsIn_TransfersGold_EndsTheTurn()
    {
        (Game game, Colony rival, Unit ship, int fid) = StageForeignTrade();
        ship.AddCargo(Tobacco, 100);
        int humanGold = game.HumanPlayer.Gold;
        int rivalGold = game.Players.First(p => p.PlayerId == fid).Gold;
        int rivalStore = rival.StoreOf(Tobacco);
        int quoted = game.CheckSellToForeignColony(ship, rival.Position, Tobacco, 60).Cost;
        Assert.True(quoted > 0);

        int got = game.SellToForeignColony(ship, rival.Position, Tobacco, 60);

        Assert.Equal(quoted, got);
        Assert.Equal(40, ship.CargoOf(Tobacco));                       // 100 − 60 left aboard
        Assert.Equal(rivalStore + 60, rival.StoreOf(Tobacco));         // joined the rival warehouse
        Assert.Equal(humanGold + got, game.HumanPlayer.Gold);          // trader credited
        Assert.Equal(rivalGold - got, game.Players.First(p => p.PlayerId == fid).Gold); // rival paid
        Assert.Equal(0, ship.MovementLeft);                            // session ended the turn
    }

    [Fact]
    public void BuyFromForeignColony_MovesGoodsOut_TransfersGold()
    {
        (Game game, Colony rival, Unit ship, int fid) = StageForeignTrade();
        rival.AddGoods(Tobacco, 200);
        game.HumanPlayer.Gold = 9999;
        int humanGold = game.HumanPlayer.Gold;
        int rivalGold = game.Players.First(p => p.PlayerId == fid).Gold;
        int quoted = game.CheckBuyFromForeignColony(ship, rival.Position, Tobacco, 80).Cost;

        int paid = game.BuyFromForeignColony(ship, rival.Position, Tobacco, 80);

        Assert.Equal(quoted, paid);
        Assert.Equal(80, ship.CargoOf(Tobacco));
        Assert.Equal(120, rival.StoreOf(Tobacco));                     // 200 − 80 drained
        Assert.Equal(humanGold - paid, game.HumanPlayer.Gold);
        Assert.Equal(rivalGold + paid, game.Players.First(p => p.PlayerId == fid).Gold);
        Assert.Equal(0, ship.MovementLeft);
    }

    [Fact]
    public void CheckBuyFromForeignColony_RefusesWhenTheColonyLacksTheGoods()
    {
        (Game game, Colony rival, Unit ship, _) = StageForeignTrade();
        game.HumanPlayer.Gold = 9999;
        rival.AddGoods(Tobacco, 10);
        Assert.False(game.CheckBuyFromForeignColony(ship, rival.Position, Tobacco, 80).Allowed);
    }

    [Fact]
    public void ForeignColonyTrade_IsDeterministic_AndDrawsNoRng()
    {
        (Game game, Colony rival, Unit ship, _) = StageForeignTrade();
        ship.AddCargo(Tobacco, 100);
        RandomState before = game.RandomState;
        game.SellToForeignColony(ship, rival.Position, Tobacco, 50);
        Assert.Equal(before, game.RandomState); // a deterministic transfer — no stream perturbation
    }

    // ───────────────────────── Demand tribute from a European colony (86d3fq0ev) ─────────────────────────

    /// <summary>Gives <paramref name="ownerId"/> <paramref name="count"/> armed soldiers, lifting its land power.</summary>
    private static void GiveSoldiers(Game g, int ownerId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Unit soldier = g.SpawnUnit(Classic.Unit(VeteranSoldier), FoundableSite(g));
            soldier.RoleId = SoldierRole;
            soldier.OwnerId = ownerId;
        }
    }

    /// <summary>Human artillery beside a rival colony; the human dominates by strength unless told otherwise.</summary>
    private static (Game game, Colony rival, Unit gun, int fid) StageColonyTribute(ulong seed = 7, int rivalGold = 1000, bool humanDominant = true)
    {
        Game game = Game.New(Classic, seed);
        int fid = ForeignPowerId(game);
        // Strip the human's mixed starting roster so the test controls the strength ratio precisely.
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap).ToList())
        {
            game.Disband(u);
        }
        Position site = FoundableSite(game);
        Colony rival = FoundColonyAt(game, fid, site);
        game.SetStance(0, fid, Stance.Peace);
        game.Players.First(p => p.PlayerId == fid).Gold = rivalGold;
        if (humanDominant)
        {
            GiveSoldiers(game, ownerId: 0, count: 6);   // human strength >> rival → ratio > 0.66
        }
        else
        {
            GiveSoldiers(game, ownerId: fid, count: 6);  // rival strength >> human → ratio < 0.5 (refuses)
        }
        Position adj = rival.Position.Neighbours().First(n => FreeLand(game, n));
        Unit gun = game.SpawnUnit(Classic.Unit(Artillery), adj); // human-owned offensive unit
        return (game, rival, gun, fid);
    }

    [Fact]
    public void CheckDemandTributeFromColony_AllowsAnArmedUnitBesideAContactedRivalColony()
    {
        (Game game, Colony rival, Unit gun, _) = StageColonyTribute();
        Assert.True(game.CheckDemandTributeFromColony(gun, rival.Position).Allowed);
    }

    [Fact]
    public void CheckDemandTributeFromColony_RejectsAnUncontactedRival()
    {
        (Game game, Colony rival, Unit gun, int fid) = StageColonyTribute();
        game.SetStance(0, fid, Stance.Uncontacted); // never met → cannot demand (war would come via the first attack)
        Assert.False(game.CheckDemandTributeFromColony(gun, rival.Position).Allowed);
    }

    [Fact]
    public void CheckDemandTributeFromColony_RejectsAnUnarmedUnit()
    {
        (Game game, Colony rival, _, _) = StageColonyTribute();
        Position adj = rival.Position.Neighbours().Last(n => FreeLand(game, n));
        Unit colonist = game.SpawnUnit(Classic.Unit(Colony.FreeColonistTypeId), adj);
        Assert.False(game.CheckDemandTributeFromColony(colonist, rival.Position).Allowed);
    }

    [Fact]
    public void EvaluateColonyTributeDemand_WhenDominant_PaysTheRoll_CappedAndTreasuryLimited()
    {
        (Game game, Colony rival, Unit gun, _) = StageColonyTribute(rivalGold: 1000);
        _ = gun;
        int gold = game.EvaluateColonyTributeDemand(rival, demanderId: 0, new TestRandom(next: 40));
        Assert.Equal(40, gold); // dominant (ratio > 0.66) → full roll, under the 100 cap and the 1000 treasury
    }

    [Fact]
    public void EvaluateColonyTributeDemand_CapsAtOneHundred()
    {
        (Game game, Colony rival, _, _) = StageColonyTribute(rivalGold: 1000);
        int gold = game.EvaluateColonyTributeDemand(rival, demanderId: 0, new TestRandom(next: 9999));
        Assert.InRange(gold, 1, Game.EuropeanTributeGoldCap);
    }

    [Fact]
    public void EvaluateColonyTributeDemand_IsTreasuryLimited()
    {
        (Game game, Colony rival, _, _) = StageColonyTribute(rivalGold: 25);
        int gold = game.EvaluateColonyTributeDemand(rival, demanderId: 0, new TestRandom(next: 9999));
        Assert.Equal(25, gold); // never extracts more than the rival actually holds
    }

    [Fact]
    public void EvaluateColonyTributeDemand_WhenOutmatched_Refuses()
    {
        (Game game, Colony rival, _, _) = StageColonyTribute(rivalGold: 1000, humanDominant: false);
        int gold = game.EvaluateColonyTributeDemand(rival, demanderId: 0, new TestRandom(next: 9999));
        Assert.Equal(0, gold); // the demander is the weaker side → the rival refuses
    }

    [Fact]
    public void DemandTributeFromColony_WhenDominant_PaysGold_RaisesRivalTension_EndsTheTurn()
    {
        (Game game, Colony rival, Unit gun, int fid) = StageColonyTribute(rivalGold: 1000);
        int humanGold = game.HumanPlayer.Gold;
        Player rivalPlayer = game.Players.First(p => p.PlayerId == fid);
        int rivalGold = rivalPlayer.Gold;
        int rivalTensionBefore = game.TensionBetween(fid, 0);

        Game.EuropeanTributeResult result = game.DemandTributeFromColony(gun, rival.Position, new TestRandom(next: 30));

        Assert.True(result.Paid);
        Assert.Equal(30, result.Gold);
        Assert.Equal(humanGold + 30, game.HumanPlayer.Gold);            // extracted to the demander
        Assert.Equal(rivalGold - 30, rivalPlayer.Gold);                // paid out of the rival treasury
        Assert.Equal(rivalTensionBefore + Game.TensionDemandTribute, game.TensionBetween(fid, 0)); // the rival resents it
        Assert.Equal(0, gun.MovementLeft);                             // the demand ended the turn
    }

    [Fact]
    public void DemandTributeFromColony_WhenOutmatched_RefusesButStillSoursRelations()
    {
        (Game game, Colony rival, Unit gun, int fid) = StageColonyTribute(rivalGold: 1000, humanDominant: false);
        int humanGold = game.HumanPlayer.Gold;
        int rivalTensionBefore = game.TensionBetween(fid, 0);

        Game.EuropeanTributeResult result = game.DemandTributeFromColony(gun, rival.Position, new TestRandom(next: 9999));

        Assert.False(result.Paid);
        Assert.Equal(0, result.Gold);
        Assert.Equal(humanGold, game.HumanPlayer.Gold);                // nothing extracted
        Assert.Equal(rivalTensionBefore + Game.TensionDemandTribute, game.TensionBetween(fid, 0)); // still an insult
        Assert.Equal(0, gun.MovementLeft);
    }

    [Fact]
    public void DemandTributeFromColony_ExplicitRng_NeverTouchesTheSavedStream()
    {
        (Game game, Colony rival, Unit gun, _) = StageColonyTribute(rivalGold: 1000);
        RandomState before = game.RandomState;
        game.DemandTributeFromColony(gun, rival.Position, new TestRandom(next: 30));
        Assert.Equal(before, game.RandomState);
    }

    [Fact]
    public void DemandTributeFromColony_RejectsAnIllegalDemand()
    {
        (Game game, Colony rival, Unit gun, _) = StageColonyTribute();
        gun.MovementLeft = 0;
        Assert.Throws<InvalidMoveException>(() => game.DemandTributeFromColony(gun, rival.Position, new TestRandom(next: 0)));
    }
}
