using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Francis Drake (military father): +50% offence AND +50% defence scoped to <c>model.unit.privateer</c>
/// (FreeCol <c>specification.xml</c> francisDrake, <c>model.modifier.offence/defence</c> index 50, percentage 50,
/// <c>&lt;scope type="model.unit.privateer"/&gt;</c>). The first <em>scoped</em> father combat modifier: it folds
/// the owner's Congress into <see cref="Game.OffenceBase"/>/<see cref="Game.DefenceBase"/> only for a privateer,
/// per-owner, dormant unless elected, riding the persisted Congress (no save-format change, no RNG).
/// </summary>
public class DrakeTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Drake = "model.foundingFather.francisDrake";
    private const string Privateer = "model.unit.privateer"; // 8/8
    private const string Frigate = "model.unit.frigate";     // 16/16, no piracy/Drake scope
    private static readonly Position Origin = new(0, 0);

    /// <summary>A fresh game, optionally with Drake in the human's Congress.</summary>
    private static Game DrakeGame(bool drake)
    {
        Game game = Game.New(Classic, Seed);
        if (!drake)
        {
            return game;
        }
        SaveGame save = SaveGame.From(game);
        return (save with
        {
            Players = save.Players!.Select(p => p.IsHuman ? p with { Congress = new[] { Drake } } : p).ToList(),
        }).Restore(Classic);
    }

    [Fact]
    public void Drake_GivesAPrivateer_FiftyPercentOffenceAndDefence()
    {
        var privateer = new Unit(1, Classic.Unit(Privateer), Origin); // human-owned (OwnerId 0)

        Assert.Equal(DrakeGame(false).OffenceBase(privateer) * 1.5, DrakeGame(true).OffenceBase(privateer), 5);
        Assert.Equal(DrakeGame(false).DefenceBase(privateer) * 1.5, DrakeGame(true).DefenceBase(privateer), 5);
    }

    [Fact]
    public void Drake_DoesNotBoostNonPrivateers()
    {
        // The bonus is scoped to the privateer; a frigate (also a captureGoods warship) gets nothing.
        var frigate = new Unit(1, Classic.Unit(Frigate), Origin);
        Assert.Equal(DrakeGame(false).OffenceBase(frigate), DrakeGame(true).OffenceBase(frigate), 5);
        Assert.Equal(DrakeGame(false).DefenceBase(frigate), DrakeGame(true).DefenceBase(frigate), 5);
    }

    [Fact]
    public void Drake_OnlyBoostsItsOwnersPrivateers()
    {
        // The fold is per-owner: a foreign power's Drake boosts ITS privateer; the human's is untouched.
        Game game = Game.New(Classic, Seed);
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        SaveGame save = SaveGame.From(game);
        Game loaded = (save with
        {
            Players = save.Players!.Select(p => p.PlayerId == foreignId ? p with { Congress = new[] { Drake } } : p).ToList(),
        }).Restore(Classic);

        var humanPrivateer = new Unit(1, Classic.Unit(Privateer), Origin);                       // OwnerId 0
        var foreignPrivateer = new Unit(2, Classic.Unit(Privateer), Origin) { OwnerId = foreignId };
        double bare = Classic.Unit(Privateer).Offence; // 8

        Assert.Equal(bare * 1.5, loaded.OffenceBase(foreignPrivateer), 5); // the holder's privateer is boosted
        Assert.Equal(bare, loaded.OffenceBase(humanPrivateer), 5);         // the human's privateer is not
    }

    [Fact]
    public void Drake_CombatModifiers_AreScopedToPrivateers_InSpec()
    {
        var modifiers = Classic.Father(Drake).Modifiers;
        Assert.Contains(modifiers, m => m.TargetId == "model.modifier.offence"
            && m.AppliesTo(Privateer) && !m.AppliesTo(Frigate));
        Assert.Contains(modifiers, m => m.TargetId == "model.modifier.defence"
            && m.AppliesTo(Privateer) && !m.AppliesTo(Frigate));
    }
}
