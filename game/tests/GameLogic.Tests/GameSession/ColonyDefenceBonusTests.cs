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
    private const string Artillery = "model.unit.artillery"; // offence 7, movement 3 (no movement penalty)
    private const string Brave = "model.unit.brave";
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
        // artillery 7 ×1.5 = 10.5 attack vs the colony's lone colonist (defence 1). Unfortified win prob ≈ 0.913;
        // a stockade doubles the defence (→2) so win prob ≈ 0.84. A roll of 0.875 sits in that gap.
        var (open, openColony, openAtk, _, humanId) = StageRival(stockade: false);
        var (walled, walledColony, walledAtk, foreignId, _) = StageRival(stockade: true);

        open.AttackColony(openAtk, openColony.Position, new FixedRandom(0.875));
        walled.AttackColony(walledAtk, walledColony.Position, new FixedRandom(0.875));

        Assert.Equal(humanId, open.Colonies.First(c => c.Id == openColony.Id).OwnerId);     // captured
        Assert.Equal(foreignId, walled.Colonies.First(c => c.Id == walledColony.Id).OwnerId); // the stockade held
    }

    /// <summary>A human colony with a free-colonist garrison on its tile and a foreign artillery beside it (at war); optionally fortified.</summary>
    private static (Game game, Colony colony, Unit attacker) StageGarrison(bool fortress)
    {
        Game game = Game.New(Classic, Seed);
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        if (fortress)
        {
            colony.AddBuilding(Fortress);
        }
        game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position); // human garrison on the colony tile (defence 1)

        Position adj = colony.Position.Neighbours().First(n => FreeLand(game, n));
        Unit attacker = game.SpawnUnit(Classic.Unit(Artillery), adj);
        attacker.OwnerId = foreignId;
        game.SetStance(foreignId, game.HumanPlayer.PlayerId, Stance.War);
        return (game, colony, attacker);
    }

    [Fact]
    public void AFortress_HelpsAGarrisonRepelAnAttackOnTheColonyTile()
    {
        // The main Attack path with a garrison on the colony tile. Defending in a colony suppresses the tile
        // terrain bonus (FreeCol), so the garrison's defence is exactly base (1) × fortification. Foreign artillery
        // in the open ≈ 7 ×1.5 ×0.25 = 2.625 attack: no fortress → def 1, win prob ≈ 0.72 (attacker wins); fortress
        // → def 3 (+200%), win prob ≈ 0.47 (repelled). A roll of 0.5 sits in that gap, terrain-independent.
        var (open, openColony, openAtk) = StageGarrison(fortress: false);
        var (walled, _, walledAtk) = StageGarrison(fortress: true);

        CombatResult openResult = open.Attack(openAtk, openColony.Position, new FixedRandom(0.5));
        CombatResult walledResult = walled.Attack(walledAtk, walled.Colonies.First().Position, new FixedRandom(0.5));

        Assert.True(openResult is CombatResult.GreatWin or CombatResult.Win);     // unfortified → attacker wins
        Assert.True(walledResult is CombatResult.Loss or CombatResult.GreatLoss); // fortress → garrison repels it
    }

    /// <summary>A human colony stocked with tobacco and a brave beside it; optionally fortified.</summary>
    private static (Game game, Colony colony, Unit brave) StagePillage(bool stockade)
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
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
