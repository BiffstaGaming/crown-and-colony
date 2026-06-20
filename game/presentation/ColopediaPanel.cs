using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Specification;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The in-game reference panel (FreeCol's <c>Colopedia</c>), first category only: <b>Goods</b>. Lists every goods
/// type from the ruleset with its key facts — a readable name, what kind of good it is (food / farmed raw / refined
/// from another good / manufactured), and, for market-tradeable goods, its current sell (bid) and buy (ask) price
/// from the human's market.
/// <para>
/// Pure presentation (ADR-006): reads <see cref="Game.Ruleset"/> (<see cref="Ruleset.GoodsTypes"/>) and
/// <see cref="Game.Market"/> only, never mutates. Built programmatically into the fixed <c>VBox/Dynamic</c> shell,
/// like the other reference panels. Hidden by default; opened by the Colopedia button or the <b>C</b> key.
/// </para>
/// <para>
/// Scoped to the Goods category for this slice. The other FreeCol Colopedia categories (Terrain, Units, Buildings,
/// Founding Fathers, Nations, Resources, Concepts) are a follow-up — each is the same read-only list pattern over a
/// different <see cref="Ruleset"/> collection.
/// </para>
/// </summary>
public partial class ColopediaPanel : PanelContainer
{
    /// <summary>Opens the Colopedia on the Goods category over the current ruleset / market.</summary>
    public void Open(Game game)
    {
        GetNode<Label>("VBox/ColopediaTitle").Text = "Colopedia — Goods";
        var dynamic = GetNode<VBoxContainer>("VBox/Scroll/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free();
        }

        // FreeCol's Colopedia lists every goods type in ruleset order. One named row per good (Goods_{shortName})
        // so the L3 test can find a known good. Each row carries the good's facts; tradeable goods also show price.
        List<GoodsType> goods = game.Ruleset.GoodsTypes.ToList();
        foreach (GoodsType g in goods)
        {
            string price = g.IsTradeable
                ? $"sell {game.Market.BidPrice(g.Id)} / buy {game.Market.AskPrice(g.Id)} gold"
                : "not traded in Europe";
            dynamic.AddChild(new Label
            {
                Name = $"Goods_{g.ShortName}",
                Text = $"{Title(g.ShortName)} — {Kind(g)}  ·  {price}",
            });
        }

        Show();
    }

    /// <summary>A short descriptor of the good's role: food, a farmed raw good, refined from another good, or manufactured.</summary>
    private static string Kind(GoodsType g)
    {
        if (g.IsFood)
        {
            return "food";
        }
        if (g.MadeFrom is { } raw)
        {
            return $"refined from {Title(raw[(raw.LastIndexOf('.') + 1)..])}";
        }
        if (g.IsFarmed)
        {
            return g.IsNewWorldGoods ? "New World raw good" : "raw good";
        }
        return "manufactured";
    }

    /// <summary>Title-cases a short id for display (e.g. <c>tradeGoods</c> → <c>Trade Goods</c>, <c>food</c> → <c>Food</c>).</summary>
    private static string Title(string shortName)
    {
        // Split camelCase into words, then capitalise each (mirrors the colony screen's display helper, kept local).
        var words = new List<string>();
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
        return string.Join(" ", words.Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
