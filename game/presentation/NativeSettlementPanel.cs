using System;
using System.Collections.Generic;
using System.Linq;
using CrownAndColony.GameLogic.Combat;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Natives;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Godot;

namespace CrownAndColony.Presentation;

/// <summary>
/// The on-map native-settlement interaction panel (slice 1c — native UI): opened by clicking a
/// discovered native settlement, it offers the peaceful actions over the already-shipped logic —
/// speak with the chief (<see cref="Game.CheckVisit"/>/<see cref="Game.Visit(Unit, NativeSettlement)"/>),
/// learn the settlement's skill (<see cref="Game.CheckLearnSkill"/>/<see cref="Game.LearnSkill"/>) and
/// <b>two-way trade</b> when the acting unit is a carrier — <b>sell</b> cargo
/// (<see cref="Game.CheckSellToNatives"/>/<see cref="Game.SellToNatives(Unit, NativeSettlement, string, int)"/>,
/// <see cref="Game.NativeSalePrice"/>) and <b>buy</b> from the settlement's store
/// (<see cref="Game.CheckBuyFromNatives"/>/<see cref="Game.BuyFromNatives(Unit, NativeSettlement, string, int)"/>,
/// <see cref="Game.NativeBuyPrice"/>/<see cref="Game.GoodsToSell"/>), surfacing the haggle offer/counter
/// (<see cref="Game.TryHaggleSell"/>/<see cref="Game.TryHaggleBuy"/>) — plus, when the acting unit is a
/// <b>missionary</b>, <b>establish a mission</b>
/// (<see cref="Game.CheckEstablishMission"/>/<see cref="Game.EstablishMission(Unit, NativeSettlement)"/>) or, over a
/// rival European mission, <b>denounce</b> it
/// (<see cref="Game.CheckDenounceMission"/>/<see cref="Game.DenounceMission(Unit, NativeSettlement)"/>) — and the
/// option to attack it
/// (<see cref="Game.CheckAttackSettlement"/>/<see cref="Game.AttackSettlement(Unit, World.Position)"/>),
/// each shown only when the acting unit is allowed it. Presentation only (ADR-006): every action and every
/// gate is a Game oracle; the panel renders state and forwards clicks. Guard failures are shown in the panel
/// (and forwarded to the status bar) — never thrown.
/// </summary>
public partial class NativeSettlementPanel : PanelContainer
{
    private Game _game = null!;
    private NativeSettlement _settlement = null!;
    private int _actingUnitId; // 0 = no acting unit (unit ids start at 1)
    private Action<string> _onAction = _ => { };
    // The two mission commands (thin GameController wrappers, guarded against InvalidMoveException) — the panel forwards
    // the establish/denounce click to these and surfaces their one-line outcome, exactly as the trade actions do over the
    // Game oracles (ADR-006). Defaulted to no-ops so a panel opened without them (a bare test) still rebuilds safely.
    private Func<Unit, NativeSettlement, string> _establishMission = (_, _) => "";
    private Func<Unit, NativeSettlement, string> _denounceMission = (_, _) => "";
    private string _outcome = "";

    /// <summary>The in-panel screen currently shown: the action menu, one of the trade sub-flows, or the incite rival-picker.</summary>
    private enum Screen { Actions, TradePick, TradeHaggle, IncitePick }

    private Screen _screen = Screen.Actions;
    private bool _buying; // true = the trade sub-flow is a Buy (from the settlement), false = a Sell (the ship's cargo)
    private string? _tradeGoodsId; // the good chosen in the TradePick screen
    private int _tradeAmount; // the amount chosen
    private int _haggleRound; // prior haggle rounds this trade session (0 = the opening offer)
    private int _haggleCounter; // the natives' last counter-price (the standing offer the player can accept)
    private bool _haggleDone; // the natives lost patience this session — no deal possible

