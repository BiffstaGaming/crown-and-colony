namespace CrownAndColony.GameLogic.World;

/// <summary>
/// The tunable map-generation dials a player may set at New Game — FreeCol's <c>mapGeneratorOptions</c> /
/// <c>model.option.*Number</c> counts plus its <c>temperature</c>/<c>humidity</c> climate bands — promoted off the
/// hardcoded <see cref="MapGenerator"/> constants so the New-Game dialog can offer them (86d3fq18b / 86d3fq13u).
/// "Data not code" like <see cref="WorldSizeOptions"/> / <see cref="Specification.DifficultyOptions"/>: each field
/// carries its classic-spec default, so a game that omits the options (and every existing host/test) gets the
/// historical generator, byte-identical (ADR-009).
/// </summary>
/// <remarks>
/// <para>
/// Pure and immutable: no state, no randomness. Threaded into <see cref="MapGenerator.Generate"/> (and from there into
/// the range/river/forest/bonus passes) via <see cref="GameSession.Game.New"/>. The <see cref="Classic"/> singleton
/// holds the verbatim historical values, so passing it (or omitting the argument) draws the <em>same</em> RNG sequence
/// the old hardcoded path drew — a default map is byte-identical. Changing a count changes how many range/river/forest/
/// bonus draws happen, which deterministically changes the map for a given seed; the same seed + same options always
/// reproduce the same map.
/// </para>
/// <para>
/// The two climate dials (<see cref="TemperatureBias"/> / <see cref="HumidityBias"/>) are RNG-free offsets applied
/// after the latitude temperature and the smoothed humidity noise are computed, so they never shift the draw sequence:
/// a default-bias (0/0) map is byte-identical, and a warmer/wetter map re-picks terrain from the shifted climate
/// without any extra randomness (FreeCol's <c>temperature</c>/<c>humidity</c> map-generator options likewise bias the
/// climate envelope, not the noise).
/// </para>
/// </remarks>
/// <param name="MountainNumber">
/// "One elevation tile per this many land tiles" — FreeCol <c>model.option.mountainNumber</c> (classic <b>10</b>;
/// higher = fewer mountains/hills). Splits half-and-half between walked ranges and a random sprinkle.
/// </param>
/// <param name="RiverNumber">
/// River budget as a percentage of the river-allowed land tiles — FreeCol <c>model.option.riverNumber</c> (classic
/// <b>15</b>; higher = more river tiles). The river pass stops once it has laid this fraction.
/// </param>
/// <param name="ForestChance">
/// Fraction (0–1) of climate-matching land tiles that come up forested — the share of FreeCol's
/// <c>model.option.forestNumber</c> the generator targets (classic <b>0.45</b>; higher = more forest).
/// </param>
/// <param name="LandBonusChance">
/// Chance (0–1) a land tile hosts a bonus resource — FreeCol <c>model.option.bonusNumber</c> (classic <b>0.10</b>;
/// higher = more bonus resources). Water-resource odds are unchanged (their adjacency formula is faithful to FreeCol).
/// </param>
/// <param name="TemperatureBias">
/// A degrees-Celsius offset added to every tile's latitude temperature before its climate terrain is picked — a
/// faithful stand-in for FreeCol's <c>model.option.temperature</c> band (cold..hot). <b>0</b> = the classic
/// latitude-only climate; positive = warmer (more tropical/desert), negative = colder (more tundra/marsh). The result
/// is clamped to the spec's −20..40 envelope.
/// </param>
/// <param name="HumidityBias">
/// A 0–100 offset added to every tile's smoothed humidity before its climate terrain is picked — a faithful stand-in
/// for FreeCol's <c>model.option.humidity</c> band (veryDry..veryWet). <b>0</b> = the classic noise-only humidity;
/// positive = wetter, negative = drier. The result is clamped to the 0..100 envelope.
/// </param>
public sealed record MapGenerationOptions(
    int MountainNumber,
    int RiverNumber,
    double ForestChance,
    double LandBonusChance,
    int TemperatureBias,
    int HumidityBias)
{
    /// <summary>
    /// The classic-spec defaults — the verbatim values the historical <see cref="MapGenerator"/> constants held, so a
    /// game that passes this (or omits the options) reproduces the historical map byte-for-byte (ADR-009). The source of
    /// truth for the default new game's map-gen counts and the no-bias climate.
    /// </summary>
    public static readonly MapGenerationOptions Classic = new(
        MountainNumber: 10,
        RiverNumber: 15,
        ForestChance: 0.45,
        LandBonusChance: 0.10,
        TemperatureBias: 0,
        HumidityBias: 0);
}
