using System;
using System.Collections.Generic;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The <b>Historical Figure attained</b> celebration modal (WS2.7, doc 19's "Historical Figure attained" template): when a
/// human elects a new Founding Father / Australian Pioneer, this small parchment popup marks the moment — the figure's name
/// and the body they join (the variant's <see cref="GameVariant.CongressName"/> — "Federation Convention" for Australia),
/// their category, a lore hook, and a plain-English summary of the perk they bring. Before this, the join was only a buried
/// line in the Reports → History log.
///
/// <para>Pure presentation (ADR-006): the election happens in <c>GameLogic</c> (<see cref="Game.HumanPlayer"/>'s Congress
/// grows); <see cref="GameController"/> detects the new Congress member each refresh and opens this modal. Built entirely in
/// code (no scene file), added to the UI layer, mirroring <see cref="FederationPanel"/>. The controller only shows it for the
/// Australian Federation (gated on <c>Ruleset.VictoryFederation</c>), so the classic game is unchanged. Hidden by default.</para>
/// </summary>
public partial class PioneerAttainedPanel : PanelContainer
{
    // Palette — re-declared locally from ColonyTheme (private), the EuropePanel/FederationPanel precedent.
    private static readonly Color WoodMid = Color.FromHtml("7a4f30");
    private static readonly Color WoodDark = Color.FromHtml("4a2e1a");
    private static readonly Color Gold = Color.FromHtml("c9a24b");
    private static readonly Color Ink = Color.FromHtml("2b1d10");
    private static readonly Color ParchmentDark = Color.FromHtml("d9c290");
    private static readonly Color ParchmentEdge = Color.FromHtml("c2a86a");
    private static readonly Color TextOnWood = Color.FromHtml("f2e2c2");

    private Label _title = null!;
    private Label _category = null!;
    private Label _lore = null!;
    private Label _effect = null!;
    private TextureRect _portrait = null!;

    /// <summary>Builds the fixed shell (portrait + title + category + lore + perk + OK) on the parchment look. Hidden by default.</summary>
    public override void _Ready()
    {
        Name = "PioneerAttainedPanel";
        Theme = ColonyTheme.GetInGame();
        AddThemeStyleboxOverride("panel", ColonyArt.ParchmentSkin());
        Visible = false;
        CustomMinimumSize = new Vector2(460, 0);
        // Centre the modal by absolute position off the viewport rect (a code-built overlay CENTRE-ANCHORED corner-pins
        // against its 0-size CanvasLayer parent — the known modal-positioning trap). Recentred when shown + on resize.
        GetViewport().SizeChanged += Recenter;

        var vbox = new VBoxContainer { Name = "VBox" };
        vbox.AddThemeConstantOverride("separation", 8);
        AddChild(vbox);

        _portrait = new TextureRect
        {
            Name = "Portrait",
            Visible = false,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(120, 142),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        vbox.AddChild(_portrait);

        // Pin an explicit width on the autowrapping labels: an autowrap Label with no width constraint reports its minimum
        // height as if it were one character wide (every word on its own line), which would balloon the panel's height.
        _title = new Label { Name = "PioneerTitle", HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "ColonyTitle", AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        vbox.AddChild(_title);

        _category = new Label { Name = "PioneerCategory", HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "SectionHeader" };
        vbox.AddChild(_category);

        _lore = new Label { Name = "PioneerLore", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        _lore.AddThemeColorOverride("font_color", new Color(Ink, 0.82f));
        vbox.AddChild(_lore);

        vbox.AddChild(new HSeparator());

        // The perk, in a gold-edged parchment chip so the gameplay reward reads as the takeaway.
        var chip = new PanelContainer { Name = "PerkChip" };
        chip.AddThemeStyleboxOverride("panel", Flat(ParchmentDark, Gold, 1, 5, 12, 6));
        _effect = new Label { Name = "PioneerPerk", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        chip.AddChild(_effect);
        vbox.AddChild(chip);

        var ok = new Button { Name = "OkButton", Text = "Welcome them" };
        ok.AddThemeStyleboxOverride("normal", Flat(WoodMid, Gold, 2, 5, 14, 6));
        ok.AddThemeStyleboxOverride("hover", Flat(WoodMid.Lightened(0.12f), Gold, 2, 5, 14, 6));
        ok.AddThemeStyleboxOverride("pressed", Flat(WoodDark, Gold, 2, 5, 14, 6));
        ok.AddThemeColorOverride("font_color", TextOnWood);
        ok.AddThemeColorOverride("font_hover_color", TextOnWood);
        ok.Pressed += Hide;
        vbox.AddChild(ok);
    }

    /// <summary>
    /// Opens the celebration for the just-elected <paramref name="fatherId"/> on <paramref name="game"/>. The title names
    /// the body joined (<paramref name="congressName"/> — the variant's Congress name); goods/unit names in the perk summary
    /// read through <paramref name="displayOverrides"/> (the Australian reskin). A no-op if the father is not in the ruleset.
    /// </summary>
    /// <param name="game">The running game (its ruleset resolves the father + the perk names).</param>
    /// <param name="fatherId">The elected founding-father / Pioneer id.</param>
    /// <param name="congressName">The variant's electing-body name (e.g. "Federation Convention").</param>
    /// <param name="displayOverrides">The variant's display-name overrides for goods/units in the perk summary, or null.</param>
    public void Open(Game game, string fatherId, string congressName, IReadOnlyDictionary<string, string>? displayOverrides)
    {
        FoundingFather father = game.Ruleset.Father(fatherId);
        string name = Naming.Humanize(father.ShortName, displayOverrides);

        _title.Text = $"{name} joins the {congressName}";
        _category.Text = CategoryName(father.Type);
        _lore.Text = $"{name} joins the national story.";
        _effect.Text = $"Brings: {EffectSummary(game, father, displayOverrides)}";

        if (ColonyArt.FatherPortrait(father.ShortName) is { } portrait)
        {
            _portrait.Texture = portrait;
            _portrait.Visible = true;
        }
        else
        {
            _portrait.Visible = false; // no portrait sourced yet → text-only (graceful)
        }

        Show();
        Callable.From(Recenter).CallDeferred(); // centre once the panel has laid out to its real size (it is 0-sized while hidden)
    }

    /// <summary>
    /// Sizes the modal to hug its content and centres it over the viewport by absolute position. A code-built overlay
    /// CENTRE-ANCHORED corner-pins against its 0-size <see cref="CanvasLayer"/> parent (the known modal-positioning trap),
    /// and a <see cref="Control"/>'s <see cref="Control.Size"/> never auto-shrinks back to its content — so we force it to
    /// the combined minimum size and place it explicitly. Called deferred on show + on viewport resize (the size is only
    /// meaningful once the panel has laid out).
    /// </summary>
    private void Recenter()
    {
        Vector2 want = GetCombinedMinimumSize(); // content-hug size (CustomMinimumSize pins the width; height follows the content)
        Size = want;
        Vector2 vp = GetViewportRect().Size;
        Position = new Vector2(Mathf.Max(0f, (vp.X - want.X) / 2f), Mathf.Max(0f, (vp.Y - want.Y) / 2f));
    }

    /// <summary>The Australian category name for a Pioneer's <see cref="FatherType"/> (the five design domains, docs 07–12).</summary>
    private static string CategoryName(FatherType type) => type switch
    {
        FatherType.Trade => "Industry & Commerce",
        FatherType.Exploration => "Exploration & Infrastructure",
        FatherType.Military => "Settlement, Administration & Defence",
        FatherType.Political => "Democracy & Federation",
        FatherType.Religious => "Social Reform & First Nations",
        _ => type.ToString(),
    };

    /// <summary>
    /// A plain-English summary of the perk a father brings — the free units/buildings, boycott lift / colony reveal, and a
    /// count of standing bonuses / special abilities (mirrors the Colopedia's father-effect line). Names read through the
    /// variant's display overrides so a Pioneer's "free Shepherd" / "Wool" perk reads in Australian terms.
    /// </summary>
    private static string EffectSummary(Game game, FoundingFather father, IReadOnlyDictionary<string, string>? overrides)
    {
        var parts = new List<string>();
        if (father.LiftsBoycotts)
        {
            parts.Add("lifts all trade boycotts");
        }
        if (father.RevealsAllColonies)
        {
            parts.Add("reveals every colony");
        }
        foreach (string unitId in father.FreeUnits)
        {
            parts.Add($"a free {Naming.Humanize(game.Ruleset.Unit(unitId).ShortName, overrides)}");
        }
        foreach (string buildingId in father.FreeBuildings)
        {
            parts.Add($"a free {Naming.Humanize(game.Ruleset.Building(buildingId).ShortName, overrides)} in every colony");
        }
        if (father.Modifiers.Count > 0)
        {
            parts.Add($"{father.Modifiers.Count} standing bonus{(father.Modifiers.Count == 1 ? "" : "es")}");
        }
        if (father.Abilities.Count > 0)
        {
            parts.Add($"{father.Abilities.Count} special abilit{(father.Abilities.Count == 1 ? "y" : "ies")}");
        }
        return parts.Count > 0 ? string.Join(", ", parts) : "their standing in the movement";
    }

    /// <summary>A house-style rounded, anti-aliased <see cref="StyleBoxFlat"/> matching the theme's cells/buttons.</summary>
    private static StyleBoxFlat Flat(Color bg, Color border, int borderWidth, int radius, int marginH, int marginV)
    {
        var box = new StyleBoxFlat { BgColor = bg, BorderColor = border, AntiAliasing = true };
        box.SetBorderWidthAll(borderWidth);
        box.SetCornerRadiusAll(radius);
        box.ContentMarginLeft = box.ContentMarginRight = marginH;
        box.ContentMarginTop = box.ContentMarginBottom = marginV;
        return box;
    }
}
