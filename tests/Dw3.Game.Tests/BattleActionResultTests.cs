using Dw3.Data.Battle;
using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;
using TechData = Dw3Production.Simulation.TechData;

namespace Dw3.Game.Tests;

public sealed class BattleActionResultTests
{
    [Fact]
    public void RevealShowsOneLineThenTheNextAfterMoreThanThreeFramesThenTheArrow()
    {
        var reveal = new BattleMessageReveal(2);
        var trace = new List<(int Lines, bool Arrow)>();
        for (int frame = 0; frame < 12; frame++)
        {
            reveal.Step(confirmPressed: true);
            trace.Add((reveal.VisibleLines, reveal.Arrow));
        }

        Assert.Equal((0, false), trace[0]);
        Assert.Equal((1, false), trace[1]);
        Assert.Equal((1, false), trace[4]);
        Assert.Equal((2, false), trace[6]);
        Assert.Equal((2, false), trace[9]);
        Assert.Equal((2, true), trace[10]);
        Assert.True(reveal.IsClosed);
    }

    [Fact]
    public void RevealIgnoresCrossUntilTheArrowAndWaitsForIt()
    {
        var reveal = new BattleMessageReveal(2);
        for (int frame = 0; frame < 11; frame++)
        {
            reveal.Step(confirmPressed: true);
        }

        Assert.True(reveal.Arrow);
        Assert.False(reveal.IsClosed);
        for (int frame = 0; frame < 300; frame++)
        {
            reveal.Step(confirmPressed: false);
        }

        Assert.False(reveal.IsClosed);
        reveal.Step(confirmPressed: true);
        Assert.True(reveal.IsClosed);
    }

    [Fact]
    public void SingleHitShowsTheFormulaDamageOnTheTarget()
    {
        var context = Context(BattleSide.Player, Hit(0, landed: true, damage: 40, formula: 108));

        var page = Assert.Single(BattleActionResultPages.Build(context));

        Assert.Equal(("damage", BattleSide.Enemy, 20, 108), (page.Key, page.TargetSide, page.TargetId, page.Amount));
        Assert.Equal([108], page.Numbers);
    }

    [Fact]
    public void MissShowsTheMissLineAndKnockOutShowsNothing()
    {
        var miss = Assert.Single(BattleActionResultPages.Build(Context(BattleSide.Enemy, Hit(0, landed: false, damage: 0, formula: 0))));
        Assert.Equal(("missed", BattleSide.Player, 10), (miss.Key, miss.TargetSide, miss.TargetId));
        Assert.Empty(miss.Numbers);

        Assert.Empty(BattleActionResultPages.Build(Context(BattleSide.Player, Hit(0, landed: true, damage: 100, formula: 100, effect: BattleTechniqueEffect.KnockOut))));
        Assert.Empty(BattleActionResultPages.Build(Context(BattleSide.Player)));
    }

    [Fact]
    public void MultiHitShowsPerHitDamageLandedHitsAndTotal()
    {
        var context = Context(
            BattleSide.Player,
            Hit(0, true, 30, 30, BattleTechniqueEffect.MultiHit),
            Hit(1, false, 0, 0, BattleTechniqueEffect.MultiHit),
            Hit(2, true, 10, 30, BattleTechniqueEffect.MultiHit));

        var page = Assert.Single(BattleActionResultPages.Build(context));

        Assert.Equal("multiHitDamage", page.Key);
        Assert.Equal([30, 2, 60], page.Numbers);
    }

    [Fact]
    public void DoubleMagicShowsTheSumOfItsHitsAndCounterShowsItsOwnResult()
    {
        var context = Context(
            BattleSide.Player,
            Hit(0, true, 25, 25, BattleTechniqueEffect.DoubleMagic),
            Hit(1, true, 35, 35, BattleTechniqueEffect.DoubleMagic),
            new BattleHitEvent(BattleSide.Enemy, 0, true, 7, BattleTechniqueEffect.NoSpecialEffect, IsCounter: true) { FormulaDamage = 7 });

        var pages = BattleActionResultPages.Build(context);

        Assert.Equal(2, pages.Count);
        Assert.Equal(("damage", BattleSide.Enemy, 20, 60), (pages[0].Key, pages[0].TargetSide, pages[0].TargetId, pages[0].Amount));
        Assert.Equal(("damage", BattleSide.Player, 10, 7), (pages[1].Key, pages[1].TargetSide, pages[1].TargetId, pages[1].Amount));
    }

