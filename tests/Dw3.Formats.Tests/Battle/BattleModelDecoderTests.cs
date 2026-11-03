using System.Buffers.Binary;
using System.Text.Json;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>
/// Battle model structure tests. Every byte is fabricated here: the fixtures exercise the
/// container grammar (fighter table, archive offsets, model descriptor, mesh command stream)
/// without any data taken from the game.
/// </summary>
public sealed class BattleModelDecoderTests
{
    // ── Fighter model table ─────────────────────────────────────────────────
    private static byte[] FighterTable()
    {
        // Header: base address, entries offset, partner table offset, enemy table offset.
        byte[] data = new byte[0x200];
        WriteU32(data, 0x00, 0x80090000);
        WriteU32(data, 0x04, 0x10);
        WriteU32(data, 0x08, 0x40);
        WriteU32(data, 0x0C, 0x100);
        WriteEntry(data, 0x10, 501, 1, 0x05);
        WriteEntry(data, 0x14, 77, 2, FighterModelTable.EnemyKindMinimum);
        WriteEntry(data, 0x18, 0, 0, 0);
        WriteU32(data, 0x40 + (1 * FighterModelTable.PartnerStride), (12u << 16) | 3);
        WriteU32(data, 0x40 + (1 * FighterModelTable.PartnerStride) + 4, (12u << 16) | 9);
        WriteU32(data, 0x100 + (2 * FighterModelTable.EnemyStride), (40u << 16) | 0x21);
        WriteU32(data, 0x100 + (2 * FighterModelTable.EnemyStride) + 4, (41u << 16) | 0x22);
        return data;
    }

    [Fact]
    public void FighterTable_ListsEntriesUntilTheZeroIdTerminator()
    {
        FighterModelTable table = FighterModelTable.Parse(FighterTable());

        Assert.Equal([new FighterModelEntry(501, 1, 0x05), new FighterModelEntry(77, 2, FighterModelTable.EnemyKindMinimum)], table.Entries);
    }

    [Fact]
    public void FighterTable_ResolvesPartnerAndEnemyRecordsToFileReferences()
    {
        FighterModelTable table = FighterModelTable.Parse(FighterTable());

        Assert.Equal(new FighterModelReference(501, 1, 0x05, false, 12, 3, 12, 9), table.Resolve(501));
        Assert.Equal(new FighterModelReference(77, 2, FighterModelTable.EnemyKindMinimum, true, 40, 0x21, 41, 0x22), table.Resolve(77));
    }

    [Fact]
    public void FighterTable_RejectsAbsentFightersAndRecordsOutsideTheFile()
    {
        byte[] data = FighterTable();
        FighterModelTable table = FighterModelTable.Parse(data);
        Assert.Throws<FormatException>(() => table.Resolve(3));

        WriteEntry(data, 0x14, 77, 200, FighterModelTable.EnemyKindMinimum);
        Assert.Throws<FormatException>(() => FighterModelTable.Parse(data).Resolve(77));
        Assert.Throws<FormatException>(() => FighterModelTable.Parse(new byte[12]));
    }

    // ── Archive offset table ────────────────────────────────────────────────
    [Fact]
    public void Archive_KeepsSparseEntriesAsEmptyRangesAndEndsTheLastAtTheFileEnd()
    {
        byte[] data = new byte[0x40];
        WriteU32(data, 0x0, 0x10);
        WriteU32(data, 0x4, 0);
        WriteU32(data, 0x8, 0x20);
        WriteU32(data, 0xC, 0x30);

        IReadOnlyList<ArchiveRange> bounds = ModelArchive.ReadBounds(data);

        Assert.Equal([new ArchiveRange(0x10, 0x20), new ArchiveRange(0, 0), new ArchiveRange(0x20, 0x30), new ArchiveRange(0x30, 0x40)], bounds);
    }

    [Theory]
    [InlineData(new uint[] { 0 })]
    [InlineData(new uint[] { 6, 0 })]
    [InlineData(new uint[] { 0x0C, 0x20, 0x10 })]
    [InlineData(new uint[] { 0x08, 0x80 })]
    public void Archive_RejectsMalformedOffsetTables(uint[] offsets)
    {
        byte[] data = new byte[0x40];
        for (int i = 0; i < offsets.Length; i++)
        {
            WriteU32(data, i * 4, offsets[i]);
        }

        Assert.Throws<FormatException>(() => ModelArchive.ReadBounds(data));
    }

    // ── Mesh command stream ─────────────────────────────────────────────────

