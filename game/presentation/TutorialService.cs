using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;

namespace CrownAndColony.Presentation;

/// <summary>
/// The <b>kind</b> of a tutorial step — a stable, wording-independent tag the UI and the tests key off (so a step can be
/// asserted without matching on its display text). The enum order is the tutorial's display order.
/// </summary>
public enum TutorialStepKind
{
    /// <summary>Opening greeting shown the moment a fresh game starts: "select a unit and explore".</summary>
    Welcome,

    /// <summary>Get a land colonist onto the map (off the ship / ashore) so it can act.</summary>
    MoveAshore,

    /// <summary>Found the first colony — shown once a colonist stands on foundable land.</summary>
    FoundColony,

    /// <summary>Open the colony and put colonists to work — shown once the first colony exists.</summary>
    OpenColony,

    /// <summary>End the turn to advance the game — the closing step, after which the tutorial completes.</summary>
    EndTurn,
}

/// <summary>
/// The tutorial's observed <b>UI-action progress</b> — the handful of player actions that are not visible in the game
/// state alone (opening a colony, ending a turn). The <see cref="GameController"/> flips these via
/// <see cref="TutorialService.NotifyColonyOpened"/> / <see cref="TutorialService.NotifyTurnEnded"/>; a step's goal reads
/// them alongside the live <see cref="Game"/>. Carrying them explicitly keeps every <see cref="TutorialStep.GoalMet"/>
/// predicate a pure function (no hidden capture), so the steps stay unit-testable in isolation.
/// </summary>
/// <param name="ColonyOpened">Whether the player has opened a colony panel at least once this game.</param>
/// <param name="TurnEnded">Whether the player has ended a turn at least once since the current step was reached.</param>
public readonly record struct TutorialProgress(bool ColonyOpened, bool TurnEnded);

/// <summary>
/// One step of the guided intro: a stable <see cref="Kind"/>, the <see cref="Title"/> and <see cref="Body"/> the
/// <see cref="TutorialPanel"/> renders, and the <see cref="GoalMet"/> predicate that decides when the step is done and the
/// tutorial advances to the next one. The predicate is a pure read over the live <see cref="Game"/> and the observed
/// <see cref="TutorialProgress"/> (ADR-006) — it never mutates game state and uses no randomness (ADR-009).
/// </summary>
/// <param name="Kind">The category of step (for ordering/testing), independent of the wording.</param>
/// <param name="Title">The card's heading.</param>
/// <param name="Body">The card's plain-English instruction.</param>
/// <param name="GoalMet">Given the live game + observed UI progress, whether this step's teaching goal has been achieved (advance when true).</param>
public sealed record TutorialStep(
    TutorialStepKind Kind,
    string Title,
    string Body,
    Func<Game, TutorialProgress, bool> GoalMet);

/// <summary>
/// Drives the <b>guided-intro tutorial</b> (ClickUp <c>86d3fq1h9</c>): a small, ordered sequence of contextual tip cards
/// that teach the opening loop — explore, get a colonist ashore, found a colony, put colonists to work, end the turn. It
/// is <b>pure presentation</b> (ADR-006): it only <em>reads</em> the live <see cref="Game"/> through existing public
/// oracles to decide which tip is current, and it holds no game state, runs no rule logic, mutates nothing, and uses no
/// randomness (ADR-009). The seen/enabled state is a CLIENT preference on <c>SettingsService</c>, never the save.
/// <para>
/// Design (faithful to Col1's tutorial advisor + FreeCol's tutorial option, but bounded and non-nagging): the steps are a
/// fixed list keyed to observable game state. Each step shows once; when its goal is met the tutorial advances to the next
/// step and never returns to an earlier one. After the last step the tutorial is complete and shows nothing more. The
/// player can dismiss a card ("Got it") to move past a step manually, or skip the whole tutorial ("Skip tutorial", which
/// also flips the client preference off).
/// </para>
/// </summary>
/// <remarks>
/// This class is engine-light on purpose (no Godot base type) so it is trivially unit-testable and can be owned as a plain
/// field by <see cref="GameController"/>. The <see cref="TutorialPanel"/> renders whatever <see cref="CurrentStep"/> the
/// controller hands it after each <c>RefreshView</c>.
/// </remarks>
public sealed class TutorialService
{
    private readonly IReadOnlyList<TutorialStep> _steps;
    private int _index;
    private bool _dismissedCurrent;
    private bool _colonyOpened;
    private bool _turnEnded;

    /// <summary>Builds the service with the shipped, ordered step list.</summary>
    public TutorialService()
        : this(DefaultSteps())
    {
    }

    /// <summary>Builds the service with an explicit step list (test seam — production uses the parameterless constructor's <see cref="DefaultSteps"/>).</summary>
    /// <param name="steps">The ordered steps to walk through.</param>
    public TutorialService(IReadOnlyList<TutorialStep> steps)
    {
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
    }

    /// <summary>The number of steps in the tutorial sequence.</summary>
    public int StepCount => _steps.Count;

    /// <summary>The zero-based index of the step the tutorial is currently on (clamped to <see cref="StepCount"/> once complete). Exposed for tests/observers.</summary>
    public int CurrentIndex => _index;

    /// <summary>Whether every step has been walked through (the tutorial is finished and will show nothing more).</summary>
    public bool IsComplete => _index >= _steps.Count;

