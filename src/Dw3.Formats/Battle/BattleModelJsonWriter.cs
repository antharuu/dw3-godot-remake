using System.Text;
using System.Text.Json;

namespace Dw3.Formats.Battle;

/// <summary>Result of decoding one fighter-table entry: either a resolved model or a failure reason.</summary>
public sealed record BattleModelOutcome(FighterModelEntry Entry, FighterModelReference? Reference, string? Reason)
{
    /// <summary>A fighter whose model decoded.</summary>
    public static BattleModelOutcome Decoded(FighterModelReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new(new FighterModelEntry((short)reference.FighterId, (byte)reference.Index, (byte)reference.Kind), reference, null);
    }

    /// <summary>A fighter whose resolution or model decoding failed with <paramref name="reason"/>.</summary>
    public static BattleModelOutcome Failed(FighterModelEntry entry, string reason) => new(entry, null, reason);

    /// <summary>True when the model decoded.</summary>
    public bool IsDecoded => Reason is null;
}

/// <summary>
/// Writes the per-fighter model manifest (<c>fighter-&lt;id&gt;-model.json</c>) and the decode report
/// of the battle models domain. Property names are snake_case because the runtime contract
/// predates this extractor.
/// </summary>
public static class BattleModelJsonWriter
{
    /// <summary>Format label of the model manifest.</summary>
    public const string ModelFormat = "dw3-battle-model-v8";

    /// <summary>Schema label of the decode report.</summary>
    public const string ReportSchema = "battle-model-decode-report-v1";

    private static readonly JsonWriterOptions Options = new() { Indented = true };

