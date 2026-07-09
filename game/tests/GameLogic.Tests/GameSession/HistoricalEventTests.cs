using System.IO;
using System.Linq;
using System.Xml.Linq;
using CrownAndColony.GameLogic.Colonies;
using CrownAndColony.GameLogic.GameSession;
using CrownAndColony.GameLogic.Persistence;
using CrownAndColony.GameLogic.Specification;
using CrownAndColony.GameLogic.Units;
using Xunit;

namespace CrownAndColony.GameLogic.Tests.GameSession;

/// <summary>
/// The data-driven historical-event engine (Australian Federation, tasks 4c.2–4c.5): the <see cref="EventDef"/> schema +
/// parser, the per-turn eligibility → seeded-draw → offer → resolve → cooldown/expiry runtime, the reusable effect
/// vocabulary, and the forced scenario-setup mechanism. The classic ruleset ships <b>no</b> historical events, so the
/// whole runtime is a strict no-op there (zero RNG draws, zero new save tokens) — these tests assert that invariant and
/// exercise the engine against SYNTHETIC in-memory fixture rulesets (the real Federation events are authored later by
/// another stream; no fixture text here is production content).
/// </summary>
public class HistoricalEventTests
{
    private static readonly Ruleset Classic = Ruleset.LoadClassic();
    private const ulong Seed = 0xEEEEUL;

    /// <summary>Loads the classic ruleset with a synthetic <c>&lt;historical-events&gt;</c> section spliced in (neutral placeholder content only).</summary>
    private static Ruleset LoadClassicWithEvents(string historicalEventsXml)
    {
        using Stream spec = typeof(Ruleset).Assembly.GetManifestResourceStream(GameVariants.ClassicSpecResource)!;
        XDocument doc = XDocument.Load(spec);
        doc.Root!.Add(XElement.Parse(historicalEventsXml));
        var buffer = new MemoryStream();
        doc.Save(buffer);
        buffer.Position = 0;
        return Ruleset.Load(buffer);
    }

    private static Colony FoundColony(Game game)
    {
        Colony colony = game.FoundColony(game.Units.First(u => u.IsOnMap && u.Type.CanFoundColony));
        colony.AddGoods("model.goods.food", 100);
        return colony;
    }

    // ===== Classic: strict no-op =====

    [Fact]
    public void Classic_DefinesNoHistoricalEvents()
    {
        Assert.Empty(Classic.HistoricalEvents);
        Assert.Null(Classic.HistoricalEvent("event.anything"));
    }

    [Fact]
    public void ClassicDefault_FiresNoEvents_NoDrawsNoTokens()
    {
        // The classic game defines no events → the per-turn roll guard-returns before any RNG, and no event ever fires,
        // so no pending offer, no history line, and no EventLastFiredTurn save token.
        Game game = Game.New(Classic, Seed);
        FoundColony(game);
        for (int i = 0; i < 15; i++)
        {
            game.EndTurn();
        }
        Assert.Null(game.PendingEventOffer);
        Assert.Empty(game.EventLastFiredTurn);
        Assert.DoesNotContain(game.History, h => h.Kind == HistoryEventKind.HistoricalEvent);
        Assert.DoesNotContain("EventLastFiredTurn", SaveGame.From(game).ToJson());
    }

    [Fact]
    public void ByteStabilityTwin_EventsOff_TwoSameSeedGamesStayIdentical()
    {
        // With no events, two same-seed games — one an untouched twin — stay byte-identical over a long horizon and
        // neither draws on the event stream (ADR-009); their stream-0 states stay in lockstep.
        Game a = Game.New(Classic, Seed);
        Game b = Game.New(Classic, Seed);
        FoundColony(a);
        FoundColony(b);
        for (int turn = 0; turn < 40; turn++)
        {
            a.EndTurn();
            b.EndTurn();
        }
        Assert.Equal(SaveGame.From(a).ToJson(), SaveGame.From(b).ToJson());
        Assert.Equal(a.RandomState, b.RandomState);
    }

    // ===== 4c.2: schema + parser round-trip =====