    /// <summary>
    /// Opens the panel for a settlement, acting with the unit of id <paramref name="actingUnitId"/> (0 = none).
    /// <paramref name="establishMission"/> / <paramref name="denounceMission"/> are the guarded mission commands (thin
    /// <see cref="GameController"/> wrappers over <see cref="Game.EstablishMission(Unit, NativeSettlement)"/> /
    /// <see cref="Game.DenounceMission(Unit, NativeSettlement)"/>); each returns the one-line outcome the panel shows.
    /// <paramref name="onAction"/> runs after every action with its one-line outcome — the controller uses it to
    /// surface a status notice and to re-sync the map selection (an action may spend, upgrade, or destroy the unit).
    /// </summary>
    public void Open(Game game, NativeSettlement settlement, int actingUnitId,
        Func<Unit, NativeSettlement, string> establishMission, Func<Unit, NativeSettlement, string> denounceMission,
        Action<string> onAction)
    {
        ColonyArt.FramePanel(this, dense: true); // parchment image frame + dark-ink in-game theme (not Godot's default gray box)
        _game = game;
        _settlement = settlement;
        _actingUnitId = actingUnitId;
        _establishMission = establishMission;
        _denounceMission = denounceMission;
        _onAction = onAction;
        _outcome = "";
        _screen = Screen.Actions;
        _tradeGoodsId = null;
        Rebuild();
        Show();
    }

    /// <summary>The acting unit, re-resolved by id each rebuild (a learned colonist is swapped for a new object keeping its id).</summary>
    private Unit? ActingUnit => _game.Units.FirstOrDefault(u => u.Id == _actingUnitId && u.IsOnMap);

    private void Changed()
    {
        _onAction(_outcome); // controller surfaces the outcome + re-syncs selection (the unit may be gone/upgraded)
        Rebuild();           // re-gate this panel's own buttons (or hide if the settlement was just sacked)
    }

    private static string Short(string id) => id[(id.LastIndexOf('.') + 1)..];

    private static string Title(string id)
    {
        string s = Short(id);
        return s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
    }

    private void Rebuild()
    {
        // The settlement may have just been destroyed by an attack launched from this very panel.
        if (!_game.NativeSettlements.Contains(_settlement))
        {
            Hide();
            return;
        }

        string nation = Title(_settlement.NationTypeId);
        string type = Short(_settlement.SettlementTypeId);
        GetNode<Label>("VBox/NativeTitle").Text = _settlement.IsCapital ? $"{nation} {type} ★" : $"{nation} {type}";

        string teaches = _settlement.LearnableSkill is { } skill && !_settlement.SkillConsumed ? Naming.Humanize(Short(skill)) : "nothing";
        GetNode<Label>("VBox/NativeInfo").Text =
            $"Mood: {_settlement.AlarmLevel}   |   Teaches: {teaches}\n" +
            (_settlement.HasBeenVisited ? "You have spoken with this chief.\n" : "") +
            _outcome;

        switch (_screen)
        {
            case Screen.TradePick:
                BuildTradePick();
                break;
            case Screen.TradeHaggle:
                BuildTradeHaggle();
                break;
            case Screen.IncitePick:
                BuildIncitePick();
                break;
            default:
                BuildActions();
                break;
        }
    }

    /// <summary>Detaches and frees the dynamic-area children (signal-safe), returning the cleared container.</summary>
    private VBoxContainer ClearDynamic()
    {
        var dynamic = GetNode<VBoxContainer>("VBox/Dynamic");
        foreach (Node child in dynamic.GetChildren())
        {
            dynamic.RemoveChild(child); child.QueueFree(); // detach now (signal-safe), free deferred — avoids freed-while-emitting when a child button's handler drives the rebuild
        }
        return dynamic;
    }

