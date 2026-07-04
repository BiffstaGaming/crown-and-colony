using System;
using System.Collections.Generic;
using CrownAndColony.GameLogic.World;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The hidden <b>Admin / cheat menu</b> (86d3jypd1) — an homage to the 90s/2000s cheat codes. It is unlocked by pressing
/// the backtick/tilde (<c>`</c>) key in-game and typing a secret code into the box that appears; once unlocked <b>for the
/// session</b> (deliberately not persisted — a fresh game re-locks it) the same key opens the Admin menu directly. The
/// menu currently offers <b>Show all map</b>, a presentation-only reveal of the whole board.
/// <para>Presentation-only (ADR-006): nothing here touches game state, saves or the RNG. "Show all map" only changes what
/// <see cref="RefreshView"/> draws (it feeds the map layers an all-tiles "explored/visible" set and bypasses the fog
/// checks on the colony/settlement/rumour/unit markers), so it is fully reversible and can never desync a game.</para>
/// </summary>
public partial class GameController
{
    /// <summary>The Admin unlock code (case-insensitive). Chris's pick (2026-07-04). Session-only, not persisted.</summary>
    private const string AdminCode = "eldorado";

    /// <summary>Whether the Admin menu has been unlocked this game session. Reset whenever the game scene reloads (a new/loaded game builds a fresh controller) — the "just this session" choice, so the code is re-entered each game.</summary>
    private bool _adminUnlocked;

    /// <summary>The "Show all map" cheat: when true, <see cref="RefreshView"/> draws every tile + every colony/settlement/rumour/unit regardless of the fog of war. Presentation-only.</summary>
    private bool _revealAll;

    /// <summary>Every position on the current map, cached for the reveal-all cheat (rebuilt if the map's size changes).</summary>
    private HashSet<Position>? _allMapPositions;

    /// <summary>The live Admin dialog (code box or menu), tracked so the backtick key can't stack duplicates.</summary>
    private Window? _adminDialog;

    /// <summary>
    /// The hidden Admin entry point, called from the backtick/tilde key (<see cref="_UnhandledInput"/>): opens the Admin
    /// menu directly if the code was already entered this session, otherwise the code box. No-op if an Admin dialog is
    /// already open, or before a game is running.
    /// </summary>
    private void OpenAdminMenu()
    {
        if (_adminDialog is not null || _game is null)
        {
            return;
        }
        if (_adminUnlocked)
        {
            ShowAdminMenu();
        }
        else
        {
            ShowCodePrompt();
        }
    }

    /// <summary>The code box: a parchment dialog with a text field. The correct code (case-insensitive) unlocks the Admin menu for this session and opens it; a wrong code reports it and closes.</summary>
    private void ShowCodePrompt()
    {
        var dialog = new AcceptDialog
        {
            Theme = ColonyTheme.Get(), // parchment-framed (AcceptDialog panel), not Godot's default gray box
            Title = "Enter code",
            OkButtonText = "Unlock",
            Unresizable = true,
        };
        dialog.AddCancelButton("Cancel");

        var field = new LineEdit { Name = "CodeField", PlaceholderText = "enter code…", CustomMinimumSize = new Vector2(240, 0) };
        var box = new VBoxContainer { Name = "CodeBox" };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(new Label { Text = "Enter the code:" });
        box.AddChild(field);
        dialog.AddChild(box);

        void Submit()
        {
            string entered = field.Text.Trim();
            dialog.QueueFree();
            _adminDialog = null;
            if (string.Equals(entered, AdminCode, StringComparison.OrdinalIgnoreCase))
            {
                _adminUnlocked = true;
                ShowAdminMenu();
            }
            else
            {
                InfoPopup.Show(GetNode<CanvasLayer>("UI"), "Admin", "That code doesn't work.");
            }
        }

        dialog.Confirmed += Submit;                            // the Unlock button
        field.TextSubmitted += _ => { dialog.Hide(); Submit(); }; // Enter in the field
        dialog.Canceled += () => { dialog.QueueFree(); _adminDialog = null; };
        dialog.CloseRequested += () => { dialog.QueueFree(); _adminDialog = null; }; // window close (X)

        _adminDialog = dialog;
        AddChild(dialog);
        dialog.PopupCentered();
        field.CallDeferred(Control.MethodName.GrabFocus); // focus the field so the player can type immediately
    }

    /// <summary>The Admin menu: a parchment dialog carrying the cheat toggles. For now, just "Show all map".</summary>
    private void ShowAdminMenu()
    {
        var dialog = new AcceptDialog
        {
            Theme = ColonyTheme.Get(),
            Title = "Admin",
            OkButtonText = "Close",
            Unresizable = true,
        };

        var box = new VBoxContainer { Name = "AdminBox" };
        box.AddThemeConstantOverride("separation", 10);
        box.AddChild(new Label { Text = "Cheats", HorizontalAlignment = HorizontalAlignment.Center });

        var reveal = new CheckButton { Name = "ShowAllMapToggle", Text = "Show all map", ButtonPressed = _revealAll };
        reveal.Toggled += SetRevealAll;
        box.AddChild(reveal);
        dialog.AddChild(box);

        dialog.Confirmed += () => { dialog.QueueFree(); _adminDialog = null; }; // Close
        dialog.CloseRequested += () => { dialog.QueueFree(); _adminDialog = null; };
        _adminDialog = dialog;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    /// <summary>Toggles the "Show all map" reveal cheat and redraws immediately. Presentation-only (ADR-006).</summary>
    private void SetRevealAll(bool on)
    {
        _revealAll = on;
        RefreshView();
    }

    /// <summary>Every position on the current map — the "explored/visible" set fed to the map layers while the reveal-all cheat is on. Built once and cached (rebuilt only if the map dimensions change).</summary>
    private IReadOnlySet<Position> AllMapPositions()
    {
        int expected = _game.Map.Width * _game.Map.Height;
        if (_allMapPositions is null || _allMapPositions.Count != expected)
        {
            _allMapPositions = new HashSet<Position>(expected);
            for (int y = 0; y < _game.Map.Height; y++)
            {
                for (int x = 0; x < _game.Map.Width; x++)
                {
                    _allMapPositions.Add(new Position(x, y));
                }
            }
        }
        return _allMapPositions;
    }
}
