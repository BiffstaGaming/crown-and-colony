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
    // ── Palette (single tuning point) ───────────────────────────────────────────────────────────────────────
    private static readonly Color Parchment = Color.FromString("#E8D9B0", Colors.Beige);
    private static readonly Color ParchmentDark = Color.FromString("#D9C290", Colors.Beige);
    private static readonly Color ParchmentEdge = Color.FromString("#C2A86A", Colors.Beige);
    private static readonly Color WoodDark = Color.FromString("#4A2E1A", Colors.Brown); // borders
    private static readonly Color WoodMid = Color.FromString("#7A4F30", Colors.Brown);   // button face
    private static readonly Color WoodLight = Color.FromString("#9A6A42", Colors.Brown); // hover
    private static readonly Color Ink = Color.FromString("#2B1D10", Colors.Black);        // body text on parchment
    private static readonly Color InkTitle = Color.FromString("#3A2410", Colors.Black);
    private static readonly Color Gold = Color.FromString("#C9A24B", Colors.Goldenrod);
    private static readonly Color TextOnWood = Color.FromString("#F2E2C2", Colors.White);

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
