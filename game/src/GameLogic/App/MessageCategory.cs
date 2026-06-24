namespace CrownAndColony.GameLogic.App;

/// <summary>
/// The category a per-turn player notice falls into, used by the message log to <b>group and filter</b> events
/// (combat / diplomacy / economy / natives / monarch / colony). It coarsely mirrors FreeCol's per-type
/// <c>ModelMessage.MessageType</c> "show this kind of message" client options (e.g. <c>guiShowWarehouseCapacity</c>,
/// <c>guiShowForeignDiplomacy</c>): rather than one toggle per fine-grained type, our notices roll up into these six
/// broad buckets, each of which the player can independently hide via a checkbox in the message-log panel.
/// <para>
/// Lives in the engine-free <c>GameLogic.App</c> layer (alongside <see cref="SettingsModel"/>, which persists the
/// hidden set) so the classification and the persisted-filter round-trip get L1 unit coverage without the Godot
/// runtime. The notice → category mapping itself stays in presentation (the controller knows the concrete notice
/// types), keeping rules/formatting on one side of ADR-006 and this pure tag on the other.
/// </para>
/// </summary>
public enum MessageCategory
{
    /// <summary>Attacks, raids, sieges, ships sunk, defeat — anything from the combat resolver (FreeCol <c>COMBAT_RESULT</c> / <c>UNIT_LOST</c>).</summary>
    Combat,

    /// <summary>King's-court and foreign-power relations (FreeCol <c>FOREIGN_DIPLOMACY</c>); reserved for diplomacy notices as they reach the log.</summary>
    Diplomacy,

    /// <summary>Trade, gold and goods movement — custom-house sales, warehouse overflow (FreeCol <c>MARKET_PRICES</c> / <c>GOODS_MOVEMENT</c> / <c>WAREHOUSE_CAPACITY</c>).</summary>
    Economy,

    /// <summary>Native-tribe events the human suffers or receives — pillages, demands, raids on colonies (FreeCol <c>DEMANDS</c> and native combat).</summary>
    Natives,

    /// <summary>The Crown's decrees — tax changes, war/peace declared on the player's behalf, royal support, REF growth (FreeCol monarch actions).</summary>
    Monarch,

    /// <summary>Colony-internal events — famine, starvation, building completion (FreeCol <c>BUILDING_COMPLETED</c> / <c>MISSING_GOODS</c>).</summary>
    Colony,
}