    [Fact]
    public void RunnerHoldsTheActionOnTheDamageMessageUntilCross()
    {
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        flow.Text = new BattleTextCatalog(TextData());
        flow.NameIds = new Dictionary<int, int> { [20] = 5 };
        flow.PlayerBasicTech = new TechData { Script = 5, Accuracy = 100, Power = 1 };
        bool cross = false;
        var runner = new BattleSequenceRunner(flow, new StubScriptProvider(new BattleActionScript(5, [new BattleActionCommand(BattleActionOpcode.End, [])])))
        {
            Confirm = () => cross,
        };
        runner.SetPlayback(new SequenceTestPlayback { Ready = true, AcceptAssets = true, ChildrenReady = true });
        Assert.True(flow.Choose(BattleCommand.Attack, 0, new FixedRng()));
        int hp = flow.State.ActiveEnemyFighter!.Hp;
        BattleHitEvent hit = Assert.Single(flow.PendingSequenceContext!.HitEvents);

        runner.Tick();
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
        Assert.NotNull(flow.ActionResult);
        Assert.Equal("MFIGHT entry=22 message=damage", flow.MessageText.Debug);
        Assert.Equal("MFIGHT entry=11 message=damage", flow.MessageText.Second!.Debug);
        Assert.Equal(BattleTextCatalog.Number(hit.FormulaDamage), flow.MessageText.Second.Substitutions[0]);
        Assert.Equal(TextData().DigimonNames[5], flow.MessageText.Substitutions[0]);
        Assert.Equal(hp, BattleViewBuilder.Build(flow, new BattleMenuView(BattleMenuKind.Root, 0, 0, 6, 1, [], null, null, null), null, _ => BattleTextLine.Missing).Enemies[0].Hp);
        for (int frame = 0; frame < 120; frame++)
        {
            runner.Tick();
        }

        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
        Assert.True(flow.MessageArrow);
        Assert.Equal(2, flow.MessageLines);
        cross = true;
        runner.Tick();
        Assert.NotEqual(BattleFlowPhase.ActionSequence, flow.Phase);
        Assert.Null(flow.ActionResult);
        Assert.False(flow.MessageVisible);
    }

    [Fact]
    public void CancelDropsTheResultMessageWithoutCompletingTheAction()
    {
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        var runner = new BattleSequenceRunner(flow, new StubScriptProvider(new BattleActionScript(5, [new BattleActionCommand(BattleActionOpcode.End, [])])))
        {
            Confirm = () => false,
        };
        runner.SetPlayback(new SequenceTestPlayback { Ready = true, AcceptAssets = true, ChildrenReady = true });
        Assert.True(flow.Choose(BattleCommand.Attack, 0, new FixedRng()));
        runner.Tick();
        Assert.NotNull(flow.ActionResult);

        runner.Cancel();

        Assert.Null(flow.ActionResult);
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
        Assert.False(flow.MessageVisible);
        Assert.Equal(2, flow.MessageLines);
    }

    private static ActionSequenceContext Context(BattleSide side, params BattleHitEvent[] hits) =>
        side == BattleSide.Player ? new ActionSequenceContext(side, 10, 20, hits, 5) : new ActionSequenceContext(side, 20, 10, hits, 5);

    private static BattleHitEvent Hit(int index, bool landed, int damage, int formula, int effect = BattleTechniqueEffect.NoSpecialEffect) =>
        new(BattleSide.Player, index, landed, damage, effect, IsCounter: false) { FormulaDamage = formula };

    private static BattleTextData TextData()
    {
        var plain = (IReadOnlyList<TextToken>)[new TextToken("g", 14, 0, [])];
        var name = (IReadOnlyList<TextToken>)[new TextToken("g", 30, 0, [])];
        var names = Enumerable.Range(0, 251).Select(i => i == 5 ? name : plain).ToArray();
        var table = Enumerable.Range(0, 500).Select(_ => plain).ToArray();
        return new BattleTextData(
            table,
            names,
            table,
            table,
            table,
            table,
            [.. Enumerable.Range(0, 6).Select(i => new BattleTextTemplate(i + 1, "menu", [], "disc battle menu"))],
            new Dictionary<string, BattleTextTemplate>
            {
                ["basicAttack"] = new(8, "message", ["actor"], "disc battle behavior"),
                ["damage"] = new(11, "message", ["value"], "disc battle behavior"),
                ["missed"] = new(0x1D, "message", [], "disc battle behavior"),
            },
            new Dictionary<string, int>());
    }

    private sealed class StubScriptProvider : IActionScriptProvider
    {
        private readonly BattleActionScript _script;

        public StubScriptProvider(BattleActionScript script) => _script = script;

        public BattleActionScript? TryLoad(int fighterId, int scriptSelector) => _script;
    }

    private sealed class FixedRng : IBattleRng
    {
        public int NextValue() => 0;
    }
}