    private void BuildActions()
    {
        VBoxContainer dynamic = ClearDynamic();

        Unit? unit = ActingUnit;
        if (unit is null)
        {
            dynamic.AddChild(Hint("Select a unit, then click the settlement, to interact."));
            return;
        }
        if (unit.Position != _settlement.Position && !unit.Position.IsAdjacentTo(_settlement.Position))
        {
            dynamic.AddChild(Hint($"Move your {unit.Type.ShortName} next to the settlement to interact."));
            return;
        }

        bool any = false;
        if (_game.CheckVisit(unit, _settlement).Allowed)
        {
            any = true;
            dynamic.AddChild(ActionButton("Speak", "Speak with chief", () =>
            {
                int gift = _game.Visit(unit, _settlement);
                _outcome = _game.PendingFirstContact is not null
                    ? "" // first contact — the tribe's peace offer awaits; the controller opens the accept/reject dialog
                    : gift > 0
                        ? $"The chief shared tales of nearby lands and gave {gift} gold."
                        : "The chief shared tales of nearby lands.";
                Changed();
            }));
        }
        if (_game.CheckLearnSkill(unit, _settlement).Allowed)
        {
            any = true;
            dynamic.AddChild(ActionButton("Learn", $"Learn {Naming.Humanize(Short(_settlement.LearnableSkill!))}", () =>
            {
                Unit expert = _game.LearnSkill(unit, _settlement);
                _outcome = $"Your colonist trained as a {Naming.Humanize(Short(expert.Type.Id))}.";
                Changed();
            }));
        }
        // Missionary actions (`86d3f62qr`): a unit in the missionary role at/adjacent to the settlement may found a mission.
        // With a *rival* European mission already standing here, founding routes through a denounce (a roll) — so we show
        // a single, honest button: "Denounce mission" when a denounceable rival mission is present, "Establish mission"
        // otherwise. Each forwards to its guarded GameController command and surfaces the engine's outcome (established /
        // missionary killed by a hostile tribe / rival ousted / denunciation failed) via the result text + onAction.
        if (_game.CheckDenounceMission(unit, _settlement).Allowed)
        {
            any = true;
            dynamic.AddChild(ActionButton("Denounce", "Denounce mission", () =>
            {
                _outcome = _denounceMission(unit, _settlement);
                Changed();
            }));
        }
        else if (_game.CheckEstablishMission(unit, _settlement).Allowed)
        {
            any = true;
            dynamic.AddChild(ActionButton("Establish", "Establish mission", () =>
            {
                _outcome = _establishMission(unit, _settlement);
                Changed();
            }));
        }
        // Two-way trade: a carrier on the map at/adjacent to the settlement may sell its cargo and/or buy from the store.
        // The Sell button shows when the ship is carrying any goods; Buy when it has free hold space and the settlement
        // offers goods. Both open an in-panel sub-flow (pick a good + amount, then haggle). Each is gated by its oracle.
        if (unit.Type.IsCarrier)
        {
            if (CanSell(unit))
            {
                any = true;
                dynamic.AddChild(ActionButton("Sell", "Sell goods", () => OpenTrade(buying: false)));
            }
            if (CanBuy(unit))
            {
                any = true;
                dynamic.AddChild(ActionButton("Buy", "Buy goods", () => OpenTrade(buying: true)));
            }
        }
        // Demand tribute (86d3fq01a, FreeCol scout SCOUT_SETTLEMENT_TRIBUTE → demandTribute): an armed unit beside the
        // settlement can shake the chief down for gold instead of attacking. Forwards straight to the Game oracle
        // (ADR-006); the calm tribe pays, an angry one refuses, and either way it's an insult that raises alarm.
        if (_game.CheckDemandTribute(unit, _settlement.Position).Allowed)
        {
            any = true;
            dynamic.AddChild(ActionButton("DemandTribute", "Demand tribute", () =>
            {
                Game.TributeResult result = _game.DemandTribute(unit, _settlement.Position);
                _outcome = result.Paid
                    ? $"The chief paid {result.Gold} gold in tribute."
                    : "The chief refused your demand for tribute.";
                Changed();
            }));
        }
        // Incite (86d3fq00w/86d3fq0bt, FreeCol missionary MISSIONARY_INCITE_INDIANS → incite): a missionary can pay the
        // chief to turn the tribe against a chosen European rival. We open an in-panel rival-picker (one button per live
        // rival, showing the bribe) — but only when at least one rival exists and one can be afforded right now.
        if (IncitableRivals(unit).Any())
        {
            any = true;
            dynamic.AddChild(ActionButton("Incite", "Incite against a rival", () =>
            {
                _outcome = "";
                _screen = Screen.IncitePick;
                Rebuild();
            }));
        }
        if (_game.CheckAttackSettlement(unit, _settlement.Position).Allowed)
        {
            any = true;
            dynamic.AddChild(ActionButton("Attack", "Attack settlement", () =>
            {
                CombatResult result = _game.AttackSettlement(unit, _settlement.Position);
                _outcome = result is CombatResult.GreatWin or CombatResult.Win
                    ? "The settlement was sacked!"
                    : "Your assault was repelled.";
                Changed();
            }));
        }
        if (!any)
        {
            dynamic.AddChild(Hint("There is nothing to do here right now."));
        }
    }

