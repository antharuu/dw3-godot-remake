using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that blast-return and dedigivolve cause a source entrance on the same slot while other
/// same-slot form changes keep the existing instant rebuild (B9-058d).</summary>
public sealed class BattleReturnEntranceTriggerTests
{
    private const int BlastFormId = 392;
    private const int BaseFormId = 383;
    private const int EvolvedFormId = 109;

    // ── BattleEntranceTrigger.Resolve ─────────────────────────────────────────

    /// <summary>A blast return on the active player slot triggers a player-blast-return entrance.</summary>
    [Fact]
    public void BlastReturn_SameSlot_ReturnsPlayerBlastReturn()
    {
        var state = StateWithActivePlayer(BaseFormId);
        var rendered = new BattleRenderedPair(0, BlastFormId, 0, 1);
        var kind = BattleEntranceTrigger.Resolve(state, rendered, false, BattleScheduledEventKind.BlastReturn);
        Assert.Equal(BattleEntranceKind.PlayerBlastReturn, kind);
    }

    /// <summary>A forced dedigivolve on the active player slot triggers a player-dedigivolve entrance.</summary>
    [Fact]
    public void Dedigivolve_SameSlot_ReturnsPlayerDedigivolve()
    {
        var state = StateWithActivePlayer(BaseFormId);
        var rendered = new BattleRenderedPair(0, EvolvedFormId, 0, 1);
        var kind = BattleEntranceTrigger.Resolve(state, rendered, false, BattleScheduledEventKind.Dedigivolve);
        Assert.Equal(BattleEntranceKind.PlayerDedigivolve, kind);
    }

    /// <summary>A digivolution command (no scheduled cause) keeps the instant rebuild.</summary>
    [Fact]
    public void Digivolution_NoCause_ReturnsNone()
    {
        var state = StateWithActivePlayer(EvolvedFormId);
        var rendered = new BattleRenderedPair(0, BaseFormId, 0, 1);
        var kind = BattleEntranceTrigger.Resolve(state, rendered, false, formChangeCause: null);
        Assert.Equal(BattleEntranceKind.None, kind);
    }

    /// <summary>An unrecognised scheduled cause on the same slot keeps the instant rebuild.</summary>
    [Fact]
    public void UnknownCause_SameSlot_ReturnsNone()
    {
        var state = StateWithActivePlayer(BaseFormId);
        var rendered = new BattleRenderedPair(0, EvolvedFormId, 0, 1);
        var kind = BattleEntranceTrigger.Resolve(state, rendered, false, BattleScheduledEventKind.ParalysisEnd);
        Assert.Equal(BattleEntranceKind.None, kind);
    }

    /// <summary>When the fighter id did not change (same slot, same form), no entrance fires even with a cause.</summary>
    [Fact]
    public void SameId_WithBlastReturnCause_ReturnsNone()
    {
        var state = StateWithActivePlayer(BaseFormId);
        var rendered = new BattleRenderedPair(0, BaseFormId, 0, 1);
        var kind = BattleEntranceTrigger.Resolve(state, rendered, false, BattleScheduledEventKind.BlastReturn);
        Assert.Equal(BattleEntranceKind.None, kind);
    }

    // ── BattleState cause pipeline ────────────────────────────────────────────

    /// <summary>BattleBlastResolver.ProcessNext sets the pending BlastReturn cause on the state.</summary>
    [Fact]
    public void BlastResolver_ProcessNext_SetsPendingBlastReturnCause()
    {
        var state = new BattleState
        {
            Players = [new BattleFighter { Id = BlastFormId, Hp = 100, MaxHp = 200, Temporary = true, PrevId = BaseFormId }, null, null],
            Enemies = [new BattleFighter { Id = 1, Hp = 1, MaxHp = 1 }, null, null],
            ActivePlayer = 0,
            ActiveEnemy = 0,
        };
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueBlastReturn(0, 0);
        bool processed = BattleBlastResolver.ProcessNext(state, scheduler);
        Assert.True(processed);
        Assert.Equal(BattleScheduledEventKind.BlastReturn, state.PendingFormChangeEventKind);
        Assert.Equal(BaseFormId, state.ActivePlayerFighter?.Id);
    }

    /// <summary>BattleDedigivolveResolver.ProcessNext sets the pending Dedigivolve cause on the state.</summary>
    [Fact]
    public void DedigivolveResolver_ProcessNext_SetsPendingDedigicolveCause()
    {
        var seed = new PartnerSeed { Id = BaseFormId, Hp = 100, MaxHp = 200 };
        var state = new BattleState
        {
            Players = [BattleFighter.FromPartner(seed, EvolvedFormId), null, null],
            Enemies = [new BattleFighter { Id = 1, Hp = 1, MaxHp = 1 }, null, null],
            ActivePlayer = 0,
            ActiveEnemy = 0,
            StarterDigimonIds = [BaseFormId],
        };
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueDedigivolve(0);
        bool processed = BattleDedigivolveResolver.ProcessNext(state, scheduler);
        Assert.True(processed);
        Assert.Equal(BattleScheduledEventKind.Dedigivolve, state.PendingFormChangeEventKind);
    }

