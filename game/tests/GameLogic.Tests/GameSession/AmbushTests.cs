using System.Linq;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Terrain ambush (<c>86d3c9tp0</c>, FreeCol <c>Unit.canAmbush</c> + <c>SimpleCombatModel</c>): a native attacker
/// striking in the open from — or at a defender on — concealing forest/hills negates the defender's terrain cover
/// by gaining it as an offence bonus. The REF <c>ambushPenalty</c> side is deferred to the War of Independence (P6).
/// </summary>
public class AmbushTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xA3B05BUL;
    private const string Brave = "model.unit.brave";
    private const string FreeColonist = "model.unit.freeColonist";

    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    // ---- Ruleset parse ----

    [Fact]
    public void ForestsAndHills_AreAmbushTerrain_PlainsIsNot()
    {
        Assert.True(Classic.Terrain("model.tile.broadleafForest").AmbushTerrain);
        Assert.True(Classic.Terrain("model.tile.hills").AmbushTerrain);
        Assert.False(Classic.Terrain("model.tile.plains").AmbushTerrain);
        Assert.False(Classic.Terrain("model.tile.ocean").AmbushTerrain);
    }

    // ---- Combat model arithmetic ----

    [Fact]
    public void AttackPower_AppliesTheAmbushBonus()
    {
        // Brave (offence 1) ambushing a defender whose forest gives +50%: 1 × 1.5 attack × 1.5 ambush = 2.25.
        Assert.Equal(2.25, CombatModel.AttackPower(1, new AttackContext(AmbushBonus: 50)), 5);
    }

    // ---- End-to-end gating ----

    [Fact]
    public void ANativeRaider_AmbushesAColonistInForest_ButNotOneDugIn()
    {
        // A colonist standing in forest defends at 1 × 1.5 (cover) = 1.5. A native brave (offence 1) attacking it:
        //   not dug in → AMBUSH negates the cover (+50% offence): 1 ×1.5 ×1.5 = 2.25 vs 1.5 → win prob 0.6.
        //   dug in     → fortifying DENIES the ambush and adds +50% defence: 1.5 vs 1×1.5×1.5 = 2.25 → win prob 0.4.
        // A forced roll of 0.55 sits between: the brave overruns the colonist in the open but is repelled by a
        // dug-in one. Without the ambush bonus the open colonist's cover would hold (1.5 vs 1.5 → 0.5, a loss at 0.55).
        Assert.True(StageForestRaid(fortified: false).result is CombatResult.GreatWin or CombatResult.Win);
        Assert.True(StageForestRaid(fortified: true).result is CombatResult.Loss or CombatResult.GreatLoss);
    }

    private static (Game game, CombatResult result) StageForestRaid(bool fortified)
    {
        Game game = Game.New(Classic, Seed);
        string nation = game.NativeSettlements.First().NationTypeId;

        // A forest tile with a free land neighbour, clear of the player/settlements.
        Position forest = game.Map.AllPositions().First(p =>
            game.Map.InBounds(p) && game.Map.TerrainAt(p).AmbushTerrain && Free(game, p)
            && p.Neighbours().Any(n => Free(game, n)));
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), forest); // human defender on the cover
        if (fortified)
        {
            colonist.Orders = UnitOrders.Fortified; // dug in → can't be ambushed, and +50% defence
        }
        Position adj = forest.Neighbours().First(n => Free(game, n));
        Unit brave = game.SpawnUnit(Classic.Unit(Brave), adj, nation);
        brave.MovementLeft = 3; // normalise away the movement penalty for a clean forced roll

        return (game, game.Attack(brave, forest, new FixedRandom(0.55)));
    }

    private static bool Free(Game g, Position p) =>
        g.Map.InBounds(p) && !g.Map.TerrainAt(p).IsWater
        && g.ColonyAt(p) is null && g.NativeSettlementAt(p) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == p);
}
