using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Ferdinand Magellan (exploration father): +3 movement and −1 high-seas sail turn for **naval** units
/// (FreeCol <c>model.modifier.movementBonus</c> / <c>sailHighSeas</c>, both scoped to naval units). Among
/// fathers only Magellan carries these, so the <c>IsNaval</c> gate is his scope. Both fold the owner's Congress,
/// are dormant unless elected, and ride on the persisted Congress (no save-format change, no RNG).
/// </summary>
public class MagellanTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string Magellan = "model.foundingFather.ferdinandMagellan";
    private const string Caravel = "model.unit.caravel";
    private const string HighSeas = "model.tile.highSeas";

    /// <summary>A game with a human caravel on a high-seas tile, optionally with Magellan in the human's Congress.</summary>
    private static Game ShipGame(bool magellan)
    {
        Game game = Game.New(Classic, Seed);
        Position seas = game.Map.AllPositions().First(p => game.Map.TerrainAt(p).Id == HighSeas);
        game.SpawnUnit(Classic.Unit(Caravel), seas);
        if (!magellan)
        {
            return game;
        }
        SaveGame save = SaveGame.From(game);
        return (save with
        {
            Players = save.Players!.Select(p => p.IsHuman ? p with { Congress = new[] { Magellan } } : p).ToList(),
        }).Restore(Classic);
    }

    [Fact]
    public void Magellan_GivesNavalUnits_PlusThreeMovement()
    {
        Game plain = ShipGame(magellan: false);
        plain.EndTurn(); // movement reset recomputes InitialMovement
        Assert.Equal(Classic.Unit(Caravel).Movement, plain.PlayerUnits.First(u => u.Type.IsNaval).MovementLeft);

        Game withMagellan = ShipGame(magellan: true);
        withMagellan.EndTurn();
        Assert.Equal(Classic.Unit(Caravel).Movement + 3, withMagellan.PlayerUnits.First(u => u.Type.IsNaval).MovementLeft);
    }

    [Fact]
    public void Magellan_ShortensTheHighSeasCrossingByOne()
    {
        Game plain = ShipGame(magellan: false);
        Unit ship = plain.PlayerUnits.First(u => u.Type.IsNaval);
        plain.SailToEurope(ship);
        Assert.Equal(Game.SailTurns, ship.SailTurnsRemaining); // 3 (no father)

        Game withMagellan = ShipGame(magellan: true);
        Unit mShip = withMagellan.PlayerUnits.First(u => u.Type.IsNaval);
        withMagellan.SailToEurope(mShip);
        Assert.Equal(Game.SailTurns - 1, mShip.SailTurnsRemaining); // 2
    }

    [Fact]
    public void Magellan_IsPerPower_BoostsTheHoldersShip_NotOthers()
    {
        // The fold is per-owner: a foreign power's Magellan boosts ITS ship; the human's ship is untouched.
        Game game = Game.New(Classic, Seed);
        Position seas = game.Map.AllPositions().First(p => game.Map.TerrainAt(p).Id == HighSeas);
        Position seas2 = game.Map.AllPositions().First(p => p != seas && game.Map.TerrainAt(p).Id == HighSeas);
        Unit humanShip = game.SpawnUnit(Classic.Unit(Caravel), seas);
        int foreignId = game.Players.First(p => !p.IsHuman && p.PlayerType == PlayerType.Colonial).PlayerId;
        Unit foreignShip = game.SpawnUnit(Classic.Unit(Caravel), seas2);
        foreignShip.OwnerId = foreignId; // reassign from the human

        // Magellan only in the FOREIGN power's Congress.
        SaveGame save = SaveGame.From(game);
        Game loaded = (save with
        {
            Players = save.Players!.Select(p => p.PlayerId == foreignId ? p with { Congress = new[] { Magellan } } : p).ToList(),
        }).Restore(Classic);
        loaded.EndTurn(); // movement reset recomputes both ships

        int caravelBase = Classic.Unit(Caravel).Movement;
        Assert.Equal(caravelBase + 3, loaded.Units.First(u => u.Id == foreignShip.Id).MovementLeft); // the holder's ship
        Assert.Equal(caravelBase, loaded.Units.First(u => u.Id == humanShip.Id).MovementLeft);       // the human's ship, unaffected
    }

    [Fact]
    public void Magellan_DoesNotBoostLandUnits()
    {
        // The bonus is naval-scoped: the human's starting land colonist gets nothing.
        Game withMagellan = ShipGame(magellan: true);
        withMagellan.EndTurn();
        Unit colonist = withMagellan.PlayerUnits.First(u => u.IsOnMap && !u.Type.IsNaval);
        Assert.Equal(Classic.Unit(Game.StartingUnitTypeId).Movement, colonist.MovementLeft);
    }
}
