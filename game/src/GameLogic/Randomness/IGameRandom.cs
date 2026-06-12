namespace CrownAndColony.GameLogic.Randomness;

/// <summary>
/// The only source of randomness permitted in game logic (ADR-009).
/// Implementations must be deterministic: the same seed and call sequence
/// always produce the same results, on every platform and .NET version.
/// </summary>
public interface IGameRandom
{
    /// <summary>Returns a uniformly distributed integer in [0, maxExclusive).</summary>
    int Next(int maxExclusive);

    /// <summary>Returns a uniformly distributed integer in [minInclusive, maxExclusive).</summary>
    int Next(int minInclusive, int maxExclusive);

    /// <summary>Returns a uniformly distributed double in [0, 1).</summary>
    double NextDouble();

    /// <summary>
    /// Captures the generator's complete internal state so a game can be saved
    /// and resumed with an identical future random sequence.
    /// </summary>
    RandomState SaveState();
}

/// <summary>
/// Serializable snapshot of a generator's internal state.
/// Restoring it (see <see cref="Pcg32Random.FromState"/>) resumes the exact sequence.
/// </summary>
/// <param name="State">The generator's current internal state word.</param>
/// <param name="Increment">The generator's stream-selection constant.</param>
public readonly record struct RandomState(ulong State, ulong Increment);