    /// <summary>ConsumePendingFormChangeEventKind returns and clears the pending cause in a single call.</summary>
    [Fact]
    public void ConsumePending_ClearsAndReturnsKind()
    {
        var state = new BattleState
        {
            Players = [new BattleFighter { Id = BlastFormId, Hp = 100, MaxHp = 200, Temporary = true, PrevId = BaseFormId }, null, null],
            Enemies = [new BattleFighter { Id = 1, Hp = 1, MaxHp = 1 }, null, null],
            ActivePlayer = 0,
            ActiveEnemy = 0,
        };
        var scheduler = new BattleTurnScheduler();
        scheduler.EnqueueBlastReturn(0, 0);
        BattleBlastResolver.ProcessNext(state, scheduler);
        Assert.Equal(BattleScheduledEventKind.BlastReturn, state.PendingFormChangeEventKind);
        var consumed = state.ConsumePendingFormChangeEventKind();
        Assert.Equal(BattleScheduledEventKind.BlastReturn, consumed);
        Assert.Null(state.PendingFormChangeEventKind);
    }

    /// <summary>Consuming a second time when no cause is set returns null.</summary>
    [Fact]
    public void ConsumePending_WhenEmpty_ReturnsNull()
    {
        var state = new BattleState { Players = [null, null, null], Enemies = [null, null, null] };
        Assert.Null(state.ConsumePendingFormChangeEventKind());
    }

    // ── WeakIdleRule on return kinds ──────────────────────────────────────────

    /// <summary>A blast-return entrance applies the weak idle rule (not forced normal).</summary>
    [Theory]
    [InlineData(1, 100, BattleWeakIdleRule.Weak)]
    [InlineData(26, 100, BattleWeakIdleRule.Normal)]
    public void BlastReturn_IdleFollowsWeakRule(int hp, int maxHp, int expectedIdle)
    {
        var fighter = new BattleFighter { Id = BaseFormId, Hp = hp, MaxHp = maxHp };
        var idle = BattleEntranceTrigger.Idle(BattleEntranceKind.PlayerBlastReturn, fighter);
        Assert.Equal(expectedIdle, idle);
    }

    /// <summary>A dedigivolve entrance applies the weak idle rule (not forced normal).</summary>
    [Theory]
    [InlineData(1, 100, BattleWeakIdleRule.Weak)]
    [InlineData(26, 100, BattleWeakIdleRule.Normal)]
    public void Dedigivolve_IdleFollowsWeakRule(int hp, int maxHp, int expectedIdle)
    {
        var fighter = new BattleFighter { Id = BaseFormId, Hp = hp, MaxHp = maxHp };
        var idle = BattleEntranceTrigger.Idle(BattleEntranceKind.PlayerDedigivolve, fighter);
        Assert.Equal(expectedIdle, idle);
    }

    // ── BattleEntranceMessages ────────────────────────────────────────────────

    /// <summary>A blast-return entrance has no before message.</summary>
    [Fact]
    public void BlastReturnMessages_HasNoBeforeMessage()
    {
        var (before, _) = BattleEntranceMessages.For(BattleEntranceKind.PlayerBlastReturn, null, [], []);
        Assert.Null(before);
    }

    /// <summary>A dedigivolve entrance has no before message.</summary>
    [Fact]
    public void DedigivolveMessages_HasNoBeforeMessage()
    {
        var (before, _) = BattleEntranceMessages.For(BattleEntranceKind.PlayerDedigivolve, null, [], []);
        Assert.Null(before);
    }

    /// <summary>A blast-return after message routes through the blastReturn catalogue key (debug label contains the key).</summary>
    [Fact]
    public void BlastReturnMessages_AfterMessageContainsBlastReturnDebugKey()
    {
        var (_, after) = BattleEntranceMessages.For(BattleEntranceKind.PlayerBlastReturn, StubCatalog(), [], []);
        Assert.Contains("message=blastReturn", after.Text.Debug, System.StringComparison.Ordinal);
    }

    /// <summary>A dedigivolve after message routes through the dedigivolve catalogue key (debug label contains the key).</summary>
    [Fact]
    public void DedigivolveMessages_AfterMessageContainsDedigivolveDebugKey()
    {
        var (_, after) = BattleEntranceMessages.For(BattleEntranceKind.PlayerDedigivolve, StubCatalog(), [], []);
        Assert.Contains("message=dedigivolve", after.Text.Debug, System.StringComparison.Ordinal);
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static BattleTextCatalog StubCatalog()
    {
        IReadOnlyList<IReadOnlyList<TextToken>> menu = [.. Enumerable.Range(0, 0x70).Select(_ => (IReadOnlyList<TextToken>)[])];
        var messages = new System.Collections.Generic.Dictionary<string, BattleTextTemplate>
        {
            ["blastReturn"] = new(0x62, "menu", ["actor"], "test"),
            ["dedigivolve"] = new(0x47, "menu", ["actor"], "test"),
        };
        return new(new BattleTextData(menu, [], [], [], [], [], [], messages, new System.Collections.Generic.Dictionary<string, int>()));
    }

    private static BattleState StateWithActivePlayer(int formId) =>
        new()
        {
            Players = [new BattleFighter { Id = formId, Hp = 100, MaxHp = 200 }, null, null],
            Enemies = [new BattleFighter { Id = 1, Hp = 1, MaxHp = 1 }, null, null],
            ActivePlayer = 0,
            ActiveEnemy = 0,
        };
}
