using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The value-weighted AI founding-father choice (<c>86d3e49ej</c>): a foreign power banks toward the offered father
/// most worth having this age, faithful to FreeCol <c>EuropeanAIPlayer.selectFoundingFather</c> — highest
/// <see cref="FoundingFather.WeightForAge"/>, with a custom-house grantor (Peter Stuyvesant) chosen outright ahead of
/// any weight, ties broken by ordinal id. The pick is RNG-free: it no longer draws the power's stream (the offer SET is
/// still seeded in <c>GenerateOffers</c>; only this SELECT is now deterministic). The human player's pick is untouched.
/// </summary>
public class AiFoundingFatherChoiceTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    private const string Minuit = "model.foundingFather.peterMinuit";          // trade,       weight1 = 9
    private const string Stuyvesant = "model.foundingFather.peterStuyvesant";  // trade,       weight1 = 2, BUILD_CUSTOM_HOUSE
    private const string PaulRevere = "model.foundingFather.paulRevere";       // military,    weight1 = 10
    private const string HenryHudson = "model.foundingFather.henryHudson";     // exploration, weight1 = 10

    /// <summary>
    /// A turn-1 game (age 1) with the human (stream 0) plus one foreign colonial power (id 1) that has no colony, no
    /// gold and an empty recruit dock — so the only thing its economy does on End Turn is the father pick. The power's
    /// offered fathers and starting liberty are caller-supplied; <paramref name="liberty"/> stays low so the pick is
    /// observed without an election firing first.
    /// </summary>
    private static (Game Game, Player Power) ForeignPowerWithOffers(IReadOnlyList<string> offered, int liberty = 0)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 1,
            MapHeight = 1,
            Terrain = ["model.tile.plains"],
            Units = [],
            Explored = [0],
            Players =
            [
                new SavedPlayer(0, NationId: null, IsHuman: true, PlayerType: (int)PlayerType.Colonial),
                new SavedPlayer(1, "model.nation.dutch", IsHuman: false, PlayerType: (int)PlayerType.Colonial,
                    Liberty: liberty, OfferedFathers: offered, RecruitDock: []),
            ],
        };
        Game game = save.Restore(Classic);
        Player power = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial);
        return (game, power);
    }

    [Fact]
    public void Picks_TheHighestAgeWeightFather_AmongOffers()
    {
        // Minuit (weight1 9) beats Henry Hudson (weight1 10)? No — Hudson is higher, so Hudson is chosen. Pair a
        // low-weight trade father with a high-weight exploration one so weight, not category order, decides.
        (Game game, Player power) = ForeignPowerWithOffers([Minuit, HenryHudson]);

        game.EndTurn();

        Assert.Equal(HenryHudson, power.CurrentFather); // weight1 10 > 9
    }

    [Fact]
    public void CustomHouseGrantor_IsChosenOutright_OverAHigherWeightFather()
    {
        // Stuyvesant (weight1 2, BUILD_CUSTOM_HOUSE) must win over Paul Revere (weight1 10) — FreeCol always grabs the
        // custom house, ahead of any weight, because building it early eases the AI's transport burden.
        (Game game, Player power) = ForeignPowerWithOffers([PaulRevere, Stuyvesant]);
        Assert.True(Classic.Father(PaulRevere).WeightForAge(1) > Classic.Father(Stuyvesant).WeightForAge(1));

        game.EndTurn();

        Assert.Equal(Stuyvesant, power.CurrentFather);
    }

    [Fact]
    public void TieOnWeight_BreaksByOrdinalFatherId()
    {
        // Paul Revere and Henry Hudson both have weight1 10. The lower ordinal id wins — "henryHudson" < "paulRevere".
        Assert.Equal(Classic.Father(PaulRevere).WeightForAge(1), Classic.Father(HenryHudson).WeightForAge(1));
        (Game game, Player power) = ForeignPowerWithOffers([PaulRevere, HenryHudson]);

        game.EndTurn();

        Assert.True(string.CompareOrdinal(HenryHudson, PaulRevere) < 0); // the tie-break order we rely on
        Assert.Equal(HenryHudson, power.CurrentFather); // ordinal: model.foundingFather.henryHudson < ...paulRevere
    }

    [Fact]
    public void TheFatherPick_DrawsNoRandomness_FromThePowersOwnStream()
    {
        // The select no longer calls RandomFor (ADR-009): with no colony, no gold and an empty dock, the father pick is
        // the only economy step that runs, so the power's own RNG stream must be byte-identical across End Turn.
        (Game game, Player power) = ForeignPowerWithOffers([Minuit, HenryHudson]);
        ulong before = power.Rng!.SaveState().State;

        game.EndTurn();

        Assert.Equal(HenryHudson, power.CurrentFather);            // it still made the pick
        Assert.Equal(before, power.Rng!.SaveState().State);        // …without drawing its stream
    }

    [Fact]
    public void TheChoiceIsDeterministic_AcrossTwoSameSeedRuns()
    {
        // Two identical setups make the identical pick (reproducible without RNG).
        (Game a, Player pa) = ForeignPowerWithOffers([Minuit, Stuyvesant, PaulRevere]);
        (Game b, Player pb) = ForeignPowerWithOffers([Minuit, Stuyvesant, PaulRevere]);

        a.EndTurn();
        b.EndTurn();

        Assert.Equal(Stuyvesant, pa.CurrentFather); // custom-house override
        Assert.Equal(pa.CurrentFather, pb.CurrentFather);
    }

    [Fact]
    public void HumanFatherPick_StaysManual_UnaffectedByTheAiSelection()
    {
        // The AI-only change must not touch the human path: a fresh game offers the human fathers but picks none for
        // them — ChooseFather is the human's manual route and remains the only thing that sets the human's father.
        var game = Game.New(Classic, seed: 7);
        Assert.NotEmpty(game.OfferedFathers);
        Assert.Null(game.CurrentFather); // the human is never auto-assigned a father

        game.ChooseFather(game.OfferedFathers[0]);
        Assert.Equal(game.OfferedFathers[0], game.CurrentFather);
    }
}