    [Fact]
    public void Parser_ReadsEventDef_WithRequirementsOptionsAndEffects()
    {
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.test.boom' weight='7' cooldown='4' one-shot='false' earliest-year='1500' expiry-year='1600'>
              <requires>
                <limit id='event.test.boom.year' operator='ge'>
                  <left-hand-side operand-type='year' scope-level='game'/>
                  <right-hand-side value='1493'/>
                </limit>
              </requires>
              <option id='accept' weight='3'>
                <effect kind='grantGold' value='500'/>
                <effect kind='timedModifier' target='model.goods.bells' type='percentage' value='50' duration='3'/>
              </option>
              <option id='refuse' weight='1'>
                <effect kind='recordHistory' text='They refused the offer.'/>
              </option>
            </event-def>
          </historical-events>");

        EventDef ev = r.HistoricalEvent("event.test.boom")!;
        Assert.NotNull(ev);
        Assert.Equal(7, ev.Weight);
        Assert.Equal(4, ev.Cooldown);
        Assert.False(ev.OneShot);
        Assert.Equal(1500, ev.EarliestYear);
        Assert.Equal(1600, ev.ExpiryYear);
        Assert.Equal(EventTrigger.Normal, ev.Trigger);
        Assert.Single(ev.Requirements);
        Assert.Equal(2, ev.Options.Count);

        EventOption accept = ev.Option("accept")!;
        Assert.Equal(3, accept.Weight);
        Assert.Equal(2, accept.Effects.Count);
        Assert.Equal(EventEffectKind.GrantGold, accept.Effects[0].Kind);
        Assert.Equal(500, accept.Effects[0].Value);
        Assert.Equal(EventEffectKind.TimedModifier, accept.Effects[1].Kind);
        Assert.Equal("model.goods.bells", accept.Effects[1].TargetId);
        Assert.Equal(ModifierType.Percentage, accept.Effects[1].ModifierType);
        Assert.Equal(3, accept.Effects[1].Duration);
    }

    [Fact]
    public void Parser_ReadsEventPresentationText_NamePromptLabel_AndHumanizesTheIdWhenAbsent()
    {
        // WS1.1a — the event-popup text layer. Authored name/prompt/label render verbatim; an unauthored event/option
        // falls back to the humanized id (event.merinoSheep -> "Merino Sheep", invest -> "Invest") so partial authoring
        // still shows sensible text. Presentation-only — never read by the resolver.
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.merinoSheep' name='Merino Sheep Arrive' prompt='John Macarthur lands merino stock. Back the flocks?'>
              <option id='invest' label='Back the flocks'><effect kind='grantGold' value='10'/></option>
              <option id='ignore'><effect kind='recordHistory' text='The offer passes.'/></option>
            </event-def>
            <event-def id='event.bushfire'>
              <option id='endure'><effect kind='recordHistory' text='The colony endures.'/></option>
            </event-def>
          </historical-events>");

        EventDef merino = r.HistoricalEvent("event.merinoSheep")!;
        Assert.Equal("Merino Sheep Arrive", merino.Name);
        Assert.Equal("Merino Sheep Arrive", merino.DisplayName);
        Assert.Equal("John Macarthur lands merino stock. Back the flocks?", merino.Prompt);
        Assert.Equal("Back the flocks", merino.Option("invest")!.DisplayLabel); // authored label

        // Fallbacks: an option with no label, and an event with no name, humanize their ids.
        Assert.Null(merino.Option("ignore")!.Label);
        Assert.Equal("Ignore", merino.Option("ignore")!.DisplayLabel);
        EventDef bushfire = r.HistoricalEvent("event.bushfire")!;
        Assert.Null(bushfire.Name);
        Assert.Equal("Bushfire", bushfire.DisplayName); // event.bushfire -> "Bushfire"
    }

    [Fact]
    public void Parser_ScenarioStartEvent_IsImplicitlyOneShot()
    {
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.setup' trigger='scenarioStart'>
              <option id='only'><effect kind='recordHistory' text='Founded.'/></option>
            </event-def>
          </historical-events>");
        EventDef ev = r.HistoricalEvent("event.setup")!;
        Assert.True(ev.IsScenarioStart);
        Assert.True(ev.OneShot); // scenario-start ⇒ one-shot even without the attribute
    }

