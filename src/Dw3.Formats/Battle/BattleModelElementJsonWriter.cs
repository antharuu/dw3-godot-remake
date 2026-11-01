using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Dw3.Formats.Battle;

/// <summary>
/// Writes the JSON sections that fighter model manifests and fight-stage manifests share:
/// textures and their VRAM layout, bones, meshes, key tracks, motions and source invariants.
/// Property names are snake_case to match the runtime manifest contract.
/// </summary>
internal static class BattleModelElementJsonWriter
{
    private static readonly JsonWriterOptions Options = new() { Indented = true };

    /// <summary>Runs <paramref name="body"/> against an indented writer and returns the UTF-8 text with a trailing newline.</summary>
    /// <param name="body">Writes one complete JSON value.</param>
    /// <returns>The serialised document.</returns>
    public static string Serialize(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, Options))
        {
            body(json);
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    /// <summary>Writes one decoded texture page object; its PNG is named <c>&lt;imagePrefix&gt;-texture-&lt;key&gt;.png</c>.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="imagePrefix">Owner prefix of the image file, such as <c>fighter-12</c> or <c>stage-3</c>.</param>
    /// <param name="texture">Decoded texture.</param>
    public static void WriteTexture(Utf8JsonWriter json, string imagePrefix, BattleTexture texture)
    {
        json.WriteStartObject();
        json.WriteString("kind", "ps1-tpage");
        json.WriteString("image", $"{imagePrefix}-texture-{texture.Key}.png");
        json.WritePropertyName("size");
        WriteInts(json, [texture.Width, texture.Height]);
        json.WriteNumber("key", texture.Key);
        json.WriteNumber("mode", texture.Mode);
        json.WriteNumber("page", texture.Page);
        json.WritePropertyName("clut_xy");
        if (texture.ClutX is null)
        {
            json.WriteNullValue();
        }
        else
        {
            WriteInts(json, [texture.ClutX.Value, texture.ClutY!.Value]);
        }

        json.WriteNumber("transparent_pixels", texture.TransparentPixels);
        json.WriteNumber("stp_pixels", texture.StpPixels);
        json.WriteEndObject();
    }

    /// <summary>Writes the <c>texture_vram</c> object: local VRAM size and the column of each upload.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="vram">Local VRAM layout.</param>
    public static void WriteTextureVram(Utf8JsonWriter json, BattleTextureVram vram)
    {
        json.WriteStartObject("texture_vram");
        json.WriteNumber("width_words", vram.WidthWords);
        json.WriteNumber("height", vram.Height);
        json.WriteStartArray("uploads");
        foreach (BattleTextureUpload upload in vram.Uploads)
        {
            json.WriteStartObject();
            json.WriteNumber("x_words", upload.XWords);
            json.WriteNumber("width_words", upload.WidthWords);
            json.WriteNumber("height", upload.Height);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>Writes <c>bone_count</c> and the <c>bones</c> array.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="model">Decoded hierarchy.</param>
    public static void WriteBones(Utf8JsonWriter json, BattleModelStructure model)
    {
        json.WriteNumber("bone_count", model.BoneCount);
        json.WriteStartArray("bones");
        foreach (BattleModelBone bone in model.Bones)
        {
            json.WriteStartObject();
            json.WriteNumber("index", bone.Index);
            WriteNullable(json, "parent", bone.Parent);
            WriteNullable(json, "mesh_entry", bone.MeshEntry);
            WriteNullable(json, "key_entry", bone.KeyEntry);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Writes the <c>meshes</c> array.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="meshes">Meshes in bone order.</param>
    /// <param name="textures">Decoded textures; when set, every polygon also gets its <c>texture_key</c>.</param>
    public static void WriteMeshes(Utf8JsonWriter json, IReadOnlyList<BattleMesh> meshes, BattleTextureSet? textures)
    {
        json.WriteStartArray("meshes");
        foreach (BattleMesh mesh in meshes)
        {
            WriteMesh(json, mesh, textures);
        }

        json.WriteEndArray();
    }

    /// <summary>Writes the <c>tracks</c> object: per bone index, the translation, rotation and scale key lists.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="tracks">Tracks keyed by bone index; written in ascending bone order.</param>
    public static void WriteTracks(Utf8JsonWriter json, IReadOnlyDictionary<int, BattleBoneTracks> tracks)
    {
        json.WriteStartObject("tracks");
        foreach ((int bone, BattleBoneTracks boneTracks) in tracks.OrderBy(pair => pair.Key))
        {
            json.WriteStartArray(bone.ToString(CultureInfo.InvariantCulture));
            foreach (IReadOnlyList<BattleTrackKey> track in boneTracks.All)
            {
                json.WriteStartArray();
                foreach (BattleTrackKey key in track)
                {
                    WriteInts(json, [key.Frame, key.X, key.Y, key.Z]);
                }

                json.WriteEndArray();
            }

            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    /// <summary>Writes an array named <paramref name="name"/> holding one step array per motion.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="name">Property name.</param>
    /// <param name="motions">Motions in archive order.</param>
    public static void WriteMotions(Utf8JsonWriter json, string name, IReadOnlyList<IReadOnlyList<BattleMotionStep>> motions)
    {
        json.WriteStartArray(name);
        foreach (IReadOnlyList<BattleMotionStep> motion in motions)
        {
            WriteSteps(json, motion);
        }

        json.WriteEndArray();
    }

    /// <summary>Writes an anonymous array of <paramref name="steps"/>.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="steps">Steps of one motion.</param>
    public static void WriteSteps(Utf8JsonWriter json, IReadOnlyList<BattleMotionStep> steps)
    {
        json.WriteStartArray();
        foreach (BattleMotionStep step in steps)
        {
            WriteStep(json, step);
        }

        json.WriteEndArray();
    }

    /// <summary>Writes the <c>source_invariants</c> object: first and terminator steps of motion 1.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="motions">Decoded motions; motion 1 has at least its terminator step.</param>
    public static void WriteSourceInvariants(Utf8JsonWriter json, IReadOnlyList<IReadOnlyList<BattleMotionStep>> motions)
    {
        json.WriteStartObject("source_invariants");
        json.WritePropertyName("motion_1_first_step");
        WriteStep(json, motions[0][0]);
        json.WritePropertyName("motion_1_terminator");
        WriteStep(json, motions[0][^1]);
        json.WriteEndObject();
    }

    /// <summary>Writes the <c>unsupported</c> array listing source features the manifest does not reproduce.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="features">Feature labels.</param>
    public static void WriteUnsupported(Utf8JsonWriter json, IReadOnlyList<string> features)
    {
        json.WriteStartArray("unsupported");
        foreach (string value in features)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }

    /// <summary>Writes one motion step as [index, count, frame, end frame].</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="step">Motion step.</param>
    public static void WriteStep(Utf8JsonWriter json, BattleMotionStep step) => WriteInts(json, [step.Index, step.Count, step.Frame, step.EndFrame]);

    /// <summary>Writes an anonymous array of integers.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="values">Values in order.</param>
    public static void WriteInts(Utf8JsonWriter json, IReadOnlyList<int> values)
    {
        json.WriteStartArray();
        foreach (int value in values)
        {
            json.WriteNumberValue(value);
        }

        json.WriteEndArray();
    }

    /// <summary>Writes one mesh object: streams, triangles, polygons and source offsets.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="mesh">Decoded mesh.</param>
    /// <param name="textures">Decoded textures, or null to omit <c>texture_key</c>.</param>
    private static void WriteMesh(Utf8JsonWriter json, BattleMesh mesh, BattleTextureSet? textures)
    {
        json.WriteStartObject();
        json.WriteNumber("bone", mesh.Bone);
        json.WriteNumber("vertex_count", mesh.Vertices.Count);
        WriteVectors(json, "vertices", mesh.Vertices);
        WriteVectors(json, "normals", mesh.Normals);
        json.WriteStartArray("triangles");
        foreach (int[] triangle in mesh.Triangles)
        {
            WriteInts(json, triangle);
        }

        json.WriteEndArray();
        json.WriteStartArray("polygons");
        foreach (MeshPolygon polygon in mesh.Polygons)
        {
            WritePolygon(json, polygon, textures);
        }

        json.WriteEndArray();
        json.WriteNumber("entry_offset", mesh.EntryOffset);
        json.WriteNumber("command_offset", mesh.CommandOffset);
        json.WriteNumber("bounds_offset", mesh.BoundsOffset);
        json.WriteEndObject();
    }

    /// <summary>Writes one polygon object with its indices, UVs, texture state and draw flags.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="polygon">Decoded polygon.</param>
    /// <param name="textures">Decoded textures, or null to omit <c>texture_key</c>.</param>
    private static void WritePolygon(Utf8JsonWriter json, MeshPolygon polygon, BattleTextureSet? textures)
    {
        json.WriteStartObject();
        json.WritePropertyName("indices");
        WriteInts(json, polygon.Indices);
        json.WritePropertyName("normal_indices");
        if (polygon.NormalIndices is null)
        {
            json.WriteNullValue();
        }
        else
        {
            WriteInts(json, polygon.NormalIndices);
        }

        json.WriteStartArray("uv");
        foreach (MeshUv uv in polygon.Uvs)
        {
            WriteInts(json, [uv.U, uv.V]);
        }

        json.WriteEndArray();
        WriteTextureState(json, polygon.Texture);
        if (textures is not null)
        {
            json.WriteNumber("texture_key", textures.KeyOf(polygon.Texture));
        }

        json.WriteBoolean("lit", polygon.Lit);
        json.WritePropertyName("flat_color");
        WriteInts(json, [polygon.FlatColor.R, polygon.FlatColor.G, polygon.FlatColor.B]);
        json.WriteBoolean("quad", polygon.Quad);
        json.WriteNumber("abr", polygon.Abr);
        json.WriteBoolean("gouraud", polygon.Gouraud);
        json.WriteBoolean("textured", polygon.Textured);
        json.WriteBoolean("unk8", polygon.Flag0xA);
        json.WriteBoolean("unk4", polygon.Flag0xB);
        json.WriteEndObject();
    }

    /// <summary>Writes the polygon's <c>texture</c> object as read from its texture command.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="texture">Texture state of the polygon.</param>
    private static void WriteTextureState(Utf8JsonWriter json, MeshTextureState texture)
    {
        json.WriteStartObject("texture");
        json.WriteNumber("u_base", texture.UBase);
        json.WriteNumber("page", texture.Page);
        json.WriteNumber("v_base", texture.VBase);
        json.WritePropertyName("clut_xy");
        WriteInts(json, [texture.ClutX, texture.ClutY]);
        json.WriteNumber("tpage_mode", texture.TpageMode);
        json.WriteEndObject();
    }

    /// <summary>Writes an array named <paramref name="name"/> of [x, y, z] vectors.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="name">Property name.</param>
    /// <param name="vectors">Vectors in stream order.</param>
    private static void WriteVectors(Utf8JsonWriter json, string name, IReadOnlyList<MeshVector> vectors)
    {
        json.WriteStartArray(name);
        foreach (MeshVector v in vectors)
        {
            WriteInts(json, [v.X, v.Y, v.Z]);
        }

        json.WriteEndArray();
    }

    /// <summary>Writes <paramref name="value"/> under <paramref name="name"/>, or JSON null when absent.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="name">Property name.</param>
    /// <param name="value">Optional value.</param>
    private static void WriteNullable(Utf8JsonWriter json, string name, int? value)
    {
        if (value is { } number)
        {
            json.WriteNumber(name, number);
        }
        else
        {
            json.WriteNull(name);
        }
    }
}
