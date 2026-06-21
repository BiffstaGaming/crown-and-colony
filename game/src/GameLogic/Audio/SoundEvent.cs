namespace CrownAndColony.GameLogic.Audio;

/// <summary>
/// The set of game events that have a sound-effect cue. This lives in the engine-free <c>GameLogic</c> assembly (not in
/// the Godot presentation layer) so the event→clip mapping (<see cref="SoundEventCatalog"/>) is plain data that can be
/// unit-tested without the engine. The presentation-layer <c>SoundService</c> resolves each value to an
/// <c>AudioStream</c> and plays it on the SFX bus (ADR-006: logic decides <em>what</em> to cue; presentation decides
/// <em>how</em> it sounds).
/// </summary>
public enum SoundEvent
{
    /// <summary>A new colony is founded by the player (FreeCol <c>sound.event.captureColony</c> family — colony chime).</summary>
    ColonyFounded,

    /// <summary>A colony finishes constructing a building (FreeCol <c>sound.event.buildingComplete</c>).</summary>
    BuildingComplete,

    /// <summary>A land/artillery attack is resolved (FreeCol <c>sound.attack.artillery</c>).</summary>
    Combat,

    /// <summary>Cargo is loaded onto or unloaded from a carrier (FreeCol <c>sound.event.loadCargo</c>/<c>unloadCargo</c>).</summary>
    CargoMoved,

    /// <summary>Goods are sold on a market / a treasure train is cashed in (FreeCol <c>sound.event.sellCargo</c>).</summary>
    CargoSold,

    /// <summary>A ship is sunk in naval combat (FreeCol <c>sound.event.shipSunk</c>).</summary>
    ShipSunk,

    /// <summary>An illegal/blocked action — also reused as the generic UI "click/deny" cue (FreeCol <c>sound.event.illegalMove</c>).</summary>
    IllegalMove,

    /// <summary>A turn alert / attention cue (FreeCol <c>sound.event.alertSound</c>).</summary>
    Alert,
}