    /// <summary>Builds a mesh archive entry: six offsets, vertices, optional normals, commands, bounds.</summary>
    private static byte[] Mesh(int vertexCount, byte[] commands, int normalCount = 0)
    {
        var bytes = new List<byte>(new byte[24]);
        int verticesAt = bytes.Count;
        bytes.AddRange(I16(vertexCount));
        bytes.AddRange(new byte[4]);
        for (int i = 0; i < vertexCount; i++)
        {
            bytes.AddRange(I16(i));
            bytes.AddRange(I16(-i));
            bytes.AddRange(I16(i * 2));
        }

        int normalsAt = 0;
        if (normalCount > 0)
        {
            normalsAt = bytes.Count;
            bytes.AddRange(I16(normalCount));
            bytes.AddRange(new byte[4]);
            for (int i = 0; i < normalCount; i++)
            {
                bytes.AddRange(I16(4096));
                bytes.AddRange(I16(0));
                bytes.AddRange(I16(-i));
            }
        }

        int commandsAt = bytes.Count;
        bytes.AddRange(commands);
        int boundsAt = bytes.Count;
        bytes.AddRange(new byte[8]);
        byte[] data = [.. bytes];
        WriteU32(data, 0, (uint)verticesAt);
        WriteU32(data, 4, (uint)normalsAt);
        WriteU32(data, 8, (uint)commandsAt);
        WriteU32(data, 20, (uint)boundsAt);
        return data;
    }

    private static readonly byte[] TextureCommand = [0x01, 0x10, 0x02, 0x20, 0x30, 0x40, 0x00];

    [Fact]
    public void Mesh_DecodesTexturedQuadsAndSplitsThemIntoTwoTriangles()
    {
        byte[] commands =
        [
            .. TextureCommand,
            0x02, 9, 8, 7,          // flat colour slot 0
            0x81, 0x91, 0xE2,       // quad, textured, blend mode 2
            0x00, 0, 1, 2, 3, 0xF8, 0, 1, 2, 3, 4, 5, 6,
            0xFF,
        ];

        BattleMesh mesh = BattleMeshDecoder.Decode(Mesh(4, commands), 0, Mesh(4, commands).Length, bone: 1);

        Assert.Equal(1, mesh.Bone);
        Assert.Equal(4, mesh.Vertices.Count);
        Assert.Equal(new MeshVector(3, -3, 6), mesh.Vertices[3]);
        Assert.Empty(mesh.Normals);
        Assert.Equal([[0, 1, 2], [2, 1, 3]], mesh.Triangles);
        MeshPolygon polygon = Assert.Single(mesh.Polygons);
        Assert.Equal([0, 1, 2, 3], polygon.Indices);
        Assert.Null(polygon.NormalIndices);

        // u = (byte + u base) & 0xFF where the u base word is bytes 1..2 of the texture command.
        Assert.Equal([new MeshUv(0x08, 0x20), new MeshUv(0x11, 0x22), new MeshUv(0x13, 0x24), new MeshUv(0x15, 0x26)], polygon.Uvs);
        Assert.Equal(new MeshTextureState(0x0210, 0x02, 0x20, 0x30, 0x40, 0), polygon.Texture);
        Assert.Equal(new MeshColor(9, 8, 7), polygon.FlatColor);
        Assert.True(polygon.Quad && polygon.Textured);
        Assert.False(polygon.Lit || polygon.Gouraud || polygon.Flag0xA || polygon.Flag0xB);
        Assert.Equal(2, polygon.Abr);
        Assert.Equal(24 + 6 + (4 * 6), mesh.CommandOffset);
        Assert.Equal(mesh.CommandOffset + commands.Length, mesh.BoundsOffset);
    }

    [Fact]
    public void Mesh_UntexturedPolygonsInheritThePreviousUvsAndDefaultGreyColour()
    {
        byte[] commands =
        [
            .. TextureCommand,
            0x91, 0x00, 0, 1, 2, 1, 1, 2, 2, 3, 3,   // textured triangle
            0x90, 0xA1, 0xB1, 0xD1,                   // untextured, both unknown flags, gouraud
            0x00, 2, 1, 0, 0x00, 0, 0, 0,             // two chained polygons
            0xFF,
        ];
        byte[] mesh = Mesh(3, commands);

        BattleMesh decoded = BattleMeshDecoder.Decode(mesh, 0, mesh.Length, bone: 4);

        Assert.Equal(3, decoded.Polygons.Count);
        MeshPolygon inherited = decoded.Polygons[1];
        Assert.False(inherited.Textured);
        Assert.True(inherited.Gouraud && inherited.Flag0xA && inherited.Flag0xB);
        Assert.Equal(decoded.Polygons[0].Uvs, inherited.Uvs);
        Assert.Equal(new MeshColor(128, 128, 128), inherited.FlatColor);
        Assert.Equal([0, 0, 0], decoded.Polygons[2].Indices);
    }

