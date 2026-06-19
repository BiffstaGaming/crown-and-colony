using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

public class FoundingFatherTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    [Fact]
    public void Spec_DefinesTheClassicFathers_AcrossFiveCategories()
    {
        Assert.Equal(25, Classic.FoundingFathers.Count);
        foreach (FatherType type in Enum.GetValues<FatherType>())
        {
            Assert.Equal(5, Classic.FoundingFathers.Count(f => f.Type == type));
        }

        FoundingFather smith = Classic.Father("model.foundingFather.adamSmith");
        Assert.Equal(FatherType.Trade, smith.Type);
        Assert.Equal((2, 8, 6), (smith.Weight1, smith.Weight2, smith.Weight3));
    }

    [Theory]
    // FreeCol getTotalFoundingFatherCost: count==0 ? factor : 2*(count+1)*factor+1.
    // Verified against Player.java:1544. Factor 24 = classic veryEasy; 40 = medium (the default).
    [InlineData(24, new[] { 24, 97, 145, 193, 241 })]
    [InlineData(40, new[] { 40, 161, 241, 321, 401 })]
    public void CostFormula_MatchesFreeCol(int factor, int[] expected)
    {
        for (int elected = 0; elected < expected.Length; elected++)
        {
            Assert.Equal(expected[elected], Game.FoundingFatherCost(elected, factor));
        }
    }

    [Fact]
    public void NewGame_OffersOneFatherPerEligibleCategory()
    {
        var game = Game.New(Classic, seed: 42);

        Assert.NotEmpty(game.OfferedFathers);
        Assert.True(game.OfferedFathers.Count <= 5);
        // Distinct categories — at most one offer per type.
        var types = game.OfferedFathers.Select(id => Classic.Father(id).Type).ToList();
        Assert.Equal(types.Count, types.Distinct().Count());
        Assert.Null(game.CurrentFather);
        Assert.Empty(game.Congress);
    }

    [Fact]
    public void Offers_AreDeterministicForASeed()
    {
        Assert.Equal(
            Game.New(Classic, seed: 99).OfferedFathers,
            Game.New(Classic, seed: 99).OfferedFathers);
    }

    [Fact]
    public void TownHallBells_BecomeLiberty_EachTurn_AndLeaveTheWarehouse()
    {
        var game = Game.New(Classic, seed: 42);
        var colony = game.FoundColony(game.Units[0]);

        game.EndTurn(); // town hall makes 1 bell, converted to liberty
        Assert.Equal(1, game.Liberty);
        Assert.Equal(0, colony.StoreOf("model.goods.bells")); // not left as tradeable stock

        game.EndTurn();
        Assert.Equal(2, game.Liberty);
    }

    [Fact]
    public void ChosenFather_IsElected_WhenLibertyReachesTheCost()
    {
        var game = Game.New(Classic, seed: 42);
        game.FoundColony(game.Units[0]);
        string target = game.OfferedFathers[0];
        game.ChooseFather(target);

        // Town hall yields 1 liberty/turn; the first father costs 40 (classic medium factor).
        for (int turn = 0; turn < 39; turn++)
        {
            game.EndTurn();
        }
        Assert.Empty(game.Congress);        // 39 liberty < 40
        Assert.Equal(target, game.CurrentFather);

        game.EndTurn();                     // 40th liberty → elect
        Assert.Contains(target, game.Congress);
        Assert.Equal(0, game.Liberty);      // cost consumed
        Assert.Null(game.CurrentFather);    // ready to choose the next
        Assert.NotEmpty(game.OfferedFathers);
        Assert.DoesNotContain(target, game.OfferedFathers); // elected, not re-offered
    }

    [Fact]
    public void Spec_ParsesJohnPaulJonesFreeFrigate()
    {
        FoundingFather jpj = Classic.Father("model.foundingFather.johnPaulJones");
        Assert.Equal(new[] { "model.unit.frigate" }, jpj.FreeUnits);
        Assert.Empty(Classic.Father("model.foundingFather.adamSmith").FreeUnits); // most fathers grant no free unit
    }

    [Fact]
    public void JohnPaulJones_GrantsAFreeFrigateInEurope_OnElection()
    {
        var game = Game.New(Classic, seed: 42);
        int humanId = game.HumanPlayer.PlayerId;
        Assert.DoesNotContain(game.Units, u => u.Type.Id == "model.unit.frigate"); // none at the start

        // Prime the human's player row with John Paul Jones pending and ample liberty, so the next turn elects him.
        SaveGame baseSave = SaveGame.From(game);
        SaveGame primed = baseSave with
        {
            Players = baseSave.Players!.Select(p => p.IsHuman
                ? p with { CurrentFather = "model.foundingFather.johnPaulJones", Liberty = 100_000 }
                : p).ToList(),
        };
        Game g = SaveGame.FromJson(primed.ToJson()).Restore(Classic);

        g.EndTurn(); // liberty >= cost → JPJ elected → his free frigate appears on the Europe dock

        Assert.Contains("model.foundingFather.johnPaulJones", g.Congress);
        var frigates = g.Units.Where(u => u.Type.Id == "model.unit.frigate").ToList();
        Unit frigate = Assert.Single(frigates);
        Assert.Equal(humanId, frigate.OwnerId);
        Assert.Equal(UnitLocation.InEurope, frigate.Location);
    }

    [Fact]
    public void Spec_ParsesJacobFuggerBoycottsLifted()
    {
        Assert.True(Classic.Father("model.foundingFather.jacobFugger").LiftsBoycotts);
        Assert.False(Classic.Father("model.foundingFather.adamSmith").LiftsBoycotts); // no other father lifts boycotts
    }

    [Fact]
    public void JacobFugger_LiftsAllBoycotts_OnElection()
    {
        var game = Game.New(Classic, seed: 42);
        game.HumanPlayer.Market.SetArrears("model.goods.furs", 5000);   // two goods under boycott
        game.HumanPlayer.Market.SetArrears("model.goods.cigars", 3000);
        Assert.False(game.HumanPlayer.Market.CanTrade("model.goods.furs"));

        // Prime the human's player row with Jacob Fugger pending and ample liberty (arrears ride the save).
        SaveGame baseSave = SaveGame.From(game);
        SaveGame primed = baseSave with
        {
            Players = baseSave.Players!.Select(p => p.IsHuman
                ? p with { CurrentFather = "model.foundingFather.jacobFugger", Liberty = 100_000 }
                : p).ToList(),
        };
        Game g = SaveGame.FromJson(primed.ToJson()).Restore(Classic);

        g.EndTurn(); // Fugger elected → every boycott lifted

        Assert.Contains("model.foundingFather.jacobFugger", g.Congress);
        Assert.True(g.HumanPlayer.Market.CanTrade("model.goods.furs"));
        Assert.True(g.HumanPlayer.Market.CanTrade("model.goods.cigars"));
        Assert.Equal(0, g.HumanPlayer.Market.Arrears("model.goods.furs"));
    }

    [Fact]
    public void Spec_ParsesCoronadoSeeAllColonies()
    {
        Assert.True(Classic.Father("model.foundingFather.franciscoDeCoronado").RevealsAllColonies);
        Assert.False(Classic.Father("model.foundingFather.adamSmith").RevealsAllColonies); // no other father reveals colonies
    }

    [Fact]
    public void Coronado_RevealsEveryColony_OnElection()
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 1,
            RandomIncrement = 1,
            MapWidth = 5,
            MapHeight = 5,
            Terrain = [.. System.Linq.Enumerable.Repeat("model.tile.plains", 25)],
            Units = [],
            Explored = [0], // only (0,0) has been seen
            Colonies = [new SavedColony(1, "Far", 4, 4, 1)],
            CurrentFather = "model.foundingFather.franciscoDeCoronado",
            Liberty = 100_000, // well over the first father's cost
        };
        Game game = save.Restore(Classic);
        Assert.False(game.IsExplored(new Position(4, 4))); // the distant colony is unseen before election

        game.EndTurn(); // Coronado elected → every colony on the map revealed

        Assert.Contains("model.foundingFather.franciscoDeCoronado", game.Congress);
        Assert.True(game.IsExplored(new Position(4, 4)));
    }

    [Fact]
    public void Spec_DeLasCasas_HasTheUpgradeConvertAbility()
    {
        Assert.Contains(Classic.Father("model.foundingFather.bartolomeDeLasCasas").Abilities,
            a => a.Id == "model.ability.upgradeConvert" && a.Value);
        Assert.DoesNotContain(Classic.Father("model.foundingFather.adamSmith").Abilities,
            a => a.Id == "model.ability.upgradeConvert");
    }

    [Fact]
    public void DeLasCasas_UpgradesEveryConvertToAFreeColonist_OnElection()
    {
        var game = Game.New(Classic, seed: 42);
        Position land = game.Map.AllPositions().First(p => !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));
        Unit convert = game.SpawnUnit(Classic.Unit("model.unit.indianConvert"), land); // human-owned native convert
        Assert.Equal("model.unit.indianConvert", convert.Type.Id);

        SaveGame baseSave = SaveGame.From(game);
        SaveGame primed = baseSave with
        {
            Players = baseSave.Players!.Select(p => p.IsHuman
                ? p with { CurrentFather = "model.foundingFather.bartolomeDeLasCasas", Liberty = 100_000 }
                : p).ToList(),
        };
        Game g = SaveGame.FromJson(primed.ToJson()).Restore(Classic);

        g.EndTurn(); // de las Casas elected → every convert becomes a free colonist

        Assert.Contains("model.foundingFather.bartolomeDeLasCasas", g.Congress);
        Unit upgraded = g.Units.Single(u => u.Id == convert.Id);
        Assert.Equal("model.unit.freeColonist", upgraded.Type.Id);
    }

    [Fact]
    public void ChooseFather_RejectsAnUnofferedFather()
    {
        var game = Game.New(Classic, seed: 42);
        string notOffered = Classic.FoundingFathers
            .First(f => !game.OfferedFathers.Contains(f.Id)).Id;

        Assert.Throws<InvalidMoveException>(() => game.ChooseFather(notOffered));
    }

    [Fact]
    public void SaveRoundTrip_PreservesCongressLibertyAndOffers()
    {
        var game = Game.New(Classic, seed: 42);
        game.FoundColony(game.Units[0]);
        game.ChooseFather(game.OfferedFathers[0]);
        game.EndTurn();
        game.EndTurn();

        Game loaded = SaveGame.FromJson(SaveGame.From(game).ToJson()).Restore(Classic);

        Assert.Equal(game.Liberty, loaded.Liberty);
        Assert.Equal(game.CurrentFather, loaded.CurrentFather);
        Assert.Equal(game.OfferedFathers, loaded.OfferedFathers);
        Assert.Equal(game.Congress, loaded.Congress);
    }

    [Fact]
    public void PreV10Save_LoadsWithEmptyCongress_AndNoLiberty()
    {
        var game = Game.New(Classic, seed: 42);
        SaveGame v9 = SaveGame.From(game) with
        {
            Version = 9,
            Liberty = 0,
            Congress = null,
            CurrentFather = null,
            OfferedFathers = null,
        };

        Game loaded = SaveGame.FromJson(v9.ToJson()).Restore(Classic);

        Assert.Equal(0, loaded.Liberty);
        Assert.Empty(loaded.Congress);
        Assert.Null(loaded.CurrentFather);
    }
}
