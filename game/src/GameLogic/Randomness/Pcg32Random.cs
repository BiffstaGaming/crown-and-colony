namespace CrownAndColony.GameLogic.Randomness;

/// <summary>
/// PCG32 (Permuted Congruential Generator, XSH-RR variant) — the project's
/// deterministic random number generator (ADR-009).
///
/// We implement our own PRNG instead of seeding <see cref="System.Random"/>
/// because .NET does not guarantee a seeded <c>Random</c> produces the same
/// sequence across framework versions; this generator's output is fixed by
/// the algorithm itself, so saves, replays, scenario tests, and visual-test
/// fixtures stay stable forever.
///
/// Reference: M.E. O'Neill, "PCG: A Family of Simple Fast Space-Efficient
/// Statistically Good Algorithms for Random Number Generation" (pcg-random.org).
/// </summary>
public sealed class Pcg32Random : IGameRandom
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    /// <summary>Creates a generator from a seed. The same seed always yields the same sequence.</summary>
    /// <param name="seed">Seed value; any 64-bit value is valid.</param>
    /// <param name="streamId">
    /// Optional stream selector. Generators with the same seed but different streams
    /// produce independent sequences — use distinct streams for independent subsystems
    /// (e.g. map generation vs. combat) so extra draws in one cannot shift the other.
    /// </param>
    public Pcg32Random(ulong seed, ulong streamId = 0)
    {
        _increment = (streamId << 1) | 1UL;
        _state = 0UL;
        NextUInt32();
        _state += seed;
        NextUInt32();
    }

    private Pcg32Random(RandomState state)
    {
        _state = state.State;
        _increment = state.Increment;
    }

    /// <summary>Recreates a generator that resumes exactly where <paramref name="state"/> was captured.</summary>
    public static Pcg32Random FromState(RandomState state) => new(state);

    /// <inheritdoc />
    public RandomState SaveState() => new(_state, _increment);

    /// <inheritdoc />
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)NextBounded((uint)maxExclusive);
    }

    /// <inheritdoc />
    public int Next(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(minInclusive, maxExclusive);
        uint range = (uint)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + NextBounded(range));
    }

    /// <inheritdoc />
    public double NextDouble() =>
        // 53 bits of randomness, the full precision of a double's mantissa.
        ((((ulong)NextUInt32() << 21) ^ (NextUInt32() >> 6)) & ((1UL << 53) - 1)) * (1.0 / (1UL << 53));

    private uint NextUInt32()
    {
        ulong oldState = _state;
        _state = oldState * Multiplier + _increment;
        uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        int rot = (int)(oldState >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>Unbiased bounded generation via rejection sampling (Lemire/PCG reference method).</summary>
    private uint NextBounded(uint bound)
    {
        uint threshold = (uint)(-bound % bound);
        while (true)
        {
            uint r = NextUInt32();
            if (r >= threshold)
            {
                return r % bound;
            }
        }
    }
}
