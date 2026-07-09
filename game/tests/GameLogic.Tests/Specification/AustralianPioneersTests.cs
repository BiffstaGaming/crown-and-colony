using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.World;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.Specification;

/// <summary>
/// The <b>Australian Pioneers</b> (P8, `86d3kwtjb`) — the Australian Federation variant's Founding-Father
/// equivalent, authored in <c>australia/specification.xml</c> from the design corpus
/// (docs/australian_federation_mode_md/07_*–12_*). Skeleton slice: 25 figures in the five design categories
/// (mapped onto the engine's five father types), every perk a REUSED effect the engine already applies for a
/// classic father (ADR-017 — no engine code), recruited via the unchanged election machinery. The age weights
/// are the soft date gate: no Democracy &amp; Federation figure carries age-1 weight (the anachronism rule).
/// </summary>
public class AustralianPioneersTests
{
    private static readonly Ruleset Australia = GameVariants.Australia.LoadRuleset();
    private static readonly Ruleset Classic = Ruleset.LoadClassic();

    // ───────────────────────── the roster ─────────────────────────

    [Fact]
    public void Roster_Has25Pioneers_FivePerCategory_AndNoClassicFatherRemains()
    {
        Assert.Equal(25, Australia.FoundingFathers.Count);
        foreach (FatherType type in System.Enum.GetValues<FatherType>())
        {
            Assert.Equal(5, Australia.FoundingFathers.Count(f => f.Type == type));
        }

        // The classic roster is fully replaced — no American father survives in the Australian spec…
        var ids = Australia.FoundingFathers.Select(f => f.Id).ToHashSet();
        foreach (FoundingFather classic in Classic.FoundingFathers)
        {
            Assert.DoesNotContain(classic.Id, ids);
        }
        // …and the headline pioneers are present (one per category).
        foreach (string id in new[]
                 {
                     "model.foundingFather.elizabethMacarthur",  // Industry & Commerce (trade)
                     "model.foundingFather.matthewFlinders",     // Exploration & Infrastructure (exploration)
                     "model.foundingFather.arthurPhillip",       // Settlement, Administration & Defence (military)
                     "model.foundingFather.henryParkes",         // Democracy & Federation (political)
                     "model.foundingFather.williamBarak",        // Social Reform & First Nations (religious)
                 })
        {
            Assert.Contains(id, ids);
        }

        // And the classic game is untouched — its 25 historic fathers still parse.
        Assert.Equal(25, Classic.FoundingFathers.Count);
        Assert.NotNull(Classic.Father("model.foundingFather.adamSmith"));
    }

    [Fact]
    public void AnachronismGuard_NoFederationFigure_CarriesAge1Weight()
    {
        // Doc 07's rule: no Federation figure in the early game. The political category (Democracy & Federation)
        // has zero weight in age 1 — GenerateOffers therefore skips the category entirely early on…
        foreach (FoundingFather f in Australia.FoundingFathers.Where(f => f.Type == FatherType.Political))
        {
            Assert.Equal(0, f.Weight1);
        }
        // …while every other category can offer someone from turn 1.
        foreach (FatherType type in new[] { FatherType.Trade, FatherType.Exploration, FatherType.Military, FatherType.Religious })
        {
            Assert.Contains(Australia.FoundingFathers, f => f.Type == type && f.Weight1 > 0);
        }
    }

    [Fact]
    public void EarlyOffers_ExcludeTheFederationCategory_AndOfferTheOtherFour()
    {
        // A fresh Australian game (age 1): four offers — one per non-political category — and never a
        // Democracy & Federation figure.
        Game game = Game.New(Australia, seed: 0xA05UL, mapSource: MapSource.Australia);
        Assert.Equal(4, game.OfferedFathers.Count);
        foreach (string offered in game.OfferedFathers)
        {
            Assert.NotEqual(FatherType.Political, Australia.Father(offered).Type);
        }
    }

    // ───────────────────────── reused perks apply through the unchanged machinery ─────────────────────────

    [Fact]
    public void CharlesTodd_ElectricTelegraph_Boosts_CivicVoiceBells_By25Percent()
    {
        Game plain = PioneerGame(congress: null);
        Game todd = PioneerGame(congress: ["model.foundingFather.charlesTodd"]);
        Assert.Equal(100, plain.ApplyGoodsModifiers("model.goods.bells", 100));
        Assert.Equal(125, todd.ApplyGoodsModifiers("model.goods.bells", 100)); // Jefferson's payload at the design's +25%
    }

