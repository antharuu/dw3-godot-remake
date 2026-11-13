using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>Tests the bounded battle-overlay registry used to resolve 2D action sprites.</summary>
public sealed class SpriteEffectRegistryDecoderTests
{
    [Fact]
    public void DecodePreservesSpriteIdentitySheetAndPackedArchiveSelector()
    {
        byte[] overlay = new byte[SpriteEffectRegistryDecoder.SpriteEffectsOffset + (SpriteEffectRegistryDecoder.SpriteEffectEntryBytes * 2)];
        int offset = SpriteEffectRegistryDecoder.SpriteEffectsOffset;
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(offset), 1012);
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(offset + 2), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(offset + 4), 0x07E20003);
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(offset + SpriteEffectRegistryDecoder.SpriteEffectEntryBytes), -1);

        IReadOnlyList<SpriteEffectReference> entries = SpriteEffectRegistryDecoder.DecodeSpriteEffects(overlay);

        SpriteEffectReference entry = Assert.Single(entries);
        Assert.Equal(1012, entry.EffectId);
        Assert.Equal(3, entry.SheetIndex);
        Assert.Equal(0x7E2, entry.AnimationFileId);
        Assert.Equal(3, entry.AnimationArchiveEntry);
    }

    [Fact]
    public void DecodeRejectsRegistryWithoutNegativeOneTerminator()
    {
        byte[] overlay = new byte[SpriteEffectRegistryDecoder.SpriteEffectsOffset + SpriteEffectRegistryDecoder.SpriteEffectEntryBytes];

        FormatException error = Assert.Throws<FormatException>(() => SpriteEffectRegistryDecoder.DecodeSpriteEffects(overlay));

        Assert.Contains("no -1 identifier terminator", error.Message, StringComparison.Ordinal);
    }
}
