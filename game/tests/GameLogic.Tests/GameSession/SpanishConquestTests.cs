using System.Linq;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The Spanish <c>conquest</c> nation-type advantage (`86d3df3...`): a <c>model.modifier.offenceAgainst</c> scoped
/// <c>isIndian</c> grants <b>+50% offence against natives</b>. Like the other nation-type advantages it folds via
/// <c>NationTypeModifiers</c> and only applies to a player holding that nation — the human defaults to no nation, so
/// a default game is unaffected. Tested by the win-probability lift on a native-settlement assault.
/// </summary>
public class SpanishConquestTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string OffenceAgainst = "model.modifier.offenceAgainst";
    private static readonly string Spanish = Classic.EuropeanNations
        .First(n => n.NationType.Modifiers.Any(m => m.TargetId == OffenceAgainst)).Id;

    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }

    private static bool Free(Game g, Position n) =>
        g.Map.InBounds(n) && !g.Map.TerrainAt(n).IsWater
        && g.NativeSettlementAt(n) is null && g.ColonyAt(n) is null
        && !g.Units.Any(u => u.IsOnMap && u.Position == n);

    [Fact]
    public void Spec_SpanishConquestType_CarriesOffenceAgainstNatives()
    {
        Assert.Contains(Classic.EuropeanNation(Spanish).NationType.Modifiers, m => m.TargetId == OffenceAgainst);
        // No other selectable nation carries it (it's the Spanish conquistador advantage).
        Assert.DoesNotContain(Classic.EuropeanNations.Where(n => n.Id != Spanish),
            n => n.NationType.Modifiers.Any(m => m.TargetId == OffenceAgainst));
    }

    [Fact]
    public void SpanishConquest_RaisesTheWinChanceAgainstANativeSettlement()
    {
        double spanish = WinThreshold(Spanish);
        double plain = WinThreshold(null);
        Assert.True(spanish > plain + 0.02, $"Spanish win threshold {spanish:F3} should clear plain {plain:F3} vs a native settlement");
    }

    /// <summary>The highest forced-RNG value at which the soldier still takes the settlement — i.e. its win probability (binary-searched; combat wins iff the draw is below p).</summary>
    private static double WinThreshold(string? nationId)
    {
        double lo = 0.0, hi = 1.0;
        for (int i = 0; i < 16; i++)
        {
            double mid = (lo + hi) / 2;
            if (AssaultWins(nationId, mid))
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }
        return lo;
    }

    private static bool AssaultWins(string? nationId, double rng)
    {
        Game game = WithHumanNation(nationId);
        NativeSettlement settlement = game.NativeSettlements.First(s => s.Position.Neighbours().Any(n => Free(game, n)));
        Position adj = settlement.Position.Neighbours().First(n => Free(game, n));
        Unit soldier = game.SpawnUnit(Classic.Unit("model.unit.freeColonist"), adj); // human-owned
        soldier.RoleId = "model.role.soldier"; // offence 2 → a mid-range win chance the +50% can visibly shift
        soldier.RoleCount = 1;

        CombatResult result = game.AttackSettlement(soldier, settlement.Position, new FixedRandom(rng));
        return result is CombatResult.Win or CombatResult.GreatWin;
    }

    private static Game WithHumanNation(string? nationId)
    {
        SaveGame save = SaveGame.From(Game.New(Classic, seed: 7));
        return (save with
        {
            Players = save.Players!.Select(p => p.IsHuman ? p with { NationId = nationId } : p).ToList(),
        }).Restore(Classic);
    }
}
