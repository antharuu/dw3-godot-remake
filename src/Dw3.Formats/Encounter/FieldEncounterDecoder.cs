using System.Buffers.Binary;

namespace Dw3.Formats.Encounter;

/// <summary>
/// Decodes the DW3 EU encounter domain from FIELDSTG.PRO and WSTAG stage overlays.
///
/// FIELDSTG.PRO (overlay, loaded at <see cref="FieldOverlayBase"/>):
///   FIELDSTG_enemies   at 0x80092DFC — 523 BattleEnemy records (12 bytes each).
///   FIELDSTG_encounters at 0x80094680 — 335 Encounter records (28 bytes each).
///   FIELDSTG_battleRates at 0x8009B6AC — 6 step-rate values (s32 each).
///
/// WSTAG*.PRO (stage overlays, loaded at <see cref="StageOverlayBase"/>):
///   stageBattles — FieldBattles array(s) at addresses discovered by StageBattlesScanner.
///   FieldBattles (28 bytes): serial s32, id s32, pad8 s32, 4 BattleList pointers.
///   BattleList   (36 bytes): count s32, 8 AreaBattle pointers.
///   AreaBattle   (12 bytes): encounter s32, stage s32, music s32.
///
/// Music encoding: music = 0x60000000 | (bank &lt;&lt; 18) | n  (sound.h).
/// No game-name table is embedded; project label is MUSIC(bank,n).
/// </summary>
public static class FieldEncounterDecoder
{
    // ── FIELDSTG.PRO layout (EU disc) ──────────────────────────────────────────
    /// <summary>Overlay RAM address where FIELDSTG.PRO is loaded.</summary>
    public const uint FieldOverlayBase = 0x80082CB0u;

    /// <summary>Overlay address of FIELDSTG_enemies (523 × 12 bytes).</summary>
    public const uint EnemiesAddr = 0x80092DFCu;

    /// <summary>Number of BattleEnemy records in the enemies table.</summary>
    public const int EnemyCount = 523;

    /// <summary>Byte size of one BattleEnemy record.</summary>
    public const int EnemyStride = 12;

    /// <summary>Overlay address of FIELDSTG_encounters (335 × 28 bytes).</summary>
    public const uint EncountersAddr = 0x80094680u;

    /// <summary>Number of Encounter records in the encounters table.</summary>
    public const int EncounterCount = 335;

    /// <summary>Byte size of one Encounter record (includes 2 bytes of alignment padding).</summary>
    public const int EncounterStride = 28;

    /// <summary>Overlay address of FIELDSTG_battleRates (6 × s32).</summary>
    public const uint BattleRatesAddr = 0x8009B6ACu;

    /// <summary>Number of step-rate values in the battle-rates table.</summary>
    public const int BattleRatesCount = 6;

    // ── Stage overlay layout (EU disc) ─────────────────────────────────────────
    /// <summary>Overlay RAM address where every WSTAG*.PRO stage overlay is loaded.</summary>
    public const uint StageOverlayBase = 0x800A5DE0u;

    /// <summary>Byte size of one AreaBattle record (encounter s32 + stage s32 + music s32).</summary>
    public const int AreaBattleStride = 12;

    /// <summary>Byte size of one BattleList record (count s32 + 8 AreaBattle pointers).</summary>
    public const int BattleListStride = 36;

    /// <summary>Byte size of one FieldBattles record (serial s32 + id s32 + pad8 s32 + 4 BattleList pointers).</summary>
    public const int FieldBattlesStride = 28;

    // ── Global field-state structure (EU disc) ─────────────────────────────────
    /// <summary>RAM address of FIELDSTG_state, the global field-state structure.</summary>
    public const uint FieldStateAddress = 0x80099D80u;

    /// <summary>Byte offset of the battles pointer field within FIELDSTG_state.</summary>
    public const short BattlesFieldOffset = 0x20;

    // ── Music encoding mask ────────────────────────────────────────────────────
    private const uint MusicMask = 0x60000000u;

