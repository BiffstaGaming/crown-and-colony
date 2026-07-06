using CrownAndColony.GameLogic.Randomness;

namespace CrownAndColony.GameLogic.Combat;

/// <summary>How much movement an attacker has spent (drives FreeCol's movement penalty on the attack).</summary>
public enum MovementPenalty
{
    /// <summary>Near full movement — no penalty.</summary>
    None,

    /// <summary>Only ~⅔ of a move left — small penalty (FreeCol <c>−33%</c>).</summary>
    Small,

    /// <summary>Only ~⅓ of a move left — big penalty (FreeCol <c>−66%</c>).</summary>
    Big,
}

/// <summary>The graded outcome of one combat round (FreeCol partitions the win/loss range into great/normal).</summary>
public enum CombatResult
{
    /// <summary>Decisive attacker win (the defender may be captured/destroyed — handled by the attack slice).</summary>
    GreatWin,

    /// <summary>Attacker win (the defender is typically demoted/beaten).</summary>
    Win,

    /// <summary>Attacker loss (the attacker is typically demoted/beaten).</summary>
    Loss,

    /// <summary>Decisive attacker loss.</summary>
    GreatLoss,

    /// <summary>The defender evaded (naval only): no one is hurt, the attacker's turn is spent. Appended so prior ordinals stay stable.</summary>
    Evade,
}

/// <summary>
/// Situational modifiers on the attacker (all FreeCol GENERAL_COMBAT_INDEX = 50 percentages).
/// The default (a zero-initialised struct) is a normal attack: the +50% attack bonus applies and
/// no penalties — hence the bonus is expressed as an opt-OUT (<see cref="WithoutAttackBonus"/>) so
/// that <c>new AttackContext()</c> behaves correctly despite C#'s record-struct default-ctor rules.
/// </summary>
/// <param name="WithoutAttackBonus">Suppresses the standing attack bonus (FreeCol <c>ATTACK_BONUS</c>, +50%).</param>
/// <param name="Movement">Movement-spent penalty.</param>
/// <param name="Amphibious">Attacking from a ship onto land (−75%).</param>
/// <param name="ArtilleryInOpen">Artillery attacking in the open, not in a settlement (−75%).</param>
/// <param name="AmbushBonus">Ambush offence bonus — the defender's terrain defence percentage, gained as offence when ambushing from concealing terrain (FreeCol <c>AMBUSH_BONUS</c>).</param>
/// <param name="GoodsCarried">Goods units in the (naval) attacker's hold — each unit is a −12.5% cargo penalty.</param>
/// <param name="Bombard">The attacker carries the bombard (siege) bonus assaulting a colony (+50%). FreeCol's classic spec scopes <c>model.modifier.bombardBonus</c> to the REF nation type; Col1 gives the bonus to the regular troops of every European power, so the attack slice sets this for any non-native attacker (a colonial power or the REF) — see <c>Game.CombatPowers</c>.</param>
public readonly record struct AttackContext(
    bool WithoutAttackBonus = false,
    MovementPenalty Movement = MovementPenalty.None,
    bool Amphibious = false,
    bool ArtilleryInOpen = false,
    double AmbushBonus = 0,
    int GoodsCarried = 0,
    bool Bombard = false);

