using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Randomness;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// A unit's identity attributes (86d3drmzz / 86d3drmzu; FreeCol <c>Unit.nationality</c>/<c>Unit.ethnicity</c> +
/// <c>Unit.name</c>): a <b>person</b> is stamped with its owner's nation as its origin nationality + ethnicity on
/// spawn (FreeCol <c>initialize</c>); a ship/wagon never is (FreeCol <c>isPerson()</c>); a type swap (promotion /
/// capture) keeps the origin; and a player may give an individual unit a custom <b>name</b> (clearing it with a
/// blank). All three persist omit-when-null (save v52), so a default game stays byte-identical to v51.
/// </summary>
public class UnitIdentityTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xC0FFEEUL;
    private const string FreeColonist = "model.unit.freeColonist";
    private const string Brave = "model.unit.brave";
    private const string Caravel = "model.unit.caravel";
    private const string VeteranSoldier = "model.unit.veteranSoldier";
    private const string Soldier = "model.role.soldier";

    /// <summary>The Dutch nation id (any classic European nation works; this one is well-known in the suite).</summary>
    private static readonly string Dutch =
        Classic.EuropeanNations.First(n => n.Id.Contains("dutch")).Id;

    private static Position OpenLand(Game game) =>
        game.Map.AllPositions().First(p =>
            !game.Map.TerrainAt(p).IsWater
            && game.ColonyAt(p) is null
            && game.NativeSettlementAt(p) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));

    private static Position OpenWater(Game game) =>
        game.Map.AllPositions().First(p =>
            game.Map.TerrainAt(p).IsWater
            && !game.Units.Any(u => u.IsOnMap && u.Position == p));

    // ---- Nationality + ethnicity ----

    [Fact]
    public void ANationLessHumanColonist_HasNoNationalityOrEthnicity()
    {
        // The classic human is nation-less, so a colonist it raises carries neither (both stay null → omitted in save).
        Game game = Game.New(Classic, Seed);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), OpenLand(game));
        Assert.Null(colonist.Nationality);
        Assert.Null(colonist.Ethnicity);
    }

    [Fact]
    public void ADutchHumanColonist_TakesTheDutchNationality_AndEthnicity()
    {
        // A human that picked a nation stamps that nation onto its person units (FreeCol owner.getNationId()).
        Game game = Game.New(Classic, Seed, humanNationId: Dutch);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), OpenLand(game));
        Assert.Equal(Dutch, colonist.Nationality);
        Assert.Equal(Dutch, colonist.Ethnicity);
    }

    [Fact]
    public void TheHumanStartingColonists_AreStampedWithTheChosenNation()
    {
        // The starting roster is spawned through the dedicated path; it must stamp identity like SpawnUnit does.
        Game game = Game.New(Classic, Seed, humanNationId: Dutch);
        Unit person = game.PlayerUnits.First(u => u.Type.IsPerson);
        Assert.Equal(Dutch, person.Nationality);
        Assert.Equal(Dutch, person.Ethnicity);
    }

    [Fact]
    public void ANativeBrave_TakesItsNativeNationAsItsNationality()
    {
        // A native-owned unit's origin nation is its OwnerNationId (the native nation type id), not a colonial player.
        Game game = Game.New(Classic, Seed);
        string nation = game.NativeSettlements.First().NationTypeId;
        Unit brave = game.SpawnUnit(Classic.Unit(Brave), OpenLand(game), nation);
        Assert.Equal(nation, brave.Nationality);
        Assert.Equal(nation, brave.Ethnicity);
    }

    [Fact]
    public void AShip_IsNotAPerson_AndCarriesNoNationalityOrEthnicity()
    {
        // FreeCol gates nationality/ethnicity on isPerson() — a ship/wagon never gets one even with a Dutch owner.
        Game game = Game.New(Classic, Seed, humanNationId: Dutch);
        Unit ship = game.SpawnUnit(Classic.Unit(Caravel), OpenWater(game));
        Assert.False(ship.Type.IsPerson);
        Assert.Null(ship.Nationality);
        Assert.Null(ship.Ethnicity);
    }

    [Fact]
    public void APromotedUnit_KeepsItsOrigin()
    {
        // A type swap (promotion, like capture) is the same individual — it keeps its nationality/ethnicity (FreeCol
        // changeOwner/promotion never re-stamp the origin). A Dutch veteran soldier great-wins and promotes.
        Game game = Game.New(Classic, Seed, humanNationId: Dutch);
        bool Free(Position n) =>
            game.Map.InBounds(n) && !game.Map.TerrainAt(n).IsWater
            && game.NativeSettlementAt(n) is null && game.ColonyAt(n) is null
            && !game.Units.Any(u => u.IsOnMap && u.Position == n);
        Unit brave = game.NativeUnits.First(b => b.Position.Neighbours().Any(Free));
        Position spot = brave.Position.Neighbours().First(Free);
        Unit attacker = game.SpawnUnit(Classic.Unit(VeteranSoldier), spot);
        attacker.RoleId = Soldier;
        attacker.RoleCount = 1;
        int id = attacker.Id;
        Assert.Equal(Dutch, attacker.Nationality);

        game.Attack(attacker, brave.Position, new FixedRandom(0.0)); // a great win promotes the veteran

        Unit after = game.Units.First(u => u.Id == id);
        Assert.NotEqual(VeteranSoldier, after.Type.Id); // it really promoted (a type swap happened)
        Assert.Equal(Dutch, after.Nationality);         // …and kept its origin across the swap
        Assert.Equal(Dutch, after.Ethnicity);
    }

    // ---- Custom name ----

    [Fact]
    public void AUnit_HasNoCustomNameByDefault()
    {
        Game game = Game.New(Classic, Seed);
        Assert.Null(game.SpawnUnit(Classic.Unit(Caravel), OpenWater(game)).Name);
    }

    [Fact]
    public void NameUnit_GivesAndTrimsACustomName()
    {
        Game game = Game.New(Classic, Seed);
        Unit ship = game.SpawnUnit(Classic.Unit(Caravel), OpenWater(game));
        game.NameUnit(ship, "  Golden Hind  ");
        Assert.Equal("Golden Hind", ship.Name); // trimmed
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NameUnit_WithABlankName_ClearsTheCustomName(string? blank)
    {
        Game game = Game.New(Classic, Seed);
        Unit ship = game.SpawnUnit(Classic.Unit(Caravel), OpenWater(game));
        game.NameUnit(ship, "Endeavour");
        game.NameUnit(ship, blank); // a blank rename resets to the generic type name (FreeCol)
        Assert.Null(ship.Name);
    }

    // ---- Persistence ----

    [Fact]
    public void Identity_RoundTripsThroughSaveLoad()
    {
        // A Dutch human's colonist (origin == owner) + a custom name; both survive a save/load round-trip exactly.
        Game game = Game.New(Classic, Seed, humanNationId: Dutch);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), OpenLand(game));
        game.NameUnit(colonist, "Pocahontas");
        int id = colonist.Id;

        string json = SaveGame.From(game).ToJson();
        Assert.Contains("\"Name\": \"Pocahontas\"", json); // the custom name is written
        Game restored = SaveGame.FromJson(json).Restore(Classic);

        Unit back = restored.Units.First(u => u.Id == id);
        Assert.Equal(Dutch, back.Nationality); // re-derived from the Dutch owner (omitted, equals owner)
        Assert.Equal(Dutch, back.Ethnicity);
        Assert.Equal("Pocahontas", back.Name);
        Assert.Equal(json, SaveGame.From(restored).ToJson()); // acid test: byte-identical round-trip
        Assert.Equal(69, SaveGame.CurrentVersion);
    }

    [Fact]
    public void ADivergedOrigin_PersistsAnExplicitToken_AndRoundTrips()
    {
        // A unit whose origin differs from its current owner (here a Dutch-origin colonist re-owned to a brave's nation)
        // is the one case that writes an explicit nationality/ethnicity token — and it round-trips verbatim.
        Game game = Game.New(Classic, Seed, humanNationId: Dutch);
        Unit colonist = game.SpawnUnit(Classic.Unit(FreeColonist), OpenLand(game));
        string nation = game.NativeSettlements.First().NationTypeId;
        colonist.OwnerNationId = nation; // pretend it was captured by a native nation (origin stays Dutch)
        colonist.OwnerId = 0;
        int id = colonist.Id;

        string json = SaveGame.From(game).ToJson();
        Assert.Contains("\"Nationality\": \"" + Dutch + "\"", json); // diverged origin → explicit token
        Assert.Contains("\"Ethnicity\": \"" + Dutch + "\"", json);
        Game restored = SaveGame.FromJson(json).Restore(Classic);

        Unit back = restored.Units.First(u => u.Id == id);
        Assert.Equal(Dutch, back.Nationality); // kept its Dutch origin despite the native owner
        Assert.Equal(Dutch, back.Ethnicity);
        Assert.Equal(json, SaveGame.From(restored).ToJson()); // byte-identical round-trip
    }

    [Fact]
    public void ADefaultGame_SerializesWithNoNationalityOrEthnicityTokens()
    {
        // Every fresh unit's origin equals its owner (a nation-less human's colonists carry none; a brave's nation
        // already rides OwnerNationId), so no nationality/ethnicity token is written → byte-identical to v51.
        string json = SaveGame.From(Game.New(Classic, Seed)).ToJson();
        Assert.DoesNotContain("\"Nationality\"", json);
        Assert.DoesNotContain("\"Ethnicity\"", json);
    }

    [Fact]
    public void APreV52Save_ReDerivesEachUnitsOriginFromItsOwner()
    {
        // A pre-v52 save carries no identity tokens; on load each person's nationality/ethnicity is re-derived from
        // its owner (so a brave gets its native nation back, a nation-less human's colonists get null) and names clear.
        SaveGame save = SaveGame.From(Game.New(Classic, Seed)) with { Version = 51 };
        Game restored = save.Restore(Classic);
        foreach (Unit brave in restored.NativeUnits)
        {
            Assert.Equal(brave.OwnerNationId, brave.Nationality); // a brave's origin re-derives to its native nation
        }
        Assert.All(restored.PlayerUnits, u => Assert.Null(u.Name)); // nobody is named
    }

    /// <summary>A deterministic RNG fixed to a value (0.0 forces a great win in the combat model).</summary>
    private sealed class FixedRandom(double value) : IGameRandom
    {
        public int Next(int maxExclusive) => 0;
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => value;
        public RandomState SaveState() => new(0, 0);
    }
}