    [Fact]
    public void ThomasMort_ColdStores_UnlocksTheFactoryTier()
    {
        Game plain = PioneerGame(congress: null);
        Game mort = PioneerGame(congress: ["model.foundingFather.thomasSutcliffeMort"]);
        Assert.False(plain.HasAbility("model.ability.buildFactory"));
        Assert.True(mort.HasAbility("model.ability.buildFactory")); // Adam Smith's payload
    }

    [Fact]
    public void MaryReibey_MerchantFleet_UnlocksTheCustomHouse()
    {
        Game reibey = PioneerGame(congress: ["model.foundingFather.maryReibey"]);
        Assert.True(reibey.HasAbility("model.ability.buildCustomHouse")); // Stuyvesant's payload
    }

    [Fact]
    public void WilliamBarak_CoranderrkDelegation_DampsAmbientTension_ByTheDesigns25Percent()
    {
        FoundingFather barak = Australia.Father("model.foundingFather.williamBarak");
        FatherModifier damp = Assert.Single(barak.Modifiers);
        Assert.Equal("model.modifier.nativeAlarmModifier", damp.TargetId);
        Assert.Equal(-25, damp.Value); // the design's gentler magnitude (Pocahontas's classic payload is −50)

        // And deliberately NO alarm-reset event — relations are earned, not wiped (doc 15).
        Assert.False(barak.RevealsAllColonies);
        Assert.Empty(barak.FreeBuildings);
    }

    [Fact]
    public void JamesRuse_ExperimentFarm_GrantsAFreeExpertFarmer_OnElection()
    {
        // Election grants the design's literal perk — a free Expert Farmer (the generic free-unit mechanism).
        FoundingFather ruse = Australia.Father("model.foundingFather.jamesRuse");
        Assert.Equal("model.unit.expertFarmer", Assert.Single(ruse.FreeUnits));

        Game game = PioneerGame(congress: null, currentFather: "model.foundingFather.jamesRuse", liberty: 45);
        int farmersBefore = game.UnitsInEurope.Count(u => u.Type.Id == "model.unit.expertFarmer");
        game.EndTurn(); // liberty 45 ≥ the first father's cost (40) → Ruse is elected and his farmer arrives
        Assert.Contains("model.foundingFather.jamesRuse", game.Congress);
        Assert.Equal(farmersBefore + 1, game.UnitsInEurope.Count(u => u.Type.Id == "model.unit.expertFarmer"));
    }

    [Fact]
    public void WilliamJervois_HarbourBatteries_FortifyAnEstablishedColony_Free()
    {
        FoundingFather jervois = Australia.Father("model.foundingFather.williamJervois");
        Assert.Equal("model.building.stockade", Assert.Single(jervois.FreeBuildings)); // La Salle's payload
    }

    [Fact]
    public void GeorgeFifeAngas_ColonialCredit_LiftsBoycotts()
    {
        Assert.True(Australia.Father("model.foundingFather.georgeFifeAngas").LiftsBoycotts); // Fugger's payload
    }

    // ───────────────────────── remapped perks (M-C, doc-audit fix 2026-07-09) ─────────────────────────
    // Five Pioneers previously carried a reused payload from the *wrong* design domain. Each is remapped to a
    // known-working effect drawn from its own design clause (docs 08–12); fuller multi-clause fidelity is 4c.9.

    [Fact]
    public void CharlesSturt_RiverHighways_Boosts_SettlementFood_By25Percent()
    {
        // "River Highways" (doc 09): Sturt's rivers make settlement powerful — +25% Food from worked tiles (the
        // tile-yield fold). Replaces the old Magellan NAVAL movement payload (wrong domain — Sturt explored inland
        // rivers, not the ocean).
        Game plain = PioneerGame(congress: null);
        Game sturt = PioneerGame(congress: ["model.foundingFather.charlesSturt"]);
        Assert.Equal(100, plain.ApplyGoodsModifiers("model.goods.food", 100));
        Assert.Equal(125, sturt.ApplyGoodsModifiers("model.goods.food", 100));

        FoundingFather father = Australia.Father("model.foundingFather.charlesSturt");
        Assert.DoesNotContain(father.Modifiers, m => m.TargetId == "model.modifier.movementBonus");
        Assert.DoesNotContain(father.Modifiers, m => m.TargetId == "model.modifier.sailHighSeas");
    }