    /// <summary>Whether the acting ship has any cargo it could sell here (it carries goods and the settlement is not too hostile).</summary>
    private bool CanSell(Unit ship) =>
        ship.Cargo.Any(kv => kv.Value > 0)
        && SellableGoods(ship).Any(g => _game.CheckSellToNatives(ship, _settlement, g.GoodsId, g.Amount).Allowed);

    /// <summary>
    /// Whether the acting ship could buy here: it has free hold space and the settlement offers a good it can afford.
    /// Under the Col1 buy-back rule this is <b>false until the ship has sold to the settlement this visit</b> — a settlement
    /// only sells to a trader that just traded to it (<see cref="Game.GoodsToSell(NativeSettlement, Unit)"/> is empty otherwise).
    /// </summary>
    private bool CanBuy(Unit ship) =>
        _game.CargoSlotsFree(ship) > 0
        && BuyableGoods(ship).Any(g => _game.CheckBuyFromNatives(ship, _settlement, g.GoodsId, g.Amount).Allowed);

    /// <summary>The goods the acting ship is carrying, as (id, amount) pairs the player can offer to sell (most-carried first).</summary>
    private IReadOnlyList<(string GoodsId, int Amount)> SellableGoods(Unit ship) =>
        ship.Cargo.Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();

    /// <summary>The goods the settlement offers <paramref name="ship"/> for sale, each lot capped at its Col1 buy-back allowance (empty until it has sold here this visit) — via <see cref="Game.GoodsToSell(NativeSettlement, Unit)"/>.</summary>
    private IReadOnlyList<(string GoodsId, int Amount)> BuyableGoods(Unit ship) => _game.GoodsToSell(_settlement, ship);

    /// <summary>Opens the buy/sell sub-flow: clears any prior pick and shows the goods list for the chosen direction.</summary>
    private void OpenTrade(bool buying)
    {
        _buying = buying;
        _tradeGoodsId = null;
        _outcome = "";
        _screen = Screen.TradePick;
        Rebuild();
    }

    /// <summary>Returns to the action menu, clearing the trade sub-flow state.</summary>
    private void CloseTrade()
    {
        _screen = Screen.Actions;
        _tradeGoodsId = null;
        Rebuild();
    }

    /// <summary>
    /// The goods-picker screen for the active direction: one row per offerable good, listing its quantity and the
    /// settlement's price for the whole lot (sale price for a sell, asking price for a buy). Picking a good opens the
    /// haggle screen at the opening (round-0) offer.
    /// </summary>
    private void BuildTradePick()
    {
        VBoxContainer dynamic = ClearDynamic();
        Unit? ship = ActingUnit;
        if (ship is null || !ship.Type.IsCarrier)
        {
            CloseTrade();
            return;
        }

        IReadOnlyList<(string GoodsId, int Amount)> goods = _buying ? BuyableGoods(ship) : SellableGoods(ship);
        dynamic.AddChild(Hint(_buying ? "Buy from their store:" : "Sell from your hold:"));
        bool any = false;
        foreach ((string goodsId, int amount) in goods)
        {
            MoveCheck check = _buying
                ? _game.CheckBuyFromNatives(ship, _settlement, goodsId, amount)
                : _game.CheckSellToNatives(ship, _settlement, goodsId, amount);
            if (!check.Allowed)
            {
                continue; // skip goods this ship can't trade right now (e.g. can't afford a buy)
            }
            any = true;
            string verb = _buying ? "for" : "→";
            dynamic.AddChild(ActionButton($"Trade_{Short(goodsId)}", $"{amount} {Naming.Humanize(Short(goodsId))}  {verb} {check.Cost} gold", () =>
            {
                _tradeGoodsId = goodsId;
                _tradeAmount = amount;
                _haggleRound = 0;
                _haggleCounter = check.Cost; // the natives' opening fair/asking price
                _haggleDone = false;
                _screen = Screen.TradeHaggle;
                Rebuild();
            }));
        }
        if (!any)
        {
            dynamic.AddChild(Hint(_buying ? "They have nothing to sell you right now." : "Nothing here they will buy."));
        }
        dynamic.AddChild(ActionButton("TradeBack", "Back", CloseTrade));
    }