    [Fact]
    public void Mesh_LitPolygonsCarryIndicesIntoTheIndependentNormalStream()
    {
        byte[] commands = [.. TextureCommand, 0x91, 0xC1, 0x00, 0, 1, 2, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0xFF];
        byte[] mesh = Mesh(3, commands, normalCount: 2);

        BattleMesh decoded = BattleMeshDecoder.Decode(mesh, 0, mesh.Length, bone: 2);

        Assert.Equal([new MeshVector(4096, 0, 0), new MeshVector(4096, 0, -1)], decoded.Normals);
        Assert.Equal([1, 0, 1], decoded.Polygons[0].NormalIndices);
        Assert.True(decoded.Polygons[0].Lit);
    }

    public static TheoryData<byte[], int, int> MalformedMeshes => new()
    {
        { [.. TextureCommand, 0x91, 0x00, 0, 1, 2, 0, 0, 0, 0, 0, 0], 3, 0 },           // no 0xFF terminator
        { [0x90, 0x00, 0, 1, 2, 0xFF], 3, 0 },                                             // untextured before any texture state
        { [.. TextureCommand, 0x91, 0x00, 0, 1, 9, 0, 0, 0, 0, 0, 0, 0xFF], 3, 0 },        // vertex index out of range
        { [0x06, 0xFF], 3, 0 },                                                             // unknown low command
        { [.. TextureCommand, 0x91, 0xC1, 0x00, 0, 1, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF], 3, 0 }, // lit without normals
        { [.. TextureCommand, 0x91, 0xC1, 0x00, 0, 1, 2, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0xFF], 3, 2 }, // normal index out of range
        { [0x91, 0x00, 0, 1, 2, 0, 0, 0, 0, 0, 0, 0xFF], 3, 0 },                           // textured without texture command
    };

    [Theory]
    [MemberData(nameof(MalformedMeshes))]
    public void Mesh_RefusesMalformedCommandStreams(byte[] commands, int vertices, int normals)
    {
        byte[] mesh = Mesh(vertices, commands, normals);

        Assert.Throws<FormatException>(() => BattleMeshDecoder.Decode(mesh, 0, mesh.Length, bone: 1));
    }

    [Fact]
    public void Mesh_RefusesMissingStreamsAndInvalidVertexCounts()
    {
        byte[] noCommands = Mesh(3, [0xFF]);
        WriteU32(noCommands, 8, 0);
        Assert.Throws<FormatException>(() => BattleMeshDecoder.Decode(noCommands, 0, noCommands.Length, bone: 1));

        byte[] noVertices = Mesh(3, [0xFF]);
        BinaryPrimitives.WriteInt16LittleEndian(noVertices.AsSpan(24), 0);
        Assert.Throws<FormatException>(() => BattleMeshDecoder.Decode(noVertices, 0, noVertices.Length, bone: 1));
    }

    // ── Model descriptor ────────────────────────────────────────────────────

    /// <summary>
    /// Archive with entries: 0 = descriptor, 1 = texture archive, 2/3 = meshes, 4/5 = key tracks.
    /// </summary>
    private static byte[] ModelFile(uint parentOfBone2 = 1, uint lastBone = 2, uint textureEntry = 1)
    {
        byte[] mesh = Mesh(3, [.. TextureCommand, 0x91, 0x00, 0, 1, 2, 0, 0, 0, 0, 0, 0, 0xFF]);
        var entries = new List<byte[]>
        {
            Concat(U32(textureEntry), U32(lastBone), U32(0), U32(2), U32(4), U32(parentOfBone2), U32(3), U32(5)),
            new byte[] { 1, 2, 3, 4 },
            mesh,
            mesh,
            new byte[8],
            new byte[8],
        };
        int header = entries.Count * 4;
        var data = new List<byte>(new byte[header]);
        byte[] offsets = new byte[header];
        for (int i = 0; i < entries.Count; i++)
        {
            WriteU32(offsets, i * 4, (uint)data.Count);
            data.AddRange(entries[i]);
        }

        byte[] result = [.. data];
        offsets.CopyTo(result, 0);
        return result;
    }