    [Fact]
    public void SidneyKidman_OverlandStations_GrantLandUnits_ExtraMovement_NotFood()
    {
        // "Overland Stations" (doc 08): droving across the interior — a non-naval-scoped +3 movementBonus (one extra
        // move) that ApplyMovementBonusModifiers folds onto LAND units only (LandMovementModifierTests proves the fold).
        FoundingFather kidman = Australia.Father("model.foundingFather.sidneyKidman");
        FatherModifier move = Assert.Single(kidman.Modifiers, m => m.TargetId == "model.modifier.movementBonus");
        Assert.False(move.NavalScoped); // reaches land units, not ships
        Assert.Equal(3, move.Value);    // +1 move (3 movement points)

        Assert.DoesNotContain(kidman.Modifiers, m => m.TargetId == "model.goods.food"); // no longer the +25% food
    }

    [Fact]
    public void JohnMcDouallStuart_NorthSouthCrossing_WidensSight_WithoutRevealingRivalColonies()
    {
        // "North-South Crossing" (doc 09): the overland survey opens the interior — a wider exposed-tile radius plus
        // +1 line-of-sight — but does NOT expose every rival settlement (the seeAllColonies reveal was dropped: a
        // route survey reveals terrain, not the enemy's cities).
        FoundingFather stuart = Australia.Father("model.foundingFather.johnMcDouallStuart");
        Assert.False(stuart.RevealsAllColonies);
        Assert.Contains(stuart.Modifiers, m => m.TargetId == "model.modifier.exposedTilesRadius" && m.Value == 3);
        Assert.Contains(stuart.Modifiers, m => m.TargetId == "model.modifier.lineOfSightBonus" && m.Value == 1);
    }

    [Fact]
    public void LachlanMacquarie_PublicWorks_IsGatedByTheAustralianAbility_NotAFreeUnit()
    {
        // "Public Works Governor" (doc 10): a bespoke on-election handler gated on the Australia-only ability lays a
        // free road by each pop-3+ colony (behaviour proven in AustralianContentTests). No longer a free carpenter.
        FoundingFather macquarie = Australia.Father("model.foundingFather.lachlanMacquarie");
        Assert.Contains(macquarie.Abilities, a => a.Id == "model.ability.publicWorks" && a.Value);
        Assert.Empty(macquarie.FreeUnits);
    }

    [Fact]
    public void LouisaLawson_TheDawnPress_Boosts_BuildingCivicVoice_By50Percent()
    {
        // "The Dawn Press" (doc 12): newspapers / civic buildings produce +50% Civic Voice (bells) — the person-negated
        // bells fold (bells NOT from a field colonist = building-produced), the Charles-Todd payload shape at +50%.
        Game plain = PioneerGame(congress: null);
        Game lawson = PioneerGame(congress: ["model.foundingFather.louisaLawson"]);
        Assert.Equal(100, plain.ApplyGoodsModifiers("model.goods.bells", 100));
        Assert.Equal(150, lawson.ApplyGoodsModifiers("model.goods.bells", 100));

        // No longer William Brewster's recruit payload (unrelated to newspapers).
        FoundingFather father = Australia.Father("model.foundingFather.louisaLawson");
        Assert.DoesNotContain(father.Abilities, a => a.Id == "model.ability.selectRecruit");
        Assert.DoesNotContain(father.Abilities, a => a.Id == "model.ability.canRecruitUnit");
    }

    // ───────────────────────── display ─────────────────────────

    [Fact]
    public void PioneerNames_HumanizeCleanly_IncludingMcDouall()
    {
        Assert.Equal("Elizabeth Macarthur", Naming.Humanize("elizabethMacarthur"));
        Assert.Equal("Catherine Helen Spence", Naming.Humanize("catherineHelenSpence"));
        Assert.Equal("John McDouall Stuart", Naming.Humanize("johnMcDouallStuart")); // the override — the raw split would say "Mc Douall"
    }

    [Fact]
    public void CongressName_IsFederationConvention_ForAustralia_AndContinentalCongress_ForClassic()
    {
        Assert.Equal("Federation Convention", GameVariants.Australia.CongressName);
        Assert.Equal("Continental Congress", GameVariants.ClassicAmerica.CongressName);
    }

    // ───────────────────────── helpers ─────────────────────────

    /// <summary>A minimal Australian-ruleset game with the given Congress, staged via the save layer (the
    /// <see cref="FoundingFatherEffectsTests"/> pattern — Congress membership is engine-internal).</summary>
    private static Game PioneerGame(IReadOnlyList<string>? congress, string? currentFather = null, int liberty = 0)
    {
        var save = new SaveGame
        {
            Turn = 1,
            RandomStateValue = 7,
            RandomIncrement = 1,
            MapWidth = 3,
            MapHeight = 3,
            Terrain = Enumerable.Repeat("model.tile.plains", 9).ToList(),
            Units = [],
            Explored = [],
            Congress = congress,
            CurrentFather = currentFather,
            Liberty = liberty,
        };
        return save.Restore(Australia);
    }
}
