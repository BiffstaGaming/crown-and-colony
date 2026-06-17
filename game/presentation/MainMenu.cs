using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The game's entry shell — the main-menu / title screen shown before any game starts (it replaces the old
/// boot-straight-into-a-running-game behaviour; <c>project.godot</c>'s <c>run/main_scene</c> now points here). It
/// presents <b>New Game</b> / <b>Load Game</b> / <b>Settings</b> / <b>Quit</b> over FreeCol's antique New-World map
/// backdrop, framed by the same carved-wood border and brown-parchment skin as the colony screen
/// (<see cref="ColonyArt"/> + <see cref="ColonyTheme"/>) so the two screens share one look.
/// </summary>
/// <remarks>
/// Presentation-only (ADR-006): the menu owns no game rules. <b>New Game</b> simply loads the in-game scene, which
/// builds a fresh game exactly as the app did on boot before this screen existed. <b>Load Game</b> and <b>Settings</b>
/// are wired in later slices (ClickUp <c>86d3c9y5y</c> and <c>86d3ck67h</c>); their buttons stay disabled until then.
/// </remarks>
public partial class MainMenu : Control
{
    /// <summary>The scene loaded when the player starts a new game (the in-game map/controller scene).</summary>
    public const string GameScenePath = "res://scenes/main.tscn";

    /// <summary>FreeCol's antique New-World map (GPL v2 — see <c>assets/freecol/PROVENANCE.md</c>), the menu backdrop.</summary>
    private const string BackdropPath = "res://assets/freecol/ui/map.jpg";

    /// <summary>Builds the menu's look (theme, backdrop, parchment skin, carved-wood frame) and wires the buttons.</summary>
    public override void _Ready()
    {
        Theme = ColonyTheme.Get(); // cohesive parchment/wood styling cascades to every child

        if (ResourceLoader.Exists(BackdropPath))
        {
            GetNode<TextureRect>("Background").Texture = GD.Load<Texture2D>(BackdropPath);
        }

        GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", BuildParchmentSkin());

        if (ColonyArt.ColonyBorder() is { } borderTex)
        {
            GetNode<NinePatchRect>("Border").Texture = borderTex;
        }

        GetNode<Button>("Panel/VBox/NewGameButton").Pressed += OnNewGame;
        GetNode<Button>("Panel/VBox/QuitButton").Pressed += OnQuit;
    }

    /// <summary>Starts a new game by loading the in-game scene (which builds a fresh game, as the app did on boot before).</summary>
    private void OnNewGame() => GetTree().ChangeSceneToFile(GameScenePath);

    /// <summary>Exits the application.</summary>
    private void OnQuit() => GetTree().Quit();

    /// <summary>
    /// The brown-parchment panel fill. Mirrors <c>ColonyPanel.BuildPanelBackground</c>: FreeCol's small brown-parchment
    /// tile (<see cref="ColonyArt.PanelParchment"/>) is tiled rather than stretched (it is only 291×295), and inset
    /// 26px so the content clears the 23px carved-wood frame. Falls back to a warm solid fill if the asset is absent,
    /// so the panel is still opaque in CI before the parchment is imported.
    /// </summary>
    private static StyleBox BuildParchmentSkin()
    {
        if (ColonyArt.PanelParchment() is { } parchment)
        {
            var skin = new StyleBoxTexture
            {
                Texture = parchment,
                AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Tile,
                AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Tile,
            };
            skin.SetContentMarginAll(26);
            return skin;
        }
        var flat = new StyleBoxFlat { BgColor = new Color(0.18f, 0.12f, 0.07f) };
        flat.SetContentMarginAll(26);
        return flat;
    }
}
