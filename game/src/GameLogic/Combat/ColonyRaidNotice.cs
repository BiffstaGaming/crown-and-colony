using CrownAndColony.GameLogic.World;

namespace CrownAndColony.GameLogic.Combat;

/// <summary>
/// What a native pillage carried off / destroyed (FreeCol <c>ServerPlayer.csPillageColony</c>'s uniform choice):
/// the kind discriminator on a <see cref="ColonyRaidNotice"/> so the presentation layer can phrase the raid
/// correctly. Goods/gold are <em>stolen</em> (an <see cref="ColonyRaidNotice.Amount"/>); a building is <em>burned</em>
/// (downgraded or razed); a docked ship is <em>sunk</em> (or limps off damaged).
/// </summary>
public enum PillageKind
{
    /// <summary>A slice of one stored goods stack was carried off (<see cref="ColonyRaidNotice.GoodsId"/> + amount).</summary>
    Goods,

    /// <summary>Gold was looted from the colony's owner (<see cref="ColonyRaidNotice.Amount"/>; goods id null).</summary>
    Gold,

    /// <summary>A building was burned — downgraded to its predecessor, or razed if a base building (<see cref="ColonyRaidNotice.BuildingId"/>).</summary>
    Building,

    /// <summary>A ship in the colony's port was sunk (or, if it had somewhere to repair, damaged and sent limping off).</summary>
    Ship,
}

/// <summary>
/// A record of a human colony pillaged by a native brave during the AI phase of
/// <see cref="GameSession.Game.EndTurn"/> (native colony pillage) — the raid sibling of
/// <see cref="CombatNotice"/> and <see cref="ColonyLossNotice"/>. A hostile brave beside an undefended,
/// pillageable human colony storms it and inflicts one randomly-chosen destructive outcome (FreeCol
/// <c>ServerPlayer.csPillageColony</c>): it burns a building, sinks a docked ship, carries off a slice of one
/// goods stack, or loots gold; the colony is <em>not</em> captured or destroyed (natives cannot take colonies).
/// The raid has no return value the human UI can read, so the game collects these notices and the presentation
/// layer surfaces them after the turn resolves.
/// </summary>
/// <remarks>
/// This is transient per-turn UI scratch: it is cleared at the start of every <c>EndTurn</c> and is never
/// saved or restored (no save-format impact). Fields are raw ids/names/positions — formatting the English
/// message is the presentation layer's job (ADR-006).
/// </remarks>
/// <param name="AttackerNationId">The raiding native nation type id (e.g. <c>model.nationType.apache</c>).</param>
/// <param name="ColonyName">The pillaged colony's name.</param>
/// <param name="GoodsId">The goods type carried off (e.g. <c>model.goods.tobacco</c>), or <c>null</c> for a non-goods outcome (gold/building/ship).</param>
/// <param name="Amount">How much goods/gold was stolen (0 for a building burn or ship sinking).</param>
/// <param name="Position">The tile the colony stands on.</param>
/// <param name="Kind">Which destructive outcome this raid was (goods / gold / building / ship). Defaults to <see cref="PillageKind.Goods"/>.</param>
/// <param name="BuildingId">The building type id burned (e.g. <c>model.building.lumberMill</c>) when <see cref="Kind"/> is <see cref="PillageKind.Building"/>; otherwise <c>null</c>.</param>
public readonly record struct ColonyRaidNotice(
    string AttackerNationId,
    string ColonyName,
    string? GoodsId,
    int Amount,
    Position Position,
    PillageKind Kind = PillageKind.Goods,
    string? BuildingId = null);
