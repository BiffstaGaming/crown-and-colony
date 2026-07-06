using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Colony fortification defence bonus: a colony's stockade/fort/fortress (FreeCol <c>model.modifier.defence</c>:
/// +100 / +150 / +200 %) shields whoever defends it — the transient last colonist in a capture (1c-3e/3f) or
/// pillage, and a garrison unit on the colony tile (the open-field path). Forced via a fixed RNG: each test picks
/// a roll that lands between the fortified and unfortified win probabilities, so the bonus alone flips the outcome.
/// </summary>
public class ColonyDefenceBonusTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xDEFEUL;
    private const string Artillery = "model.unit.artillery"; // offence 7, defence 5, movement 3 (no movement penalty)
    private const string Brave = "model.unit.brave";
    private const string KingsRegular = "model.unit.kingsRegular"; // offence 4, defence 5
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Tobacco = "model.goods.tobacco";
    private const string Stockade = "model.building.stockade";
    private const string Fortress = "model.building.fortress";

    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    private static bool FreeLand(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);

    [Fact]
    public void BuildingDefenceBonus_PinnedToSpec()
    {
        Assert.Equal(100, Classic.Building(Stockade).DefenceBonus);
        Assert.Equal(150, Classic.Building("model.building.fort").DefenceBonus);   // delete+redefine handled
        Assert.Equal(200, Classic.Building(Fortress).DefenceBonus);
        Assert.Equal(0, Classic.Building("model.building.townHall").DefenceBonus); // a non-defence building
    }

    [Fact]
    public void ColonyDefenceBonus_ReflectsTheFortificationBuilt()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));

        Assert.Equal(0, game.ColonyDefenceBonus(colony)); // unfortified
        colony.AddBuilding(Stockade);
        Assert.Equal(100, game.ColonyDefenceBonus(colony));
    }

    /// <summary>A human-founded colony handed to a rival, with a human artillery beside it; optionally fortified.</summary>
    private static (Game game, Colony colony, Unit attacker, int foreignId, int humanId) StageRival(bool stockade)
    {
        Game game = Game.New(Classic, Seed);
        int humanId = game.HumanPlayer.PlayerId;
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        colony.OwnerId = foreignId;
        if (stockade)
        {
            colony.AddBuilding(Stockade);
        }
        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), adj); // human-owned, offence 7
        return (game, colony, attacker, foreignId, humanId);
    }

    [Fact]
    public void AStockade_RepelsAnAssaultThatWouldOtherwiseCapture()
    {
        // artillery 7 ×1.5 attack ×1.5 colony-assault bonus = 15.75 vs the colony's lone colonist (defence 1). Col1
        // gives every European power's regulars the +50% colony-assault bonus (86d3kgbp3), so the human attacker now
        // carries it too. Unfortified win prob ≈ 0.940; a stockade doubles the defence (→2) so win prob ≈ 0.887.
        // A roll of 0.91 sits in that gap.
        var (open, openColony, openAtk, _, humanId) = StageRival(stockade: false);
        var (walled, walledColony, walledAtk, foreignId, _) = StageRival(stockade: true);

        open.AttackColony(openAtk, openColony.Position, new FixedRandom(0.91));
        walled.AttackColony(walledAtk, walledColony.Position, new FixedRandom(0.91));

        Assert.Equal(humanId, open.Colonies.First(c => c.Id == openColony.Id).OwnerId);     // captured
        Assert.Equal(foreignId, walled.Colonies.First(c => c.Id == walledColony.Id).OwnerId); // the stockade held
    }

    /// <summary>A human colony with a free-colonist garrison on its tile and a foreign artillery beside it (at war); optionally fortified.</summary>
    private static (Game game, Colony colony, Unit attacker) StageGarrison(bool fortress)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        if (fortress)
        {
            colony.AddBuilding(Fortress);
        }
        game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position); // human garrison on the colony tile (defence 1)

        // A native raider (offence 1) — deliberately not artillery, so the fortress bonus alone decides the combat.
        string nation = game.NativeSettlements.First().NationTypeId;
        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(Brave), adj, nation);
        attacker.MovementLeft = 3; // normalise away the movement penalty for a clean forced roll
        return (game, colony, attacker);
    }

    [Fact]
    public void AFortress_HelpsAGarrisonRepelAnAttackOnTheColonyTile()
    {
        // The main Attack path with a garrison on the colony tile. Defending in a colony suppresses the tile
        // terrain bonus (FreeCol), so the garrison's defence is exactly base (1) × fortification. A native raider
        // (offence 1 ×1.5 attack bonus = 1.5): no fortress → def 1, win prob 1.5/2.5 = 0.6 (attacker wins);
        // fortress → def 3 (+200%), win prob 1.5/4.5 ≈ 0.33 (repelled). A roll of 0.5 sits in that gap.
        // (A non-artillery raider keeps the fortress bonus the deciding factor — artillery would batter through,
        // since FreeCol gives it no in-the-open penalty when attacking a settlement.)
        var (open, openColony, openAtk) = StageGarrison(fortress: false);
        var (walled, _, walledAtk) = StageGarrison(fortress: true);

        CombatResult openResult = open.Attack(openAtk, openColony.Position, new FixedRandom(0.5));
        CombatResult walledResult = walled.Attack(walledAtk, walled.Colonies.First().Position, new FixedRandom(0.5));

        Assert.True(openResult is CombatResult.GreatWin or CombatResult.Win);     // unfortified → attacker wins
        Assert.True(walledResult is CombatResult.Loss or CombatResult.GreatLoss); // fortress → garrison repels it
    }

    [Fact]
    public void ArtilleryRepelsARaidBehindWalls_ButFallsCaughtInTheOpen()
    {
        // Defender artillery (def 5). Behind a colony's walls against a native raid: ×(1 + 100% against-raid) = 10,
        // so a raider (1.5 attack) bounces off. Caught in the open: ×(1 − 75% in-the-open) = 1.25, and the same
        // raider overruns it. A forced roll of 0.5 sits between the two win probabilities (≈0.13 vs ≈0.55).
        var (colonyGame, colonyTarget, raid1) = StageArtilleryDefender(inColony: true);
        var (openGame, openTarget, raid2) = StageArtilleryDefender(inColony: false);

        Assert.True(colonyGame.Attack(raid1, colonyTarget, new FixedRandom(0.5)) is CombatResult.Loss or CombatResult.GreatLoss);
        Assert.True(openGame.Attack(raid2, openTarget, new FixedRandom(0.5)) is CombatResult.GreatWin or CombatResult.Win);
    }

    [Fact]
    public void Artillery_BattersAColonyGarrison_ButIsBrittleAttackingInTheOpen()
    {
        // Attacker artillery (off 7) vs a king's regular (def 5). Sieging it inside a colony: 7 ×1.5 attack ×1.5
        // colony-assault bonus = 15.75 (a European power's regulars get the +50% colony-assault bonus, 86d3kgbp3; and
        // no in-the-open penalty when the defender is in a settlement) → wins. The same attack in the open:
        // 7 ×1.5 ×0.25 = 2.625 → loses. A forced roll of 0.5 sits between (≈0.76 vs ≈0.34).
        var (colonyGame, colonyTarget, gun1) = StageArtilleryAttacker(targetInColony: true);
        var (openGame, openTarget, gun2) = StageArtilleryAttacker(targetInColony: false);

        Assert.True(colonyGame.Attack(gun1, colonyTarget, new FixedRandom(0.5)) is CombatResult.GreatWin or CombatResult.Win);
        Assert.True(openGame.Attack(gun2, openTarget, new FixedRandom(0.5)) is CombatResult.Loss or CombatResult.GreatLoss);
    }

    /// <summary>A human artillery defender (on its colony tile, or alone on an open plains tile) with a native raider beside it.</summary>
    private static (Game game, Position target, Unit brave) StageArtilleryDefender(bool inColony)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        string nation = game.NativeSettlements.First().NationTypeId;

        Position target = inColony
            ? colony.Position
            : game.Map.AllPositions().First(p =>
                FreeLand(game, p) && game.Map.TerrainAt(p).DefenceBonus == 0 && p.Neighbours().Any(n => FreeLand(game, n)));
        game.SpawnUnit(Classic.Unit(Artillery), target); // human artillery defender
        Position adj = target.Neighbours().First(n => FreeLand(game, n));
        Unit brave = game.SpawnUnit(Classic.Unit(Brave), adj, nation);
        brave.MovementLeft = 3; // normalise away the movement penalty for a clean forced roll
        return (game, target, brave);
    }

    /// <summary>A foreign artillery attacker beside a human king's-regular defender (on a colony tile, or open plains).</summary>
    private static (Game game, Position target, Unit gun) StageArtilleryAttacker(bool targetInColony)
    {
        Game game = Game.New(Classic, Seed);
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));

        Position target = targetInColony
            ? colony.Position
            : game.Map.AllPositions().First(p =>
                FreeLand(game, p) && game.Map.TerrainAt(p).DefenceBonus == 0 && p.Neighbours().Any(n => FreeLand(game, n)));
        game.SpawnUnit(Classic.Unit(KingsRegular), target); // human defender (defence 5)
        Position adj = target.Neighbours().First(n => FreeLand(game, n));
        Unit gun = game.SpawnUnit(Classic.Unit(Artillery), adj);
        gun.OwnerId = foreignId;
        game.SetStance(foreignId, game.HumanPlayer.PlayerId, Stance.War);
        return (game, target, gun);
    }

    /// <summary>A human colony stocked with tobacco and a brave beside it; optionally fortified. Starting ships are parked away from the port so a won pillage's option-0 pick is the tobacco stack (not "sink a ship in port").</summary>
    private static (Game game, Colony colony, Unit brave) StagePillage(bool stockade)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        var port = colony.Position.Neighbours().ToHashSet();
        Position far = game.Map.AllPositions()
            .First(p => game.Map.TerrainAt(p).IsWater && !port.Contains(p)
                && !colony.Position.IsAdjacentTo(p) && p != colony.Position
                && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        foreach (Unit ship in game.PlayerUnits.Where(u => u.IsOnMap && u.Type.IsNaval && port.Contains(u.Position)).ToList())
        {
            ship.Position = far; // clear the port so the pillage loots goods, not a docked ship
        }
        colony.AddGoods(Tobacco, 100);
        if (stockade)
        {
            colony.AddBuilding(Stockade);
        }
        string nation = game.NativeSettlements.First().NationTypeId;
        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit brave = game.SpawnUnit(Classic.Unit(Brave), adj, nation);
        brave.MovementLeft = 3; // normalise away the movement penalty for a clean forced roll
        return (game, colony, brave);
    }

    [Fact]
    public void AStockade_RepelsANativePillageThatWouldOtherwiseSucceed()
    {
        // brave 1 ×1.5 = 1.5 attack vs the colonist (defence 1). Unfortified win prob = 0.6; a stockade doubles
        // the defence (→2) so win prob ≈ 0.43. A roll of 0.5 sits in that gap.
        var (open, openColony, openBrave) = StagePillage(stockade: false);
        var (walled, walledColony, walledBrave) = StagePillage(stockade: true);

        open.PillageColony(openBrave, openColony.Position, new FixedRandom(0.5));
        walled.PillageColony(walledBrave, walledColony.Position, new FixedRandom(0.5));

        Assert.True(openColony.StoreOf(Tobacco) < 100);     // pillaged
        Assert.Equal(100, walledColony.StoreOf(Tobacco));   // the stockade held — nothing taken
    }
}
