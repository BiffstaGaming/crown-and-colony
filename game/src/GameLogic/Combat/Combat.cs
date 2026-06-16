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
public readonly record struct AttackContext(
    bool WithoutAttackBonus = false,
    MovementPenalty Movement = MovementPenalty.None,
    bool Amphibious = false,
    bool ArtilleryInOpen = false,
    double AmbushBonus = 0,
    int GoodsCarried = 0);

/// <summary>Situational modifiers on the defender (all percentages).</summary>
/// <param name="TerrainDefenceBonus">The defending tile's defence bonus percentage (hills 100, forest 50, …).</param>
/// <param name="Fortified">The defender is fortified (FreeCol <c>FORTIFIED</c>, +50%).</param>
/// <param name="SettlementDefenceBonus">A settlement's defence bonus percentage, if defending one.</param>
/// <param name="ArtilleryInOpen">Artillery defending in the open, not in a settlement and not dug in (−75%, FreeCol <c>ARTILLERY_IN_THE_OPEN</c>).</param>
/// <param name="ArtilleryAgainstRaid">Artillery defending a settlement against a native raid (+100%, FreeCol <c>ARTILLERY_AGAINST_RAID</c>).</param>
/// <param name="GoodsCarried">Goods units in the (naval) defender's hold — each unit is a −12.5% cargo penalty.</param>
public readonly record struct DefenceContext(
    double TerrainDefenceBonus = 0,
    bool Fortified = false,
    double SettlementDefenceBonus = 0,
    bool ArtilleryInOpen = false,
    bool ArtilleryAgainstRaid = false,
    int GoodsCarried = 0);

/// <summary>
/// FreeCol's combat model (<c>SimpleCombatModel</c>): combines a unit's base offence/defence with
/// situational percentage modifiers, then resolves the round by the odds <c>attack / (attack + defence)</c>.
/// Pure and deterministic given an <see cref="IGameRandom"/> — no map/unit state here; the attack slice
/// wires units, targets and outcomes to it. Modifier values are pinned to the classic spec.
/// </summary>
public static class CombatModel
{
    // FreeCol GENERAL_COMBAT_INDEX (50) percentage modifiers, as multipliers.
    private const double AttackBonus = 0.50;          // +50%
    private const double SmallMovementPenalty = -0.33; // 2 moves left
    private const double BigMovementPenalty = -0.66;   // 1 move left
    private const double AmphibiousPenalty = -0.75;
    private const double ArtilleryInOpenPenalty = -0.75;
    private const double ArtilleryAgainstRaidBonus = 1.00; // +100% — artillery defending a colony against a native raid
    private const double FortifiedBonus = 0.50;        // +50%
    private const double CargoPenalty = -0.125;        // −12.5% per goods unit carried (naval, both offence & defence)

    /// <summary>The attacker's total offence power: base offence times the situational modifiers.</summary>
    public static double AttackPower(double baseOffence, AttackContext context)
    {
        double power = baseOffence;
        if (!context.WithoutAttackBonus)
        {
            power *= 1 + AttackBonus;
        }
        power *= context.Movement switch
        {
            MovementPenalty.Big => 1 + BigMovementPenalty,
            MovementPenalty.Small => 1 + SmallMovementPenalty,
            _ => 1.0,
        };
        if (context.Amphibious)
        {
            power *= 1 + AmphibiousPenalty;
        }
        if (context.ArtilleryInOpen)
        {
            power *= 1 + ArtilleryInOpenPenalty;
        }
        if (context.AmbushBonus != 0)
        {
            power *= 1 + (context.AmbushBonus / 100.0); // strike from cover: gain the defender's terrain bonus as offence
        }
        power *= System.Math.Max(0, 1 + (CargoPenalty * context.GoodsCarried)); // laden ships attack worse
        return power;
    }

    /// <summary>The defender's total defence power: base defence times terrain, fortification and settlement bonuses.</summary>
    public static double DefencePower(double baseDefence, DefenceContext context)
    {
        double power = baseDefence;
        power *= 1 + (context.TerrainDefenceBonus / 100.0);
        if (context.Fortified)
        {
            power *= 1 + FortifiedBonus;
        }
        power *= 1 + (context.SettlementDefenceBonus / 100.0);
        if (context.ArtilleryInOpen)
        {
            power *= 1 + ArtilleryInOpenPenalty; // artillery caught defending in the field is brittle (−75%)
        }
        if (context.ArtilleryAgainstRaid)
        {
            power *= 1 + ArtilleryAgainstRaidBonus; // but artillery behind a colony's walls shreds a native raid (+100%)
        }
        power *= System.Math.Max(0, 1 + (CargoPenalty * context.GoodsCarried)); // laden ships defend worse
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
