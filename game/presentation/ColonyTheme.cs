using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The colony screen's cohesive parchment/wood theme (ADR-006 presentation-only). Built once and cached, then
/// assigned to <see cref="ColonyPanel"/> so it cascades to every child Control — wood-toned buttons + dropdowns,
/// bordered building cells, dark-ink labels with a faint light halo for legibility on parchment, engraved
/// separators, and a size/colour hierarchy for the title and section headers.
/// </summary>
/// <remarks>
/// It deliberately does NOT register a <c>PanelContainer/panel</c> stylebox: the panel's own opaque parchment skin
/// (<c>ColonyPanel.EnsureOpaqueBackground</c>, a local override) owns that slot and always wins anyway. The UI font
/// is <b>Cardo</b> (SIL OFL, <c>assets/fonts/</c>) set as the theme default, with size + colour layered on top for the
/// hierarchy. (FreeCol's ShadowedBlack ships with no stated licence, so it is not used.)
/// </remarks>
public static class ColonyTheme
{
    /// <summary>
    /// Which variant's visual skin the theme is built in (WS2.1). <see cref="Skin.Classic"/> is the parchment/European-oak
    /// look every screen has had until now and is the default, so a classic game — and every existing visual golden — is
    /// untouched. <see cref="Skin.Australia"/> re-tones the same design language for the Australian campaign.
    /// </summary>
    public enum Skin
    {
        /// <summary>The original parchment + European dark-oak palette. The default; classic goldens are captured against it.</summary>
        Classic,

        /// <summary>The Australian re-tone: sun-bleached paper, red-gum/jarrah timbers, Federation blue accent.</summary>
        Australia,
    }

    /// <summary>
    /// The active skin. Set once when a variant is chosen (beside <see cref="ColonyArt.VariantArtRoot"/>) and read by
    /// <see cref="Get"/> / <see cref="GetInGame"/>. Assigning a different skin drops the cached themes so the next call
    /// rebuilds — panels pick the new theme up on their next open.
    /// </summary>
    public static Skin ActiveSkin
    {
        get => _skin;
        set
        {
            if (_skin != value)
            {
                _skin = value;
                _cached = null;
                _cachedInGame = null;
            }
        }
    }

    private static Skin _skin = Skin.Classic;

    // ── Palette ─────────────────────────────────────────────────────────────────────────────────────────────
    // Two tunings of ONE design language, not two designs: the same parchment-over-timber structure, re-toned. The
    // Australian values are the WS2.1 art direction (docs/systems/visual-identity.md):
    //   • paper is sun-bleached rather than European cream — the light is harsher here;
    //   • timbers move from dark European oak to the red-brown of jarrah / red gum, the colonial building timbers;
    //   • the metallic accent moves from gold to FEDERATION BLUE, the Southern Cross field — the campaign's own motif,
    //     and the one colour that instantly separates an Australian screen from a FreeCol one.

    private static Color Parchment => _skin == Skin.Australia
        ? Color.FromString("#EDE0BC", Colors.Beige)   // sun-bleached paper
        : Color.FromString("#E8D9B0", Colors.Beige);

    private static Color ParchmentDark => _skin == Skin.Australia
        ? Color.FromString("#DCC79A", Colors.Beige)
        : Color.FromString("#D9C290", Colors.Beige);

    private static Color ParchmentEdge => _skin == Skin.Australia
        ? Color.FromString("#C6A874", Colors.Beige)
        : Color.FromString("#C2A86A", Colors.Beige);

    private static Color WoodDark => _skin == Skin.Australia
        ? Color.FromString("#4A2118", Colors.Brown)   // jarrah — redder, less yellow than oak
        : Color.FromString("#4A2E1A", Colors.Brown);

    private static Color WoodMid => _skin == Skin.Australia
        ? Color.FromString("#7E3F2C", Colors.Brown)   // red gum
        : Color.FromString("#7A4F30", Colors.Brown);

    private static Color WoodLight => _skin == Skin.Australia
        ? Color.FromString("#9E5A3E", Colors.Brown)
        : Color.FromString("#9A6A42", Colors.Brown);

    private static Color Ink => _skin == Skin.Australia
        ? Color.FromString("#2A1A12", Colors.Black)
        : Color.FromString("#2B1D10", Colors.Black);

    private static Color InkTitle => _skin == Skin.Australia
        ? Color.FromString("#382012", Colors.Black)
        : Color.FromString("#3A2410", Colors.Black);

    /// <summary>The accent used for focus rings, title halos and pressed text — gold for classic, Federation blue for Australia.</summary>
    private static Color Gold => _skin == Skin.Australia
        ? Color.FromString("#2E5C8A", Colors.SteelBlue) // Southern Cross field
        : Color.FromString("#C9A24B", Colors.Goldenrod);

    private static Color TextOnWood => _skin == Skin.Australia
        ? Color.FromString("#F4E7CC", Colors.White)
        : Color.FromString("#F2E2C2", Colors.White);

