using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// La Salle (exploration father): grants a free <c>model.building.stockade</c> to every colony at or above the
/// stockade's required population (3) — FreeCol <c>&lt;event id="model.event.freeBuilding"&gt;</c> / <c>csFreeBuilding</c>,
/// applied each turn after election and growth. The granted stockade carries the colony fortification defence
/// bonus (see <see cref="ColonyDefenceBonusTests"/>). Per-owner, dormant unless elected, RNG-free (stream 0
/// untouched), and riding the persisted Congress + building list (no save-format change).
/// </summary>
public class LaSalleTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0x1A5A11EUL;
    private const string LaSalle = "model.foundingFather.laSalle";
    private const string Stockade = "model.building.stockade";

    /// <summary>A fresh game, optionally with La Salle in the human's Congress (injected via the save layer).</summary>
    private static Game LaSalleGame(bool elected)
    {
        Game game = Game.New(Classic, Seed);
        if (!elected)
        {
            return game;
        }
        SaveGame save = SaveGame.From(game);
        return (save with
        {
            Players = save.Players!.Select(p => p.IsHuman ? p with { Congress = new[] { LaSalle } } : p).ToList(),
        }).Restore(Classic);
    }

    /// <summary>Founds the human's colony and forces it to a given population, with a food buffer so one EndTurn won't starve it.</summary>
    private static Colony FoundColonyOfSize(Game game, int population)
    {
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        colony.Population = population;
        colony.AddGoods(Colony.FoodId, 100); // buffer (< the 200 growth threshold) so a single turn neither starves nor grows it
        return colony;
    }

    [Fact]
    public void LaSalle_FreeBuildings_PinnedToSpec()
    {
        Assert.Contains(Stockade, Classic.Father(LaSalle).FreeBuildings);
    }

    [Fact]
    public void LaSalle_GrantsAFreeStockade_ToAColonyAtTheRequiredPopulation()
    {
        Game game = LaSalleGame(elected: true);
        Colony colony = FoundColonyOfSize(game, population: 3);
        Assert.False(colony.HasBuilding(Stockade)); // not granted until the turn resolves

        game.EndTurn();

        Assert.True(colony.HasBuilding(Stockade));
        Assert.Equal(100, game.ColonyDefenceBonus(colony)); // and the stockade actually fortifies the colony
    }

    [Fact]
    public void LaSalle_DoesNotGrantBelowTheRequiredPopulation()
    {
        Game game = LaSalleGame(elected: true);
        Colony colony = FoundColonyOfSize(game, population: 2); // stockade needs population 3

        game.EndTurn();

        Assert.False(colony.HasBuilding(Stockade));
    }

    [Fact]
    public void WithoutLaSalle_NoFreeStockade()
    {
        Game game = LaSalleGame(elected: false);
        Colony colony = FoundColonyOfSize(game, population: 3);

        game.EndTurn();

        Assert.False(colony.HasBuilding(Stockade)); // a population-3 colony gets nothing without the father
    }

    [Fact]
    public void LaSalle_GrantIsRngFree_LeavingTheHumansStream0Untouched()
    {
        // Same seed + same staged colony; only one game has La Salle. The granted stockade diverges the save, but
        // the grant draws no RNG, so the human's stream 0 (and scoped state) stays byte-identical (ADR-009).
        Game plain = LaSalleGame(elected: false);
        Colony plainColony = FoundColonyOfSize(plain, population: 3);
        Game father = LaSalleGame(elected: true);
        Colony fatherColony = FoundColonyOfSize(father, population: 3);

        plain.EndTurn();
        father.EndTurn();

        Assert.False(plainColony.HasBuilding(Stockade));                              // no father → no stockade…
        Assert.True(fatherColony.HasBuilding(Stockade));                              // …father → a stockade (genuine divergence)
        Assert.NotEqual(SaveGame.From(plain).ToJson(), SaveGame.From(father).ToJson());
        Assert.Equal(plain.RandomState, father.RandomState);                          // …yet stream 0 is untouched
        Assert.Equal(plain.HumanPlayer.Gold, father.HumanPlayer.Gold);
    }
}
