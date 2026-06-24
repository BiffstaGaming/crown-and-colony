using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The in-game help screen, shown as an overlay by the main menu and the in-game pause menu. It is informational only:
/// a concise, scrollable guide to <b>what the game is</b> (the goal), <b>the core loops</b> (explore → found/run
/// colonies → economy/Europe → natives → combat → independence), and a <b>controls / keybindings reference</b>. The
/// prose is original wording derived from the player manual (<c>docs/MANUAL.md</c>); the key list mirrors the
/// authoritative key table in <see cref="GameController.BuildKeyBindings"/> (and the F1 legend) so it can't drift from
/// what the game actually does. <b>Back</b> (or Esc, handled by the host) emits <see cref="Closed"/> so the host can
/// dismiss it. Same FreeCol map backdrop + parchment/wood framing as <see cref="AboutPanel"/>/<see cref="SettingsScreen"/>
/// (presentation-only, ADR-006). FreeCol reference: its tutorial + contextual help.
/// </summary>
public partial class HelpPanel : Control
{
    /// <summary>Emitted when the player presses Back. The host removes the overlay.</summary>
    [Signal]
    public delegate void ClosedEventHandler();

    private const string BackdropPath = "res://assets/freecol/ui/map.jpg";

    /// <summary>Builds the look, fills in the help text, and wires the Back button.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get();
        if (ResourceLoader.Exists(BackdropPath))
        {
            GetNode<TextureRect>("Background").Texture = GD.Load<Texture2D>(BackdropPath);
        }
        GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        if (ColonyArt.ColonyBorder() is { } border)
        {
            GetNode<NinePatchRect>("Border").Texture = border;
        }

        // The authored placeholder body in the scene is overwritten here so the help text lives in one place (this file).
        GetNode<RichTextLabel>("Panel/VBox/Body").Text = BuildBody();

        GetNode<Button>("Panel/VBox/BackButton").Pressed += OnBack;
    }

    // The full help text: goal, the core gameplay loops, and a controls reference. Original wording (GPL-clean),
    // condensed from docs/MANUAL.md. The controls section restates the authoritative key table from
    // GameController.BuildKeyBindings — keep the two in step if a key changes.
    private static string BuildBody() =>
        "[b]The goal of the game[/b]\n" +
        "You lead one of the European powers settling the New World. Explore the unknown continent, found and grow " +
        "colonies, build an economy, deal with the native nations and rival powers, and ultimately win your " +
        "colonists' hearts toward liberty so you can declare independence and defeat the King's army. Winning the " +
        "War of Independence is the headline victory; optional alternative victories and a running score reward " +
        "every play style.\n\n" +

        "[b]Explore[/b]\n" +
        "You begin with a ship and your first settlers. Click a unit to select it, then click an adjacent tile to " +
        "move it (or arm a destination with [b]G[/b] and let it travel over several turns). Units have movement " +
        "points each turn; open land is cheap, forest and hills cost more, roads and rivers are fast. Moving reveals " +
        "the map around your units. Watch for lost-city rumours, and sail to the high seas at the map edge to cross " +
        "between Europe and the New World.\n\n" +

        "[b]Found and run colonies[/b]\n" +
        "Move a colonist onto a good tile and press [b]B[/b] to found a colony. Click a colony to open its screen, " +
        "where you assign colonists to surrounding tiles to gather food and raw goods, put others to work inside " +
        "buildings to make finished products, queue construction, and manage food and defence. Plain colonists learn " +
        "to become experts by working, by schooling, or by learning from a native settlement.\n\n" +

        "[b]The economy and Europe[/b]\n" +
        "Open the Europe screen with [b]E[/b] to sell the goods your ships carry back, buy goods, ships and trained " +
        "specialists, and recruit colonists from the docks. Prices move with supply and demand — flooding the market " +
        "drives a price down — and the King takes a tax cut that climbs over the game. Wagon trains haul goods " +
        "overland, trade routes automate hauling and trading, and a custom house can sell surplus to Europe with no " +
        "ship at all. New colonists also arrive through immigration, driven by the crosses your churches produce.\n\n" +

        "[b]Liberty and independence[/b]\n" +
        "Your town halls produce liberty bells, which raise each colony's Sons of Liberty membership (a production " +
        "bonus) and accumulate nationally toward recruiting Founding Fathers into your Continental Congress — each a " +
        "lasting benefit. Once at least half your colonists are rebels and you hold a port, you may declare " +
        "independence: Europe closes, some veterans rise as Continental regulars, and the Royal Expeditionary Force " +
        "sails in to crush you. Hold out and a foreign ally eventually lands to help.\n\n" +

        "[b]Natives[/b]\n" +
        "Several native nations live on the map. Meet their chiefs, trade, learn skills, station missionaries, and " +
        "buy or take the land you settle. Each settlement has a mood toward you: raiding, land-grabbing and crowding " +
        "raise their alarm; peaceful trade and time calm them. An angry nation sends braves against your colonies.\n\n" +

        "[b]Combat[/b]\n" +
        "Select an armed unit and click an enemy unit or colony to attack; combat resolves at once. Arm colonists " +
        "with muskets to make soldiers and add horses for dragoons; artillery hits hard but is fragile in the open. " +
        "Terrain, fortification and a colony's stockade/fort/fortress all favour the defender. Ships fight at sea and " +
        "limp back to Europe to repair when badly damaged.\n\n" +

        "[b]Controls and keybindings[/b]\n" +
        "The game is played mostly with the mouse: [b]left-click[/b] a unit to select it, a tile to move it or give " +
        "an order, and a colony to open its screen. [b]Right-click[/b] a tile for a small menu (pick a stacked unit, " +
        "centre here, go to here). Pan the view by dragging with the [b]right or middle mouse button[/b], by the " +
        "[b]mouse wheel[/b] to zoom, or by holding the [b]arrow keys[/b]. Press [b]F1[/b] in-game for the on-screen " +
        "key legend at any time. The keyboard shortcuts:\n\n" +
        "  [b]Enter[/b] — End turn\n" +
        "  [b]Space[/b] — Skip the selected unit this turn\n" +
        "  [b]W[/b] — Select the next unit needing orders\n" +
        "  [b]G[/b] — Go to (then click a destination tile)\n" +
        "  [b]B[/b] — Build (found) a colony\n" +
        "  [b]D[/b] — Disband the selected unit (asks to confirm)\n" +
        "  [b]E[/b] — Open the Europe screen\n" +
        "  [b]L[/b] — Find one of your settlements\n" +
        "  [b]F[/b] — Open the Founding Fathers panel\n" +
        "  [b]C[/b] — Open the Colopedia (in-game reference)\n" +
        "  [b]Ctrl+C[/b] — Centre the camera on the selected unit\n" +
        "  [b]N[/b] — New map\n" +
        "  [b]Ctrl+S[/b] / [b]Ctrl+O[/b] — Save / Load (named slots)\n" +
        "  [b]F5[/b] / [b]F9[/b] — Quick-save / Quick-load\n" +
        "  [b]F1[/b] — Toggle the on-screen key legend\n" +
        "  [b]Esc[/b] — Open the pause menu\n\n" +

        "For the full written guide, see the player manual ([b]docs/MANUAL.md[/b]).";

    /// <summary>Asks the host to dismiss the screen (via the <see cref="Closed"/> signal).</summary>
    private void OnBack() => EmitSignal(SignalName.Closed);
}
