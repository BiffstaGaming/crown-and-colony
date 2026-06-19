namespace CrownAndColony.GameLogic.Randomness;

/// <summary>
/// Weighted random selection (FreeCol <c>RandomUtils.getWeightedRandom</c>): pick one value from a list of
/// <c>(weight, value)</c> pairs with probability proportional to weight. Draws exactly one number from the
/// supplied <see cref="IGameRandom"/> (ADR-009 determinism), mirroring the existing weighted-resource pick in
/// <c>MapGenerator</c>. Used by the monarch action chooser, mercenary/support rolls, and REF composition.
/// </summary>
public static class RandomChoice
{
    /// <summary>
    /// Selects one value, weighted. <paramref name="choices"/> must contain at least one positive-weight entry.
    /// </summary>
    /// <exception cref="ArgumentException">No positive-weight choices were supplied.</exception>
    public static T WeightedRandom<T>(IGameRandom random, IReadOnlyList<(int Weight, T Value)> choices)
    {
        int total = 0;
        foreach ((int weight, _) in choices)
        {
            if (weight > 0)
            {
                total += weight;
            }
        }
        if (total <= 0)
        {
            throw new ArgumentException("No positive-weight choices to pick from.", nameof(choices));
        }

        int roll = random.Next(total);
        foreach ((int weight, T value) in choices)
        {
            if (weight <= 0)
            {
                continue;
            }
            roll -= weight;
            if (roll < 0)
            {
                return value;
            }
        }
        return choices[^1].Value; // unreachable (roll < total), but keeps the compiler happy
    }
}
