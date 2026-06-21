using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Combat;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Combat;

/// <summary>
/// Verifies the engine-free combat-result → animation-kind mapping (<see cref="CombatAnimationMap"/>). The rendered
/// lunge/flash animation can't be exercised headlessly, but the trigger mapping is plain data: every
/// <see cref="CombatResult"/> must resolve to exactly one <see cref="CombatAnimationKind"/>, and wins/losses/evades
/// must map to the right visual flavour. This is the unit-level guard the minimal-testing bar asks for — it catches a
/// missing or mis-wired animation cue without booting Godot.
/// </summary>
public class CombatAnimationMapTests
{
    [Fact]
    public void EveryCombatResult_ResolvesToAKind()
    {
        foreach (CombatResult result in Enum.GetValues<CombatResult>())
        {
            Assert.True(CombatAnimationMap.TryGetKind(result, out _), $"No animation kind mapped for {result}.");
        }
    }

    [Fact]
    public void Map_CoversEveryResult_NoExtras()
    {
        var mapped = CombatAnimationMap.All.Keys.ToHashSet();
        var declared = Enum.GetValues<CombatResult>().ToHashSet();
        Assert.Equal(declared, mapped);
    }

    [Theory]
    [InlineData(CombatResult.GreatWin, CombatAnimationKind.AttackerHit)]
    [InlineData(CombatResult.Win, CombatAnimationKind.AttackerHit)]
    [InlineData(CombatResult.Loss, CombatAnimationKind.AttackerRepelled)]
    [InlineData(CombatResult.GreatLoss, CombatAnimationKind.AttackerRepelled)]
    [InlineData(CombatResult.Evade, CombatAnimationKind.Evaded)]
    public void Result_MapsToExpectedKind(CombatResult result, CombatAnimationKind expected)
    {
        Assert.Equal(expected, CombatAnimationMap.KindFor(result));
    }

    [Fact]
    public void Wins_AreHits_LossesAreRepelled()
    {
        // Both win grades read as a hit; both loss grades read as a repel — the procedural animation does not
        // distinguish margin (great vs normal), only attacker success/failure (and the naval evade feint).
        Assert.Equal(CombatAnimationMap.KindFor(CombatResult.GreatWin), CombatAnimationMap.KindFor(CombatResult.Win));
        Assert.Equal(CombatAnimationMap.KindFor(CombatResult.GreatLoss), CombatAnimationMap.KindFor(CombatResult.Loss));
        Assert.NotEqual(CombatAnimationMap.KindFor(CombatResult.Win), CombatAnimationMap.KindFor(CombatResult.Loss));
    }

    [Fact]
    public void KindFor_MatchesAllDictionary()
    {
        foreach (KeyValuePair<CombatResult, CombatAnimationKind> entry in CombatAnimationMap.All)
        {
            Assert.Equal(entry.Value, CombatAnimationMap.KindFor(entry.Key));
        }
    }
}