    /// <summary>
    /// The haggle screen for the picked good: shows the natives' current offer, a button to accept it (which commits
    /// the trade via the sell/buy oracle), a button to push for a better price (one haggle round — the natives accept,
    /// counter, or lose patience), and a Back button. When the natives have walked off (<see cref="_haggleDone"/>) only
    /// Back remains.
    /// </summary>
    private void BuildTradeHaggle()
    {
        VBoxContainer dynamic = ClearDynamic();
        Unit? ship = ActingUnit;
        if (ship is null || _tradeGoodsId is not { } goodsId)
        {
            CloseTrade();
            return;
        }

        string lot = $"{_tradeAmount} {Naming.Humanize(Short(goodsId))}";
        if (_haggleDone)
        {
            dynamic.AddChild(Hint($"The chief is done bargaining over {lot}."));
            dynamic.AddChild(ActionButton("TradeBack", "Back", CloseTrade));
            return;
        }

        dynamic.AddChild(Hint(_buying
            ? $"They ask {_haggleCounter} gold for {lot}."
            : $"They offer {_haggleCounter} gold for your {lot}."));

        // Accept the standing price → commit the trade through the oracle (guarded; never throws).
        dynamic.AddChild(ActionButton("Accept", _buying ? $"Pay {_haggleCounter} gold" : $"Accept {_haggleCounter} gold", () =>
            CommitTrade(ship, goodsId)));

        // Push for a better price: a lower bid when buying, a higher ask when selling (the side that favours the player).
        // The natives accept (deal at the player's price), counter (a new standing price), or lose patience (Done).
        dynamic.AddChild(ActionButton("Haggle", _buying ? "Haggle (offer less)" : "Haggle (ask more)", () =>
        {
            int offer = _buying ? HaggleDownOffer(_haggleCounter) : HaggleUpOffer(_haggleCounter);
            Game.NativeHaggleResult result = _buying
                ? _game.TryHaggleBuy(_settlement, goodsId, _tradeAmount, offer, _haggleRound)
                // A wagon train (non-naval) is quoted the un-penalised land price, matching the committed sale.
                : _game.TryHaggleSell(_settlement, goodsId, _tradeAmount, offer, _haggleRound, ship.Type.IsNaval);
            if (result.Accepted)
            {
                _haggleCounter = result.CounterPrice; // the chief took the player's offer → deal at the player's price
                CommitTrade(ship, goodsId);
                return;
            }
            _haggleRound++;
            _haggleCounter = result.CounterPrice; // their new standing price
            _haggleDone = result.Done;
            _outcome = result.Done ? "The chief lost patience and broke off the deal." : "The chief countered your offer.";
            Rebuild();
        }));

        dynamic.AddChild(ActionButton("TradeBack", "Back", CloseTrade));
    }

    /// <summary>The player's haggle ask when selling — 10% above the standing offer (the inverse of the natives' haggleDown).</summary>
    private static int HaggleUpOffer(int price) => price * 11 / 10 + 1;

    /// <summary>The player's haggle bid when buying — 10% below the standing ask (the inverse of the natives' haggleUp).</summary>
    private static int HaggleDownOffer(int price) => price * 9 / 10;

    /// <summary>
    /// Commits the trade through the sell/buy oracle at the <b>agreed haggled price</b> (<see cref="_haggleCounter"/>,
    /// the standing figure the chief accepted): re-checks the gate (the world cannot change behind this modal, but the
    /// oracle is the single authority — ADR-006), executes the price-carrying trade
    /// (<see cref="Game.SellToNatives(Unit, NativeSettlement, string, int, int)"/> /
    /// <see cref="Game.BuyFromNatives(Unit, NativeSettlement, string, int, int)"/>), and surfaces the outcome (or the
    /// guard failure) before returning to the action menu. Never throws — a refused trade only sets the outcome. The
    /// engine validates the agreed price against the haggle band, so a hard bargain actually pays off (and the panel
    /// reports the gold actually moved, which is the agreed price).
    /// </summary>
    private void CommitTrade(Unit ship, string goodsId)
    {
        MoveCheck check = _buying
            ? _game.CheckBuyFromNatives(ship, _settlement, goodsId, _tradeAmount)
            : _game.CheckSellToNatives(ship, _settlement, goodsId, _tradeAmount);
        if (!check.Allowed)
        {
            _outcome = check.Reason ?? "The trade was refused.";
            CloseTrade();
            return;
        }
        if (_buying)
        {
            int paid = _game.BuyFromNatives(ship, _settlement, goodsId, _tradeAmount, _haggleCounter);
            _outcome = $"Bought {_tradeAmount} {Naming.Humanize(Short(goodsId))} for {paid} gold.";
        }
        else
        {
            int got = _game.SellToNatives(ship, _settlement, goodsId, _tradeAmount, _haggleCounter);
            _outcome = $"Sold {_tradeAmount} {Naming.Humanize(Short(goodsId))} for {got} gold.";
        }
        _screen = Screen.Actions;
        _tradeGoodsId = null;
        Changed(); // surfaces the outcome to the status bar + re-syncs selection (the trade ended the ship's turn)
    }

