namespace CrownAndColony.GameLogic.GameSession;

/// <summary>
/// Save/load-only internal accessors for <see cref="Game"/> (86d3hz9ga) — the seams
/// <see cref="Persistence.SaveGame.Restore"/> uses to reinstall state that has no public mutation path. Kept in a
/// dedicated partial so the persistence layer never needs an edit inside the main <c>Game.cs</c>.
/// </summary>
public sealed partial class Game
{
    /// <summary>
    /// Reinstalls the duration-bounded modifiers a save carried (v69) — the timed disaster production penalties that
    /// were in force when the game was saved (FreeCol reloads a temporary <c>Modifier</c>'s
    /// <c>firstTurn</c>/<c>lastTurn</c> via <c>Feature.readAttributes</c>). Called only by
    /// <see cref="Persistence.SaveGame.Restore"/> on a freshly-restored game, whose registry is empty; the reinstalled
    /// modifiers then fold and expire exactly as the originals did (the per-turn strip removes each one the first turn
    /// it is out of date).
    /// </summary>
    /// <param name="modifiers">The saved modifiers, already rebuilt as live <see cref="TemporaryModifier"/>s.</param>
    internal void RestoreTemporaryModifiers(IEnumerable<TemporaryModifier> modifiers) =>
        _temporaryModifiers.AddRange(modifiers);
}
