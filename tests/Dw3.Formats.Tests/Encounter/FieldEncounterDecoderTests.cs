using System.Buffers.Binary;
using Dw3.Formats.Encounter;
using Xunit;

namespace Dw3.Formats.Tests.Encounter;

/// <summary>Disc-free structural tests for the field encounter decoder.</summary>
public sealed class FieldEncounterDecoderTests
{
    // ── Layout constants (EU disc, FIELDSTG.PRO loads at 0x80082CB0) ─────────
    private const uint FieldOverlayBase = 0x80082CB0u;
    private const uint EnemiesAddr = 0x80092DFCu; // FIELDSTG_enemies
    private const uint EncountersAddr = 0x80094680u; // FIELDSTG_encounters
    private const uint BattleRatesAddr = 0x8009B6ACu; // FIELDSTG_battleRates

    // ── Enemy record layout ────────────────────────────────────────────────────
    [Fact]
    public void DecodeBattleEnemy_ReadsAllFields()
    {
        // Fabricated enemy record: fighter=7, level=12, hp=500, mp=200, strength=30
        byte[] data = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), 7);      // fighter s32
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(4), 12);     // level s16
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(6), 500);    // hp s16
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(8), 200);    // mp s16
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(10), 30);    // strength s16

        BattleEnemy enemy = FieldEncounterDecoder.DecodeBattleEnemy(data, 0);

        Assert.Equal(7, enemy.Fighter);
        Assert.Equal(12, enemy.Level);
        Assert.Equal(500, enemy.Hp);
        Assert.Equal(200, enemy.Mp);
        Assert.Equal(30, enemy.Strength);
    }

    [Fact]
    public void DecodeBattleEnemy_ThrowsWhenOutOfRange()
    {
        byte[] data = new byte[10]; // too small for 12-byte record
        Assert.Throws<FormatException>(() => FieldEncounterDecoder.DecodeBattleEnemy(data, 0));
    }

    // ── Enemy index resolution ─────────────────────────────────────────────────
    [Fact]
    public void ResolveEnemyIndex_FirstEnemy_ReturnsZero()
    {
        int index = FieldEncounterDecoder.ResolveEnemyIndex(EnemiesAddr);
        Assert.Equal(0, index);
    }

    [Fact]
    public void ResolveEnemyIndex_EnemyAtOffset_ReturnsCorrectIndex()
    {
        // Fighter at enemy 248: ptr = EnemiesAddr + 248 * 12
        uint ptr = EnemiesAddr + 248u * 12u;
        int index = FieldEncounterDecoder.ResolveEnemyIndex(ptr);
        Assert.Equal(248, index);
    }

    [Fact]
    public void ResolveEnemyIndex_UnalignedPointer_Throws()
    {
        Assert.Throws<FormatException>(() => FieldEncounterDecoder.ResolveEnemyIndex(EnemiesAddr + 1));
    }

    [Fact]
    public void ResolveEnemyIndex_OutOfRangePointer_Throws()
    {
        // Past the last enemy
        uint past = EnemiesAddr + 523u * 12u;
        Assert.Throws<FormatException>(() => FieldEncounterDecoder.ResolveEnemyIndex(past));
    }

    [Fact]
    public void ResolveEnemyIndex_BeforeBase_Throws()
    {
        Assert.Throws<FormatException>(() => FieldEncounterDecoder.ResolveEnemyIndex(EnemiesAddr - 12));
    }

    // ── Battle rates ──────────────────────────────────────────────────────────
    [Fact]
    public void DecodeBattleRates_ReadsSixValues()
    {
        // Build a minimal FIELDSTG.PRO-sized buffer with known battle rates at the correct offset.
        int ratesOffset = (int)(BattleRatesAddr - FieldOverlayBase);
        int bufferSize = ratesOffset + 6 * 4;
        byte[] data = new byte[bufferSize];
        int[] rates = [100, 3, 4, 6, 9, 18];
        for (int i = 0; i < 6; i++)
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(ratesOffset + i * 4), rates[i]);

        IReadOnlyList<int> decoded = FieldEncounterDecoder.DecodeBattleRates(data);

        Assert.Equal(rates, decoded);
    }

    // ── Music encoding ────────────────────────────────────────────────────────
    [Fact]
    public void MusicLabel_KnownValue_ReturnsCanonicalLabel()
    {
        // music 0x60080000: bank = (0x60080000 >> 18) & 0x3F = 2, n = 0 → MUSIC(2,0)
        string label = FieldEncounterDecoder.MusicLabel(0x60080000);
        Assert.Equal("MUSIC(2,0)", label);
    }

    [Fact]
    public void MusicLabel_ConfusionValue_ReturnsCanonicalLabel()
    {
        // 0x60040000: bank = 1, n = 0 → MUSIC(1,0)
        string label = FieldEncounterDecoder.MusicLabel(0x60040000);
        Assert.Equal("MUSIC(1,0)", label);
    }

    [Fact]
    public void MusicLabel_UnknownValue_ReturnsFormattedLabel()
    {
        // music = 0x60000001: bank = 0, n = 1 → MUSIC(0,1)
        string label = FieldEncounterDecoder.MusicLabel(0x60000001);
        Assert.Equal("MUSIC(0,1)", label);
    }

    // ── AreaBattle decode ─────────────────────────────────────────────────────
    [Fact]
    public void DecodeAreaBattle_ReadAllFields()
    {
        const uint StageOverlayBase = 0x800A5DE0u;
        // Fabricated area battle: encounter=200, stage=18, music=0x60080000
        byte[] overlay = new byte[256];
        const int offset = 0x20;
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(offset), 200);
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(offset + 4), 18);
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(offset + 8), unchecked((int)0x60080000u));

        uint ptr = StageOverlayBase + offset;
        AreaBattle ab = FieldEncounterDecoder.DecodeAreaBattle(overlay, ptr, StageOverlayBase);

        Assert.Equal(200, ab.Encounter);
        Assert.Equal(18, ab.Stage);
        Assert.Equal(unchecked((int)0x60080000u), ab.Music);
        Assert.Equal("MUSIC(2,0)", ab.MusicName);
    }

    // ── BattleList count validation ───────────────────────────────────────────
    [Fact]
    public void DecodeBattleList_CountOutOfRange_Throws()
    {
        const uint StageOverlayBase = 0x800A5DE0u;
        // BattleList with count=9 (outside [0, 5])
        byte[] overlay = new byte[256];
        const int offset = 0x40;
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(offset), 9); // bad count

        // Fill 8 pointers with plausible values (pointing to valid AreaBattle slots in the buffer)
        uint areaBase = StageOverlayBase + 0x60;
        for (int i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(offset + 4 + i * 4), areaBase);

        uint ptr = StageOverlayBase + offset;
        Assert.Throws<FormatException>(() => FieldEncounterDecoder.DecodeBattleList(overlay, ptr, StageOverlayBase));
    }

    // ── FieldBattles structural decode ────────────────────────────────────────
    [Fact]
    public void DecodeFieldBattles_ReadsSerialAndId()
    {
        const uint StageOverlayBase = 0x800A5DE0u;
        // Build a minimal valid FieldBattles + BattleList + AreaBattle tree.
        byte[] overlay = new byte[0x400];

        // AreaBattle at offset 0x100
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x100), 200);              // encounter
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x104), 18);               // stage
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x108), unchecked((int)0x60080000u)); // music

        // 7 more AreaBattles at 0x10C..0x160 (reuse same slot)
        for (int i = 1; i < 8; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x100 + i * 12), 0);
            BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x104 + i * 12), 0);
            BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x108 + i * 12), unchecked((int)0x60040000u));
        }

        // BattleList at offset 0x200: count=0, 8 pointers all pointing to 0x100
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x200), 0); // count (battle-rate index 0)
        uint areaAddr = StageOverlayBase + 0x100;
        for (int i = 0; i < 8; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x204 + i * 4), areaAddr + (uint)(i * 12));

        // FieldBattles at offset 0x300: serial=131, id=0, pad8=0, 4 pointers to BattleList
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x300), 131); // serial
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x304), 0);   // id
        BinaryPrimitives.WriteInt32LittleEndian(overlay.AsSpan(0x308), 0);   // pad8
        uint listAddr = StageOverlayBase + 0x200;
        for (int i = 0; i < 4; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x30C + i * 4), listAddr);

        uint fbAddr = StageOverlayBase + 0x300;
        FieldBattles fb = FieldEncounterDecoder.DecodeFieldBattles(overlay, fbAddr, StageOverlayBase);

        Assert.Equal(131, fb.Serial);
        Assert.Equal(0, fb.Id);
        Assert.Equal(0, fb.Pad8);
        Assert.Equal(4, fb.Areas.Count);
        Assert.Equal(0, fb.Areas[0].Count);
        Assert.Equal(8, fb.Areas[0].Battles.Count);
        Assert.Equal(200, fb.Areas[0].Battles[0].Encounter);
        Assert.Equal(18, fb.Areas[0].Battles[0].Stage);
        Assert.Equal("MUSIC(2,0)", fb.Areas[0].Battles[0].MusicName);
    }
}
