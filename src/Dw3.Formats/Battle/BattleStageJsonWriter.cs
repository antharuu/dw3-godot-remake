using System.Text;
using System.Text.Json;

namespace Dw3.Formats.Battle;

/// <summary>
/// Writes the per-stage model manifest (<c>stage-N-model.json</c>) for fight-stage backgrounds.
/// Property names are snake_case to match the Python cache contract.
/// </summary>
public static class BattleStageJsonWriter
{
    private static readonly JsonWriterOptions Options = new() { Indented = true };

    /// <summary>
    /// Serialises one complete fight stage: metadata, textures, bones, meshes, tracks, motions.
    /// </summary>
    /// <param name="info">Parsed FightStageInfo record (metadata, lights, bgColor).</param>
    /// <param name="payload">Fully decoded model payload (structure, textures, motions).</param>
    /// <param name="edition">Edition identifier string (e.g. "SLES-03936").</param>
    /// <param name="stagePrefix">File-naming prefix for texture images, e.g. "stage-0".</param>
    /// <param name="modelSha256">SHA-256 hex digest of the raw model file bytes.</param>
    public static string WriteStageModel(BattleStageInfo info, BattleModelPayload payload,
        string edition, string stagePrefix, string modelSha256)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(payload);

        BattleModelStructure model = payload.Structure;
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, Options))
        {
            json.WriteStartObject();
            json.WriteString("format", BattleModelJsonWriter.ModelFormat);
            json.WriteString("edition", edition);
            json.WriteString("kind", "fight-stage");
            json.WriteNumber("stage", info.StageId);

            // Background clear colour (3 bytes RGB).
            json.WriteStartArray("bg_color");
            json.WriteNumberValue(info.BgColor[0]);
            json.WriteNumberValue(info.BgColor[1]);
            json.WriteNumberValue(info.BgColor[2]);
            json.WriteEndArray();

            // LightSet: three directional lights + ambient.
            json.WriteStartObject("lights");
            json.WriteStartArray("lights");
            foreach (BattleStageLightEntry light in info.Lights)
            {
                json.WriteStartObject();
                json.WriteStartArray("dir");
                json.WriteNumberValue(light.DirX);
                json.WriteNumberValue(light.DirY);
                json.WriteNumberValue(light.DirZ);
                json.WriteEndArray();
                json.WriteStartArray("rgb");
                json.WriteNumberValue(light.R);
                json.WriteNumberValue(light.G);
                json.WriteNumberValue(light.B);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("ambient");
            foreach (int v in info.Ambient) json.WriteNumberValue(v);
            json.WriteEndArray();
            json.WriteEndObject();

            json.WriteNumber("music", info.Music);
            json.WriteNumber("model_file", info.ModelFile);
            json.WriteNumber("model_archive_entry", info.ModelEntry);
            json.WriteNumber("motion_archive_entry", info.MotionEntry);
            json.WriteNumber("texture_archive_entry", model.TextureEntry);

            // Textures use stage prefix instead of "fighter-N".
            json.WriteStartArray("textures");
            foreach (BattleTexture texture in payload.Textures.Textures)
                WriteStageTexture(json, stagePrefix, texture);
            json.WriteEndArray();

            json.WriteStartObject("texture_vram");
            json.WriteNumber("width_words", payload.Textures.Vram.WidthWords);
            json.WriteNumber("height", payload.Textures.Vram.Height);
            json.WriteStartArray("uploads");
            foreach (BattleTextureUpload upload in payload.Textures.Vram.Uploads)
            {
                json.WriteStartObject();
                json.WriteNumber("x_words", upload.XWords);
                json.WriteNumber("width_words", upload.WidthWords);
                json.WriteNumber("height", upload.Height);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();

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

            json.WriteStartArray("meshes");
            foreach (BattleMesh mesh in model.Meshes)
                WriteMesh(json, mesh, payload.Textures, stagePrefix);
            json.WriteEndArray();

            // Tracks keyed by bone index.
            json.WriteStartObject("tracks");
            foreach ((int bone, BattleBoneTracks tracks) in payload.Tracks.OrderBy(pair => pair.Key))
            {
                json.WriteStartArray(bone.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (IReadOnlyList<BattleTrackKey> track in tracks.All)
                {
                    json.WriteStartArray();
                    foreach (BattleTrackKey key in track)
                        WriteInts(json, [key.Frame, key.X, key.Y, key.Z]);
                    json.WriteEndArray();
                }
                json.WriteEndArray();
            }
            json.WriteEndObject();

            // Motions.
            json.WriteStartArray("motions");
            foreach (IReadOnlyList<BattleMotionStep> motion in payload.Motions)
            {
                json.WriteStartArray();
                foreach (BattleMotionStep step in motion) WriteStep(json, step);
                json.WriteEndArray();
            }
            json.WriteEndArray();

            json.WriteStartObject("source_invariants");
            json.WritePropertyName("motion_1_first_step"); WriteStep(json, payload.Motions[0][0]);
            json.WritePropertyName("motion_1_terminator"); WriteStep(json, payload.Motions[0][^1]);
            json.WriteEndObject();

            json.WriteString("model_sha256", modelSha256);

            json.WriteStartArray("unsupported");
            foreach (string v in new[] { "exact PS1 ABR blend equations", "stage motion playback", "bgColor fade-in" })
                json.WriteStringValue(v);
            json.WriteEndArray();

            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static void WriteStageTexture(Utf8JsonWriter json, string stagePrefix, BattleTexture texture)
    {
        json.WriteStartObject();
        json.WriteString("kind", "ps1-tpage");
        json.WriteString("image", $"{stagePrefix}-texture-{texture.Key}.png");
        json.WriteStartArray("size");
        json.WriteNumberValue(texture.Width);
        json.WriteNumberValue(texture.Height);
        json.WriteEndArray();
        json.WriteNumber("key", texture.Key);
        json.WriteNumber("mode", texture.Mode);
        json.WriteNumber("page", texture.Page);
        json.WritePropertyName("clut_xy");
        if (texture.ClutX is null) json.WriteNullValue();
        else { json.WriteStartArray(); json.WriteNumberValue(texture.ClutX.Value); json.WriteNumberValue(texture.ClutY!.Value); json.WriteEndArray(); }
        json.WriteNumber("transparent_pixels", texture.TransparentPixels);
        json.WriteNumber("stp_pixels", texture.StpPixels);
        json.WriteEndObject();
    }

    private static void WriteMesh(Utf8JsonWriter json, BattleMesh mesh, BattleTextureSet textures, string stagePrefix)
    {
        _ = stagePrefix; // prefix is embedded in texture keys, not polygon serialisation
        json.WriteStartObject();
        json.WriteNumber("bone", mesh.Bone);
        json.WriteNumber("vertex_count", mesh.Vertices.Count);
        WriteVectors(json, "vertices", mesh.Vertices);
        WriteVectors(json, "normals", mesh.Normals);
        json.WriteStartArray("triangles");
        foreach (int[] triangle in mesh.Triangles) WriteInts(json, triangle);
        json.WriteEndArray();
        json.WriteStartArray("polygons");
        foreach (MeshPolygon polygon in mesh.Polygons)
        {
            json.WriteStartObject();
            json.WritePropertyName("indices"); WriteInts(json, polygon.Indices);
            json.WritePropertyName("normal_indices");
            if (polygon.NormalIndices is null) json.WriteNullValue();
            else WriteInts(json, polygon.NormalIndices);
            json.WriteStartArray("uv");
            foreach (MeshUv uv in polygon.Uvs) { json.WriteStartArray(); json.WriteNumberValue(uv.U); json.WriteNumberValue(uv.V); json.WriteEndArray(); }
            json.WriteEndArray();
            MeshTextureState texture = polygon.Texture;
            json.WriteStartObject("texture");
            json.WriteNumber("u_base", texture.UBase); json.WriteNumber("page", texture.Page);
            json.WriteNumber("v_base", texture.VBase);
            json.WriteStartArray("clut_xy"); json.WriteNumberValue(texture.ClutX); json.WriteNumberValue(texture.ClutY); json.WriteEndArray();
            json.WriteNumber("tpage_mode", texture.TpageMode);
            json.WriteEndObject();
            json.WriteNumber("texture_key", textures.KeyOf(texture));
            json.WriteBoolean("lit", polygon.Lit);
            json.WritePropertyName("flat_color"); WriteInts(json, [polygon.FlatColor.R, polygon.FlatColor.G, polygon.FlatColor.B]);
            json.WriteBoolean("quad", polygon.Quad);
            json.WriteNumber("abr", polygon.Abr);
            json.WriteBoolean("gouraud", polygon.Gouraud);
            json.WriteBoolean("textured", polygon.Textured);
            json.WriteBoolean("unk8", polygon.Flag0xA);
            json.WriteBoolean("unk4", polygon.Flag0xB);
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteNumber("entry_offset", mesh.EntryOffset);
        json.WriteNumber("command_offset", mesh.CommandOffset);
        json.WriteNumber("bounds_offset", mesh.BoundsOffset);
        json.WriteEndObject();
    }

    private static void WriteStep(Utf8JsonWriter json, BattleMotionStep step)
        => WriteInts(json, [step.Index, step.Count, step.Frame, step.EndFrame]);

    private static void WriteVectors(Utf8JsonWriter json, string name, IReadOnlyList<MeshVector> vectors)
    {
        json.WriteStartArray(name);
        foreach (MeshVector v in vectors) WriteInts(json, [v.X, v.Y, v.Z]);
        json.WriteEndArray();
    }

    private static void WriteInts(Utf8JsonWriter json, IReadOnlyList<int> values)
    {
        json.WriteStartArray();
        foreach (int value in values) json.WriteNumberValue(value);
        json.WriteEndArray();
    }

    private static void WriteNullable(Utf8JsonWriter json, string name, int? value)
    {
        if (value is { } number) json.WriteNumber(name, number);
        else json.WriteNull(name);
    }
}
