using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Human defeat (`86d3bx04e`): the human is wiped out only when it has no colonies AND no units anywhere — a
/// conservative subset of FreeCol `checkForDeath` (never a false positive). Now reachable because a foreign power
/// can capture the human's last colony (1c-3f) and tribes/rivals can take its last units. `IsHumanDefeated` is a
/// pure computed property (no save-format impact).
/// </summary>
public class HumanDefeatTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xDEAD11FEUL;

    [Fact]
    public void AFreshGame_IsNotDefeated()
    {
        Game game = Game.New(Classic, Seed); // the human starts with a colonist (no colony) → has a unit → alive
        Assert.False(game.IsHumanDefeated);
    }

    [Fact]
    public void WithAColonyButNoUnits_IsNotDefeated()
    {
        Game game = Game.New(Classic, Seed);
        game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony)); // consumes the only unit
        Assert.DoesNotContain(game.PlayerUnits, _ => true); // no human units left…
        Assert.False(game.IsHumanDefeated);                 // …but the colony keeps the human alive
    }

    [Fact]
    public void WithNoColoniesAndNoUnits_IsDefeated()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        int rivalId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;

        colony.OwnerId = rivalId; // the last colony is captured — now 0 human colonies, 0 human units

        Assert.True(game.IsHumanDefeated);
    }

    [Fact]
    public void Defeat_SurvivesASaveRoundTrip()
    {
        Game game = Game.New(Classic, Seed);
        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap && u.Type.CanFoundColony));
        colony.OwnerId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        Assert.True(game.IsHumanDefeated);

        Game restored = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);
        Assert.True(restored.IsHumanDefeated); // computed from the (round-tripped) colonies/units, no stored flag
    }
}
