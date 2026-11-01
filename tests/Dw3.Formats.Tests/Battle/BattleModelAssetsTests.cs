using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>Synthetic tests for battle-model textures, animation tracks, motions and fighter metadata.</summary>
public sealed class BattleModelAssetsTests
{
    [Fact]
    public void TextureArchive_RebuildsVramAndDecodesFourBitClutWithPerTexelAlpha()
    {
        byte[] archive = TextureArchive();
        var state = new MeshTextureState(0, 0, 0, 0, 255, 0);

        BattleTextureSet decoded = BattleTextureDecoder.Decode(archive, [state]);

        BattleTexture texture = Assert.Single(decoded.Textures);
        Assert.Equal(256, texture.Width);
        Assert.Equal(256, texture.Height);
        Assert.Equal([0, 0, 0, 0], texture.Rgba.AsSpan(0, 4).ToArray());
        Assert.Equal([255, 0, 0, 255], texture.Rgba.AsSpan(4, 4).ToArray());
        Assert.Equal([0, 255, 0, 128], texture.Rgba.AsSpan(8, 4).ToArray());
        Assert.Equal(65531, texture.TransparentPixels);
        Assert.Equal(1, texture.StpPixels);
        Assert.Equal(64, decoded.Vram.WidthWords);
        Assert.Equal(256, decoded.Vram.Height);
        Assert.Equal(0, decoded.KeyOf(state));
    }

    [Fact]
    public void TextureArchive_RejectsMissingTerminatorAndOutOfBoundsClut()
    {
        Assert.Throws<FormatException>(() => BattleTextureDecoder.Decode(new byte[68], []));
        var state = new MeshTextureState(0, 0, 0, 60, 255, 0);
        Assert.Throws<FormatException>(() => BattleTextureDecoder.Decode(TextureArchive(), [state]));
    }

    [Fact]
    public void Tracks_DecodeThreeOrderedKeyStreamsAndRejectMissingTracks()
    {
        byte[] entry = Concat(
            U32(12), U32(28), U32(36),
            Key(0, 1, 2, 3), Key(5, 4, 5, 6),
            Key(0, 7, 8, 9),
            Key(0, 4096, 4096, 4096));

        BattleBoneTracks tracks = BattleAnimationDecoder.DecodeTracks(entry, 0, entry.Length, 2);

        Assert.Equal(2, tracks.Translation.Count);
        Assert.Equal(new BattleTrackKey(5, 4, 5, 6), tracks.Translation[1]);
        Assert.Equal(new BattleTrackKey(0, 7, 8, 9), Assert.Single(tracks.Rotation));
        Assert.Equal(new BattleTrackKey(0, 4096, 4096, 4096), Assert.Single(tracks.Scale));
        WriteU32(entry, 4, 0);
        Assert.Throws<FormatException>(() => BattleAnimationDecoder.DecodeTracks(entry, 0, entry.Length, 2));
    }

    [Fact]
    public void Motions_DecodeEveryOffsetRegionThroughItsTerminator()
    {
        byte[] entry = Concat(
            U32(12), U32(28), U32(0),
            Step(0, 1, 10, 10), Step(0x7FFF, 0, 0, 0),
            Step(2, 3, 20, 22), Step(0x7FFF, 0, 0, 0));

        IReadOnlyList<IReadOnlyList<BattleMotionStep>> motions = BattleAnimationDecoder.DecodeMotions(entry, 0, entry.Length);

        Assert.Equal(2, motions.Count);
        Assert.Equal(new BattleMotionStep(0, 1, 10, 10), motions[0][0]);
        Assert.Equal(0x7FFF, motions[1][1].Index);
    }

    [Fact]
    public void Motions_RejectEmptyAndUnterminatedRegions()
    {
        Assert.Throws<FormatException>(() => BattleAnimationDecoder.DecodeMotions(new byte[32], 0, 32));
        byte[] entry = Concat(U32(8), U32(0), Step(0, 1, 2, 3));
        Assert.Throws<FormatException>(() => BattleAnimationDecoder.DecodeMotions(entry, 0, entry.Length));
    }

    [Fact]
    public void FighterTable_ReadsCamerasGeometryAndTerminatedFacePartsFromItsRecord()
    {
        byte[] data = new byte[0x300];
        WriteU32(data, 0, 0x80090000);
        WriteU32(data, 4, 0x10);
        WriteU32(data, 8, 0x40);
        WriteU32(data, 12, 0x200);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0x10), 501);
        data[0x12] = 0; data[0x13] = 1;
        int record = 0x40;
        WriteU32(data, record + 0x0C, 0x80090200);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(record + 0x10), 192);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(record + 0x18), 64);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(record + 0x1A), -10);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(record + 0x1A + 12 * 6), 20);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(record + 0x1A + 24 * 6), 280);
        data[0x200..0x20A].CopyTo(data.AsSpan(0x200));
        data[0x200] = 4; data[0x201] = 5; data[0x202] = 6; data[0x203] = 7;
        data[0x204] = 8; data[0x205] = 9; data[0x206] = 10; data[0x207] = 11; data[0x208] = 12; data[0x209] = 13;
        data[0x20A] = 0xFF;

        FighterModelTable table = FighterModelTable.Parse(data);
        BattleFighterMetadata metadata = table.ReadMetadata(501);

        Assert.Equal(new BattleGeometry(192, 64), metadata.Geometry);
        Assert.Equal(12, metadata.Cameras.Count);
        Assert.Equal(new BattleCamera(-10, 0, 0, 20, 0, 0, 280), metadata.Cameras[0]);
        BattleFacePart part = Assert.Single(metadata.Face.Parts);
        Assert.Equal(4, part.X);
        Assert.Equal([[8, 9], [10, 11], [12, 13]], part.Frames.Select(frame => new[] { frame.X, frame.Y }));
        Assert.Equal(0x200, metadata.Face.Source.FaceOffset);
    }

    private static byte[] TextureArchive()
    {
        const int words = 64;
        const int height = 256;
        byte[] tim = new byte[20 + words * height * 2];
        WriteU32(tim, 0, 0x10);
        WriteU32(tim, 4, 2);
        WriteU32(tim, 8, (uint)(12 + words * height * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(16), words);
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(18), height);
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(20), 0x3210);
        int palette = 20 + 255 * words * 2;
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(palette + 0), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(palette + 2), 0x001F);
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(palette + 4), 0x83E0);
        BinaryPrimitives.WriteUInt16LittleEndian(tim.AsSpan(palette + 6), 0x7C00);
        return Concat(U32(8), U32(0), tim);
    }

    private static byte[] Key(short frame, short x, short y, short z) => Concat(I16(x), I16(y), I16(z), I16(frame));
    private static byte[] Step(short index, short count, short frame, short end) => Concat(I16(index), I16(count), I16(frame), I16(end));
    private static byte[] I16(int value) { byte[] b = new byte[2]; BinaryPrimitives.WriteInt16LittleEndian(b, (short)value); return b; }
    private static byte[] U32(uint value) { byte[] b = new byte[4]; WriteU32(b, 0, value); return b; }
    private static void WriteU32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();
}
