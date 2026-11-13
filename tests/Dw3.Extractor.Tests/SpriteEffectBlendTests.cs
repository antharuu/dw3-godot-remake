using System.Buffers.Binary;
using Dw3.Data;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary.Png;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class SpriteEffectBlendTests
{
    [Theory]
    [InlineData(0, 128)]
    [InlineData(-1, 255)]
    public void SourceAbrZeroBlendsStpAndMinusOneIsOpaque(short abr, byte alpha)
    {
        string root = Path.Combine(Path.GetTempPath(), $"dw3-sprite-blend-{Guid.NewGuid():N}");
        try
        {
            byte[] sheet = FabricatedSprites.Sheet([7])[4..];
            int frame = BinaryPrimitives.ReadInt32LittleEndian(sheet.AsSpan(8));
            BinaryPrimitives.WriteInt16LittleEndian(sheet.AsSpan(frame + 4), abr);
            byte[] tim = FabricatedSprites.FourBitTim();
            Fabricate.U16(tim, 22, 0x8001);
            var writer = new SpriteEffectFrameWriter(sheet, [HudTexturePageDecoder.DecodeFourBitTim(tim)]);
            new DataPackWriter(root).Write(new DataPackMetadata("test", new string('a', 40), "synthetic", DateTimeOffset.UnixEpoch), context => writer.Write(context, 123, (7, 0)));
            byte[] actual = File.ReadAllBytes(Path.Combine(root, "data", "battle", "sprites", "123", "frame-7-row-0.png"));
            Assert.Equal(PngEncoder.Encode(2, 1, [8, 0, 0, alpha, 8, 0, 0, alpha]), actual);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