    /// <summary>The live European rivals this <paramref name="unit"/> could incite the tribe against and afford to (a missionary only; the price is re-checked per rival via the oracle, so an unaffordable rival is omitted).</summary>
    private IReadOnlyList<Player> IncitableRivals(Unit unit) =>
        _game.IncitableRivals(unit)
            .Where(p => _game.CheckInciteNatives(unit, _settlement, p.PlayerId).Allowed)
            .ToList();

    /// <summary>
    /// The incite rival-picker (86d3fq00w): one button per affordable rival showing the chief's bribe; pressing it
    /// forwards to the <see cref="Game.InciteNatives(Unit, NativeSettlement, int)"/> oracle (re-checked first; never
    /// throws), turning the tribe against that rival. A Back button returns to the action menu. The list re-gates on
    /// rebuild (a unit may have moved/spent), and closes if no rival remains.
    /// </summary>
    private void BuildIncitePick()
    {
        VBoxContainer dynamic = ClearDynamic();
        Unit? unit = ActingUnit;
        if (unit is null)
        {
            CloseIncite();
            return;
        }

        IReadOnlyList<Player> rivals = IncitableRivals(unit);
        if (rivals.Count == 0)
        {
            dynamic.AddChild(Hint("There is no rival you can afford to incite them against."));
            dynamic.AddChild(ActionButton("InciteBack", "Back", CloseIncite));
            return;
        }

        dynamic.AddChild(Hint("Pay the chief to make war on:"));
        foreach (Player rival in rivals)
        {
            MoveCheck check = _game.CheckInciteNatives(unit, _settlement, rival.PlayerId);
            if (!check.Allowed)
            {
                continue; // re-gate: a rival that just became unaffordable is skipped
            }
            int rivalId = rival.PlayerId;
            string label = RivalName(rival);
            dynamic.AddChild(ActionButton($"Incite_{rivalId}", $"{label}  ({check.Cost} gold)", () =>
            {
                MoveCheck recheck = _game.CheckInciteNatives(unit, _settlement, rivalId);
                if (!recheck.Allowed)
                {
                    _outcome = recheck.Reason ?? "You cannot incite them against that power.";
                    CloseIncite();
                    return;
                }
                Game.InciteNativesResult result = _game.InciteNatives(unit, _settlement, rivalId);
                _outcome = $"The chief takes {result.Cost} gold — the tribe will war on the {RivalNameById(rivalId)}.";
                _screen = Screen.Actions;
                Changed();
            }));
        }
        dynamic.AddChild(ActionButton("InciteBack", "Back", CloseIncite));
    }

    /// <summary>Returns to the action menu from the incite picker.</summary>
    private void CloseIncite()
    {
        _screen = Screen.Actions;
        Rebuild();
    }

    /// <summary>A readable nation name for a rival power (its nation id's leaf, title-cased), or "rival" when unknown.</summary>
    private static string RivalName(Player rival) =>
        rival.NationId is { Length: > 0 } id ? Title(id) : "rival";

    /// <summary>The readable name of the rival with this id (used after the incite, when only the id is in scope).</summary>
    private string RivalNameById(int rivalId) =>
        _game.Players.FirstOrDefault(p => p.PlayerId == rivalId) is { } p ? RivalName(p) : "rival";

    private static Label Hint(string text) =>
        new() { Text = text, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };

    private static Button ActionButton(string name, string text, Action onPressed)
    {
        var button = new Button { Name = name, Text = text };
        button.Pressed += onPressed;
        return button;
    }
}
