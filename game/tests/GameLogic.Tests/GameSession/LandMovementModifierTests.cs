using System.IO;
using System.Linq;
using System.Xml.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The scope-aware land-unit movement-modifier fold (4d.1). Classic <c>model.modifier.movementBonus</c> father/nation
/// modifiers (Magellan, the naval nation-type) are all <b>naval-scoped</b> (<c>&lt;scope ability-id="model.ability.navalUnit"/&gt;</c>),
/// and <see cref="MagellanTests"/> already proves those stay naval-only and byte-identical after the un-gate. These
/// tests prove the OTHER half: a <b>non-naval-scoped</b> <c>movementBonus</c> now reaches <b>land</b> units — which the
/// previous <c>IsNaval</c> code gate suppressed — via a synthetic land-scoped father modifier.
/// </summary>
public class LandMovementModifierTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Magellan = "model.foundingFather.ferdinandMagellan";
    private const string Colonist = "model.unit.freeColonist";

    /// <summary>
    /// The classic ruleset with Magellan's <c>movementBonus</c> naval scope <b>removed</b> — turning it into a
    /// land-reaching (non-naval-scoped) movement bonus. Everything else is untouched, so it is a minimal probe for the
    /// un-gated land fold: with the naval scope gone, the +3 must now apply to a land colonist.
    /// </summary>
    private static Ruleset LoadClassicWithLandMovementFather()
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        XElement magellan = doc.Descendants("founding-father").Single(f => (string?)f.Attribute("id") == Magellan);
        XElement moveMod = magellan.Elements("modifier").Single(m => (string?)m.Attribute("id") == "model.modifier.movementBonus");
        // Drop the <scope ability-id="model.ability.navalUnit"/> child so the bonus is no longer naval-scoped.
        moveMod.Elements("scope").Where(s => (string?)s.Attribute("ability-id") == "model.ability.navalUnit").Remove();
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

    private static Game GameWithFather(Ruleset ruleset, bool elect)
    {
        Game game = Game.New(ruleset, Seed);
        if (!elect)
        {
            return game;
        }
        SaveGame save = SaveGame.From(game);
        return (save with
        {
            Players = save.Players!.Select(p => p.IsHuman ? p with { Congress = new[] { Magellan } } : p).ToList(),
        }).Restore(ruleset);
    }

    [Fact]
    public void NonNavalScopedMovementModifier_NowAppliesToLandUnits()
    {
        int landBase = Classic.Unit(Colonist).Movement;

        // Control: the UNMODIFIED classic Magellan is naval-scoped, so even elected it gives a land colonist nothing.
        Game control = GameWithFather(Classic, elect: true);
        control.EndTurn();
        Unit controlColonist = control.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        Assert.Equal(landBase, controlColonist.MovementLeft);

        // With the naval scope removed, the same +3 now reaches the land colonist (the un-gated fold).
        Ruleset landRuleset = LoadClassicWithLandMovementFather();
        Game game = GameWithFather(landRuleset, elect: true);
        game.EndTurn();
        Unit colonist = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        Assert.Equal(landBase + 3, colonist.MovementLeft);
    }

    [Fact]
    public void LandMovementFather_Unelected_LeavesLandUnitsUnchanged()
    {
        // The land-reaching modifier is dormant until the father is elected — an unelected game's colonist is base.
        Ruleset landRuleset = LoadClassicWithLandMovementFather();
        Game game = GameWithFather(landRuleset, elect: false);
        game.EndTurn();
        Unit colonist = game.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        Assert.Equal(Classic.Unit(Colonist).Movement, colonist.MovementLeft);
    }

    [Fact]
    public void ClassicLandUnits_AreByteIdentical_ToBefore()
    {
        // Sanity floor: the stock classic ruleset gives land units exactly their base + role bonus — no movement modifier
        // reaches them (all classic movementBonus modifiers are naval-scoped), so the un-gate changed nothing for classic.
        Game game = Game.New(Classic, Seed);
        game.EndTurn();
        foreach (Unit u in game.PlayerUnits.Where(u => u.IsOnMap && !u.Type.IsNaval))
        {
            int roleBonus = (int)(Classic.Roles.FirstOrDefault(r => r.Id == u.RoleId)?.MovementBonus ?? 0);
            Assert.Equal(u.Type.Movement + roleBonus, u.MovementLeft);
        }
    }
}
