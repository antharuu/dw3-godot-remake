using System.Buffers.Binary;
using System.Diagnostics;
using Dw3.Formats.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Rebuilds a model texture archive's local VRAM and decodes consumer-selected PS1 pages.</summary>
public static class BattleTextureDecoder
{
    public static BattleTextureSet Decode(byte[] archive, IEnumerable<MeshTextureState> consumers)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(consumers);
        IReadOnlyList<byte[]> entries = ReadEntries(archive);
        var uploads = new List<(int X, int Width, int Height, ushort[] Words)>();
        int maxX = 0, maxY = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            byte[] raw = RlenDecoder.DecompressIfNeeded(entries[i]).Data;
            (int width, int height, ushort[] words) = ReadTimUpload(raw);
            int x = i * 0x40;
            uploads.Add((x, width, height, words));
            maxX = Math.Max(maxX, x + width);
            maxY = Math.Max(maxY, height);
        }
        if (maxY > 256) throw new FormatException($"texture archive height {maxY} exceeds a PS1 texture page");
        var vram = new ushort[maxX * maxY];
        foreach (var upload in uploads)
            for (int y = 0; y < upload.Height; y++)
            {
                upload.Words.AsSpan(y * upload.Width, upload.Width).CopyTo(vram.AsSpan(y * maxX + upload.X, upload.Width));
            }

        var keys = new Dictionary<(int, int, int?, int?), int>();
        foreach (MeshTextureState state in consumers)
        {
            int mode = state.TpageMode;
            if (mode is < 0 or > 2) throw new FormatException($"unsupported PS1 tpage mode {mode}");
            var combination = (mode, state.Page, mode is 0 or 1 ? (int?)state.ClutX : null, mode is 0 or 1 ? (int?)state.ClutY : null);
            if (!keys.ContainsKey(combination)) keys.Add(combination, keys.Count);
        }
        if (keys.Count == 0) throw new FormatException("battle model has no textured polygon combinations");
        var textures = keys.OrderBy(pair => pair.Value).Select(pair => DecodePage(vram, maxX, maxY, pair.Key, pair.Value)).ToArray();
        return new BattleTextureSet(textures,
            new BattleTextureVram(maxX, maxY, [.. uploads.Select(u => new BattleTextureUpload(u.X, u.Width, u.Height))]), keys);
    }

    private static BattleTexture DecodePage(ushort[] vram, int width, int height, (int Mode, int Page, int? X, int? Y) key, int index)
    {
        int wordsPerRow = key.Mode switch { 0 => 64, 1 => 128, 2 => 256, _ => throw new UnreachableException() };
        int pageX = key.Page * 0x40;
        if (pageX < 0 || pageX + wordsPerRow > width || height < 256)
            throw new FormatException($"texture page {key.Page} mode {key.Mode} outside local VRAM archive");
        ushort[]? palette = null;
        if (key.Mode is 0 or 1)
        {
            int size = key.Mode == 0 ? 16 : 256;
            int cx = key.X!.Value, cy = key.Y!.Value;
            if (cy < 0 || cy >= height || cx < 0 || cx + size > width)
                throw new FormatException($"CLUT ({cx},{cy}) outside local VRAM archive");
            palette = vram.AsSpan(cy * width + cx, size).ToArray();
        }
        byte[] rgba = new byte[256 * 256 * 4];
        int transparent = 0, stp = 0, pixel = 0;
        for (int y = 0; y < 256; y++)
        {
            for (int wordIndex = 0; wordIndex < wordsPerRow; wordIndex++)
            {
                ushort word = vram[y * width + pageX + wordIndex];
                int samples = key.Mode == 0 ? 4 : key.Mode == 1 ? 2 : 1;
                for (int sample = 0; sample < samples; sample++)
                {
                    ushort color = key.Mode switch
                    {
                        0 => palette![(word >> (sample * 4)) & 0xF],
                        1 => palette![(word >> (sample * 8)) & 0xFF],
                        _ => word,
                    };
                    int at = pixel++ * 4;
                    if (color == 0) { transparent++; continue; }
                    rgba[at] = Expand5(color & 31);
                    rgba[at + 1] = Expand5((color >> 5) & 31);
                    rgba[at + 2] = Expand5((color >> 10) & 31);
                    rgba[at + 3] = (byte)((color & 0x8000) != 0 ? 128 : 255);
                    if ((color & 0x8000) != 0) stp++;
                }
            }
        }
        return new BattleTexture(index, key.Mode, key.Page, key.X, key.Y, 256, 256, transparent, stp, rgba);
    }

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

    private static IReadOnlyList<byte[]> ReadEntries(byte[] archive)
    {
        if (archive.Length < 4) throw new FormatException("texture archive too short");
        var offsets = new List<int>();
        bool terminated = false;
        for (int index = 0; index < 17; index++)
        {
            if (index * 4 + 4 > archive.Length) throw new FormatException("texture archive table lacks terminator");
            uint raw = BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(index * 4, 4));
            if (raw == 0) { terminated = true; break; }
            if (raw > int.MaxValue || raw < (index + 1) * 4 || raw >= archive.Length || offsets.Count > 0 && raw <= offsets[^1])
                throw new FormatException($"texture archive entry {index} offset out of range");
            offsets.Add((int)raw);
        }
        if (!terminated) throw new FormatException("texture archive has too many entries");
        if (offsets.Count == 0) throw new FormatException("texture archive has no entries");
        return [.. offsets.Select((start, index) => archive[start..(index + 1 < offsets.Count ? offsets[index + 1] : archive.Length)])];
    }

    private static (int Width, int Height, ushort[] Words) ReadTimUpload(byte[] data)
    {
        if (data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 0x10 || BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != 2)
            throw new FormatException("expected 16-bit TIM VRAM upload without a separate CLUT");
        uint block = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        if (block < 12 || 8L + block > data.Length) throw new FormatException("invalid TIM VRAM upload image block");
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(16));
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(18));
        if (width <= 0 || height <= 0 || 20L + width * (long)height * 2 != 8L + block)
            throw new FormatException("TIM VRAM upload dimensions do not match payload");
        var words = new ushort[width * height];
        for (int i = 0; i < words.Length; i++) words[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(20 + i * 2));
        return (width, height, words);
    }
}
