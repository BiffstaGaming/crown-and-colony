using System;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The Declaration-of-Independence signing screen (FreeCol <c>InGameController.declareIndependence</c> +
/// <c>ConfirmDeclarationDialog</c> + <c>showDeclarationPanel</c>, `86d3c9xht`): the human's ceremonial seam for
/// breaking from the Crown. Opened from the HUD when the human is eligible
/// (<see cref="Game.CheckDeclareIndependence"/> says <c>Allowed</c>), it runs in two phases:
/// <list type="number">
/// <item><b>Confirm</b> — prompts the player to <b>name the new nation</b> (FreeCol's <c>declareIndependence(nationName, …)</c>
/// prompt; defaulting to the nation's normal label), names the consequences (Europe closes and every unit there or at
/// sea is seized, the King's Royal Expeditionary Force takes the field, and a continental army musters from the colonies'
/// veterans), echoes the rebel-sentiment reading, and offers <em>"Liberty or Death!"</em> / <em>"Maybe Later"</em>.</item>
/// <item><b>Signed</b> — on confirm it forwards <see cref="Game.DeclareIndependence(Player, string?)"/> and shows the signed
/// declaration's flavour text (FreeCol's <c>declareIndependence.resolution</c>), with a single Close.</item>
/// </list>
/// <para>Pure presentation (ADR-006): every conditional reads a <see cref="Game"/> oracle — the eligibility gate
/// (<see cref="Game.CheckDeclareIndependence"/>), the national Sons-of-Liberty reading
/// (<see cref="Game.NationalSonsOfLiberty"/>) and the units-in-Europe count (<see cref="Game.UnitsInEurope"/>) — and
/// the only mutation is the forwarded <see cref="Game.DeclareIndependence(Player, string?)"/> command. The rules (the 50% SoL
/// threshold, the connected-port requirement, the last-colonial-year cutoff, the muster, the REF, losing Europe) all
/// live in GameLogic. Built programmatically into the fixed <c>VBox/Dynamic</c> shell with the signal-safe rebuild
/// idiom (<c>RemoveChild</c> then <c>QueueFree</c>, never <c>Free</c>), mirroring <see cref="FoundingFatherPanel"/>.</para>
/// </summary>
public partial class DeclarationPanel : PanelContainer
{
    private Game _game = null!;
    private Action _onChange = () => { };

    /// <summary>
    /// The signed Declaration of Independence's flavour text (FreeCol <c>declareIndependence.resolution</c>, John
    /// Adams' letter), shown on the signed screen after the human confirms — verbatim from FreeCol's message bundle.
    /// </summary>
    private const string DeclarationResolution =
        "This Day the Congress has passed the most important Resolution, that ever was taken in America.\n\n" +
        "I am well aware of the Toil and Blood and Treasure, that it will cost Us to maintain this Declaration, " +
        "and support and defend these States. Yet through all the Gloom I can see the Rays of ravishing Light and " +
        "Glory. I can see that the End is more than worth all the Means. And that Posterity will triumph in that " +
        "Days Transaction, even although We should rue it, which I trust in God We will not.\n\n" +
        "The Royal Expeditionary Force will soon be upon us. Prepare our defenses carefully while we raise " +
        "voluntaires for a new Continental Army.";

    /// <summary>
    /// Opens the declaration screen for the human on <paramref name="game"/>. When the human is <b>not</b> eligible
    /// (the <see cref="Game.CheckDeclareIndependence"/> gate is closed) it shows that reason with a single Close — so a
    /// HUD action can blindly offer it and the player learns why independence is still out of reach. When eligible it
    /// opens the confirm phase. <paramref name="onChange"/> runs after a successful declaration (to refresh the host
    /// view — the HUD, map and end-turn state all change once the nation rebels).
    /// </summary>
    public void Open(Game game, Action onChange)
    {
        _game = game;
        _onChange = onChange;
        RebuildConfirm();
        Show();
    }

    /// <summary>The confirm (or not-yet-eligible) phase: consequences + Liberty-or-Death / Maybe-Later, or the gate's reason + Close.</summary>
    private void RebuildConfirm()
    {
        Player human = _game.HumanPlayer;
        MoveCheck gate = _game.CheckDeclareIndependence(human);

        GetNode<Label>("VBox/DeclarationTitle").Text = "Declaration of Independence";
        VBoxContainer dynamic = ClearDynamic();

        if (!gate.Allowed)
        {
            // Not yet eligible — explain why (the oracle's reason), then a single Close. The HUD action can offer the
            // screen unconditionally; the player learns the requirement (≥ 50% rebel sentiment, a connected port, in time).
            dynamic.AddChild(new Label
            {
                Name = "GateReason",
                Text = gate.Reason ?? "Independence cannot be declared yet.",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            dynamic.AddChild(new HSeparator());
            var dismiss = new Button { Name = "DismissButton", Text = "Close" };
            dismiss.Pressed += Hide;
            dynamic.AddChild(dismiss);
            return;
        }

        // Eligible — lay out the consequences (read from oracles), then the confirm/cancel choice.
        dynamic.AddChild(new Label
        {
            Name = "Preamble",
            Text = $"Let us renounce the unjust tyranny of the Crown and declare the independence of our colonies! " +
                   $"Rebel sentiment stands at {_game.NationalSonsOfLiberty(human)}%.",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        dynamic.AddChild(new HSeparator());

        // Name the new nation (FreeCol's declareIndependence(nationName, …) prompt). Defaults to the nation's normal
        // label; the player may type its free-nation name over it, forwarded to the naming DeclareIndependence overload.
        dynamic.AddChild(new Label { Name = "NationNameLabel", Text = "Name your new nation:" });
        dynamic.AddChild(new LineEdit
        {
            Name = "NationNameField",
            Text = _game.NationLabelOf(human), // the current nation label is the suggested default (RNG-free, ADR-006)
        });
        dynamic.AddChild(new HSeparator());

        dynamic.AddChild(new Label { Name = "ConsequencesHeader", Text = "Should you declare independence:" });

        int inEurope = _game.UnitsInEurope.Count();
        string europeLine = inEurope > 0
            ? $"    • Europe closes — your {inEurope} unit{(inEurope == 1 ? "" : "s")} there or at sea {(inEurope == 1 ? "is" : "are")} seized by the Crown, and recruiting ends."
            : "    • Europe closes — recruiting ends and no unit may sail there again.";
        dynamic.AddChild(new Label
        {
            Name = "ConsequenceEurope",
            Text = europeLine,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        dynamic.AddChild(new Label
        {
            Name = "ConsequenceRef",
            Text = "    • The King's Royal Expeditionary Force takes the field and sails to crush the rebellion.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        dynamic.AddChild(new Label
        {
            Name = "ConsequenceMuster",
            Text = "    • A Continental Army musters — your veteran soldiers rise to colonial regulars.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        dynamic.AddChild(new HSeparator());

        var confirm = new Button { Name = "ConfirmButton", Text = "Liberty or Death!" };
        confirm.Pressed += OnConfirm;
        dynamic.AddChild(confirm);

        var cancel = new Button { Name = "CancelButton", Text = "Maybe Later" };
        cancel.Pressed += Hide;
        dynamic.AddChild(cancel);
    }

    /// <summary>
    /// Forwards the declaration command (<see cref="Game.DeclareIndependence(Player, string?)"/>) with the free nation's
    /// chosen name, then switches to the signed phase. The gate was checked when the confirm phase was built; the command
    /// re-checks it (and would throw if it had lapsed), so a stale-state click can't slip a forbidden declaration through.
    /// A blank name field falls back to the nation's default label in GameLogic (the naming overload's blank→default rule).
    /// </summary>
    private void OnConfirm()
    {
        string? nationName = GetNodeOrNull<LineEdit>("VBox/Dynamic/NationNameField")?.Text;
        _game.DeclareIndependence(_game.HumanPlayer, nationName);
        _onChange(); // the HUD/map/end-turn all change once the nation rebels — refresh the host view
        RebuildSigned();
    }

    /// <summary>The signed phase: the Declaration's flavour text + a single Close. Shown only after a successful declaration.</summary>
    private void RebuildSigned()
    {
        GetNode<Label>("VBox/DeclarationTitle").Text = "We hold these truths…";
        VBoxContainer dynamic = ClearDynamic();

        dynamic.AddChild(new Label
        {
            Name = "Resolution",
            Text = DeclarationResolution,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        dynamic.AddChild(new HSeparator());

        var close = new Button { Name = "SignedCloseButton", Text = "Close" };
        close.Pressed += Hide;
        dynamic.AddChild(close);
    }

    /// <summary>Empties the dynamic body with the signal-safe idiom (detach now, free deferred — mirrors <see cref="FoundingFatherPanel"/>), returning it ready to refill.</summary>
    private VBoxContainer ClearDynamic()
    {
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            // Detach now (signal-safe), free deferred — avoids "freed while a signal is being emitted" when a child
            // button's own handler (Confirm) drives the rebuild into the signed phase.
            dynamic.RemoveChild(child);
            child.QueueFree();
        }
        return dynamic;
    }
}