    // ── The six colony regions (WS2.1) ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A distinguishable colour per Federation colony region — the design's "six distinguishable colony-region colours".
    /// Chosen to stay legible side by side on parchment (no two adjacent hues) and to sit in the same muted, period range
    /// as the rest of the palette rather than reading as modern flat UI. Keyed by <c>Region.Key</c>; an unknown region
    /// falls back to the neutral timber tone, so this can never throw on a variant that adds regions.
    /// </summary>
    public static Color ColonyRegionColor(string regionKey) => regionKey switch
    {
        "model.region.newSouthWales" => Color.FromString("#2E5C8A", Colors.SteelBlue),   // Federation blue
        "model.region.victoria" => Color.FromString("#3F6B4A", Colors.DarkSeaGreen),     // bush green
        "model.region.queensland" => Color.FromString("#8A5A2E", Colors.Sienna),         // maroon-brown
        "model.region.southAustralia" => Color.FromString("#A03A2E", Colors.IndianRed),  // ochre red
        "model.region.tasmania" => Color.FromString("#4A6E74", Colors.CadetBlue),        // cool island slate
        "model.region.westernAustralia" => Color.FromString("#B8862E", Colors.DarkGoldenrod), // goldfields
        _ => Color.FromString("#7A4F30", Colors.Brown),
    };

    /// <summary>The bundled UI font (SIL OFL — see <c>assets/fonts/PROVENANCE.md</c>); loaded if present, else the engine default.</summary>
    private const string UiFontPath = "res://assets/fonts/Cardo-Regular.ttf";

    private static Theme? _cached;
    private static Theme? _cachedInGame;

    /// <summary>
    /// The shared parchment/wood theme for menus and dialogs (main menu, settings, pause, about, save/load, …) — the
    /// baseline sizing the menu goldens are captured against. Built once and cached.
    /// </summary>
    public static Theme Get() => _cached ??= Build(bodySize: 15, embolden: 0f, sectionSize: 17, titleSize: 28);

    /// <summary>
    /// The legibility variant for the dense in-game info screens (<see cref="ColonyPanel"/> + <see cref="EuropePanel"/>):
    /// a larger body size and a synthetically-bolded face (Cardo ships Regular-only) for bolder, higher-contrast text on
    /// the parchment. Menus/dialogs keep <see cref="Get"/> so their goldens are unaffected. Built once and cached.
    /// </summary>
    public static Theme GetInGame() => _cachedInGame ??= Build(bodySize: 17, embolden: 0.4f, sectionSize: 20, titleSize: 30);

    private static Theme Build(int bodySize, float embolden, int sectionSize, int titleSize)
    {
        var theme = new Theme { DefaultFontSize = bodySize };
        if (ResourceLoader.Exists(UiFontPath))
        {
            FontFile baseFont = GD.Load<FontFile>(UiFontPath); // cascades to every control; null-guarded for CI before import
            // Cardo ships Regular-only, so a FontVariation adds synthetic weight (VariationEmbolden) for bolder,
            // higher-contrast strokes on the parchment without a separate bold .ttf — the in-game readability ask.
            theme.DefaultFont = embolden > 0f
                ? new FontVariation { BaseFont = baseFont, VariationEmbolden = embolden }
                : baseFont;
        }
        StyleButtons(theme);
        StyleOptionButtonAndPopup(theme);
        StyleBuildingCell(theme);
        StyleLabels(theme);
        StyleSeparators(theme);
        StyleDialogs(theme);
        StyleTooltip(theme);
        StyleHeaderVariation(theme, "SectionHeader", sectionSize);
        StyleHeaderVariation(theme, "ColonyTitle", titleSize);
        return theme;
    }

    /// <summary>
    /// Backs Godot's message dialogs with the parchment image instead of the engine's default flat-gray window. The
    /// <c>panel</c> stylebox lives on the <c>AcceptDialog</c> theme type, which <c>ConfirmationDialog</c> and
    /// <c>FileDialog</c> inherit — so one entry frames every code-built prompt (Sail to Europe, Disband, Cash-in
    /// treasure, quit confirmations, …). The dialog's message Label already reads as dark <see cref="Ink"/> from
    /// <see cref="StyleLabels"/>, so text stays legible on the parchment.
    /// </summary>
    private static void StyleDialogs(Theme theme)
    {
        theme.SetStylebox("panel", "AcceptDialog", ColonyArt.ParchmentSkin());
    }

    /// <summary>
    /// Styles Godot's hover tooltips (<c>TooltipPanel</c> + <c>TooltipLabel</c>). Without this they rendered on the
    /// engine's dark default panel while inheriting the theme's dark <see cref="Ink"/> label font — black text on a dark
    /// box, unreadable (Chris's playtest: "Europe text isn't readable on the dark brown background with black writing",
    /// 86d3jy0rn — the tooltips shown when hovering the recruit/buy buttons). A dark wood panel with cream text (matching
    /// the buttons) reads clearly. The visual goldens never hover, so this adds no golden churn.
    /// </summary>
    private static void StyleTooltip(Theme theme)
    {
        var panel = WoodBox(WoodDark, Gold, borderW: 1);
        panel.SetContentMarginAll(6);
        theme.SetStylebox("panel", "TooltipPanel", panel);
        theme.SetColor("font_color", "TooltipLabel", TextOnWood); // cream on dark wood — high contrast
    }

