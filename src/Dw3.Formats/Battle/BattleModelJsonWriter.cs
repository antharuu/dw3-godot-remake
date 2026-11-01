using System.Globalization;
using System.Text.Json;

namespace Dw3.Formats.Battle;

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

    /// <summary>Source features a fighter manifest does not reproduce, listed under <c>unsupported</c>.</summary>
    private static readonly string[] UnsupportedFeatures = ["exact PS1 ABR blend equations", "lighting normals", "motion blends", "root motion"];

    /// <summary>Serialises the structural manifest of one fighter's model.</summary>
    public static string WriteModel(FighterModelReference reference, BattleModelStructure model, string edition)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(model);
        return BattleModelElementJsonWriter.Serialize(json =>
        {
            json.WriteStartObject();
            WriteHeader(json, reference, model, edition);
            BattleModelElementJsonWriter.WriteBones(json, model);
            BattleModelElementJsonWriter.WriteMeshes(json, model.Meshes, null);
            json.WriteEndObject();
        });
    }

    /// <summary>Serialises one complete fighter model including texture, motion and source metadata.</summary>
    public static string WriteModel(FighterModelReference reference, BattleModelPayload payload, string edition)
    {
        BattleModelStructure model = payload.Structure;
        return BattleModelElementJsonWriter.Serialize(json =>
        {
            json.WriteStartObject();
            WriteHeader(json, reference, model, edition);
            WriteTextures(json, reference.FighterId, payload.Textures);
            BattleModelElementJsonWriter.WriteBones(json, model);
            BattleModelElementJsonWriter.WriteMeshes(json, model.Meshes, payload.Textures);
            BattleModelElementJsonWriter.WriteTracks(json, payload.Tracks);
            BattleModelElementJsonWriter.WriteMotions(json, "motions", payload.Motions);
            json.WritePropertyName("motion_1_steps");
            BattleModelElementJsonWriter.WriteSteps(json, payload.Motions[0]);
            WriteCameras(json, payload.Metadata.Cameras);
            WriteGeometry(json, payload.Metadata.Geometry);
            WriteFace(json, payload.Metadata.Face);
            BattleModelElementJsonWriter.WriteSourceInvariants(json, payload.Motions);
            BattleModelElementJsonWriter.WriteUnsupported(json, UnsupportedFeatures);
            json.WriteEndObject();
        });
    }

    /// <summary>Serialises the decode report: one record per fighter-table entry plus totals.</summary>
    public static string WriteReport(IReadOnlyList<BattleModelOutcome> outcomes, int withFace = 0)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        return BattleModelElementJsonWriter.Serialize(json =>
        {
            json.WriteStartObject();
            json.WriteString("schema", ReportSchema);
            json.WriteStartObject("fighters");
            foreach (BattleModelOutcome outcome in outcomes)
            {
                WriteOutcome(json, outcome);
            }

            json.WriteEndObject();
            WriteFailures(json, outcomes);
            json.WriteNumber("decoded", outcomes.Count(o => o.IsDecoded));
            json.WriteNumber("failed", outcomes.Count(o => !o.IsDecoded));
            json.WriteNumber("with_face", withFace);
            json.WriteEndObject();
        });
    }

    /// <summary>Writes the identity properties every fighter manifest starts with.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="reference">Fighter-table resolution of the fighter.</param>
    /// <param name="model">Decoded hierarchy, which supplies the model and texture archive entries.</param>
    /// <param name="edition">Disc edition label.</param>
    private static void WriteHeader(Utf8JsonWriter json, FighterModelReference reference, BattleModelStructure model, string edition)
    {
        json.WriteString("format", ModelFormat);
        json.WriteString("edition", edition);
        json.WriteNumber("fighter_id", reference.FighterId);
        json.WriteNumber("model_archive_entry", model.ModelEntry);
        json.WriteNumber("motion_archive_entry", reference.MotionEntry);
        json.WriteNumber("texture_archive_entry", model.TextureEntry);
    }

    /// <summary>Writes <c>textures</c>, the first texture again as <c>texture</c>, and <c>texture_vram</c>.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="fighterId">Fighter id used in the texture image names.</param>
    /// <param name="textures">Decoded textures; at least one.</param>
    private static void WriteTextures(Utf8JsonWriter json, int fighterId, BattleTextureSet textures)
    {
        string prefix = $"fighter-{fighterId}";
        json.WriteStartArray("textures");
        foreach (BattleTexture texture in textures.Textures)
        {
            BattleModelElementJsonWriter.WriteTexture(json, prefix, texture);
        }

        json.WriteEndArray();
        json.WritePropertyName("texture");
        BattleModelElementJsonWriter.WriteTexture(json, prefix, textures.Textures[0]);
        BattleModelElementJsonWriter.WriteTextureVram(json, textures.Vram);
    }

    /// <summary>Writes the fighter's source cameras as <c>camPos</c>, <c>camRef</c> and <c>proj</c>.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="cameras">Cameras in fighter-record order.</param>
    private static void WriteCameras(Utf8JsonWriter json, IReadOnlyList<BattleCamera> cameras)
    {
        json.WriteStartArray("cameras");
        foreach (BattleCamera camera in cameras)
        {
            json.WriteStartObject();
            json.WritePropertyName("camPos");
            BattleModelElementJsonWriter.WriteInts(json, [camera.PosX, camera.PosY, camera.PosZ]);
            json.WritePropertyName("camRef");
            BattleModelElementJsonWriter.WriteInts(json, [camera.RefX, camera.RefY, camera.RefZ]);
            json.WriteNumber("proj", camera.Projection);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    /// <summary>Writes the <c>geometry</c> object (placement distance and height).</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="geometry">Fighter placement values.</param>
    private static void WriteGeometry(Utf8JsonWriter json, BattleGeometry geometry)
    {
        json.WriteStartObject("geometry");
        json.WriteNumber("distance", geometry.Distance);
        json.WriteNumber("height", geometry.Height);
        json.WriteEndObject();
    }

    /// <summary>Writes the <c>face</c> object: where the face table was read and its animated parts.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="face">Decoded face table.</param>
    private static void WriteFace(Utf8JsonWriter json, BattleFace face)
    {
        json.WriteStartObject("face");
        json.WriteStartObject("source");
        json.WriteString("file", "SMDLDATA.PRO");
        json.WriteNumber("record_offset", face.Source.RecordOffset);
        json.WriteNumber("face_pointer_offset", face.Source.FacePointerOffset);
        json.WriteNumber("face_offset", face.Source.FaceOffset);
        json.WriteNumber("record_size", face.Source.RecordSize);
        json.WriteString("terminator", face.Source.Terminator);
        json.WriteEndObject();
        json.WriteStartArray("parts");
        foreach (BattleFacePart part in face.Parts)
        {
            json.WriteStartObject();
            json.WriteNumber("index", part.Index);
            json.WriteNumber("x", part.X);
            json.WriteNumber("y", part.Y);
            json.WriteNumber("w", part.Width);
            json.WriteNumber("h", part.Height);
            json.WriteStartArray("frames");
            foreach (BattleFaceFrame frame in part.Frames)
            {
                BattleModelElementJsonWriter.WriteInts(json, [frame.X, frame.Y]);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    /// <summary>Writes one fighter entry of the report, keyed by fighter id.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="outcome">Decode outcome of the fighter.</param>
    private static void WriteOutcome(Utf8JsonWriter json, BattleModelOutcome outcome)
    {
        json.WriteStartObject(outcome.Entry.FighterId.ToString(CultureInfo.InvariantCulture));
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

    /// <summary>Writes the <c>failures</c> array: fighter id and reason of every failed entry.</summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="outcomes">All outcomes, in fighter-table order.</param>
    private static void WriteFailures(Utf8JsonWriter json, IReadOnlyList<BattleModelOutcome> outcomes)
    {
        json.WriteStartArray("failures");
        foreach (BattleModelOutcome outcome in outcomes.Where(o => !o.IsDecoded))
        {
            json.WriteStartObject();
            json.WriteNumber("fighter", outcome.Entry.FighterId);
            json.WriteString("reason", outcome.Reason);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }
}
