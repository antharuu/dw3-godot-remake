using System.Text.Json;

namespace Dw3.Formats.Battle;

/// <summary>
/// Writes the per-stage model manifest (<c>stage-N-model.json</c>) for fight-stage backgrounds.
/// Property names are snake_case to match the Python cache contract.
/// </summary>
public static class BattleStageJsonWriter
{
    /// <summary>Gets source features the current stage manifest intentionally does not reproduce.</summary>
    public static IReadOnlyList<string> UnsupportedFeatures { get; } = ["exact PS1 ABR blend equations"];

    /// <summary>
    /// Serialises one complete fight stage: metadata, textures, bones, meshes, tracks, motions.
    /// </summary>
    /// <param name="info">Parsed FightStageInfo record (metadata, lights, bgColor).</param>
    /// <param name="payload">Fully decoded model payload (structure, textures, motions).</param>
    /// <param name="edition">Edition identifier string (e.g. "SLES-03936").</param>
    /// <param name="stagePrefix">File-naming prefix for texture images, e.g. "stage-0".</param>
    /// <param name="modelSha256">SHA-256 hex digest of the raw model file bytes.</param>
    /// <returns>Indented JSON string conforming to the <c>dw3-battle-model-v8</c> schema for a fight stage.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> or <paramref name="payload"/> is null.</exception>
    public static string WriteStageModel(
        BattleStageInfo info,
        BattleModelPayload payload,
        string edition,
        string stagePrefix,
        string modelSha256)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(payload);

        BattleModelStructure model = payload.Structure;
        return BattleModelElementJsonWriter.Serialize(json =>
        {
            json.WriteStartObject();
            json.WriteString("format", BattleModelJsonWriter.ModelFormat);
            json.WriteString("edition", edition);
            json.WriteString("kind", "fight-stage");
            json.WriteNumber("stage", info.StageId);
            WriteEnvironment(json, info);
            json.WriteNumber("texture_archive_entry", model.TextureEntry);

            // Textures use stage prefix instead of "fighter-N".
            json.WriteStartArray("textures");
            foreach (BattleTexture texture in payload.Textures.Textures)
            {
                BattleModelElementJsonWriter.WriteTexture(json, stagePrefix, texture);
            }

            json.WriteEndArray();
            BattleModelElementJsonWriter.WriteTextureVram(json, payload.Textures.Vram);
            BattleModelElementJsonWriter.WriteBones(json, model);
            BattleModelElementJsonWriter.WriteMeshes(json, model.Meshes, payload.Textures);
            BattleModelElementJsonWriter.WriteTracks(json, payload.Tracks);
            BattleModelElementJsonWriter.WriteMotions(json, "motions", payload.Motions);
            BattleModelElementJsonWriter.WriteSourceInvariants(json, payload.Motions);
            json.WriteString("model_sha256", modelSha256);
            BattleModelElementJsonWriter.WriteUnsupported(json, UnsupportedFeatures);
            json.WriteEndObject();
        });
    }

    /// <summary>
    /// Writes the FightStageInfo fields: background clear colour, lights and ambient colour,
    /// music track and the model and motion archive references.
    /// </summary>
    /// <param name="json">Destination writer.</param>
    /// <param name="info">Parsed FightStageInfo record.</param>
    private static void WriteEnvironment(Utf8JsonWriter json, BattleStageInfo info)
    {
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
            json.WritePropertyName("dir");
            BattleModelElementJsonWriter.WriteInts(json, [light.DirX, light.DirY, light.DirZ]);
            json.WritePropertyName("rgb");
            BattleModelElementJsonWriter.WriteInts(json, [light.R, light.G, light.B]);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WritePropertyName("ambient");
        BattleModelElementJsonWriter.WriteInts(json, info.Ambient);
        json.WriteEndObject();

        json.WriteNumber("music", info.Music);
        json.WriteNumber("model_file", info.ModelFile);
        json.WriteNumber("model_archive_entry", info.ModelEntry);
        json.WriteNumber("motion_file", info.MotionFile);
        json.WriteNumber("motion_archive_entry", info.MotionEntry);
        json.WriteStartArray("no_bounds_bones");
        foreach (byte bone in info.NoBoundsBones)
        {
            json.WriteNumberValue(bone);
        }

        json.WriteEndArray();
    }
}