/// <summary>Situational modifiers on the defender (all percentages).</summary>
/// <param name="TerrainDefenceBonus">The defending tile's defence bonus percentage (hills 100, forest 50, …).</param>
/// <param name="Fortified">The defender is fortified (FreeCol <c>FORTIFIED</c>, +50%).</param>
/// <param name="SettlementDefenceBonus">A settlement's defence bonus percentage, if defending one.</param>
/// <param name="ArtilleryInOpen">Artillery defending in the open, not in a settlement and not dug in (−75%, FreeCol <c>ARTILLERY_IN_THE_OPEN</c>).</param>
/// <param name="ArtilleryAgainstRaid">Artillery defending a settlement against a native raid (+100%, FreeCol <c>ARTILLERY_AGAINST_RAID</c>).</param>
/// <param name="GoodsCarried">Goods units in the (naval) defender's hold — each unit is a −12.5% cargo penalty.</param>
/// <param name="PopularSupportBonus">Popular-support defence percentage in a War-of-Independence settlement battle (FreeCol <c>model.modifier.popularSupport</c>): a rebel-held colony defends at its Sons-of-Liberty %, the REF attacks against <c>100 − SoL%</c> (a higher figure is added as the colony's defence either way; 0 outside the war). See <see cref="CombatModel.PopularSupportPercent"/>.</param>
public readonly record struct DefenceContext(
    double TerrainDefenceBonus = 0,
    bool Fortified = false,
    double SettlementDefenceBonus = 0,
    bool ArtilleryInOpen = false,
    bool ArtilleryAgainstRaid = false,
    int GoodsCarried = 0,
    double PopularSupportBonus = 0);

/// <summary>
/// The unattached, top-level combat <c>&lt;modifier&gt;</c> percentages FreeCol keeps in the spec's
/// <c>&lt;modifiers&gt;</c> section (the <c>GENERAL_COMBAT_INDEX = 50</c> situational bonuses/penalties that
/// are <em>not</em> attached to a unit/terrain/building). Stored as fractions (the spec's <c>-33</c> percentage
/// becomes <c>-0.33</c>) so <see cref="CombatModel"/>'s arithmetic is unchanged. <see cref="Classic"/> holds the
/// hardcoded classic values; <see cref="Specification.Ruleset.CombatModifiers"/> parses the same numbers from the
/// spec so a variant can retune combat by data alone (FreeCol <c>Specification.getModifiers</c> + <c>Modifier</c>).
/// </summary>
/// <param name="AttackBonus">The standing attack bonus (<c>model.modifier.attackBonus</c>, classic +0.50).</param>
/// <param name="SmallMovementPenalty">Penalty with ~⅔ of a move left (<c>model.modifier.smallMovementPenalty</c>, classic −0.33).</param>
/// <param name="BigMovementPenalty">Penalty with ~⅓ of a move left (<c>model.modifier.bigMovementPenalty</c>, classic −0.66).</param>
/// <param name="AmphibiousPenalty">Attacking from a ship onto land (<c>model.modifier.amphibiousAttack</c>, classic −0.75).</param>
/// <param name="ArtilleryInOpenPenalty">Artillery in the open (<c>model.modifier.artilleryInTheOpen</c>, classic −0.75).</param>
/// <param name="ArtilleryAgainstRaidBonus">Artillery defending a settlement against a native raid (<c>model.modifier.artilleryAgainstRaid</c>, classic +1.00).</param>
/// <param name="FortifiedBonus">The fortified defence bonus (<c>model.modifier.fortified</c>, classic +0.50).</param>
/// <param name="CargoPenalty">Per-goods-unit naval cargo penalty (<c>model.modifier.cargoPenalty</c>, classic −0.125).</param>
/// <param name="BombardBonus">The siege (bombard) bonus an attacker gets assaulting a colony (<c>model.modifier.bombardBonus</c>, classic +0.50). FreeCol's classic spec parks this modifier on the Royal Expeditionary Force nation type, but Col1 grants the +50% colony-assault bonus to the regular troops of every European power; the attack slice therefore sets <see cref="AttackContext.Bombard"/> for any non-native attacker assaulting a colony (not just the REF), and <see cref="CombatModel"/> applies the value.</param>
public readonly record struct CombatModifiers(
    double AttackBonus,
    double SmallMovementPenalty,
    double BigMovementPenalty,
    double AmphibiousPenalty,
    double ArtilleryInOpenPenalty,
    double ArtilleryAgainstRaidBonus,
    double FortifiedBonus,
    double CargoPenalty,
    double BombardBonus)
{
    /// <summary>
    /// The hardcoded classic FreeCol combat percentages (as fractions), used as the default when no ruleset value
    /// is supplied — so the no-argument <see cref="CombatModel.AttackPower(double, AttackContext)"/> /
    /// <see cref="CombatModel.DefencePower(double, DefenceContext)"/> overloads stay byte-identical.
    /// </summary>
    public static readonly CombatModifiers Classic = new(
        AttackBonus: 0.50,               // +50%
        SmallMovementPenalty: -0.33,     // 2 moves left
        BigMovementPenalty: -0.66,       // 1 move left
        AmphibiousPenalty: -0.75,
        ArtilleryInOpenPenalty: -0.75,
        ArtilleryAgainstRaidBonus: 1.00, // +100% — artillery defending a colony against a native raid
        FortifiedBonus: 0.50,            // +50%
        CargoPenalty: -0.125,            // −12.5% per goods unit carried (naval, both offence & defence)
        BombardBonus: 0.50);             // +50% — a European power's regulars assaulting a colony (Col1; classic spec parks the value on the REF nation type)
}

