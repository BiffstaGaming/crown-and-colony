using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Role movement bonuses (<c>86d3bbvv6</c>): a mounted role (dragoon/scout/cavalry) adds movement at the
/// start of each turn — FreeCol <c>model.modifier.movementBonus</c>, +9 (one "move" is 3 points, so +3 tiles)
/// — while a foot/unarmed unit is unchanged. The bonus also raises the "near full movement" threshold the
/// partial-move rule compares against.
/// </summary>
public class RoleMovementTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Dragoon = "model.role.dragoon";
    private const string Scout = "model.role.scout";

    private static Game GameWithColony(out Colony colony, out Unit colonist)
    {
        var game = Game.New(Classic, seed: 99);
        colony = game.FoundColony(game.PlayerUnits.First());
        colonist = game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position);
        return game;
    }

    [Fact]
    public void Dragoon_GainsNineMovementOnTurnReset()
    {
        Game game = GameWithColony(out Colony colony, out Unit colonist);
        colony.AddGoods("model.goods.muskets", 50);
        colony.AddGoods("model.goods.horses", 50);
        game.EquipRole(colonist, colony, Dragoon);
        int baseMovement = colonist.Type.Movement;

        game.EndTurn(); // movement resets to base + role bonus

        Assert.Equal(9, (int)Classic.Role(Dragoon).MovementBonus); // spec sanity: dragoon +9
        Assert.Equal(baseMovement + 9, colonist.MovementLeft);
    }

    [Fact]
    public void Scout_OutrunsAFootColonist_AfterReset()
    {
        Game game = GameWithColony(out Colony colony, out Unit scout);
        colony.AddGoods("model.goods.horses", 50);
        game.EquipRole(scout, colony, Scout);
        Unit footColonist = game.SpawnUnit(Classic.Unit(FreeColonist), colony.Position);

        game.EndTurn();

        Assert.Equal(scout.Type.Movement + 9, scout.MovementLeft);          // mounted: +9
        Assert.Equal(footColonist.Type.Movement, footColonist.MovementLeft); // unarmed: unchanged
        Assert.True(scout.MovementLeft > footColonist.MovementLeft);
    }

    [Fact]
    public void DefaultRoleUnit_MovementUnchangedByTheBonus()
    {
        var game = Game.New(Classic, seed: 99);
        Unit colonist = game.PlayerUnits.First();

        game.EndTurn();

        Assert.Equal(colonist.Type.Movement, colonist.MovementLeft); // the unarmed default role adds nothing
    }
}