    /// <summary>Serialises the structural manifest of one fighter's model.</summary>
    public static string WriteModel(FighterModelReference reference, BattleModelStructure model, string edition)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(model);
        return Write(json =>
        {
            json.WriteStartObject();
            json.WriteString("format", ModelFormat);
            json.WriteString("edition", edition);
            json.WriteNumber("fighter_id", reference.FighterId);
            json.WriteNumber("model_archive_entry", model.ModelEntry);
            json.WriteNumber("motion_archive_entry", reference.MotionEntry);
            json.WriteNumber("texture_archive_entry", model.TextureEntry);
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
                WriteMesh(json, mesh, null);
            json.WriteEndArray();
            json.WriteEndObject();
        });
    }

    /// <summary>Serialises one complete fighter model including texture, motion and source metadata.</summary>
    public static string WriteModel(FighterModelReference reference, BattleModelPayload payload, string edition)
    {
        BattleModelStructure model = payload.Structure;
        return Write(json =>
        {
            json.WriteStartObject();
            json.WriteString("format", ModelFormat);
            json.WriteString("edition", edition);
            json.WriteNumber("fighter_id", reference.FighterId);
            json.WriteNumber("model_archive_entry", model.ModelEntry);
            json.WriteNumber("motion_archive_entry", reference.MotionEntry);
            json.WriteNumber("texture_archive_entry", model.TextureEntry);
            json.WriteStartArray("textures");
            foreach (BattleTexture texture in payload.Textures.Textures) WriteTexture(json, reference.FighterId, texture);
            json.WriteEndArray();
            json.WritePropertyName("texture");
            WriteTexture(json, reference.FighterId, payload.Textures.Textures[0]);
            json.WriteStartObject("texture_vram");
            json.WriteNumber("width_words", payload.Textures.Vram.WidthWords);
            json.WriteNumber("height", payload.Textures.Vram.Height);
            json.WriteStartArray("uploads");
            foreach (BattleTextureUpload upload in payload.Textures.Vram.Uploads)
            {
                json.WriteStartObject(); json.WriteNumber("x_words", upload.XWords); json.WriteNumber("width_words", upload.WidthWords); json.WriteNumber("height", upload.Height); json.WriteEndObject();
            }
            json.WriteEndArray(); json.WriteEndObject();
            json.WriteNumber("bone_count", model.BoneCount);
            json.WriteStartArray("bones");
            foreach (BattleModelBone bone in model.Bones)
            {
                json.WriteStartObject(); json.WriteNumber("index", bone.Index); WriteNullable(json, "parent", bone.Parent); WriteNullable(json, "mesh_entry", bone.MeshEntry); WriteNullable(json, "key_entry", bone.KeyEntry); json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("meshes");
            foreach (BattleMesh mesh in model.Meshes) WriteMesh(json, mesh, payload.Textures);
            json.WriteEndArray();
            json.WriteStartObject("tracks");
            foreach ((int bone, BattleBoneTracks tracks) in payload.Tracks.OrderBy(pair => pair.Key))
            {
                json.WriteStartArray(bone.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (IReadOnlyList<BattleTrackKey> track in tracks.All)
                {
                    json.WriteStartArray();
                    foreach (BattleTrackKey key in track) WriteInts(json, [key.Frame, key.X, key.Y, key.Z]);
                    json.WriteEndArray();
                }
                json.WriteEndArray();
            }
            json.WriteEndObject();
            WriteMotions(json, "motions", payload.Motions);
            json.WriteStartArray("motion_1_steps"); foreach (BattleMotionStep step in payload.Motions[0]) WriteStep(json, step); json.WriteEndArray();
            json.WriteStartArray("cameras");
            foreach (BattleCamera camera in payload.Metadata.Cameras)
            {
                json.WriteStartObject(); json.WritePropertyName("camPos"); WriteInts(json, [camera.PosX, camera.PosY, camera.PosZ]); json.WritePropertyName("camRef"); WriteInts(json, [camera.RefX, camera.RefY, camera.RefZ]); json.WriteNumber("proj", camera.Projection); json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartObject("geometry"); json.WriteNumber("distance", payload.Metadata.Geometry.Distance); json.WriteNumber("height", payload.Metadata.Geometry.Height); json.WriteEndObject();
            WriteFace(json, payload.Metadata.Face);
            json.WriteStartObject("source_invariants"); json.WritePropertyName("motion_1_first_step"); WriteStep(json, payload.Motions[0][0]); json.WritePropertyName("motion_1_terminator"); WriteStep(json, payload.Motions[0][^1]); json.WriteEndObject();
            json.WriteStartArray("unsupported");
            foreach (string value in new[] { "exact PS1 ABR blend equations", "lighting normals", "motion blends", "root motion" }) json.WriteStringValue(value);
            json.WriteEndArray();
            json.WriteEndObject();
        });
    }

    /// <summary>Serialises the decode report: one record per fighter-table entry plus totals.</summary>
    public static string WriteReport(IReadOnlyList<BattleModelOutcome> outcomes, int withFace = 0)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        return Write(json =>
        {
            json.WriteStartObject();
            json.WriteString("schema", ReportSchema);
            json.WriteStartObject("fighters");
            foreach (BattleModelOutcome outcome in outcomes)
            {
                json.WriteStartObject(outcome.Entry.FighterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                json.WriteString("status", outcome.IsDecoded ? "decoded" : "failed");
                json.WriteNumber("index", outcome.Entry.Index);
                json.WriteNumber("kind", outcome.Entry.Kind);
                if (outcome.Reference is { } reference)
                {
                    json.WriteNumber("model_file", reference.ModelFile);
                    json.WriteNumber("model_entry", reference.ModelEntry);
                }
                else
                {
                    json.WriteString("reason", outcome.Reason);
                }
                json.WriteEndObject();
            }
            json.WriteEndObject();
            json.WriteStartArray("failures");
            foreach (BattleModelOutcome outcome in outcomes.Where(o => !o.IsDecoded))
            {
                json.WriteStartObject();
                json.WriteNumber("fighter", outcome.Entry.FighterId);
                json.WriteString("reason", outcome.Reason);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteNumber("decoded", outcomes.Count(o => o.IsDecoded));
            json.WriteNumber("failed", outcomes.Count(o => !o.IsDecoded));
            json.WriteNumber("with_face", withFace);
            json.WriteEndObject();
        });
    }

    private static void WriteTexture(Utf8JsonWriter json, int fighterId, BattleTexture texture)
    {
        json.WriteStartObject(); json.WriteString("kind", "ps1-tpage"); json.WriteString("image", $"fighter-{fighterId}-texture-{texture.Key}.png");
        json.WritePropertyName("size"); WriteInts(json, [texture.Width, texture.Height]); json.WriteNumber("key", texture.Key); json.WriteNumber("mode", texture.Mode); json.WriteNumber("page", texture.Page);
        json.WritePropertyName("clut_xy"); if (texture.ClutX is null) json.WriteNullValue(); else WriteInts(json, [texture.ClutX.Value, texture.ClutY!.Value]);
        json.WriteNumber("transparent_pixels", texture.TransparentPixels); json.WriteNumber("stp_pixels", texture.StpPixels); json.WriteEndObject();
    }

    private static void WriteMotions(Utf8JsonWriter json, string name, IReadOnlyList<IReadOnlyList<BattleMotionStep>> motions)
    {
        json.WriteStartArray(name); foreach (IReadOnlyList<BattleMotionStep> motion in motions) { json.WriteStartArray(); foreach (BattleMotionStep step in motion) WriteStep(json, step); json.WriteEndArray(); }
        json.WriteEndArray();
    }

    private static void WriteStep(Utf8JsonWriter json, BattleMotionStep step) => WriteInts(json, [step.Index, step.Count, step.Frame, step.EndFrame]);

    private static void WriteFace(Utf8JsonWriter json, BattleFace face)
    {
        json.WriteStartObject("face"); json.WriteStartObject("source"); json.WriteString("file", "SMDLDATA.PRO"); json.WriteNumber("record_offset", face.Source.RecordOffset); json.WriteNumber("face_pointer_offset", face.Source.FacePointerOffset); json.WriteNumber("face_offset", face.Source.FaceOffset); json.WriteNumber("record_size", face.Source.RecordSize); json.WriteString("terminator", face.Source.Terminator); json.WriteEndObject();
        json.WriteStartArray("parts"); foreach (BattleFacePart part in face.Parts) { json.WriteStartObject(); json.WriteNumber("index", part.Index); json.WriteNumber("x", part.X); json.WriteNumber("y", part.Y); json.WriteNumber("w", part.Width); json.WriteNumber("h", part.Height); json.WriteStartArray("frames"); foreach (BattleFaceFrame frame in part.Frames) WriteInts(json, [frame.X, frame.Y]); json.WriteEndArray(); json.WriteEndObject(); }
        json.WriteEndArray(); json.WriteEndObject();
    }

    private static void WriteMesh(Utf8JsonWriter json, BattleMesh mesh, BattleTextureSet? textures)
    {
        json.WriteStartObject();
        json.WriteNumber("bone", mesh.Bone);
        json.WriteNumber("vertex_count", mesh.Vertices.Count);
        WriteVectors(json, "vertices", mesh.Vertices);
        WriteVectors(json, "normals", mesh.Normals);
        json.WriteStartArray("triangles");
        foreach (int[] triangle in mesh.Triangles)
            WriteInts(json, triangle);
        json.WriteEndArray();
        json.WriteStartArray("polygons");
        foreach (MeshPolygon polygon in mesh.Polygons)
            WritePolygon(json, polygon, textures);
        json.WriteEndArray();
        json.WriteNumber("entry_offset", mesh.EntryOffset);
        json.WriteNumber("command_offset", mesh.CommandOffset);
        json.WriteNumber("bounds_offset", mesh.BoundsOffset);
        json.WriteEndObject();
    }

    private static void WritePolygon(Utf8JsonWriter json, MeshPolygon polygon, BattleTextureSet? textures)
    {
        json.WriteStartObject();
        json.WritePropertyName("indices");
        WriteInts(json, polygon.Indices);
        json.WritePropertyName("normal_indices");
        if (polygon.NormalIndices is null) json.WriteNullValue();
        else WriteInts(json, polygon.NormalIndices);
        json.WriteStartArray("uv");
        foreach (MeshUv uv in polygon.Uvs)
        {
            json.WriteStartArray();
            json.WriteNumberValue(uv.U);
            json.WriteNumberValue(uv.V);
            json.WriteEndArray();
        }
        json.WriteEndArray();
        MeshTextureState texture = polygon.Texture;
        json.WriteStartObject("texture");
        json.WriteNumber("u_base", texture.UBase);
        json.WriteNumber("page", texture.Page);
        json.WriteNumber("v_base", texture.VBase);
        json.WritePropertyName("clut_xy");
        WriteInts(json, [texture.ClutX, texture.ClutY]);
        json.WriteNumber("tpage_mode", texture.TpageMode);
        json.WriteEndObject();
        if (textures is not null)
            json.WriteNumber("texture_key", textures.KeyOf(texture));
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

    private static void WriteVectors(Utf8JsonWriter json, string name, IReadOnlyList<MeshVector> vectors)
    {
        json.WriteStartArray(name);
        foreach (MeshVector v in vectors)
            WriteInts(json, [v.X, v.Y, v.Z]);
        json.WriteEndArray();
    }

    private static void WriteInts(Utf8JsonWriter json, IReadOnlyList<int> values)
    {
        json.WriteStartArray();
        foreach (int value in values)
            json.WriteNumberValue(value);
        json.WriteEndArray();
    }

    private static void WriteNullable(Utf8JsonWriter json, string name, int? value)
    {
        if (value is { } number) json.WriteNumber(name, number);
        else json.WriteNull(name);
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, Options))
            body(json);
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }
}