    [Fact]
    public void Model_DecodesBoneHierarchyAndOneMeshPerNonRootBone()
    {
        BattleModelStructure model = BattleModelDecoder.Decode(ModelFile(), modelEntry: 0);

        Assert.Equal(0, model.ModelEntry);
        Assert.Equal(1, model.TextureEntry);
        Assert.Equal(
            [new BattleModelBone(0, null, null, null), new BattleModelBone(1, 0, 2, 4), new BattleModelBone(2, 1, 3, 5)],
            model.Bones);
        Assert.Equal([1, 2], model.Meshes.Select(mesh => mesh.Bone));
        Assert.Equal(model.Meshes[0].EntryOffset + 24 + 6 + (3 * 6), model.Meshes[0].CommandOffset);
    }

    [Fact]
    public void Model_RefusesForwardParentsBadBoneCountsAndOutOfRangeEntries()
    {
        Assert.Throws<FormatException>(() => BattleModelDecoder.Decode(ModelFile(parentOfBone2: 2), 0));
        Assert.Throws<FormatException>(() => BattleModelDecoder.Decode(ModelFile(lastBone: 0), 0));
        Assert.Throws<FormatException>(() => BattleModelDecoder.Decode(ModelFile(lastBone: 64), 0));
        Assert.Throws<FormatException>(() => BattleModelDecoder.Decode(ModelFile(textureEntry: 6), 0));
        Assert.Throws<FormatException>(() => BattleModelDecoder.Decode(ModelFile(), 6));
    }

    // ── JSON projection ─────────────────────────────────────────────────────
    [Fact]
    public void Json_WritesTheStructuralModelManifest()
    {
        BattleModelStructure model = BattleModelDecoder.Decode(ModelFile(), modelEntry: 0);
        var reference = new FighterModelReference(501, 1, 5, false, 12, 0, 12, 7);

        using JsonDocument json = JsonDocument.Parse(BattleModelJsonWriter.WriteModel(reference, model, "TEST-EDITION"));
        JsonElement root = json.RootElement;

        Assert.Equal(BattleModelJsonWriter.ModelFormat, root.GetProperty("format").GetString());
        Assert.Equal("TEST-EDITION", root.GetProperty("edition").GetString());
        Assert.Equal(501, root.GetProperty("fighter_id").GetInt32());
        Assert.Equal(7, root.GetProperty("motion_archive_entry").GetInt32());
        Assert.Equal(1, root.GetProperty("texture_archive_entry").GetInt32());
        Assert.Equal(3, root.GetProperty("bone_count").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("bones")[0].GetProperty("parent").ValueKind);
        JsonElement polygon = root.GetProperty("meshes")[0].GetProperty("polygons")[0];
        Assert.Equal(JsonValueKind.Null, polygon.GetProperty("normal_indices").ValueKind);
        Assert.Equal(0x10, polygon.GetProperty("texture").GetProperty("u_base").GetInt32() & 0xFF);
        Assert.Equal(3, polygon.GetProperty("flat_color").GetArrayLength());
        Assert.False(polygon.GetProperty("unk8").GetBoolean());
        Assert.Equal(3, root.GetProperty("meshes")[0].GetProperty("vertex_count").GetInt32());
    }

    [Fact]
    public void Json_WritesTheDecodeReportWithFailuresInEntryOrder()
    {
        var outcomes = new[]
        {
            BattleModelOutcome.Decoded(new FighterModelReference(501, 1, 5, false, 12, 3, 12, 9)),
            BattleModelOutcome.Failed(new FighterModelEntry(77, 2, 0x40), "bone 1: broken"),
        };

        using JsonDocument json = JsonDocument.Parse(BattleModelJsonWriter.WriteReport(outcomes));
        JsonElement root = json.RootElement;

        Assert.Equal(1, root.GetProperty("decoded").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        Assert.Equal(12, root.GetProperty("fighters").GetProperty("501").GetProperty("model_file").GetInt32());
        Assert.Equal("failed", root.GetProperty("fighters").GetProperty("77").GetProperty("status").GetString());
        Assert.Equal("bone 1: broken", root.GetProperty("failures")[0].GetProperty("reason").GetString());
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private static void WriteU32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);

    private static void WriteEntry(byte[] data, int offset, short id, byte index, byte kind)
    {
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset), id);
        data[offset + 2] = index;
        data[offset + 3] = kind;
    }

    private static byte[] I16(int value)
    {
        byte[] bytes = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)value);
        return bytes;
    }

    private static byte[] U32(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(part => part)];
}
