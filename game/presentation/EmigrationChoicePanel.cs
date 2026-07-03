using System;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The emigration choice modal (`86d3c9xft`, FreeCol's <c>selectRecruit</c>): when a player who has earned William
/// Brewster (<c>model.ability.selectRecruit</c>) is due an emigrant, <see cref="Game.PendingEmigration"/> is set and
/// this panel asks which of the three waiting recruits should step ashore in Europe. Choosing one (a `Choose_{i}`
/// button) calls <see cref="Game.ChooseEmigrant"/>; the recruit emigrates and the dock refills.
/// <para>
/// Presentation only (ADR-006): the rules — who emigrates, the immigration bookkeeping, the re-arm when more than one
/// is due — live in <c>GameLogic</c>; this panel forwards the slot and reports the outcome through <see cref="Open"/>'s
/// callback. Built programmatically into the fixed <c>VBox/Dynamic</c> shell. Hidden by default.
/// </para>
/// </summary>
public partial class EmigrationChoicePanel : PanelContainer
{
    private Game _game = null!;
    private Action<string> _onResolved = _ => { };

    /// <summary>
    /// Opens the modal for the pending emigration choice over <paramref name="game"/>. <paramref name="onResolved"/>
    /// gets a one-line outcome. A no-op (stays hidden) when no choice is pending.
    /// </summary>
    public void Open(Game game, Action<string> onResolved)
    {
        ColonyArt.FramePanel(this); // parchment image frame + dark-ink theme (not Godot's transparent default)
        _game = game;
        _onResolved = onResolved;
        if (game.PendingEmigration is not { } pending)
        {
            Hide();
            return;
        }

        if (pending.IsFountainOfYouth)
        {
            // The Fountain-of-Youth burst routes through the same select-recruit seam (FreeCol MigrationType.FOUNTAIN):
            // the human hand-picks each of the dx free immigrants, one prompt at a time.
            GetNode<Label>("VBox/EmigrationTitle").Text = "A Fountain of Youth!";
            GetNode<Label>("VBox/EmigrationInfo").Text =
                $"Settlers flock to your docks ({pending.Remaining} to choose). Pick who sails to the New World:";
        }
        else
        {
            GetNode<Label>("VBox/EmigrationTitle").Text = "A new emigrant is ready";
            GetNode<Label>("VBox/EmigrationInfo").Text =
                "Religious unrest in Europe has produced an emigrant. Choose who sails to the New World:";
        }

        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            dynamic.RemoveChild(child); child.QueueFree(); // detach now (signal-safe), free deferred — avoids freed-while-emitting when a child button's handler drives the rebuild
        }

        for (int slot = 0; slot < pending.RecruitTypeIds.Count; slot++)
        {
            int chosen = slot; // capture for the closure
            var button = new Button
            {
                Name = $"Choose_{slot}",
                Text = Title(_game.Ruleset.Unit(pending.RecruitTypeIds[slot]).ShortName),
            };
            button.Pressed += () => Resolve(chosen);
            dynamic.AddChild(button);
        }

        Show();
    }

    private void Resolve(int slot)
    {
        if (_game.ChooseEmigrant(slot) is not { } recruit)
        {
            Hide();
            return;
        }
        string name = Title(recruit.Type.ShortName);

        // More than one emigrant can be due in one turn (a crosses bumper crop) — the engine re-arms the choice, so
        // re-open for the next one; otherwise close and report.
        if (_game.PendingEmigration is not null)
        {
            Open(_game, _onResolved);
            _onResolved($"{name} emigrated to Europe. Another emigrant is waiting.");
            return;
        }
        Hide();
        _onResolved($"{name} emigrated to Europe.");
    }

    /// <summary>Title-cases a unit short id for display (e.g. <c>expertFisherman</c> → <c>Expert Fisherman</c>).</summary>
    private static string Title(string shortName)
    {
        var words = new System.Collections.Generic.List<string>();
        int start = 0;
        for (int i = 1; i < shortName.Length; i++)
        {
            if (char.IsUpper(shortName[i]) && !char.IsUpper(shortName[i - 1]))
            {
                words.Add(shortName[start..i]);
                start = i;
            }
        }
        words.Add(shortName[start..]);
        return string.Join(" ", words.ConvertAll(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
