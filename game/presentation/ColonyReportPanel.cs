using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The empire <b>colony report</b> (FreeCol <c>ReportColonyPanel</c>, batch 1 — Colony / Production / Requirements):
/// one row per human colony summarising its population, Sons-of-Liberty %, production bonus, net food, what it is
/// building (with the materials still required), and its net per-turn production. Pure presentation (ADR-006) — it
/// only reads <see cref="Game"/> oracles (<see cref="Game.ColonyNetProduction"/>, <see cref="Game.DescribeBuildable"/>,
/// per-colony state) and never mutates. Built programmatically into the fixed <c>VBox/Dynamic</c> shell, like the
/// other panels. A dedicated Trade (market) report is a follow-up sub-report.
/// </summary>
public partial class ColonyReportPanel : PanelContainer
{
    private Game _game = null!;

    /// <summary>Opens the report over the current game state.</summary>
    public void Open(Game game)
    {
        _game = game;
        Rebuild();
        Show();
    }

    private void Rebuild()
    {
        GetNode<Label>("VBox/ReportTitle").Text = "Colony report";
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            child.Free();
        }

        List<Colony> colonies = _game.Colonies
            .Where(c => c.OwnerId == _game.HumanPlayer.PlayerId)
            .OrderBy(c => c.Id)
            .ToList();

        if (colonies.Count == 0)
        {
            dynamic.AddChild(new Label { Text = "You have no colonies yet.", HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }

        foreach (Colony c in colonies)
        {
            IReadOnlyDictionary<string, int> net = _game.ColonyNetProduction(c);
            int food = net.GetValueOrDefault(Colony.FoodId);

            string bonus = c.ProductionBonus != 0 ? $", bonus {Signed(c.ProductionBonus)}" : "";
            // The header label carries the per-colony name + summary; named for the L3 test to find by colony id.
            dynamic.AddChild(new Label
            {
                Name = $"Colony_{c.Id}",
                Text = $"{c.Name} — pop {c.Population}, SoL {c.SonsOfLiberty}%{bonus}",
            });
            dynamic.AddChild(new Label { Text = $"    Food {Signed(food)}/turn  ·  Building: {BuildingSummary(c)}" });
            dynamic.AddChild(new Label { Text = $"    Producing: {ProductionSummary(net)}" });
            dynamic.AddChild(new HSeparator());
        }
    }

    /// <summary>The current build + the materials still required to finish it (FreeCol's "Requirements"), or a dash.</summary>
    private string BuildingSummary(Colony colony)
    {
        if (colony.CurrentBuild is not { } id || _game.DescribeBuildable(id) is not { } info)
        {
            return "—";
        }
        IEnumerable<string> reqs = info.BuildCost
            .Select(o => $"{_game.Ruleset.Goods(o.GoodsId).ShortName} {colony.StoreOf(o.GoodsId)}/{o.Amount}");
        string needs = reqs.Any() ? $" (needs {string.Join(", ", reqs)})" : "";
        return $"{info.ShortName}{needs}";
    }

    /// <summary>The colony's non-food net production, signed and named, or "nothing".</summary>
    private string ProductionSummary(IReadOnlyDictionary<string, int> net)
    {
        List<string> parts = net
            .Where(kv => kv.Key != Colony.FoodId && kv.Value != 0)
            .OrderBy(kv => kv.Key, System.StringComparer.Ordinal)
            .Select(kv => $"{Signed(kv.Value)} {_game.Ruleset.Goods(kv.Key).ShortName}")
            .ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : "nothing";
    }

    private static string Signed(int n) => (n > 0 ? "+" : "") + n;
}
