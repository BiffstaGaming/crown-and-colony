using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// Unit attrition (<c>86d3drmzp</c>; FreeCol <c>ServerUnit.csNewTurn</c> + <c>UnitType.getMaximumAttrition</c>):
/// a unit standing on a settlement-less map tile accrues +1 attrition each world turn; once it exceeds its type's
/// maximum attrition the unit wastes away and its owner is notified; sheltering in a settlement (or sailing/Europe)
/// resets it to 0. In the classic ruleset only the Indian Convert (<c>maximum-attrition="8"</c>) is subject to
/// attrition — every other unit type is immune, so the default game never loses a unit this way and the save field
/// omits when 0 (byte-identical to v49).
/// </summary>
public class UnitAttritionTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xA77217UL;
    private const string IndianConvert = "model.unit.indianConvert";
    private const string FreeColonist = "model.unit.freeColonist";
    private const int ConvertMax = 8; // classic model.unit.indianConvert maximum-attrition

    /// <summary>An open land tile with no settlement and no other unit (the wilderness, where attrition bites).</summary>
    private static Position OpenWilderness(Game game) =>
        game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null
            && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));

    // ---- Accrual ----

    [Fact]
    public void AConvertInTheOpen_AccruesOneAttritionPerTurn()
    {
        Game game = Game.New(Classic, Seed);
        Unit convert = game.SpawnUnit(Classic.Unit(IndianConvert), OpenWilderness(game));
        Assert.Equal(0, convert.Attrition);

        game.EndTurn();
        Assert.Equal(1, convert.Attrition);
        game.EndTurn();
        Assert.Equal(2, convert.Attrition); // +1 each world turn it ends in the open
    }

    [Fact]
    public void ExceedingMaximumAttrition_DestroysTheConvert_AndNotifiesItsOwner()
    {
        Game game = Game.New(Classic, Seed);
        Position spot = OpenWilderness(game);
        Unit convert = game.SpawnUnit(Classic.Unit(IndianConvert), spot);
        int id = convert.Id;

        // Run exactly maximum-attrition turns: attrition reaches the cap (8) but the unit survives (destroyed only
        // when it EXCEEDS the cap, matching FreeCol's `attrition > getMaximumAttrition()`).
        for (int t = 0; t < ConvertMax; t++)
        {
            game.EndTurn();
        }
        Assert.Equal(ConvertMax, convert.Attrition);
        Assert.Contains(game.Units, u => u.Id == id);
        Assert.Empty(game.AttritionNotices);

        // The next turn pushes attrition to 9 (> 8): the convert wastes away and its owner is notified.
        game.EndTurn();

        Assert.DoesNotContain(game.Units, u => u.Id == id);
        AttritionNotice notice = Assert.Single(game.AttritionNotices);
        Assert.Equal(0, notice.OwnerId);       // the human owns it
        Assert.Equal(IndianConvert, notice.UnitTypeId);
        Assert.Equal(spot, notice.Position);
    }

    [Fact]
    public void AttritionNotices_AreRefreshedEachTurn()
    {
        Game game = Game.New(Classic, Seed);
        Unit convert = game.SpawnUnit(Classic.Unit(IndianConvert), OpenWilderness(game));
        for (int t = 0; t <= ConvertMax; t++)
        {
            game.EndTurn(); // the last of these destroys the convert and records a notice
        }
        Assert.Single(game.AttritionNotices);

        game.EndTurn(); // a turn with no attrition loss clears the transient notice list
        Assert.Empty(game.AttritionNotices);
    }

    // ---- Reset ----

    [Fact]
    public void EnteringASettlement_ResetsAttrition()
    {
        Game game = Game.New(Classic, Seed);
        // Found a colony, then place a convert on the colony tile: standing on a settlement is sheltered.
        Unit founder = game.PlayerUnits.First(u => u.Type.CanFoundColony);
        Colony colony = game.FoundColony(founder);
        Unit convert = game.SpawnUnit(Classic.Unit(IndianConvert), OpenWilderness(game));

        game.EndTurn();
        game.EndTurn();
        Assert.Equal(2, convert.Attrition); // accrued in the open

        convert.Position = colony.Position; // it reaches the colony (the dedicated test seam for placement)
        game.EndTurn();

        Assert.Equal(0, convert.Attrition);              // sheltering resets it (FreeCol's else setAttrition(0))
        Assert.Contains(game.Units, u => u.Id == convert.Id); // and it is safe
    }

    [Fact]
    public void SailingToEurope_ResetsAttrition()
    {
        Game game = Game.New(Classic, Seed);
        Unit convert = game.SpawnUnit(Classic.Unit(IndianConvert), OpenWilderness(game));
        game.EndTurn();
        Assert.Equal(1, convert.Attrition);

        convert.Location = UnitLocation.SailingToEurope; // off the map → not "in the open"
        game.EndTurn();

        Assert.Equal(0, convert.Attrition);
    }

    // ---- Immune types ----

    [Fact]
    public void AUnitWithInfiniteMaxAttrition_NeverAccrues()
    {
        // A free colonist has no maximum-attrition (FreeCol INFINITY) → it is not subject to attrition at all.
        Game game = Game.New(Classic, Seed);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), OpenWilderness(game));

        for (int t = 0; t < ConvertMax + 5; t++)
        {
            game.EndTurn();
        }

        Assert.Equal(0, colonist.Attrition);              // never ticks
        Assert.Contains(game.Units, u => u.Id == colonist.Id); // never wastes away
    }

    [Fact]
    public void TheDefaultGame_LosesNoUnitToAttrition_AndRecordsNoNotice()
    {
        // The classic starting units are all attrition-immune types, so a string of turns destroys nobody to attrition.
        Game game = Game.New(Classic, Seed);
        for (int t = 0; t < 12; t++)
        {
            game.EndTurn();
            Assert.Empty(game.AttritionNotices); // no unit ever wastes away in a default classic game
        }
    }

    // ---- Persistence ----

    [Fact]
    public void Attrition_RoundTripsThroughSaveLoad()
    {
        Game game = Game.New(Classic, Seed);
        Unit convert = game.SpawnUnit(Classic.Unit(IndianConvert), OpenWilderness(game));
        game.EndTurn();
        game.EndTurn();
        game.EndTurn();
        int id = convert.Id;
        Assert.Equal(3, convert.Attrition);

        string json = SaveGame.From(game).ToJson();
        Assert.Contains("\"Attrition\"", json); // a non-zero attrition is written
        Game restored = SaveGame.FromJson(json).Restore(Classic);

        Assert.Equal(3, restored.Units.First(u => u.Id == id).Attrition);
        Assert.Equal(json, SaveGame.From(restored).ToJson()); // acid test: byte-identical round-trip
        Assert.Equal(51, SaveGame.CurrentVersion);
    }

    [Fact]
    public void AZeroAttritionGame_SerializesWithoutAnyAttritionToken()
    {
        // The field is omitted for a unit that has accrued no attrition (the default), so the game stays
        // byte-identical to v49 — every classic starting unit is attrition-free.
        string json = SaveGame.From(Game.New(Classic, Seed)).ToJson();
        Assert.DoesNotContain("\"Attrition\"", json);
    }

    [Fact]
    public void APreV50Save_WithNoAttrition_LoadsEveryUnitAtZero()
    {
        SaveGame save = SaveGame.From(Game.New(Classic, Seed)) with { Version = 49 };
        Game restored = save.Restore(Classic);
        Assert.All(restored.Units, u => Assert.Equal(0, u.Attrition));
    }
}
