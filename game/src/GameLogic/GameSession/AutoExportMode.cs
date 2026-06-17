namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// How a colony's custom house decides what to auto-sell each turn (Chris's decision: ship both modes,
/// selectable via a setting). A game-wide play preference, not ruleset data.
/// </summary>
public enum AutoExportMode
{
    /// <summary>
    /// FreeCol behaviour (the default): the player toggles export <em>per good</em> and sets each good's retain
    /// level (default <see cref="Colonies.Colony.DefaultExportLevel"/>); only goods explicitly marked exported sell.
    /// </summary>
    PerGood = 0,

    /// <summary>
    /// The original 1994 Colonization behaviour: every storable trade good's surplus over
    /// <see cref="Colonies.Colony.DefaultExportLevel"/> auto-sells, with no per-good toggle.
    /// </summary>
    ExportAllOverLevel = 1,
}
