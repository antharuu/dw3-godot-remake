using System.Buffers.Binary;
using Dw3.Formats.Battle;

namespace Dw3.Extractor.Tests;

/// <summary>Builds a fully fabricated two-sheet sprite source with distinct animation structures.</summary>
internal static class FabricatedSpriteEffects
{
    internal static (FabricatedDisc Disc, byte[] Overlay) Create(int paletteRow = 253, int placementX = 0, int placementY = 0)
    {
        byte[] executable = Fabricate.Executable(0x80050000);
        byte[] overlay = new byte[SpriteEffectRegistryDecoder.SpriteEffectsOffset + 24];
        var disc = new FabricatedDisc().Add("SLES_039.36", executable).Add("AAA/PRO/FIGHTSTG.PRO", overlay);
        AddSprite(disc, executable, overlay, (0, 123, 5), 0x300, 1, paletteRow);
        int placement = SpriteEffectRegistryDecoder.EffectSheetsOffset + (5 * 16) + 8;
        Fabricate.U32(overlay, placement, (uint)placementX);
        Fabricate.U32(overlay, placement + 4, (uint)placementY);
        AddSprite(disc, executable, overlay, (1, 456, 7), 0x301, 2, 253);
        Fabricate.U16(overlay, SpriteEffectRegistryDecoder.SpriteEffectsOffset + 16, ushort.MaxValue);
        AddFighter(disc, executable);
        return (disc, overlay);
    }

    private static void AddSprite(FabricatedDisc disc, byte[] executable, byte[] overlay, (int Ordinal, ushort Id, ushort Sheet) identity, int file, int duration, int paletteRow)
    {
        byte[] sheet = FabricatedSprites.Sheet([7])[4..];
        int frameOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(sheet.AsSpan(8));
        Fabricate.U16(sheet, frameOffset + 2, 2);
        Fabricate.U16(sheet, frameOffset + 4, 3);
        byte[] texture = FabricatedSprites.FourBitTim((255, 0));
        var animation = new byte[24 + (duration * 2)];
        Fabricate.U16(animation, 0, 1);
        Fabricate.U16(animation, 2, 1);
        Fabricate.U16(animation, 4, (ushort)duration);
        Fabricate.U16(animation, 8, (ushort)Math.Max(0, paletteRow));
        for (int i = 0; i < duration; i++)
        {
            Fabricate.U16(animation, 24 + (i * 2), 7);
        }

        byte[] archive = Fabricate.Archive([sheet, Fabricate.Archive([FabricatedSprites.Rlen(texture)]), Fabricate.Archive([animation])]);
        if (paletteRow < 0)
        {
            Fabricate.U32(archive, 8, 0);
        }

        string path = $"AAA/DAT/FIGHT/SYNTH-{file}.BIN";
        disc.Add(path, archive);
        Fabricate.FileSector(executable, file, disc.LbaOf(path));
        int at = SpriteEffectRegistryDecoder.EffectSheetsOffset + (identity.Sheet * 16);
        Fabricate.U32(overlay, at, ((uint)file << 16) | 1);
        Fabricate.U32(overlay, at + 4, (uint)file << 16);
        int record = SpriteEffectRegistryDecoder.SpriteEffectsOffset + (identity.Ordinal * 8);
        Fabricate.U16(overlay, record, identity.Id);
        Fabricate.U16(overlay, record + 2, identity.Sheet);
        Fabricate.U32(overlay, record + 4, ((uint)file << 16) | 2);
    }

    private static void AddFighter(FabricatedDisc disc, byte[] executable)
    {
        var table = new byte[0x104];
        Fabricate.U32(table, 4, 0x10);
        Fabricate.U32(table, 8, 0x40);
        Fabricate.U32(table, 12, 0x100);
        Fabricate.U16(table, 0x10, 1);
        Fabricate.U32(table, 0x48, 0x03020000);
        disc.Add("AAA/DAT/FIGHT/FIGHTERS.BIN", table);
        disc.Add("AAA/DAT/FIGHT/ACTIONS.BIN", Fabricate.Archive([Fabricate.Archive([[7, 0, 0, 0, 12, 0, 7, 0, 1, 0, 9, 0, 0, 0]])]));
        Fabricate.FileSector(executable, 0x1CC, disc.LbaOf("AAA/DAT/FIGHT/FIGHTERS.BIN"));
        Fabricate.FileSector(executable, 0x302, disc.LbaOf("AAA/DAT/FIGHT/ACTIONS.BIN"));
    }
}