    [Fact]
    public void Parser_UnknownEffectKind_IsDropped()
    {
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.mixed'>
              <option id='only'>
                <effect kind='grantGold' value='10'/>
                <effect kind='someFutureVariantEffect' value='99'/>
              </option>
            </event-def>
          </historical-events>");
        EventOption only = r.HistoricalEvent("event.mixed")!.Option("only")!;
        Assert.Single(only.Effects); // the unrecognised kind is skipped, the known one kept
        Assert.Equal(EventEffectKind.GrantGold, only.Effects[0].Kind);
    }

    [Fact]
    public void Parser_DuplicateEventId_Throws()
    {
        Assert.Throws<RulesetFormatException>(() => LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.dup'><option id='a'/></event-def>
            <event-def id='event.dup'><option id='a'/></event-def>
          </historical-events>"));
    }

    // ===== 4c.3: eligibility → draw → resolve =====

    [Fact]
    public void GatedEvent_FiresOnlyOnceRequirementHolds_AndGrantsGold()
    {
        // A single-option event gated on year ≥ 1600 (age-2). It cannot fire before 1600; once eligible it auto-resolves
        // (single option) and grants its gold. AI-free: the human is the only colonial player, single option → no offer.
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.grant' weight='100'>
              <requires>
                <limit id='event.grant.year' operator='ge'>
                  <left-hand-side operand-type='year' scope-level='game'/>
                  <right-hand-side value='1600'/>
                </limit>
              </requires>
              <option id='only'><effect kind='grantGold' value='250'/></option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        FoundColony(game);
        int goldBefore = game.HumanPlayer.Gold;

        // Advance a few turns while still before 1600 (turn 1 ≈ 1492): the event must not fire.
        for (int i = 0; i < 5; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);
        Assert.False(game.EventLastFiredTurn.ContainsKey("event.grant"));

        // Jump the calendar past 1600 by ending many turns; the event fires exactly once (single-option auto-resolve).
        for (int i = 0; i < 120 && !game.EventLastFiredTurn.ContainsKey("event.grant"); i++)
        {
            game.EndTurn();
        }
        Assert.True(game.EventLastFiredTurn.ContainsKey("event.grant"));
        Assert.Equal(goldBefore + 250, game.HumanPlayer.Gold);
    }

    [Fact]
    public void OneShotEvent_FiresAtMostOnce()
    {
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.once' weight='100' one-shot='true'>
              <option id='only'><effect kind='grantGold' value='100'/></option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        FoundColony(game);
        int goldBefore = game.HumanPlayer.Gold;
        for (int i = 0; i < 20; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(goldBefore + 100, game.HumanPlayer.Gold); // exactly one grant, never repeated
    }

    [Fact]
    public void CooldownEvent_WaitsBetweenFirings()
    {
        // A repeatable (non-one-shot) event with a 5-turn cooldown fires, then cannot fire again until 5 turns pass.
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.cool' weight='100' cooldown='5'>
              <option id='only'><effect kind='grantGold' value='10'/></option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        FoundColony(game);
        game.EndTurn(); // fires on the human's first processed turn
        int firstTurn = game.EventLastFiredTurn["event.cool"];
        int goldAfterFirst = game.HumanPlayer.Gold;

        game.EndTurn(); // still within cooldown → no second firing
        Assert.Equal(firstTurn, game.EventLastFiredTurn["event.cool"]);
        Assert.Equal(goldAfterFirst, game.HumanPlayer.Gold);

        for (int i = 0; i < 6; i++)
        {
            game.EndTurn(); // let the cooldown lapse
        }
        Assert.True(game.EventLastFiredTurn["event.cool"] > firstTurn); // fired again once cooled down
        Assert.True(game.HumanPlayer.Gold > goldAfterFirst);
    }

    [Fact]
    public void ExpiredEvent_NeverFiresPastExpiryYear()
    {
        // Expiry year 1493 (essentially turn 1); by the time the roll runs the event is already past expiry → never fires.
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.expired' weight='100' expiry-year='1400'>
              <option id='only'><effect kind='grantGold' value='999'/></option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        FoundColony(game);
        int goldBefore = game.HumanPlayer.Gold;
        for (int i = 0; i < 10; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);
        Assert.Empty(game.EventLastFiredTurn);
    }

    // ===== 4c.3: offer → choose / auto-resolve =====

    [Fact]
    public void MultiOptionEvent_OffersToHuman_AndChooseResolvesTheEffects()
    {
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.choice' weight='100' one-shot='true'>
              <option id='gold' weight='1'><effect kind='grantGold' value='777'/></option>
              <option id='liberty' weight='1'><effect kind='grantLiberty' value='40'/></option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        FoundColony(game);
        int goldBefore = game.HumanPlayer.Gold;

        game.EndTurn(); // a multi-option event for the human is OFFERED, not auto-resolved
        Assert.NotNull(game.PendingEventOffer);
        Assert.Equal("event.choice", game.PendingEventOffer!.EventId);
        Assert.Equal(new[] { "gold", "liberty" }, game.PendingEventOffer.OptionIds);
        Assert.Equal(goldBefore, game.HumanPlayer.Gold); // nothing applied until chosen

        game.ChooseEventOption("gold");
        Assert.Null(game.PendingEventOffer);
        Assert.Equal(goldBefore + 777, game.HumanPlayer.Gold);
    }

    [Fact]
    public void ChooseEventOption_RejectsAnUnofferedOption()
    {
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.choice2' weight='100' one-shot='true'>
              <option id='a' weight='1'><effect kind='grantGold' value='1'/></option>
              <option id='b' weight='1'><effect kind='grantGold' value='2'/></option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        FoundColony(game);
        game.EndTurn();
        Assert.NotNull(game.PendingEventOffer);
        Assert.Throws<InvalidMoveException>(() => game.ChooseEventOption("not-an-option"));
    }

    [Fact]
    public void ChooseEventOption_WithNoOffer_Throws()
    {
        Game game = Game.New(Classic, Seed);
        Assert.Throws<InvalidMoveException>(() => game.ChooseEventOption("anything"));
    }

    [Fact]
    public void UnansweredOffer_AutoResolvesToTheHeaviestOption_OnEndTurn()
    {
        // The human ignores a two-option offer; the next EndTurn auto-resolves it to the deterministic default — the
        // heaviest-weight option ('liberty', weight 5 > gold's 1). RNG-free auto path.
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.auto' weight='100' one-shot='true'>
              <option id='gold' weight='1'><effect kind='grantGold' value='500'/></option>
              <option id='liberty' weight='5'><effect kind='grantLiberty' value='60'/></option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        FoundColony(game);
        int goldBefore = game.HumanPlayer.Gold;
        int libertyBefore = game.HumanPlayer.Liberty;

        game.EndTurn(); // offered
        Assert.NotNull(game.PendingEventOffer);
        game.EndTurn(); // ignored → auto-resolved to the heaviest option
        Assert.Null(game.PendingEventOffer);
        Assert.Equal(goldBefore, game.HumanPlayer.Gold);          // 'gold' NOT chosen
        Assert.True(game.HumanPlayer.Liberty > libertyBefore);    // 'liberty' (heaviest) auto-chosen
    }

    // ===== 4c.4: effect vocabulary =====

    [Fact]
    public void TimedModifierEffect_AppliesWhileActive_AndExpires()
    {
        // A single-option event registers a colony-scoped +100% bells timed modifier for 2 turns. While active the fold
        // doubles the colony's bell output; after expiry the registry is empty again.
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.timed' weight='100' one-shot='true'>
              <option id='only'>
                <effect kind='timedModifier' target='model.goods.bells' type='percentage' value='100' duration='2'/>
              </option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        FoundColony(game);
        game.EndTurn(); // event fires, registers the timed modifier
        Assert.NotEmpty(game.TemporaryModifiers);
        Assert.Contains(game.TemporaryModifiers, m => m.TargetId == "model.goods.bells");

        // Advance until the window closes; the strip removes it.
        for (int i = 0; i < 4; i++)
        {
            game.EndTurn();
        }
        Assert.DoesNotContain(game.TemporaryModifiers, m => m.TargetId == "model.goods.bells");
    }

    [Fact]
    public void GrantEffects_LiberyGoodsUnit_Apply()
    {
        // Two same-seed games differing ONLY by the grantGoods effect, so the colony's normal tools production/consumption
        // is identical in both and cancels — the difference is exactly the +40 grant (robust to whatever the colony does
        // with tools this turn). Liberty/unit grants are asserted from a second event whose only effect is that grant, so
        // no grant perturbs another's baseline.
        // Silver is neither produced (no miner on a fresh centre-tile colony) nor consumed (not a build material) by a
        // 1-colonist colony, so its store after a turn is exactly the granted amount — an inert probe for grantGoods.
        Ruleset goodsOnly = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.goods' weight='100' one-shot='true'>
              <option id='only'><effect kind='grantGoods' target='model.goods.silver' value='40'/></option>
            </event-def>
          </historical-events>");
        Game goodsGame = Game.New(goodsOnly, Seed);
        Colony goodsColony = FoundColony(goodsGame);
        Assert.Equal(0, goodsColony.StoreOf("model.goods.silver"));
        goodsGame.EndTurn();
        Assert.Equal(40, goodsColony.StoreOf("model.goods.silver")); // exactly the +40 grant, nothing else touches silver

        // grantLiberty, grantUnit and recordHistory (no colony-perturbing goods grant) on a separate event, compared to a
        // no-event control twin: liberty is exactly +30 over the twin, and the unit roster is exactly one larger (the
        // granted free colonist) — robust to whatever units the turn otherwise creates/removes identically in both.
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.grants' weight='100' one-shot='true'>
              <option id='only'>
                <effect kind='grantLiberty' value='30'/>
                <effect kind='grantUnit' target='model.unit.freeColonist'/>
                <effect kind='recordHistory' text='A wave of migrants arrived.'/>
              </option>
            </event-def>
          </historical-events>");
        Game control = Game.New(Classic, Seed);
        FoundColony(control);
        Game game = Game.New(r, Seed);
        FoundColony(game);

        control.EndTurn();
        game.EndTurn();

        Assert.Equal(control.HumanPlayer.Liberty + 30, game.HumanPlayer.Liberty); // exactly +30 liberty over the twin
        Assert.Equal(control.Units.Count + 1, game.Units.Count);                  // exactly one extra unit: the granted free colonist
        Assert.Contains(game.History, h => h.Kind == HistoryEventKind.HistoricalEvent);
    }

    // ===== 4c.5: forced scenario-setup mechanism (synthetic fixture only) =====

    [Fact]
    public void ScenarioStartEvent_FiresUnconditionallyOnceAtSetup()
    {
        // The forced-setup mechanism: a scenarioStart event fires on turn 1 with no requirements and no draw, and only
        // once. Neutral placeholder content (the real Warrane first-contact content is authored elsewhere).
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.firstSettlement' trigger='scenarioStart'>
              <option id='only'>
                <effect kind='grantGoods' target='model.goods.silver' value='50'/>
                <effect kind='recordHistory' text='The first settlement is established.'/>
              </option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        Colony colony = FoundColony(game);
        Assert.Equal(0, colony.StoreOf("model.goods.silver"));

        game.EndTurn(); // the human's turn-1 processing fires the setup event unconditionally
        Assert.True(game.EventLastFiredTurn.ContainsKey("event.firstSettlement"));
        Assert.Equal(50, colony.StoreOf("model.goods.silver")); // the +50 grant landed (silver is inert on a fresh colony)
        Assert.Contains(game.History, h => h.Kind == HistoryEventKind.HistoricalEvent);

        int firedTurn = game.EventLastFiredTurn["event.firstSettlement"];
        for (int i = 0; i < 5; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(firedTurn, game.EventLastFiredTurn["event.firstSettlement"]); // never fires again
    }

    // ===== 4c.10: era-frequency bands =====

    /// <summary>A repeatable (cooldown-1) single-option event that adds exactly +1 silver to the colony whenever it
    /// fires. Silver is neither produced (no miner on a fresh centre-tile colony) nor consumed by a 1-colonist colony,
    /// so the colony's silver store is exactly the number of times the era band let the event fire over a run of turns —
    /// an inert counter robust to whatever the base game does to gold (tax/upkeep) meanwhile (see the GrantEffects test).</summary>
    private const string CadenceProbeEvents = @"
      <historical-events>
        <event-def id='event.cadence' weight='100' cooldown='1'>
          <option id='only'><effect kind='grantGoods' target='model.goods.silver' value='1'/></option>
        </event-def>
      </historical-events>";

    [Theory]
    // Below the Australian window (the classic 1492 calendar's whole run) → the inert Pre sentinel.
    [InlineData(1492, Game.EventEra.Pre)]
    [InlineData(1787, Game.EventEra.Pre)]
    // The six eras of doc 13's frequency table, lower-bound inclusive, tested at each boundary and one interior year.
    [InlineData(1788, Game.EventEra.Survival)]
    [InlineData(1796, Game.EventEra.Survival)]
    [InlineData(1797, Game.EventEra.Expansion)]
    [InlineData(1829, Game.EventEra.Expansion)]
    [InlineData(1830, Game.EventEra.ColonyFormation)]
    [InlineData(1850, Game.EventEra.ColonyFormation)]
    [InlineData(1851, Game.EventEra.GoldRush)]
    [InlineData(1871, Game.EventEra.GoldRush)]
    [InlineData(1872, Game.EventEra.Infrastructure)]
    [InlineData(1888, Game.EventEra.Infrastructure)]
    [InlineData(1889, Game.EventEra.Federation)]
    [InlineData(1901, Game.EventEra.Federation)]
    [InlineData(1950, Game.EventEra.Federation)] // runs open-ended past 1901
    public void EraForYear_MapsBoundaryYearsToTheCorrectEra(int year, Game.EventEra expected)
    {
        Assert.Equal(expected, Game.EraForYear(year));
    }

    [Fact]
    public void EraFireChance_IsInertOutsideTheWindow_AndModulatedInside()
    {
        // Pre (outside 1788–1901, i.e. every classic-calendar year) is full chance → the gate is skipped, no behaviour
        // change. Inside the window the "frequent" eras (gold rush, Federation) are strictly busier than the "moderate"
        // and "survival" eras, matching doc 13.
        Assert.Equal(100, Game.EraFireChance(Game.EventEra.Pre));

        int survival = Game.EraFireChance(Game.EventEra.Survival);
        int moderate = Game.EraFireChance(Game.EventEra.Expansion);
        int goldRush = Game.EraFireChance(Game.EventEra.GoldRush);
        int federation = Game.EraFireChance(Game.EventEra.Federation);

        Assert.InRange(survival, 1, 99);   // sparse but not silent
        Assert.True(survival < moderate);  // survival is the sparsest band
        Assert.True(moderate < goldRush);  // "frequent" > "moderate"
        Assert.Equal(goldRush, federation); // both finale eras are "frequent"
        Assert.Equal(Game.EraFireChance(Game.EventEra.ColonyFormation), moderate);   // the moderate eras share a chance
        Assert.Equal(Game.EraFireChance(Game.EventEra.Infrastructure), moderate);
    }

    [Fact]
    public void CurrentEventEra_ReadsOffTheCalendarYear()
    {
        // The classic calendar (turn 1 = 1492) is always Pre; a variant calendar started in 1851 opens in the gold rush.
        Ruleset classicCal = LoadClassicWithEvents(CadenceProbeEvents);
        Game classic = Game.New(classicCal, Seed);
        Assert.Equal(Game.EventEra.Pre, classic.CurrentEventEra);

        Ruleset goldCal = LoadClassicWithEvents(CadenceProbeEvents).WithStartingYear(1851);
        Game gold = Game.New(goldCal, Seed);
        Assert.Equal(1851, gold.CurrentYear);
        Assert.Equal(Game.EventEra.GoldRush, gold.CurrentEventEra);
    }

    [Fact]
    public void EraBand_IsInertOnTheClassicCalendar_EventStillFiresEveryTurn()
    {
        // Pre = full chance = the gate is skipped entirely (no RNG roll before the draw), so on the classic 1492 calendar
        // a cooldown-1 event fires on every one of the human's processed turns exactly as it did before 4c.10.
        Game game = Game.New(LoadClassicWithEvents(CadenceProbeEvents), Seed);
        Colony colony = FoundColony(game);
        const int turns = 12;
        for (int i = 0; i < turns; i++)
        {
            game.EndTurn();
        }
        Assert.Equal(turns, colony.StoreOf("model.goods.silver")); // fired on all 12 turns — band inert outside the window
    }

    [Fact]
    public void EraBand_FiresSparselyInSurvival_AndBusilyInGoldRush()
    {
        // Same event, same seed, same number of turns — only the era differs. The Survival band (sparse) must let the
        // event fire on markedly fewer turns than the Gold-Rush band (frequent), and neither degenerately (0 or every
        // turn) inside the window. This is the cadence the era-frequency modulation exists to produce (doc 13).
        const int turns = 16; // stays inside a single era for both calendars (no boundary crossing over 16 turns)

        int survivalFirings = CountFiringsOverEra(startingYear: 1788, turns);
        int goldRushFirings = CountFiringsOverEra(startingYear: 1851, turns);

        // Sanity: both games actually stayed in their intended era for the whole window.
        Assert.True(survivalFirings is > 0 and < turns, $"survival should be sparse but non-silent, was {survivalFirings}");
        Assert.True(goldRushFirings is > 0 and < turns, $"gold rush should be busy but not every turn, was {goldRushFirings}");
        Assert.True(goldRushFirings > survivalFirings,
            $"gold rush ({goldRushFirings}) should fire more often than survival ({survivalFirings})");
    }

    [Fact]
    public void EraBand_IsDeterministic_SameSeedSameCalendarSameCadence()
    {
        // The band draws on the throwaway event stream only, seeded from read-only player state — so two same-seed,
        // same-calendar games produce the identical firing count (ADR-009).
        Assert.Equal(CountFiringsOverEra(1788, 20), CountFiringsOverEra(1788, 20));
        Assert.Equal(CountFiringsOverEra(1851, 20), CountFiringsOverEra(1851, 20));
    }

    /// <summary>Runs a fresh 1-colony game on a calendar started at <paramref name="startingYear"/> for
    /// <paramref name="turns"/> human turns and returns how many times the +1-silver cadence probe fired (the inert
    /// silver store, which only the event touches).</summary>
    private static int CountFiringsOverEra(int startingYear, int turns)
    {
        Ruleset r = LoadClassicWithEvents(CadenceProbeEvents).WithStartingYear(startingYear);
        Game game = Game.New(r, Seed);
        Colony colony = FoundColony(game);
        for (int i = 0; i < turns; i++)
        {
            game.EndTurn();
        }
        return colony.StoreOf("model.goods.silver");
    }

    // ===== Persistence: fired-turns round-trip (v71, omit-when-empty) =====

    [Fact]
    public void EventState_RoundTripsAcrossSaveLoad()
    {
        Ruleset r = LoadClassicWithEvents(@"
          <historical-events>
            <event-def id='event.persist' weight='100' one-shot='true'>
              <option id='only'><effect kind='grantGold' value='42'/></option>
            </event-def>
          </historical-events>");
        Game game = Game.New(r, Seed);
        FoundColony(game);
        game.EndTurn(); // fires the one-shot
        Assert.True(game.EventLastFiredTurn.ContainsKey("event.persist"));

        string json = SaveGame.From(game).ToJson();
        Assert.Contains("EventLastFiredTurn", json); // the token is present once an event has fired
        Game loaded = SaveGame.FromJson(json).Restore(r);
        Assert.True(loaded.EventLastFiredTurn.ContainsKey("event.persist"));

        // The one-shot stays spent after reload — gold is not granted a second time.
        int goldAfterLoad = loaded.HumanPlayer.Gold;
        for (int i = 0; i < 5; i++)
        {
            loaded.EndTurn();
        }
        Assert.Equal(goldAfterLoad, loaded.HumanPlayer.Gold);
    }
}
