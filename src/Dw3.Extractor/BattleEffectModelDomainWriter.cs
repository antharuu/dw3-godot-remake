using Dw3.Data;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary.Png;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>Writes shared three-dimensional effect-model manifests, textures and provenance into a battle data pack.</summary>
internal static class BattleEffectModelDomainWriter
{
    private const string DirectoryName = "battle/effects";

    /// <summary>Persists every decoded effect model in stable effect-id order.</summary>
    /// <param name="context">Current data-pack write transaction and image root.</param>
    /// <param name="models">Decoded shared effect models.</param>
    /// <param name="edition">Source-disc edition label.</param>
    internal static void Write(DataPackWriteContext context, IReadOnlyList<EffectModelExtraction> models, string edition)
    {
        Directory.CreateDirectory(context.GetImagePath(DirectoryName));
        foreach (EffectModelExtraction model in models.OrderBy(item => item.Reference.Id))
        {
            WriteOne(context, model, edition);
        }
    }

    /// <summary>Writes one effect manifest, its material textures and its SQLite index row.</summary>
    /// <param name="context">Current data-pack write transaction and image root.</param>
    /// <param name="model">Decoded effect resource and provenance.</param>
    /// <param name="edition">Source-disc edition label.</param>
    private static void WriteOne(DataPackWriteContext context, EffectModelExtraction model, string edition)
    {
        EffectModelReference reference = model.Reference;
        BattleModelPayload payload = model.Payload;
        string manifest = $"{DirectoryName}/effect-{reference.Id}-model.json";
        File.WriteAllText(context.GetImagePath(manifest), EffectModelJsonWriter.Write(reference, payload, edition));
        foreach (BattleTexture texture in payload.Textures.Textures)
        {
            string path = $"{DirectoryName}/effect-{reference.Id}-texture-{texture.Key}.png";
            File.WriteAllBytes(context.GetImagePath(path), PngEncoder.Encode(texture.Width, texture.Height, texture.Rgba));
            context.RegisterImage(path);
        }

        Insert(
            context,
            "INSERT INTO battle_effect_models (effect_id,model_file,model_entry,motion_file,motion_entry,texture_entry,bone_count,mesh_count,disc_path,sha256,manifest_path) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)",
            reference.Id,
            reference.ModelFile,
            reference.ModelEntry,
            reference.MotionFile,
            reference.MotionEntry,
            payload.Structure.TextureEntry,
            payload.Structure.BoneCount,
            payload.Structure.Meshes.Count,
            model.DiscPath,
            model.Sha256,
            manifest);
    }

    /// <summary>Executes a parameterized insert against the active data-pack transaction.</summary>
    /// <param name="context">Current data-pack write transaction.</param>
    /// <param name="sql">SQL text using positional parameters.</param>
    /// <param name="values">Values in positional order.</param>
    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
        {
            command.Parameters.AddWithValue($"${index + 1}", values[index]);
        }

        command.ExecuteNonQuery();
    }
}