/// <summary>
/// FreeCol's combat model (<c>SimpleCombatModel</c>): combines a unit's base offence/defence with
/// situational percentage modifiers, then resolves the round by the odds <c>attack / (attack + defence)</c>.
/// Pure and deterministic given an <see cref="IGameRandom"/> — no map/unit state here; the attack slice
/// wires units, targets and outcomes to it. The situational percentages come from <see cref="CombatModifiers"/>;
/// the no-argument overloads default to <see cref="CombatModifiers.Classic"/> (the pinned classic spec values).
/// </summary>
public static class CombatModel
{
    /// <summary>The attacker's total offence power: base offence times the classic situational modifiers.</summary>
    public static double AttackPower(double baseOffence, AttackContext context) =>
        AttackPower(baseOffence, context, CombatModifiers.Classic);

    /// <summary>The attacker's total offence power: base offence times the situational modifiers from <paramref name="modifiers"/>.</summary>
    public static double AttackPower(double baseOffence, AttackContext context, CombatModifiers modifiers)
    {
        double power = baseOffence;
        if (!context.WithoutAttackBonus)
        {
            power *= 1 + modifiers.AttackBonus;
        }
        power *= context.Movement switch
        {
            MovementPenalty.Big => 1 + modifiers.BigMovementPenalty,
            MovementPenalty.Small => 1 + modifiers.SmallMovementPenalty,
            _ => 1.0,
        };
        if (context.Amphibious)
        {
            power *= 1 + modifiers.AmphibiousPenalty;
        }
        if (context.ArtilleryInOpen)
        {
            power *= 1 + modifiers.ArtilleryInOpenPenalty;
        }
        if (context.AmbushBonus != 0)
        {
            power *= 1 + (context.AmbushBonus / 100.0); // strike from cover: gain the defender's terrain bonus as offence
        }
        if (context.Bombard)
        {
            power *= 1 + modifiers.BombardBonus; // the REF's siege train hammering a rebel settlement (+50%)
        }
        power *= System.Math.Max(0, 1 + (modifiers.CargoPenalty * context.GoodsCarried)); // laden ships attack worse
        return power;
    }

    /// <summary>The defender's total defence power: base defence times the classic terrain, fortification and settlement bonuses.</summary>
    public static double DefencePower(double baseDefence, DefenceContext context) =>
        DefencePower(baseDefence, context, CombatModifiers.Classic);

    /// <summary>The defender's total defence power: base defence times the terrain, fortification and settlement bonuses from <paramref name="modifiers"/>.</summary>
    public static double DefencePower(double baseDefence, DefenceContext context, CombatModifiers modifiers)
    {
        double power = baseDefence;
        power *= 1 + (context.TerrainDefenceBonus / 100.0);
        if (context.Fortified)
        {
            power *= 1 + modifiers.FortifiedBonus;
        }
        power *= 1 + (context.SettlementDefenceBonus / 100.0);
        if (context.PopularSupportBonus != 0)
        {
            power *= 1 + (context.PopularSupportBonus / 100.0); // popular support: a town behind the cause defends harder (War of Independence)
        }
        if (context.ArtilleryInOpen)
        {
            power *= 1 + modifiers.ArtilleryInOpenPenalty; // artillery caught defending in the field is brittle (−75%)
        }
        if (context.ArtilleryAgainstRaid)
        {
            power *= 1 + modifiers.ArtilleryAgainstRaidBonus; // but artillery behind a colony's walls shreds a native raid (+100%)
        }
        power *= System.Math.Max(0, 1 + (modifiers.CargoPenalty * context.GoodsCarried)); // laden ships defend worse
        return power;
    }

