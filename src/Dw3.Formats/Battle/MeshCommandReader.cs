namespace Dw3.Formats.Battle;

/// <summary>
/// Walks the command stream of one mesh entry (grammar in <see cref="BattleMeshDecoder"/>) and
/// collects its polygons and triangle list.
/// </summary>
internal ref struct MeshCommandReader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly int _end;
    private readonly int _entryStart;
    private readonly int _bone;
    private readonly int _vertexCount;
    private readonly int _normalCount;
    private readonly MeshDrawState _state = new();
    private readonly List<int[]> _triangles = [];
    private readonly List<MeshPolygon> _polygons = [];
    private int _position;

    /// <summary>Initializes a new instance of the <see cref="MeshCommandReader"/> struct.</summary>
    /// <param name="data">Whole model file.</param>
    /// <param name="mesh">Entry layout: command stream offset, entry start and end, owning bone.</param>
    /// <param name="vertexCount">Size of the vertex stream; polygon indices must stay below it.</param>
    /// <param name="normalCount">Size of the normal stream (0 when absent); normal indices must stay below it.</param>
    public MeshCommandReader(ReadOnlySpan<byte> data, (int Commands, int Start, int End, int Bone) mesh, int vertexCount, int normalCount)
    {
        _data = data;
        _position = mesh.Commands;
        _entryStart = mesh.Start;
        _end = mesh.End;
        _bone = mesh.Bone;
        _vertexCount = vertexCount;
        _normalCount = normalCount;
    }

    /// <summary>Reads commands up to the 0xFF terminator.</summary>
    /// <returns>The triangle list and the polygons in stream order.</returns>
    /// <exception cref="FormatException">The stream violates the command grammar or overruns the entry.</exception>
    public (List<int[]> Triangles, List<MeshPolygon> Polygons) Read()
    {
        while (true)
        {
            if (_position >= _end)
            {
                throw BattleMeshDecoder.Fail(_bone, "command stream lacks 0xff terminator");
            }

            byte op = _data[_position];
            if (op == 0xFF)
            {
                break;
            }

            int hi = op >> 4;
            int lo = op & 0xF;
            if (hi != 0)
            {
                _state.ApplyStateCommand(hi, lo);
                _position++;
            }
            else
            {
                ReadPayloadCommand(lo);
            }
        }

        return (_triangles, _polygons);
    }

    /// <summary>Reads a command whose high nibble is 0: texture (lo 1), colour (lo 2..5) or a polygon run (lo 0).</summary>
    /// <param name="lo">Low nibble of the command byte.</param>
    /// <exception cref="FormatException">The low nibble is 6..15 or the payload overruns the entry.</exception>
    private void ReadPayloadCommand(int lo)
    {
        switch (lo)
        {
            case 1:
                ReadTextureCommand();
                break;
            case >= 2 and <= 5:
                ReadColorCommand(lo - 2);
                break;
            case 0:
                ReadPolygonRun();
                break;
            default:
                throw BattleMeshDecoder.Fail(_bone, $"unsupported low command 0x{lo:X} at 0x{_position - _entryStart:X}");
        }
    }

    /// <summary>Reads the six payload bytes of a texture command: u low, page (= u high), v base, CLUT x, CLUT y, tpage mode.</summary>
    /// <exception cref="FormatException">The payload overruns the entry.</exception>
    private void ReadTextureCommand()
    {
        int p = _position;
        if (p + 7 > _end)
        {
            throw BattleMeshDecoder.Fail(_bone, "texture command overruns entry");
        }

        _state.Texture = new MeshTextureState(_data[p + 1] | (_data[p + 2] << 8), _data[p + 2], _data[p + 3], _data[p + 4], _data[p + 5], _data[p + 6]);
        _position += 7;
    }

    /// <summary>Reads the r, g, b payload of a colour command into colour slot <paramref name="slot"/>.</summary>
    /// <param name="slot">Colour slot 0..3.</param>
    /// <exception cref="FormatException">The payload overruns the entry.</exception>
    private void ReadColorCommand(int slot)
    {
        int p = _position;
        if (p + 4 > _end)
        {
            throw BattleMeshDecoder.Fail(_bone, "color command overruns entry");
        }

        _state.Colors[slot] = new MeshColor(_data[p + 1], _data[p + 2], _data[p + 3]);
        _position += 4;
    }

    /// <summary>Reads polygons, each introduced by a 0x00 byte, while the byte after a polygon is 0x00.</summary>
    /// <exception cref="FormatException">A polygon is invalid or its payload overruns the entry.</exception>
    private void ReadPolygonRun()
    {
        do
        {
            _position++;
            ReadPolygon();
            if (_position >= _end)
            {
                throw BattleMeshDecoder.Fail(_bone, "polygon payload overruns entry");
            }
        }
        while (_data[_position] == 0);
    }

    /// <summary>Reads one polygon at the current position: vertex indices, then normal indices when lit, then UVs when textured.</summary>
    /// <exception cref="FormatException">An index is out of range, a payload overruns the entry or the texture state is missing.</exception>
    private void ReadPolygon()
    {
        int n = _state.IndexCount;
        if (_position + n > _end)
        {
            throw BattleMeshDecoder.Fail(_bone, "polygon indices overrun entry");
        }

        int[] indices = ReadIndices(_position, n, _vertexCount, ("polygon index outside", "vertices"));
        int payload = _position + n;
        int[]? normalIndices = _state.Lit ? ReadNormalIndices(payload, n) : null;
        if (_state.Lit)
        {
            payload += n;
        }

        MeshUv[] uvs = ResolveUvs(payload, n);
        MeshDrawState s = _state;
        _polygons.Add(new MeshPolygon(indices, normalIndices, uvs, s.Texture!, s.Lit, s.Colors[0], s.Quad, s.Abr, s.Gouraud, s.Textured, s.FlagA, s.FlagB));
        _triangles.Add([indices[0], indices[1], indices[2]]);
        if (s.Quad)
        {
            _triangles.Add([indices[2], indices[1], indices[3]]);
        }

        _position = payload + (s.Textured ? n * 2 : 0);
    }

    /// <summary>Reads the normal indices of a lit polygon.</summary>
    /// <param name="at">Offset of the first normal index.</param>
    /// <param name="n">Index count (3 or 4).</param>
    /// <returns>Indices into the normal stream.</returns>
    /// <exception cref="FormatException">The mesh has no normals, the indices overrun the entry or one is out of range.</exception>
    private readonly int[] ReadNormalIndices(int at, int n)
    {
        if (_normalCount == 0)
        {
            throw BattleMeshDecoder.Fail(_bone, "lit polygon without normals");
        }

        if (at + n > _end)
        {
            throw BattleMeshDecoder.Fail(_bone, "normal indices overrun entry");
        }

        return ReadIndices(at, n, _normalCount, ("normal index outside", "normals"));
    }

    /// <summary>
    /// Returns the polygon UVs: the u8 pairs of a textured polygon offset by the texture bases
    /// modulo 256, or a copy of the previous textured polygon's UVs for an untextured one.
    /// </summary>
    /// <param name="at">Offset of the UV payload (only read when textured).</param>
    /// <param name="n">Corner count (3 or 4).</param>
    /// <returns>One UV per corner.</returns>
    /// <exception cref="FormatException">No texture command precedes the polygon, the payload overruns the entry, or an untextured polygon has no UVs to reuse.</exception>
    private readonly MeshUv[] ResolveUvs(int at, int n)
    {
        MeshTextureState? texture = _state.Texture;
        if (!_state.Textured)
        {
            if (_state.ResidualUvs is null || texture is null)
            {
                throw BattleMeshDecoder.Fail(_bone, "untextured polygon precedes texture/UV state");
            }

            return (MeshUv[])_state.ResidualUvs.Clone();
        }

        if (texture is null)
        {
            throw BattleMeshDecoder.Fail(_bone, "textured polygon without texture command");
        }

        if (at + (n * 2) > _end)
        {
            throw BattleMeshDecoder.Fail(_bone, "UV payload overruns entry");
        }

        var uvs = new MeshUv[n];
        for (int i = 0; i < n; i++)
        {
            uvs[i] = new MeshUv((_data[at + (i * 2)] + texture.UBase) & 0xFF, (_data[at + (i * 2) + 1] + texture.VBase) & 0xFF);
        }

        _state.ResidualUvs = uvs;
        return uvs;
    }

    /// <summary>Reads <paramref name="n"/> u8 indices and checks each against <paramref name="limit"/>.</summary>
    /// <param name="at">Offset of the first index.</param>
    /// <param name="n">Index count.</param>
    /// <param name="limit">Exclusive upper bound (stream size).</param>
    /// <param name="error">Message prefix and stream noun used when an index is out of range.</param>
    /// <returns>The indices.</returns>
    /// <exception cref="FormatException">An index is not below <paramref name="limit"/>.</exception>
    private readonly int[] ReadIndices(int at, int n, int limit, (string Message, string Noun) error)
    {
        var indices = new int[n];
        for (int i = 0; i < n; i++)
        {
            indices[i] = _data[at + i];
            if (indices[i] >= limit)
            {
                throw BattleMeshDecoder.Fail(_bone, $"{error.Message} {limit} {error.Noun}");
            }
        }

        return indices;
    }
}
