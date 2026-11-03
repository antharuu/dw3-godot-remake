using Dw3.Formats.Mips;
using Xunit;
using static Dw3.Formats.Tests.Mips.MipsAssembler;

namespace Dw3.Formats.Tests.Mips;

public sealed class MipsStoreScannerTests
{
    private const uint State = 0x80091230u;
    private const uint Global = 0x80054560u;

    private static IReadOnlyList<MipsStoreMatch> Scan(byte[] code, short field, params uint[] watched) =>
        MipsStoreScanner.FindStores(code, State, field, watched);

    [Fact]
    public void LuiAddiuMaterializesBaseAndLuiOriMaterializesValue()
    {
        byte[] code = Code(
            Lui(S0, State),
            AddiuLow(S0, S0, State),
            LuiForOri(T0, 0x800AF000),
            Ori(T0, T0, 0x800AF000),
            Sw(T0, S0, 0x4C));

        MipsStoreMatch store = Assert.Single(Scan(code, 0x4C));

        Assert.Equal(MipsValue.Constant(0x800AF000), store.Value);
        Assert.Equal(16, store.Offset);
    }

    [Fact]
    public void SignedLowHalfIsCompensatedByHighHalf()
    {
        const uint value = 0x8009FFF0u;
        byte[] code = Code(Lui(S0, State), AddiuLow(S0, S0, State), Lui(T0, value), AddiuLow(T0, T0, value), Sw(T0, S0, 8));

        Assert.Equal(MipsValue.Constant(value), Assert.Single(Scan(code, 8)).Value);
    }

    [Fact]
    public void StoreDisplacementSelectsStructureField()
    {
        byte[] code = Code(Lui(S0, State), AddiuLow(S0, S0, State), Addiu(T0, Zero, 7), Sw(T0, S0, 0x40), Sw(T0, S0, 0x44));

        Assert.Equal(12, Assert.Single(Scan(code, 0x40)).Offset);
    }

    [Fact]
    public void BaseMayBeAnAddressWhoseDisplacementReachesTheState()
    {
        // lui-only base, the low half of State + 0x44 folded into the store displacement.
        byte[] code = Code(Lui(S0, State + 0x44), Addiu(T0, Zero, 3), Sw(T0, S0, (short)((State + 0x44) & 0xFFFF)));

        Assert.Equal(MipsValue.Constant(3), Assert.Single(Scan(code, 0x44)).Value);
    }

    [Fact]
    public void WatchedWordLoadIsTrackedSymbolicallyThroughAddiu()
    {
        byte[] code = Code(
            Lui(V0, Global),
            LwLow(V0, V0, Global),
            Lui(S0, State),
            AddiuLow(S0, S0, State),
            Addiu(V0, V0, 0xC5),
            Sw(V0, S0, 0x44));

        MipsStoreMatch store = Assert.Single(Scan(code, 0x44, Global));

        Assert.Equal(MipsValue.LoadedWord(Global, 0xC5), store.Value);
    }

    [Fact]
    public void UnwatchedLoadInvalidatesItsDestination()
    {
        byte[] code = Code(Lui(V0, Global), LwLow(V0, V0, Global), Lui(S0, State), AddiuLow(S0, S0, State), Sw(V0, S0, 0x44));

        Assert.Empty(Scan(code, 0x44));
    }

    [Fact]
    public void ZeroRegisterCopiesPropagateValues()
    {
        byte[] code = Code(
            Lui(S0, State),
            AddiuLow(S0, S0, State),
            Addiu(T0, Zero, 9),
            Addu(T1, T0, Zero),
            Or(A0, Zero, T1),
            Addu(S1, S0, Zero),
            Sw(A0, S1, 0x10));

        Assert.Equal(MipsValue.Constant(9), Assert.Single(Scan(code, 0x10)).Value);
    }

    [Fact]
    public void UnknownWriterInvalidatesDestination()
    {
        byte[] code = Code(
            Lui(S0, State),
            AddiuLow(S0, S0, State),
            Addiu(T0, Zero, 9),
            Subu(T0, T0, T0),
            Sw(T0, S0, 0x10),
            Addiu(T1, Zero, 9),
            Lbu(T1, Sp, 0),
            Sw(T1, S0, 0x10),
            Addiu(A0, Zero, 9),
            Sll(A0, A0, 2),
            Sw(A0, S0, 0x10));

        Assert.Empty(Scan(code, 0x10));
    }

    [Fact]
    public void WritesToZeroRegisterAreDiscarded()
    {
        byte[] code = Code(Lui(S0, State), AddiuLow(S0, S0, State), Addiu(Zero, Zero, 5), Sw(Zero, S0, 0x10));

        Assert.Equal(MipsValue.Constant(0), Assert.Single(Scan(code, 0x10)).Value);
    }

    [Fact]
    public void CallClobbersCallerSavedRegistersAfterItsDelaySlot()
    {
        byte[] code = Code(
            Lui(S0, State),
            AddiuLow(S0, S0, State),
            Addiu(T0, Zero, 1),
            Addiu(S1, Zero, 2),
            Jal(0x80020000),
            Sw(T0, S0, 0x10),   // delay slot still sees t0
            Sw(T0, S0, 0x14),                    // t0 is caller-saved: lost
            Sw(S1, S0, 0x18));                   // s1 is callee-saved: kept

        Assert.Equal(MipsValue.Constant(1), Assert.Single(Scan(code, 0x10)).Value);
        Assert.Empty(Scan(code, 0x14));
        Assert.Equal(MipsValue.Constant(2), Assert.Single(Scan(code, 0x18)).Value);
    }

    [Fact]
    public void ReturnEndsTheFunctionAndForgetsEveryRegisterAfterItsDelaySlot()
    {
        byte[] code = Code(
            Lui(S0, State),
            AddiuLow(S0, S0, State),
            Addiu(S1, Zero, 4),
            Jr(Ra),
            Sw(S1, S0, 0x10),
            Sw(S1, S0, 0x14));

        Assert.Single(Scan(code, 0x10));
        Assert.Empty(Scan(code, 0x14));
    }

    [Fact]
    public void ReportsEveryMatchingStoreInCodeOrder()
    {
        byte[] code = Code(
            Lui(S0, State),
            AddiuLow(S0, S0, State),
            Addiu(T0, Zero, 1),
            Sw(T0, S0, 0x10),
            Addiu(T0, Zero, 2),
            Sw(T0, S0, 0x10));

        Assert.Equal([MipsValue.Constant(1), MipsValue.Constant(2)], Scan(code, 0x10).Select(store => store.Value));
    }

    [Fact]
    public void TrailingPartialWordIsIgnored()
    {
        byte[] code = [.. Code(Lui(S0, State), AddiuLow(S0, S0, State), Addiu(T0, Zero, 1), Sw(T0, S0, 0x10)), 0xAA, 0xBB];

        Assert.Single(Scan(code, 0x10));
    }
}
