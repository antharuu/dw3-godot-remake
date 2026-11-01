using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Decodes one battle mesh archive entry.
/// </summary>
/// <remarks>
/// A mesh entry is a nested archive of six u32 offsets relative to the entry start:
/// <code>
/// [0] vertices  required  s16 count (1..255), 4 padding bytes, then count x { s16 x, y, z }
/// [1] normals   optional  s16 count, 4 padding bytes, then count x { s16 x, y, z }
/// [2] commands  required  byte command stream, see below
/// [3] [4]       not read by this decoder
/// [5] bounds    required  only its offset is recorded
/// </code>
/// Command stream grammar. Each command starts with one byte <c>op</c>; <c>hi = op &gt;&gt; 4</c>,
/// <c>lo = op &amp; 0xF</c>:
/// <code>
/// 0xFF                 end of stream
/// hi != 0              one-byte state command, the value is lo:
///   0x8  quad          0x9 textured     0xA flag A     0xB flag B
///   0xC  lit           0xD gouraud      0xE blend mode (abr = lo)
///   0x1..0x7, 0xF      carry no state and are consumed as one byte
/// hi == 0:
///   lo 1  texture      6 payload bytes: u_lo, page (= u_hi), v_base, clut_x, clut_y, tpage_mode
///   lo 2..5 colour     3 payload bytes r, g, b into colour slot lo - 2 (slots start grey 128,128,128)
///   lo 0  polygons     a run of polygons, each introduced by a 0x00 byte:
///                        n = quad ? 4 : 3 vertex indices (u8)
///                        lit:      n normal indices (u8)
///                        textured: n x { u8 u, u8 v }, added to (u base low byte, v base) modulo 256
///                      the run continues while the byte after a polygon is 0x00
///   other lo           invalid
/// </code>
/// An untextured polygon has no UV payload: it reuses the UVs of the previous textured polygon
/// and the current texture state, exactly as the disc data expects. An untextured polygon that
/// precedes any textured polygon, a textured polygon before any texture command, a lit polygon
/// without a normal stream, an out-of-range index or a missing terminator is refused.
/// </remarks>
public static class BattleMeshDecoder
{
    private const int EntryCount = 6;
    private const int StreamHeaderBytes = 6;
    private const int MaximumVertices = 255;
    private static readonly MeshColor DefaultColor = new(128, 128, 128);

    /// <summary>Decodes the mesh entry spanning [<paramref name="start"/>, <paramref name="end"/>) of <paramref name="data"/>.</summary>
    /// <param name="bone">Owning bone, used in the result and in error messages.</param>
    /// <exception cref="FormatException">The entry violates the grammar above.</exception>
    public static BattleMesh Decode(ReadOnlySpan<byte> data, int start, int end, int bone)
    {
        int[] offsets = ModelArchive.ReadNestedOffsets(data, start, end, EntryCount);
        if (offsets[0] == 0 || offsets[2] == 0 || offsets[5] == 0)
        {
            throw Fail(bone, "missing vertices, commands, or bounds archive entry");
        }

        MeshVector[] vertices = ReadVertices(data, start + offsets[0], end, bone);
        MeshVector[] normals = offsets[1] == 0 ? [] : ReadNormals(data, start + offsets[1], end, bone);
        int commands = start + offsets[2];
        var (triangles, polygons) = ReadCommands(data, commands, end, start, bone, vertices.Length, normals.Length);
        return new BattleMesh(bone, vertices, normals, triangles, polygons, start, commands, start + offsets[5]);
    }

    private static MeshVector[] ReadVertices(ReadOnlySpan<byte> data, int at, int end, int bone)
    {
        int count = ReadI16(data, at, bone, "vertex count");
        if (count <= 0 || count > MaximumVertices)
            throw Fail(bone, $"invalid vertex count {count}");
        if (at + StreamHeaderBytes + count * 6 > end)
            throw Fail(bone, "vertex stream overruns entry");
        return ReadVectors(data, at + StreamHeaderBytes, count);
    }

    private static MeshVector[] ReadNormals(ReadOnlySpan<byte> data, int at, int end, int bone)
    {
        if (at + StreamHeaderBytes > end)
            throw Fail(bone, "normal stream header overruns entry");
        int count = ReadI16(data, at, bone, "normal count");
        if (count < 0 || at + StreamHeaderBytes + count * 6 > end)
            throw Fail(bone, "normal stream overruns entry");
        return ReadVectors(data, at + StreamHeaderBytes, count);
    }

    private static MeshVector[] ReadVectors(ReadOnlySpan<byte> data, int at, int count)
    {
        var vectors = new MeshVector[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> v = data.Slice(at + i * 6, 6);
            vectors[i] = new MeshVector(
                BinaryPrimitives.ReadInt16LittleEndian(v),
                BinaryPrimitives.ReadInt16LittleEndian(v[2..]),
                BinaryPrimitives.ReadInt16LittleEndian(v[4..]));
        }
        return vectors;
    }

