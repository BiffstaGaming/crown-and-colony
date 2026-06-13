using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// The variant / game-mode selection layer (ADR-018) and the transposability
/// contract: the engine plays by whatever the selected variant's ruleset defines,
/// so a different ruleset = a different game with no code change.
/// </summary>
public class GameVariantTests
{
    [Fact]
    public void Registry_ContainsClassic_AsDefault()
    {
        Assert.Contains(GameVariants.All, v => v.Id == "classic");
        Assert.Equal("classic", GameVariants.Default.Id);
        Assert.Same(GameVariants.ClassicAmerica, GameVariants.Default);
        Assert.False(string.IsNullOrWhiteSpace(GameVariants.ClassicAmerica.DisplayName));
    }

    [Fact]
    public void ById_ResolvesKnown_AndThrowsOnUnknown()
    {
        Assert.Same(GameVariants.ClassicAmerica, GameVariants.ById("classic"));
        Assert.Throws<KeyNotFoundException>(() => GameVariants.ById("atlantis"));
    }

    [Fact]
    public void Resolve_NullFallsBackToDefault()
    {
        // Legacy (pre-v15) saves carry no variant id.
        Assert.Same(GameVariants.Default, GameVariants.Resolve(null));
        Assert.Same(GameVariants.ClassicAmerica, GameVariants.Resolve("classic"));
    }

    [Fact]
    public void ClassicVariant_LoadsTheAmericanWorld()
    {
        Ruleset classic = GameVariants.ClassicAmerica.LoadRuleset();

        // The faithful Colonial-America data: many Founding Fathers and the eight
        // indigenous nations all come from this variant's spec.
        Assert.True(classic.FoundingFathers.Count >= 20, $"only {classic.FoundingFathers.Count} fathers");
        Assert.Equal(8, classic.NativeNationTypes.Count);
        Assert.Contains(classic.NativeNationTypes, n => n.Id == "model.nationType.apache");
    }

    [Fact]
    public void DifferentRuleset_YieldsDifferentFathersAndNations()
    {
        // The transposability proof: a *different* spec — one custom Founding Father
        // with its own perk, no native nations — produces a different game purely from
        // data. The engine never hard-codes "American" content.
        Ruleset classic = GameVariants.ClassicAmerica.LoadRuleset();
        Ruleset other = Ruleset.Load(Spec(
            "<founding-fathers>" +
            "  <founding-father id=\"model.foundingFather.captainCook\" type=\"exploration\"" +
            "                   weight1=\"5\" weight2=\"5\" weight3=\"5\">" +
            "    <modifier id=\"model.goods.furs\" type=\"percentage\" value=\"100\"/>" +
            "  </founding-father>" +
            "</founding-fathers>"));

        // The custom variant has exactly its own father, with its own perk parsed.
        FoundingFather cook = Assert.Single(other.FoundingFathers);
        Assert.Equal("model.foundingFather.captainCook", cook.Id);
        Assert.Contains(cook.Modifiers, m => m is { TargetId: "model.goods.furs", Value: 100 });
        Assert.Empty(other.NativeNationTypes);

        // ...and it shares no Founding Fathers with the classic world.
        Assert.DoesNotContain(classic.FoundingFathers, f => f.Id == "model.foundingFather.captainCook");
        Assert.DoesNotContain(other.FoundingFathers, f => classic.FoundingFathers.Any(c => c.Id == f.Id));
    }

    [Fact]
    public void Save_RecordsVariant_AndLegacySavesFallBack()
    {
        Game game = Game.New(GameVariants.ClassicAmerica.LoadRuleset(), 0xC0FFEEUL);

        SaveGame save = SaveGame.From(game, GameVariants.ClassicAmerica.Id);
        Assert.Equal("classic", save.Variant);
        Assert.Equal("classic", SaveGame.FromJson(save.ToJson()).Variant); // survives JSON round-trip

        // From() with no variant defaults to the standard one (keeps existing call sites working).
        Assert.Equal("classic", SaveGame.From(game).Variant);

        // A pre-v15 save carries no variant → null → resolves to the default.
        SaveGame legacy = SaveGame.FromJson(
            "{\"Version\":14,\"Turn\":1,\"RandomStateValue\":1,\"RandomIncrement\":1," +
            "\"MapWidth\":1,\"MapHeight\":1,\"Terrain\":[\"model.tile.plains\"],\"Units\":[]}");
        Assert.Null(legacy.Variant);
        Assert.Same(GameVariants.Default, GameVariants.Resolve(legacy.Variant));
    }

    /// <summary>Wraps a fragment in a minimal valid spec (tile + unit types are required to load).</summary>
    private static Stream Spec(string body)
    {
        var ms = new MemoryStream();
        using (var writer = new StreamWriter(ms, leaveOpen: true))
        {
            writer.Write(
                "<freecol-specification>" +
                "<tile-types><tile-type id=\"model.tile.plains\" basic-move-cost=\"3\" basic-work-turns=\"3\"/></tile-types>" +
                "<unit-types><unit-type id=\"model.unit.freeColonist\"/></unit-types>" +
                body +
                "</freecol-specification>");
        }
        ms.Position = 0;
        return ms;
    }
}
