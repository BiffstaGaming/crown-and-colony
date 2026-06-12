using CrownAndColony.GameLogic.Randomness;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Randomness;

public class Pcg32RandomTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = new Pcg32Random(seed: 42);
        var b = new Pcg32Random(seed: 42);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.Next(1_000_000), b.Next(1_000_000));
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var a = new Pcg32Random(seed: 42);
        var b = new Pcg32Random(seed: 43);

        var aValues = Enumerable.Range(0, 100).Select(_ => a.Next(1_000_000)).ToList();
        var bValues = Enumerable.Range(0, 100).Select(_ => b.Next(1_000_000)).ToList();

        Assert.NotEqual(aValues, bValues);
    }

    [Fact]
    public void DifferentStreams_SameSeed_ProduceIndependentSequences()
    {
        var a = new Pcg32Random(seed: 42, streamId: 1);
        var b = new Pcg32Random(seed: 42, streamId: 2);

        var aValues = Enumerable.Range(0, 100).Select(_ => a.Next(1_000_000)).ToList();
        var bValues = Enumerable.Range(0, 100).Select(_ => b.Next(1_000_000)).ToList();

        Assert.NotEqual(aValues, bValues);
    }

    [Fact]
    public void SaveState_ThenResume_ContinuesIdenticalSequence()
    {
        var original = new Pcg32Random(seed: 7);

        // Burn some draws, snapshot mid-sequence (as a save game would).
        for (int i = 0; i < 50; i++)
        {
            original.Next(100);
        }

        RandomState snapshot = original.SaveState();
        var resumed = Pcg32Random.FromState(snapshot);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(original.Next(1_000_000), resumed.Next(1_000_000));
        }
    }

    [Fact]
    public void KnownSeed_ProducesPinnedSequence()
    {
        // Golden values: pin the exact output of seed 1 so any change to the
        // algorithm (or a platform/runtime difference) fails loudly. These
        // values define our determinism contract — never update them without
        // an ADR, because doing so invalidates every recorded game and fixture.
        var rng = new Pcg32Random(seed: 1);

        int[] actual = Enumerable.Range(0, 8).Select(_ => rng.Next(1_000_000)).ToArray();

        // Captured 2026-06-13 from the first verified run (.NET 10, win-x64).
        int[] expected = [398737, 903413, 275701, 195274, 30198, 257974, 798104, 124240];
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(1_000_000)]
    public void Next_StaysWithinBounds(int maxExclusive)
    {
        var rng = new Pcg32Random(seed: 99);

        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.Next(maxExclusive);
            Assert.InRange(value, 0, maxExclusive - 1);
        }
    }

    [Fact]
    public void Next_WithRange_StaysWithinBounds()
    {
        var rng = new Pcg32Random(seed: 99);

        for (int i = 0; i < 10_000; i++)
        {
            int value = rng.Next(-50, 50);
            Assert.InRange(value, -50, 49);
        }
    }

    [Fact]
    public void NextDouble_StaysWithinUnitInterval()
    {
        var rng = new Pcg32Random(seed: 99);

        for (int i = 0; i < 10_000; i++)
        {
            double value = rng.NextDouble();
            Assert.True(value is >= 0.0 and < 1.0, $"NextDouble returned {value}");
        }
    }

    [Fact]
    public void Next_RejectsInvalidBounds()
    {
        var rng = new Pcg32Random(seed: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(-5));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(10, 5));
    }

    [Fact]
    public void Next_SmallBound_IsReasonablyUniform()
    {
        // Sanity check against gross bias (e.g. a broken rejection threshold):
        // 60,000 draws over 6 buckets — each bucket should land near 10,000.
        var rng = new Pcg32Random(seed: 123);
        var buckets = new int[6];

        for (int i = 0; i < 60_000; i++)
        {
            buckets[rng.Next(6)]++;
        }

        foreach (int count in buckets)
        {
            Assert.InRange(count, 9_500, 10_500);
        }
    }
}