    private static (List<int[]> Triangles, List<MeshPolygon> Polygons) ReadCommands(
        ReadOnlySpan<byte> data, int p, int end, int start, int bone, int vertexCount, int normalCount)
    {
        bool textured = false, lit = false, quad = false, gouraud = false, flagA = false, flagB = false;
        int abr = 0;
        MeshTextureState? texture = null;
        MeshUv[]? residualUvs = null;
        var colors = new MeshColor[] { DefaultColor, DefaultColor, DefaultColor, DefaultColor };
        var triangles = new List<int[]>();
        var polygons = new List<MeshPolygon>();
        while (true)
        {
            if (p >= end)
                throw Fail(bone, "command stream lacks 0xff terminator");
            byte op = data[p];
            if (op == 0xFF)
                break;
            int hi = op >> 4, lo = op & 0xF;
            if (hi != 0)
            {
                switch (hi)
                {
                    case 0x8: quad = lo != 0; break;
                    case 0x9: textured = lo != 0; break;
                    case 0xA: flagA = lo != 0; break;
                    case 0xB: flagB = lo != 0; break;
                    case 0xC: lit = lo != 0; break;
                    case 0xD: gouraud = lo != 0; break;
                    case 0xE: abr = lo; break;
                }
                p++;
                continue;
            }
            if (lo == 1)
            {
                if (p + 7 > end)
                    throw Fail(bone, "texture command overruns entry");
                texture = new MeshTextureState(data[p + 1] | (data[p + 2] << 8), data[p + 2], data[p + 3], data[p + 4], data[p + 5], data[p + 6]);
                p += 7;
                continue;
            }
            if (lo is >= 2 and <= 5)
            {
                if (p + 4 > end)
                    throw Fail(bone, "color command overruns entry");
                colors[lo - 2] = new MeshColor(data[p + 1], data[p + 2], data[p + 3]);
                p += 4;
                continue;
            }
            if (lo != 0)
                throw Fail(bone, $"unsupported low command 0x{lo:X} at 0x{p - start:X}");

            while (true)
            {
                p++;
                int n = quad ? 4 : 3;
                if (p + n > end)
                    throw Fail(bone, "polygon indices overrun entry");
                int[] indices = ReadIndices(data, p, n, vertexCount, bone, "polygon index outside", "vertices");
                int payload = p + n;
                int[]? normalIndices = null;
                if (lit)
                {
                    if (normalCount == 0)
                        throw Fail(bone, "lit polygon without normals");
                    if (payload + n > end)
                        throw Fail(bone, "normal indices overrun entry");
                    normalIndices = ReadIndices(data, payload, n, normalCount, bone, "normal index outside", "normals");
                    payload += n;
                }
                MeshUv[] uvs;
                if (textured)
                {
                    if (texture is null)
                        throw Fail(bone, "textured polygon without texture command");
                    if (payload + n * 2 > end)
                        throw Fail(bone, "UV payload overruns entry");
                    uvs = new MeshUv[n];
                    for (int i = 0; i < n; i++)
                        uvs[i] = new MeshUv((data[payload + i * 2] + texture.UBase) & 0xFF, (data[payload + i * 2 + 1] + texture.VBase) & 0xFF);
                    residualUvs = uvs;
                }
                else if (residualUvs is null || texture is null)
                {
                    throw Fail(bone, "untextured polygon precedes texture/UV state");
                }
                else
                {
                    uvs = (MeshUv[])residualUvs.Clone();
                }
                polygons.Add(new MeshPolygon(indices, normalIndices, uvs, texture, lit, colors[0], quad, abr, gouraud, textured, flagA, flagB));
                triangles.Add([indices[0], indices[1], indices[2]]);
                if (quad)
                    triangles.Add([indices[2], indices[1], indices[3]]);
                p += n;
                if (lit) p += n;
                if (textured) p += n * 2;
                if (p >= end)
                    throw Fail(bone, "polygon payload overruns entry");
                if (data[p] != 0)
                    break;
            }
        }
        return (triangles, polygons);
    }

    private static int[] ReadIndices(ReadOnlySpan<byte> data, int at, int n, int limit, int bone, string message, string noun)
    {
        var indices = new int[n];
        for (int i = 0; i < n; i++)
        {
            indices[i] = data[at + i];
            if (indices[i] >= limit)
                throw Fail(bone, $"{message} {limit} {noun}");
        }
        return indices;
    }

    private static short ReadI16(ReadOnlySpan<byte> data, int at, int bone, string what)
    {
        if (at < 0 || at + 2 > data.Length)
            throw Fail(bone, $"{what} at 0x{at:X} lies outside the data");
        return BinaryPrimitives.ReadInt16LittleEndian(data.Slice(at, 2));
    }

    private static FormatException Fail(int bone, string message) => new($"bone {bone}: {message}");
}
