namespace Dw3.Extractor.Tests;

/// <summary>
/// Builders for fabricated sprite data: sprite sheets (one two-texel part per frame), 4-bit TIM
/// texture pages, RLEN streams and the archives that wrap them.
/// </summary>
internal static class FabricatedSprites
{
    private const int PartBytes = 14;
    private const int FrameBytes = 12;
    private const int PageWidthWords = 64;
    private const int PageHeight = 256;

    /// <summary>
    /// Builds a sheet whose leading u32 is <paramref name="skip"/>, followed by the body: parts offset,
    /// ids offset, one frame offset per id, one part (u=0, v=0, 2×1, CLUT x=0 y=0), the id list with its
    /// 0xFF terminator, then one frame per id drawing part 0 at (0, 0).
    /// </summary>
    internal static byte[] Sheet(IReadOnlyList<int> ids, uint skip = 0)
    {
        int partsOffset = 8 + (ids.Count * 4);
        int idsOffset = partsOffset + PartBytes;
        int framesOffset = idsOffset + ids.Count + 1;
        var data = new byte[4 + framesOffset + (ids.Count * FrameBytes)];
        Fabricate.U32(data, 0, skip);
        Fabricate.U32(data, 4, (uint)partsOffset);
        Fabricate.U32(data, 8, (uint)idsOffset);
        for (int index = 0; index < ids.Count; index++)
        {
            Fabricate.U32(data, 12 + (index * 4), (uint)(framesOffset + (index * FrameBytes)));
            data[4 + idsOffset + index] = (byte)ids[index];
            Fabricate.U16(data, 4 + framesOffset + (index * FrameBytes), 1);
        }

        data[4 + idsOffset + ids.Count] = 0xFF;
        Fabricate.U16(data, 4 + partsOffset + 4, 2);
        Fabricate.U16(data, 4 + partsOffset + 6, 1);
        return data;
    }

    /// <summary>
    /// Builds a 64×256-halfword 4-bit TIM without CLUT block. Row 0 starts with 0x0011 (texels 0 and 1
    /// use palette index 1) and its second halfword is 0x7C00 (palette index 1 of CLUT row 0 is pure blue).
    /// Each <paramref name="opaqueRows"/> entry fills halfwords Word..Word+15 of Row with white, making
    /// that 16-colour palette fully opaque.
    /// </summary>
    internal static byte[] FourBitTim(params (int Row, int Word)[] opaqueRows)
    {
        int pixelBytes = PageWidthWords * PageHeight * 2;
        var tim = new byte[8 + 12 + pixelBytes];
        Fabricate.U32(tim, 0, 0x10);
        Fabricate.U32(tim, 8, (uint)(12 + pixelBytes));
        Fabricate.U16(tim, 16, PageWidthWords);
        Fabricate.U16(tim, 18, PageHeight);
        Fabricate.U16(tim, 20, 0x0011);
        Fabricate.U16(tim, 22, 0x7C00);
        foreach ((int row, int word) in opaqueRows)
        {
            for (int index = 0; index < 16; index++)
            {
                Fabricate.U16(tim, 20 + (((row * PageWidthWords) + word + index) * 2), 0x7FFF);
            }
        }

        return tim;
    }

    /// <summary>Encodes <paramref name="data"/> as RLEN: zero runs become repeat commands, other bytes literals.</summary>
    internal static byte[] Rlen(byte[] data)
    {
        var stream = new List<byte> { (byte)'R', (byte)'L', (byte)'E', (byte)'N' };
        stream.AddRange(BitConverter.GetBytes((uint)data.Length));
        int cursor = 0;
        while (cursor < data.Length)
        {
            int run = 0;
            while (cursor + run < data.Length && data[cursor + run] == 0 && run < 127)
            {
                run++;
            }

            if (run > 0)
            {
                stream.Add((byte)(0x80 | run));
                stream.Add(0);
                cursor += run;
                continue;
            }

            stream.Add(1);
            stream.Add(data[cursor]);
            cursor++;
        }

        stream.Add(0);
        return [.. stream];
    }

    /// <summary>
    /// Builds FILE_BATTLE_IMAGES: entry 0 is a direct-16 cursor strip TIM (156×12, first pixel white),
    /// entry 1 a one-entry archive holding the RLEN-packed texture page.
    /// </summary>
    internal static byte[] BattleImages()
    {
        const int width = 156;
        const int height = 12;
        var cursor = new byte[20 + (width * height * 2)];
        Fabricate.U32(cursor, 0, 0x10);
        Fabricate.U16(cursor, 16, width);
        Fabricate.U16(cursor, 18, height);
        Fabricate.U16(cursor, 20, 0x7FFF);
        byte[] texture = Rlen(FourBitTim());
        var data = new byte[8 + cursor.Length + 4 + texture.Length];
        Fabricate.U32(data, 0, 8);
        Fabricate.U32(data, 4, (uint)(8 + cursor.Length));
        cursor.CopyTo(data, 8);
        Fabricate.U32(data, 8 + cursor.Length, 4);
        texture.CopyTo(data, 8 + cursor.Length + 4);
        return data;
    }

    /// <summary>Builds a menu texture archive: a skip word, one offset (relative to byte 4), a zero end marker, then an RLEN page built by <see cref="FourBitTim"/>.</summary>
    internal static byte[] MenuTexture(params (int Row, int Word)[] opaqueRows)
    {
        byte[] texture = Rlen(FourBitTim(opaqueRows));
        var data = new byte[12 + texture.Length];
        Fabricate.U32(data, 4, 8);
        texture.CopyTo(data, 12);
        return data;
    }

    /// <summary>Builds a report texture file: the outer pointer 4, then a one-entry model archive holding a raw TIM page.</summary>
    internal static byte[] ReportTexture()
    {
        byte[] tim = FourBitTim();
        var data = new byte[8 + tim.Length];
        Fabricate.U32(data, 0, 4);
        Fabricate.U32(data, 4, 4);
        tim.CopyTo(data, 8);
        return data;
    }
}