    // ── Public decoder API ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns a project-owned neutral label for a music identifier read from disc.
    /// Format: MUSIC(bank,n) where bank and n are derived from the raw 32-bit value.
    /// Music encoding from disc: value = 0x60000000 | (bank &lt;&lt; 18) | n.
    /// </summary>
    public static string MusicLabel(int music)
    {
        uint u = unchecked((uint)music);
        int bank = (int)((u >> 18) & 0x3F);
        int n    = (int)(u & 0x3FFFFu);
        return $"MUSIC({bank},{n})";
    }

    /// <summary>
    /// Decodes one BattleEnemy record from a 12-byte span at <paramref name="offset"/>.
    /// Layout: fighter s32 + level s16 + hp s16 + mp s16 + strength s16.
    /// </summary>
    public static BattleEnemy DecodeBattleEnemy(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || checked(offset + EnemyStride) > data.Length)
            throw new FormatException("BattleEnemy record lies outside the supplied buffer.");
        int   fighter  = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        short level    = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 4, 2));
        short hp       = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 6, 2));
        short mp       = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 8, 2));
        short strength = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset + 10, 2));
        return new BattleEnemy(0, fighter, level, hp, mp, strength);
    }

    /// <summary>
    /// Resolves a BattleEnemy* overlay pointer back to a zero-based index in the enemies table.
    /// Throws <see cref="FormatException"/> when the pointer is unaligned, before the table base,
    /// or past the last valid entry.
    /// </summary>
    public static int ResolveEnemyIndex(uint pointer)
    {
        long delta = (long)pointer - EnemiesAddr;
        if (delta < 0 || delta % EnemyStride != 0 || delta / EnemyStride >= EnemyCount)
            throw new FormatException(
                $"Encounter enemy pointer 0x{pointer:X8} does not index FIELDSTG_enemies.");
        return (int)(delta / EnemyStride);
    }

    /// <summary>
    /// Decodes all 523 BattleEnemy records from the FIELDSTG.PRO data buffer.
    /// </summary>
    public static IReadOnlyList<BattleEnemy> DecodeEnemies(ReadOnlySpan<byte> fieldStg)
    {
        int start = OverlayOffset(EnemiesAddr, FieldOverlayBase, fieldStg.Length, "FIELDSTG_enemies");
        var result = new BattleEnemy[EnemyCount];
        for (int i = 0; i < EnemyCount; i++)
        {
            BattleEnemy raw = DecodeBattleEnemy(fieldStg, start + i * EnemyStride);
            result[i] = raw with { Index = i };
        }
        return result;
    }

    /// <summary>
    /// Decodes all 335 Encounter records from the FIELDSTG.PRO data buffer, resolving each
    /// enemy pointer to an index into <paramref name="enemies"/>.
    /// </summary>
    public static IReadOnlyList<EncounterRecord> DecodeEncounters(
        ReadOnlySpan<byte> fieldStg,
        IReadOnlyList<BattleEnemy> enemies)
    {
        ArgumentNullException.ThrowIfNull(enemies);
        int start = OverlayOffset(EncountersAddr, FieldOverlayBase, fieldStg.Length, "FIELDSTG_encounters");
        var result = new EncounterRecord[EncounterCount];
        for (int i = 0; i < EncounterCount; i++)
        {
            int offset = start + i * EncounterStride;
            if (offset + EncounterStride > fieldStg.Length)
                throw new FormatException("Encounter record lies outside FIELDSTG.PRO.");
            uint p0 = BinaryPrimitives.ReadUInt32LittleEndian(fieldStg.Slice(offset, 4));
            uint p1 = BinaryPrimitives.ReadUInt32LittleEndian(fieldStg.Slice(offset + 4, 4));
            uint p2 = BinaryPrimitives.ReadUInt32LittleEndian(fieldStg.Slice(offset + 8, 4));
            int[] indices = [ResolveEnemyIndex(p0), ResolveEnemyIndex(p1), ResolveEnemyIndex(p2)];
            byte ambush = fieldStg[offset + 12];
            byte kind   = fieldStg[offset + 13];
            byte[] blocks = fieldStg.Slice(offset + 14, 12).ToArray();
            var slots = indices
                .Select(idx => new EncounterEnemySlot(idx, enemies[idx]))
                .ToList();
            result[i] = new EncounterRecord(i, indices, slots, ambush, kind, blocks);
        }
        return result;
    }

    /// <summary>
    /// Decodes the 6 battle-rate step values from the FIELDSTG.PRO data buffer.
    /// </summary>
    public static IReadOnlyList<int> DecodeBattleRates(ReadOnlySpan<byte> fieldStg)
    {
        int start = OverlayOffset(BattleRatesAddr, FieldOverlayBase, fieldStg.Length, "FIELDSTG_battleRates");
        var result = new int[BattleRatesCount];
        for (int i = 0; i < BattleRatesCount; i++)
            result[i] = BinaryPrimitives.ReadInt32LittleEndian(fieldStg.Slice(start + i * 4, 4));
        return result;
    }

    /// <summary>
    /// Decodes one AreaBattle record from the stage overlay at the given RAM <paramref name="pointer"/>.
    /// </summary>
    public static AreaBattle DecodeAreaBattle(ReadOnlySpan<byte> overlay, uint pointer, uint overlayBase)
    {
        int offset = OverlayOffset(pointer, overlayBase, overlay.Length, "AreaBattle");
        if (offset + AreaBattleStride > overlay.Length)
            throw new FormatException($"AreaBattle record lies outside overlay at 0x{pointer:X8}.");
        int encounter = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset, 4));
        int stage     = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 4, 4));
        int music     = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 8, 4));
        return new AreaBattle(encounter, stage, music, MusicLabel(music));
    }

    /// <summary>
    /// Decodes one BattleList record from the stage overlay at the given RAM <paramref name="pointer"/>.
    /// </summary>
    public static BattleList DecodeBattleList(ReadOnlySpan<byte> overlay, uint pointer, uint overlayBase)
    {
        int offset = OverlayOffset(pointer, overlayBase, overlay.Length, "BattleList");
        if (offset + BattleListStride > overlay.Length)
            throw new FormatException($"BattleList record lies outside overlay at 0x{pointer:X8}.");
        int count = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset, 4));
        if (count < 0 || count >= BattleRatesCount)
            throw new FormatException($"BattleList count {count} is not a valid battle-rate index (0..{BattleRatesCount - 1}).");
        var battles = new List<AreaBattle>(8);
        for (int i = 0; i < 8; i++)
        {
            uint ptr = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 4 + i * 4, 4));
            battles.Add(DecodeAreaBattle(overlay, ptr, overlayBase));
        }
        return new BattleList(count, battles);
    }

    /// <summary>
    /// Decodes one FieldBattles record from the stage overlay at the given RAM <paramref name="pointer"/>.
    /// </summary>
    public static FieldBattles DecodeFieldBattles(ReadOnlySpan<byte> overlay, uint pointer, uint overlayBase)
    {
        int offset = OverlayOffset(pointer, overlayBase, overlay.Length, "FieldBattles");
        if (offset + FieldBattlesStride > overlay.Length)
            throw new FormatException($"FieldBattles record lies outside overlay at 0x{pointer:X8}.");
        int serial  = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset, 4));
        int id      = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 4, 4));
        int pad8    = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 8, 4));
        var areas = new List<BattleList>(4);
        for (int i = 0; i < 4; i++)
        {
            uint ptr = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 12 + i * 4, 4));
            areas.Add(DecodeBattleList(overlay, ptr, overlayBase));
        }
        return new FieldBattles(serial, id, pad8, areas);
    }

    // ── Internal helpers ───────────────────────────────────────────────────────

    private static int OverlayOffset(uint address, uint overlayBase, int size, string what)
    {
        long delta = (long)address - overlayBase;
        if (delta < 0 || delta >= size)
            throw new FormatException($"{what} address 0x{address:X8} is outside overlay (base 0x{overlayBase:X8}).");
        return (int)delta;
    }
}
