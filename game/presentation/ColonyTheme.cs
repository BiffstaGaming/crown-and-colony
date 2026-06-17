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

    /// <summary>The shared colony theme, built once and cached.</summary>
    public static Theme Get() => _cached ??= Build();

    private static Theme Build()
    {
        var theme = new Theme { DefaultFontSize = 15 };
        if (ResourceLoader.Exists(UiFontPath))
        {
            theme.DefaultFont = GD.Load<FontFile>(UiFontPath); // cascades to every control; null-guarded for CI before import
        }
        StyleButtons(theme);
        StyleOptionButtonAndPopup(theme);
        StyleBuildingCell(theme);
        StyleLabels(theme);
        StyleSeparators(theme);
        StyleHeaderVariation(theme, "SectionHeader", 17);
        StyleHeaderVariation(theme, "ColonyTitle", 28);
        return theme;
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