    // Compact content margins (8 L/R, 4 top/bottom) so the tiny tile ✕ and the building +/− buttons stay small.
    private static StyleBoxFlat WoodBox(Color bg, Color border, int borderW = 2, int radius = 5)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border, AntiAliasing = true };
        sb.SetBorderWidthAll(borderW);
        sb.SetCornerRadiusAll(radius);
        sb.SetContentMarginAll(4);
        sb.ContentMarginLeft = 8;
        sb.ContentMarginRight = 8;
        return sb;
    }

    private static void StyleButtons(Theme theme)
    {
        theme.SetStylebox("normal", "Button", WoodBox(WoodMid, WoodDark));
        theme.SetStylebox("hover", "Button", WoodBox(WoodLight, WoodDark));
        theme.SetStylebox("pressed", "Button", WoodBox(WoodDark, WoodDark));
        theme.SetStylebox("disabled", "Button", WoodBox(ParchmentEdge, WoodDark));

        var focus = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), DrawCenter = false, BorderColor = Gold, AntiAliasing = true };
        focus.SetBorderWidthAll(2);
        focus.SetCornerRadiusAll(5);
        theme.SetStylebox("focus", "Button", focus);

        theme.SetColor("font_color", "Button", TextOnWood);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Gold);
        theme.SetColor("font_disabled_color", "Button", new Color(TextOnWood, 0.5f));
    }

    private static void StyleOptionButtonAndPopup(Theme theme)
    {
        StyleBoxFlat normal = WoodBox(WoodMid, WoodDark);
        normal.ContentMarginRight = 26; // room for the dropdown arrow
        theme.SetStylebox("normal", "OptionButton", normal);
        theme.SetStylebox("hover", "OptionButton", WoodBox(WoodLight, WoodDark));
        theme.SetStylebox("pressed", "OptionButton", WoodBox(WoodDark, WoodDark));
        theme.SetColor("font_color", "OptionButton", TextOnWood);

        var popupPanel = new StyleBoxFlat { BgColor = Parchment, BorderColor = WoodDark };
        popupPanel.SetBorderWidthAll(2);
        popupPanel.SetCornerRadiusAll(4);
        popupPanel.SetContentMarginAll(4);
        theme.SetStylebox("panel", "PopupMenu", popupPanel);
        theme.SetColor("font_color", "PopupMenu", Ink);
        theme.SetColor("font_hover_color", "PopupMenu", InkTitle);
        var hoverBox = new StyleBoxFlat { BgColor = ParchmentDark };
        hoverBox.SetCornerRadiusAll(3);
        theme.SetStylebox("hover", "PopupMenu", hoverBox);
    }

    private static void StyleBuildingCell(Theme theme)
    {
        var cell = new StyleBoxFlat { BgColor = ParchmentDark, BorderColor = ParchmentEdge, AntiAliasing = true };
        cell.SetBorderWidthAll(1);
        cell.SetCornerRadiusAll(4);
        cell.SetContentMarginAll(6);
        theme.SetTypeVariation("BuildingCell", "PanelContainer");
        theme.SetStylebox("panel", "BuildingCell", cell);
    }

    private static void StyleLabels(Theme theme)
    {
        theme.SetColor("font_color", "Label", Ink);
        theme.SetColor("font_outline_color", "Label", new Color(Parchment, 0.85f));
        theme.SetConstant("outline_size", "Label", 1);
        // A RichTextLabel does NOT inherit Label's font_color — it has its own "default_color" theme item whose engine
        // default is white, so without this the Help/About bodies (the only RichTextLabels, both on parchment) render
        // near-white on cream and are unreadable. Dark ink matches the surrounding Labels. (86d3jy0rn playtest audit.)
        theme.SetColor("default_color", "RichTextLabel", Ink);
    }

    private static void StyleSeparators(Theme theme)
    {
        var rule = new StyleBoxFlat { BgColor = new Color(WoodDark, 0.5f) };
        rule.ContentMarginTop = 1;
        rule.ContentMarginBottom = 1;
        theme.SetStylebox("separator", "HSeparator", rule);
        theme.SetConstant("separation", "HSeparator", 6);
    }

    // A Label type-variation with a bigger size + warm title ink and a faint gold halo (engine default font face).
    private static void StyleHeaderVariation(Theme theme, string variation, int size)
    {
        theme.SetTypeVariation(variation, "Label");
        theme.SetFontSize("font_size", variation, size);
        theme.SetColor("font_color", variation, InkTitle);
        theme.SetColor("font_outline_color", variation, new Color(Gold, 0.55f));
        theme.SetConstant("outline_size", variation, 1);
    }
}