    /// <summary>
    /// The attacker's win probability (FreeCol <c>attack / (attack + defence)</c>); 0 when neither
    /// side has any power.
    /// </summary>
    public static double WinProbability(double attackPower, double defencePower)
    {
        double total = attackPower + defencePower;
        return total <= 0 ? 0 : attackPower / total;
    }

    /// <summary>
    /// The popular-support defence percentage a colony adds in a War-of-Independence settlement battle (FreeCol
    /// <c>SimpleCombatModel.addPopularSupportBonus</c>): a colony's Sons-of-Liberty membership measures how hard the
    /// townsfolk fight. When the <b>rebel</b> defends, the bonus is its <paramref name="sonsOfLiberty"/> % directly
    /// (a town fully behind the revolution gets +100%); when the <b>REF</b> attacks, the figure flips to
    /// <c>100 − SoL%</c> — the loyalist remnant rallies hardest where rebel sentiment is weakest. Either way the
    /// result is the colony's <em>defence</em> bonus. Outside the war (and at the trivial 0%/100% endpoints where the
    /// flipped or raw figure is 0) it is 0 — no draw, no modifier (ADR-009). Clamped to 0..100.
    /// </summary>
    /// <param name="sonsOfLiberty">The defending colony's Sons-of-Liberty membership percentage (0..100).</param>
    /// <param name="attackerIsRef">Whether the attacker is the Royal Expeditionary Force (flips the figure to <c>100 − SoL%</c>).</param>
    public static int PopularSupportPercent(int sonsOfLiberty, bool attackerIsRef)
    {
        int bonus = System.Math.Clamp(sonsOfLiberty, 0, 100);
        if (attackerIsRef)
        {
            bonus = 100 - bonus;
        }
        return bonus;
    }

    /// <summary>
    /// Resolves one combat round into a graded result, drawing from <paramref name="random"/>
    /// (FreeCol's partition: the first 10% of the win range is a great win, the last 10% of the
    /// loss range a great loss). Land units do not evade here.
    /// </summary>
    public static CombatResult Resolve(double winProbability, IGameRandom random)
    {
        double r = random.NextDouble();
        if (r < 0.1 * winProbability)
        {
            return CombatResult.GreatWin;
        }
        if (r < winProbability)
        {
            return CombatResult.Win;
        }
        if (r >= (0.1 * winProbability) + 0.9)
        {
            return CombatResult.GreatLoss;
        }
        return CombatResult.Loss;
    }

    /// <summary>
    /// Resolves one <em>naval</em> round (FreeCol <c>SimpleCombatModel</c> ship-vs-ship): like <see cref="Resolve"/>
    /// but the first 20% of the loss range is an <see cref="CombatResult.Evade"/> (every ship has
    /// <c>evadeAttack</c>). Bands: <c>r &lt; 0.1·win</c> great win; <c>&lt; win</c> win; <c>&lt; 0.8·win+0.2</c>
    /// evade; <c>≥ 0.1·win+0.9</c> great loss; else loss. One draw from <paramref name="random"/> (ADR-009).
    /// </summary>
    public static CombatResult ResolveNaval(double winProbability, IGameRandom random)
    {
        double r = random.NextDouble();
        if (r < 0.1 * winProbability)
        {
            return CombatResult.GreatWin;
        }
        if (r < winProbability)
        {
            return CombatResult.Win;
        }
        if (r < (0.8 * winProbability) + 0.2)
        {
            return CombatResult.Evade;
        }
        if (r >= (0.1 * winProbability) + 0.9)
        {
            return CombatResult.GreatLoss;
        }
        return CombatResult.Loss;
    }
}