    /// <summary>
    /// The step to display right now, or <c>null</c> when there is nothing to show (the tutorial is complete, or the
    /// current step has been dismissed). Advances past any steps whose goal is already met before returning, so a player
    /// who skips ahead in the game (e.g. immediately founds a colony) is never shown a stale earlier tip.
    /// </summary>
    /// <param name="game">The live game to read state from (never mutated).</param>
    /// <returns>The current step to render, or <c>null</c> to render nothing.</returns>
    public TutorialStep? Evaluate(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var progress = new TutorialProgress(_colonyOpened, _turnEnded);

        // Advance past every step whose teaching goal the player has already achieved. A step the player dismissed with
        // "Got it" is treated the same as met (we advance) — but only once, so dismissing step N moves us to N+1 without
        // skipping any un-met steps beyond it. On advancing, reset the per-step turn-ended flag so a later "end a turn"
        // goal counts turns ended *while that step is showing*, not one the player happened to end earlier.
        while (_index < _steps.Count && (_steps[_index].GoalMet(game, progress) || _dismissedCurrent))
        {
            _index++;
            _dismissedCurrent = false;
            _turnEnded = false;
            progress = new TutorialProgress(_colonyOpened, _turnEnded);
        }

        return _index < _steps.Count ? _steps[_index] : null;
    }

    /// <summary>
    /// Dismisses the <em>current</em> step ("Got it"): the tutorial advances past it on the next <see cref="Evaluate"/>,
    /// even if its game-state goal is not yet met. A no-op once the tutorial is complete.
    /// </summary>
    public void DismissCurrent()
    {
        if (!IsComplete)
        {
            _dismissedCurrent = true;
        }
    }

    /// <summary>Records that the player opened a colony panel (advances the "put your colonists to work" step). Called by <see cref="GameController"/>.</summary>
    public void NotifyColonyOpened() => _colonyOpened = true;

    /// <summary>Records that the player ended a turn (advances the closing "end your turn" step). Called by <see cref="GameController"/>.</summary>
    public void NotifyTurnEnded() => _turnEnded = true;

    /// <summary>Marks the whole tutorial complete (the "Skip tutorial" path) so it shows nothing more this game. The caller also flips the client preference off so it stays off for future games.</summary>
    public void Skip() => _index = _steps.Count;

    /// <summary>
    /// The shipped tutorial steps, in order. Each <see cref="TutorialStep.GoalMet"/> predicate reads only public
    /// <see cref="Game"/> oracles (ADR-006). Kept deliberately small (5 steps) and non-nagging — one milestone each.
    /// </summary>
    public static IReadOnlyList<TutorialStep> DefaultSteps() => new List<TutorialStep>
    {
        // 1. Welcome — shown the instant a game starts. Its goal is met once the player has a land colonist on the map
        //    (they got a colonist ashore / already have one), so a start with land colonists on the map advances quickly
        //    to the found-colony guidance; the classic start has land units on the map from turn one.
        new(TutorialStepKind.Welcome,
            "Welcome to the New World",
            "Your expedition has arrived. Click one of your units to select it, then click an "
            + "adjacent tile to move and explore. Sail your ship toward land to unload your colonists.",
            (game, _) => HasOnMapLandUnit(game)),

        // 2. Get a colonist ashore — a land colonist standing on the map (off the ship). Only shown if the player has no
        //    land unit on the map yet (e.g. everyone still aboard the ship); otherwise the Welcome step already advanced.
        new(TutorialStepKind.MoveAshore,
            "Send a colonist ashore",
            "Land units settle the New World. Move a colonist off your ship and onto a land tile "
            + "so it can look for a good spot to build.",
            (game, _) => HasOnMapLandUnit(game)),

        // 3. Found your first colony — advances once the human owns a colony.
        new(TutorialStepKind.FoundColony,
            "Found your first colony",
            "When a colonist stands on suitable land, you can found a colony there — press B, or use "
            + "the unit's orders. A colony is where your empire grows.",
            (game, _) => HumanHasColony(game)),

        // 4. Put colonists to work — the first colony exists; advances once the player opens a colony panel (observed via
        //    NotifyColonyOpened) — a distinct action from founding, so this card is not skipped the instant a colony
        //    appears. Dismissing ("Got it") also moves it along.
        new(TutorialStepKind.OpenColony,
            "Put your colonists to work",
            "Open your colony (click it on the map) to assign colonists to farm, gather, and build. "
            + "Producing goods is how you feed your people and earn gold.",
            (_, progress) => progress.ColonyOpened),

        // 5. End the turn — the closing step. Advances once the player ends a turn while this card is showing (observed
        //    via NotifyTurnEnded), then the tutorial is complete. Dismissing ("Got it") also completes it.
        new(TutorialStepKind.EndTurn,
            "End your turn to continue",
            "When you have given your units their orders, press End Turn (or Enter) to advance the "
            + "game. That's the core loop — explore, settle, produce, and grow. Good luck!",
            (_, progress) => progress.TurnEnded),
    };

    // A land unit the human owns is standing on the map (not a ship, not aboard a carrier, not sailing / in Europe).
    private static bool HasOnMapLandUnit(Game game) =>
        game.PlayerUnits.Any(u => u.IsOnMap && !u.Type.IsNaval);

    // The human owns at least one colony.
    private static bool HumanHasColony(Game game) =>
        game.Colonies.Any(c => c.OwnerId == game.HumanPlayer.PlayerId);
}
