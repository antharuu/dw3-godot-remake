using System.Text.Json;

namespace Dw3.Formats.Battle;

/// <summary>Serializes a fully decoded shared three-dimensional effect model for the portable battle data pack.</summary>
public static class EffectModelJsonWriter
{
    /// <summary>Gets the versioned format label of an effect-model manifest.</summary>
    public const string Format = "dw3-battle-effect-model-v1";

    /// <summary>Writes one complete effect-model manifest, including mesh, texture, animation and motion data.</summary>
    /// <param name="reference">The script-visible effect reference and its source archive locations.</param>
    /// <param name="payload">The decoded model payload.</param>
    /// <param name="edition">The source-disc edition.</param>
    /// <returns>Indented JSON with a trailing newline.</returns>
    public static string Write(EffectModelReference reference, BattleModelPayload payload, string edition)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(payload);
        return BattleModelElementJsonWriter.Serialize(json =>
        {
            json.WriteStartObject();
            json.WriteString("format", Format);
            json.WriteString("edition", edition);
            json.WriteNumber("effect_id", reference.Id);
            json.WriteNumber("model_file", reference.ModelFile);
            json.WriteNumber("model_archive_entry", reference.ModelEntry);
            json.WriteNumber("motion_file", reference.MotionFile);
            json.WriteNumber("motion_archive_entry", reference.MotionEntry);
            json.WriteNumber("texture_archive_entry", payload.Structure.TextureEntry);
            WriteTextures(json, reference.Id, payload.Textures);
            BattleModelElementJsonWriter.WriteBones(json, payload.Structure);
            BattleModelElementJsonWriter.WriteMeshes(json, payload.Structure.Meshes, payload.Textures);
            BattleModelElementJsonWriter.WriteTracks(json, payload.Tracks);
            BattleModelElementJsonWriter.WriteMotions(json, "motions", payload.Motions);
            BattleModelElementJsonWriter.WriteSourceInvariants(json, payload.Motions);
            BattleModelElementJsonWriter.WriteUnsupported(json, ["effect instance position and rotation are action-script operands", "exact PS1 ABR blend equations", "lighting normals"]);
            json.WriteEndObject();
        });
    }

    /// <summary>Writes the texture list and local VRAM layout used by an effect model's material polygons.</summary>
    /// <param name="json">Destination JSON writer.</param>
    /// <param name="effectId">Stable effect identifier used in texture image names.</param>
    /// <param name="textures">Decoded texture pages and their VRAM source layout.</param>
    private static void WriteTextures(Utf8JsonWriter json, int effectId, BattleTextureSet textures)
    {
        string prefix = $"effect-{effectId}";
        json.WriteStartArray("textures");
        foreach (BattleTexture texture in textures.Textures)
        {
            BattleModelElementJsonWriter.WriteTexture(json, prefix, texture);
        }

        json.WriteEndArray();
        BattleModelElementJsonWriter.WriteTextureVram(json, textures.Vram);
    }
}
