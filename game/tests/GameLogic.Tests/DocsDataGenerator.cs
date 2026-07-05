using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CrownAndColony.GameLogic.Specification;
using Xunit;

namespace CrownAndColony.GameLogic.Tests;

/// <summary>
/// Documentation data generator — NOT a regression test. When run with <c>DOCS_DATA=1</c> it loads the classic
/// ruleset through the game's own parser (so every number is exactly what the game runs, conversions already applied)
/// and writes Markdown reference tables for the player Handbook's "Tables &amp; Data" appendix. Normal test runs skip it.
///   $env:DOCS_DATA='1'; dotnet test game/tests/GameLogic.Tests/GameLogic.Tests.csproj --filter FullyQualifiedName~DocsDataGenerator
/// The founding-father effect text is read from FreeCol's shipped strings file.
/// </summary>
public class DocsDataGenerator
{
    private const string OutFile = "C:/Users/Chris/Code/Colonization/docs/guide/23-tables-data.md";
    private const string MessagesFile =
        "C:/Users/Chris/Code/Colonization/freecol/data/strings/FreeColMessages.properties";

    private static bool Enabled => Environment.GetEnvironmentVariable("DOCS_DATA") == "1";

    [Fact]
    public void GenerateHandbookTables()
    {
        if (!Enabled) return;
        Ruleset r = Ruleset.LoadClassic();
        Dictionary<string, string> msg = LoadProperties(MessagesFile);

        var sb = new StringBuilder();
        sb.AppendLine("# Tables & Data");
        sb.AppendLine();
        sb.AppendLine("!!! note \"Generated from the game's own rules\"");
        sb.AppendLine("    Every table on this page is generated directly from the game's ruleset data, so it always matches");
        sb.AppendLine("    the version you are playing. For anything not listed here — or the exact figure in *your* game — the");
        sb.AppendLine("    in-game **Colopedia** (press `C`) is the live reference.");
        sb.AppendLine();

        Section(sb, "Terrain",
            "Move cost is **tiles per turn** to cross (open ground costs 1). Defence is the bonus a defender gains standing there. "
            + "The centre yield is what a colony's own centre tile produces automatically, without a worker. See [The Map & Terrain](04-map-terrain.md).",
            Terrain(r));
        Section(sb, "Goods & the market",
            "**Sell** is the gold Europe pays you per unit at the start of the game; **Buy** is what it charges you. These are "
            + "starting prices — they slide with supply and demand as everyone trades. See [Trade & the Europe Screen](12-trade-europe.md) "
            + "and [Goods & Production Chains](10-goods-production.md).",
            Goods(r));
        Section(sb, "Buildings",
            "**Hammers** and **Tools** are the build cost; **Workers** is how many colonists can work inside; **Req. pop** is the "
            + "colony size needed before you can build it. See [Buildings](11-buildings.md).",
            Buildings(r));
        Section(sb, "Founding Fathers",
            "All 25 fathers and exactly what each one does, grouped by field. You earn them with liberty bells and choose one "
            + "candidate per field — see [Founding Fathers & the Continental Congress](14-founding-fathers.md).",
            FoundingFathers(r, msg));
        Section(sb, "Units",
            "**Offence** and **Defence** are a unit's own base combat strength; a **combat role** (soldier, dragoon, scout) adds to "
            + "them — see the roles table below. **Move** is tiles per turn, **Cargo** the hold slots, **Europe price** the cost to buy "
            + "or train, and **Build** the hammers/tools to construct one in a colony. See [Units & Movement](05-units-movement.md) "
            + "and [Combat: Land & Naval](17-combat.md).",
            Units(r));
        Section(sb, "Native nations",
            "The eight indigenous nations, their settlement kind, how easily they take offence, and the expert skills their people "
            + "can teach a visiting colonist. See [Natives & Diplomacy](15-natives-diplomacy.md).",
            Natives(r));
        Section(sb, "Difficulty levels",
            "The five levels, from **Discoverer** (gentlest) to **Viceroy** (harshest), with **Conquistador** the default. Higher "
            + "levels raise the liberty cost of each Founding Father, let the King tax you higher, make native land dearer, and make "
            + "converts rarer. See [Starting a New Game](02-new-game.md).",
            Difficulty());

        File.WriteAllText(OutFile, sb.ToString().Replace("\r\n", "\n").TrimEnd() + "\n");
        Console.WriteLine($"[DOCS_DATA] wrote {OutFile} ({sb.Length} chars)");
    }

    private static void Section(StringBuilder sb, string title, string intro, string table)
    {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine(intro);
        sb.AppendLine();
        sb.AppendLine(table.TrimEnd());
        sb.AppendLine();
    }

    // ---- tables ---------------------------------------------------------

