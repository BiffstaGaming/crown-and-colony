using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The owner-id seam (FP-2, ADR-019): units and colonies carry an owner; the enemy/fog/ability rules
/// resolve by owner rather than the old human-vs-native binary. Verified with a <em>simulated</em> foreign
/// colonial unit/colony (owner id 1, no native nation) injected through the save layer — so the seam is
/// proven before any real foreign-power Player exists (that lands in FP-3b).
/// </summary>
public class OwnerTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const string FreeColonist = "model.unit.freeColonist";

    [Fact]
    public void HumanUnitsAndColonies_DefaultToOwnerZero()
    {
        var game = Game.New(Classic, seed: 7);
        Assert.All(game.PlayerUnits, u => Assert.Equal(0, u.OwnerId));

        Colony colony = game.FoundColony(game.PlayerUnits.First(u => u.IsOnMap));
        Assert.Equal(0, colony.OwnerId);
    }

    [Fact]
    public void HumanOwnerId_IsOmittedFromTheSave_AndRoundTrips()
    {
        var game = Game.New(Classic, seed: 7);
        SaveGame save = SaveGame.From(game);
        // The human (id 0) is omitted on its units so they stay byte-stable (foreign powers carry their id).
        Assert.Null(save.Units.First(u => u.Id == game.PlayerUnits.First().Id).OwnerId);

        Game loaded = SaveGame.FromJson(save.ToJson()).Restore(Classic);
        Assert.All(loaded.PlayerUnits, u => Assert.Equal(0, u.OwnerId));
    }

    [Fact]
    public void ForeignColonialUnit_IsExcludedFromHumanUnits_AndIsAnEnemy()
    {
        var game = Game.New(Classic, seed: 7);
        Unit human = game.PlayerUnits.First(u => u.IsOnMap);
        Position rivalTile = AdjacentFreeLand(game, human.Position);

        // Put a foreign colonial power's unit on the map next to the human (the powers themselves land far
        // away; this exercises the on-map owner-inequality path right next to the human).
        SaveGame save = SaveGame.From(game);
        int rivalOwnerId = save.Players!.First(p => !p.IsHuman && p.PlayerType == (int)PlayerType.Colonial).PlayerId;
        int rivalUnitId = game.Units.Max(u => u.Id) + 1;
        var rival = new SavedUnit(rivalUnitId, FreeColonist, rivalTile.X, rivalTile.Y, 0, OwnerId: rivalOwnerId);
        Game loaded = (save with { Units = save.Units.Append(rival).ToList() }).Restore(Classic);

        Assert.Equal(rivalOwnerId, loaded.Units.First(u => u.Id == rivalUnitId).OwnerId); // owner id round-trips
        Assert.DoesNotContain(loaded.PlayerUnits, u => u.Id == rivalUnitId);              // not one of the human's units

        // Owner-inequality enemy test: the human cannot walk onto the foreign unit's tile.
        Unit loadedHuman = loaded.PlayerUnits.First(u => u.IsOnMap);
        MoveCheck check = loaded.CheckMove(loadedHuman, rivalTile);
        Assert.False(check.Allowed);
        Assert.Contains("enemy", check.Reason!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForeignColonialUnit_DoesNotLiftTheHumanFog()
    {
        var game = Game.New(Classic, seed: 7);
        // A land tile the human has never seen (beyond its starting sight).
        Position farTile = game.Map.AllPositions()
            .First(p => !game.Map.TerrainAt(p).IsWater && !game.IsExplored(p)
                        && game.NativeSettlementAt(p) is null
                        && !game.Units.Any(u => u.IsOnMap && u.Position == p));

        int rivalId = game.Units.Max(u => u.Id) + 1;
        var rival = new SavedUnit(rivalId, FreeColonist, farTile.X, farTile.Y, 0, OwnerId: 1);
        SaveGame save = SaveGame.From(game);
        Game loaded = (save with { Units = save.Units.Append(rival).ToList() }).Restore(Classic);

        Assert.False(loaded.IsVisible(farTile));            // a foreign unit standing there does not reveal it to the human
        Assert.DoesNotContain(farTile, loaded.CurrentlyVisible);
    }

    [Fact]
    public void ForeignColony_OwnerIdRoundTrips()
    {
        var game = Game.New(Classic, seed: 7);
        Position tile = game.Map.AllPositions()
            .First(p => game.Map.TerrainAt(p).CanSettle && game.ColonyAt(p) is null);

        int rivalColonyId = (game.Colonies.Count == 0 ? 0 : game.Colonies.Max(c => c.Id)) + 1;
        var rivalColony = new SavedColony(rivalColonyId, "Rivaltown", tile.X, tile.Y, 1, OwnerId: 1);
        SaveGame save = SaveGame.From(game);
        Game loaded = (save with { Colonies = save.Colonies!.Append(rivalColony).ToList() }).Restore(Classic);

        Colony colony = loaded.Colonies.First(c => c.Id == rivalColonyId);
        Assert.Equal(1, colony.OwnerId);
    }

    private static Position AdjacentFreeLand(Game game, Position from) =>
        from.Neighbours().First(n => game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n));
}