    private static string Terrain(Ruleset r)
    {
        int b = r.MovementConstants.BaseMoveCost;
        var sb = new StringBuilder();
        sb.AppendLine("| Terrain | Move (tiles) | Defence | Work turns | Can settle | Centre yield (unattended) |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (TerrainType t in r.TerrainTypes)
        {
            string move = t.IsWater ? "— (water)" : Frac(t.MoveCost, b);
            string def = t.DefenceBonus > 0 ? $"+{t.DefenceBonus:0}%" : "—";
            var centre = t.Productions.FirstOrDefault(p => p.Unattended);
            string yield = centre is null || centre.Outputs.Count == 0
                ? "—"
                : string.Join(", ", centre.Outputs.Select(o => $"{Title(Short(o.GoodsId))} {o.Amount}"));
            sb.AppendLine($"| {Title(t.ShortName)} | {move} | {def} | {t.WorkTurns} | {(t.CanSettle ? "Yes" : "No")} | {yield} |");
        }
        return sb.ToString();
    }

    private static string Goods(Ruleset r)
    {
        // reverse chain: which good is made FROM this one
        var makes = new Dictionary<string, string>();
        foreach (GoodsType g in r.GoodsTypes)
            if (g.MadeFrom is { } from) makes[from] = g.Id;

        var sb = new StringBuilder();
        sb.AppendLine("| Good | Kind | Sell | Buy | Made from | Makes |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (GoodsType g in r.GoodsTypes)
        {
            string kind = g.Market != null
                ? (g.MadeFrom != null ? "Manufactured" : g.IsNewWorldGoods ? "Raw (New World)" : "Raw")
                : g.IsFood ? "Food (not traded)"
                : g.MadeFrom != null ? "Manufactured (not traded)"
                : "Special";
            string sell = g.Market is { } m ? m.InitialPrice.ToString() : "—";
            string buy = g.Market is { } m2 ? m2.InitialAskPrice.ToString() : "—";
            string from = g.MadeFrom is { } f ? Title(Short(f)) : "—";
            string mk = makes.TryGetValue(g.Id, out string? made) ? Title(Short(made)) : "—";
            sb.AppendLine($"| {Title(g.ShortName)} | {kind} | {sell} | {buy} | {from} | {mk} |");
        }
        return sb.ToString();
    }

    private static string Buildings(Ruleset r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Building | Upgrades from | Hammers | Tools | Workers | Req. pop | Effect |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (BuildingType bld in r.BuildingTypes)
        {
            int hammers = Cost(bld.BuildCost, "model.goods.hammers");
            int tools = Cost(bld.BuildCost, "model.goods.tools");
            var conv = bld.Productions.FirstOrDefault(p => p.Inputs.Count > 0 && p.Outputs.Count > 0);
            var effect = new List<string>();
            if (conv != null)
                effect.Add($"{string.Join(" + ", conv.Inputs.Select(i => Title(Short(i.GoodsId))))} → {string.Join(" + ", conv.Outputs.Select(o => Title(Short(o.GoodsId))))}");
            else
            {
                var auto = bld.Productions.FirstOrDefault(p => p.Outputs.Count > 0);
                if (auto != null) effect.Add($"Makes {string.Join(" + ", auto.Outputs.Select(o => Title(Short(o.GoodsId))))}");
            }
            if (bld.DefenceBonus > 0) effect.Add($"Defence +{bld.DefenceBonus}%");
            if (bld.WarehouseStorage > 0) effect.Add($"Storage +{bld.WarehouseStorage}");
            if (bld.BellBonus > 0) effect.Add($"Bells +{bld.BellBonus}%");
            if (bld.MaximumSkill > 0) effect.Add($"Teaches (skill ≤{bld.MaximumSkill})");
            sb.AppendLine($"| {Title(bld.ShortName)} | {(bld.UpgradesFrom is { } u ? Title(Short(u)) : "—")} | {(hammers > 0 ? hammers.ToString() : "—")} | {(tools > 0 ? tools.ToString() : "—")} | {bld.Workplaces} | {bld.RequiredPopulation} | {(effect.Count > 0 ? string.Join("; ", effect) : "—")} |");
        }
        return sb.ToString();
    }

    private static string FoundingFathers(Ruleset r, Dictionary<string, string> msg)
    {
        var sb = new StringBuilder();
        foreach (var group in r.FoundingFathers.GroupBy(f => f.Type).OrderBy(g => g.Key.ToString()))
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Father | Effect |");
            sb.AppendLine("|---|---|");
            foreach (FoundingFather f in group)
            {
                string name = msg.GetValueOrDefault($"model.foundingFather.{f.ShortName}.name", Title(f.ShortName));
                string desc = msg.GetValueOrDefault($"model.foundingFather.{f.ShortName}.description", "").Trim();
                sb.AppendLine($"| **{name}** | {Escape(desc)} |");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Units(Ruleset r)
    {
        int b = r.MovementConstants.BaseMoveCost;
        var sb = new StringBuilder();
        sb.AppendLine("| Unit | Offence | Defence | Move (tiles) | Sight | Cargo | Europe price | Build (h/t) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        string[] joke = ["revenger", "undead", "flyingDutchman"]; // FreeCol easter-egg units, not part of normal play
        foreach (UnitType u in r.UnitTypes.Where(u => !joke.Contains(u.ShortName)).OrderBy(u => u.IsNaval).ThenBy(u => u.ShortName))
        {
            int h = Cost(u.BuildCostOrEmpty, "model.goods.hammers");
            int t = Cost(u.BuildCostOrEmpty, "model.goods.tools");
            string build = h > 0 || t > 0 ? $"{h}/{t}" : "—";
            string price = u.Price > 0 ? u.Price.ToString() : "—";
            string cargo = u.Space > 0 ? u.Space.ToString() : "—";
            sb.AppendLine($"| {Title(u.ShortName)} | {u.Offence:0.#} | {u.Defence:0.#} | {Frac(u.Movement, b)} | {u.LineOfSight} | {cargo} | {price} | {build} |");
        }
        sb.AppendLine();
        sb.AppendLine("**Combat roles** (added on top of the unit's own strength):");
        sb.AppendLine();
        sb.AppendLine("| Role | Offence | Defence | Move bonus | Equipment |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (RoleType role in r.Roles.Where(x => x.Offence != 0 || x.Defence != 0 || x.MovementBonus != 0))
        {
            string equip = role.RequiredGoods.Count > 0
                ? string.Join(", ", role.RequiredGoods.Select(g => $"{g.Amount} {Title(Short(g.GoodsId))}"))
                : "—";
            sb.AppendLine($"| {Title(role.ShortName)} | +{role.Offence:0.#} | +{role.Defence:0.#} | {(role.MovementBonus != 0 ? $"+{role.MovementBonus / b:0.#} tiles" : "—")} | {equip} |");
        }
        return sb.ToString();
    }

    private static string Natives(Ruleset r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Nation | Settlements | Aggression | Teaches |");
        sb.AppendLine("|---|---|---|---|");
        foreach (NativeNationType n in r.NativeNationTypes)
        {
            string raw = Short(n.SettlementTypeId).Replace("Capital", "");
            string kind = raw is "camp" ? "Camp" : raw is "village" ? "Village" : "City";
            string skills = n.Skills.Count > 0
                ? string.Join(", ", n.Skills.Select(s => Title(Short(s.UnitTypeId).Replace("expert", "").Replace("Expert", ""))))
                : "—";
            sb.AppendLine($"| {Title(n.ShortName)} | {Title(kind)} | {n.Aggression} | {skills} |");
        }
        return sb.ToString();
    }

    private static string Difficulty()
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Level | Father cost factor | Max tax | Land price factor | Native convert % |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (DifficultyLevel lvl in DifficultyLevels.All)
        {
            DifficultyOptions d = Ruleset.LoadClassic(lvl.Id).Difficulty;
            string name = lvl.Name + (lvl.Id == DifficultyLevels.DefaultId ? " *(default)*" : "");
            sb.AppendLine($"| {name} | {d.FoundingFatherFactor} | {d.Monarch.MaximumTaxRate}% | {d.LandPriceFactor} | {d.NativeConvertProbability}% |");
        }
        return sb.ToString();
    }

    // ---- helpers --------------------------------------------------------

    private static int Cost(IReadOnlyList<GoodsOutput> costs, string goodsId) =>
        costs.FirstOrDefault(c => c.GoodsId == goodsId)?.Amount ?? 0;

    private static string Short(string id) => id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id;

    private static string Frac(int cost, int b) =>
        (cost % b == 0) ? (cost / b).ToString() : ((double)cost / b).ToString("0.#", CultureInfo.InvariantCulture);

    // camelCase / lowercase id-suffix → "Title Case With Spaces"
    private static string Title(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        string spaced = Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return char.ToUpper(spaced[0]) + spaced[1..];
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Trim();

    private static Dictionary<string, string> LoadProperties(string path)
    {
        var dict = new Dictionary<string, string>();
        if (!File.Exists(path)) return dict;
        foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            string line = raw.TrimStart();
            if (line.Length == 0 || line[0] is '#' or '!') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string val = Unescape(line[(eq + 1)..]);
            dict[key] = val;
        }
        return dict;
    }

    private static string Unescape(string s) =>
        Regex.Replace(s, @"\\u([0-9A-Fa-f]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
}
