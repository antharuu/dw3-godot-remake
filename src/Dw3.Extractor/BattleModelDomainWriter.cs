using Dw3.Data;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary.Png;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>
/// One fighter-table entry after extraction: its outcome and, when decoded, the model structure
/// and the disc member it was read from.
/// </summary>
/// <param name="DiscPath">ISO path of the model file, null for a failed entry.</param>
/// <param name="Sha256">SHA-256 of the model file, null for a failed entry.</param>
public sealed record BattleModelExtraction(BattleModelOutcome Outcome, BattleModelStructure? Model, string? DiscPath, string? Sha256, BattleModelPayload? Payload = null);

/// <summary>
/// Writes the battle models domain into a data pack: one manifest per decoded fighter under
/// <c>battle/models</c>, the decode report, and the <c>battle_models</c> / <c>battle_model_bones</c> index tables.
/// </summary>
public static class BattleModelDomainWriter
{
    private const string ModelsDirectory = "battle/models";

    /// <summary>Writes every fighter in entry order.</summary>
    public static void Write(DataPackWriteContext context, IReadOnlyList<BattleModelExtraction> fighters, string edition)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(fighters);
        Directory.CreateDirectory(context.GetImagePath(ModelsDirectory));
        for (int ordinal = 0; ordinal < fighters.Count; ordinal++)
        {
            BattleModelExtraction fighter = fighters[ordinal];
            if (fighter.Outcome.Reference is { } reference && fighter.Model is { } model)
                WriteDecoded(context, ordinal, reference, model, fighter, edition);
            else
                WriteFailed(context, ordinal, fighter.Outcome);
        }
        File.WriteAllText(context.GetImagePath($"{ModelsDirectory}/decode-report.json"),
            BattleModelJsonWriter.WriteReport(fighters.Select(fighter => fighter.Outcome).ToArray(),
                fighters.Count(fighter => fighter.Payload?.Metadata.Face.Parts.Count > 0)));
    }

    private static void WriteDecoded(DataPackWriteContext context, int ordinal, FighterModelReference reference, BattleModelStructure model, BattleModelExtraction fighter, string edition)
    {
        string manifest = $"{ModelsDirectory}/fighter-{reference.FighterId}-model.json";
        File.WriteAllText(context.GetImagePath(manifest), fighter.Payload is { } payload
            ? BattleModelJsonWriter.WriteModel(reference, payload, edition)
            : BattleModelJsonWriter.WriteModel(reference, model, edition));
        Insert(context,
            "INSERT INTO battle_models (fighter_id,entry_ordinal,record_index,kind,status,reason,is_enemy,model_file,model_entry,motion_file,motion_entry,texture_entry,bone_count,mesh_count,disc_path,sha256,manifest_path) VALUES ($1,$2,$3,$4,'decoded',NULL,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15)",
            reference.FighterId, ordinal, reference.Index, reference.Kind, reference.IsEnemy ? 1 : 0, reference.ModelFile, reference.ModelEntry,
            reference.MotionFile, reference.MotionEntry, model.TextureEntry, model.BoneCount, model.Meshes.Count,
            (object?)fighter.DiscPath ?? DBNull.Value, (object?)fighter.Sha256 ?? DBNull.Value, manifest);
        foreach (BattleModelBone bone in model.Bones)
            Insert(context, "INSERT INTO battle_model_bones (fighter_id,bone,parent,mesh_entry,key_entry) VALUES ($1,$2,$3,$4,$5)",
                reference.FighterId, bone.Index, (object?)bone.Parent ?? DBNull.Value, (object?)bone.MeshEntry ?? DBNull.Value, (object?)bone.KeyEntry ?? DBNull.Value);
        if (fighter.Payload is { } full)
            WriteAssets(context, reference.FighterId, full);
    }

    private static void WriteAssets(DataPackWriteContext context, int fighterId, BattleModelPayload payload)
    {
        foreach (BattleTexture texture in payload.Textures.Textures)
        {
            string relative = $"{ModelsDirectory}/fighter-{fighterId}-texture-{texture.Key}.png";
            File.WriteAllBytes(context.GetImagePath(relative), PngEncoder.Encode(texture.Width, texture.Height, texture.Rgba));
            context.RegisterImage($"battle/models/fighter-{fighterId}-texture-{texture.Key}.png");
            Insert(context, "INSERT INTO battle_model_textures (fighter_id,texture_key,mode,page,clut_x,clut_y,transparent_pixels,stp_pixels,image_path) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)",
                fighterId, texture.Key, texture.Mode, texture.Page, (object?)texture.ClutX ?? DBNull.Value, (object?)texture.ClutY ?? DBNull.Value, texture.TransparentPixels, texture.StpPixels, relative);
        }
        foreach ((int bone, BattleBoneTracks tracks) in payload.Tracks)
            for (int track = 0; track < tracks.All.Count; track++)
                for (int ordinal = 0; ordinal < tracks.All[track].Count; ordinal++)
                {
                    BattleTrackKey key = tracks.All[track][ordinal];
                    Insert(context, "INSERT INTO battle_model_track_keys (fighter_id,bone,track,ordinal,frame,x,y,z) VALUES ($1,$2,$3,$4,$5,$6,$7,$8)", fighterId, bone, track, ordinal, key.Frame, key.X, key.Y, key.Z);
                }
        for (int motion = 0; motion < payload.Motions.Count; motion++)
            for (int ordinal = 0; ordinal < payload.Motions[motion].Count; ordinal++)
            {
                BattleMotionStep step = payload.Motions[motion][ordinal];
                Insert(context, "INSERT INTO battle_model_motion_steps (fighter_id,motion,ordinal,step_index,count,frame,end_frame) VALUES ($1,$2,$3,$4,$5,$6,$7)", fighterId, motion, ordinal, step.Index, step.Count, step.Frame, step.EndFrame);
            }
    }

    private static void WriteFailed(DataPackWriteContext context, int ordinal, BattleModelOutcome outcome) =>
        Insert(context, "INSERT INTO battle_models (fighter_id,entry_ordinal,record_index,kind,status,reason) VALUES ($1,$2,$3,$4,'failed',$5)",
            outcome.Entry.FighterId, ordinal, outcome.Entry.Index, outcome.Entry.Kind, outcome.Reason!);

    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int i = 0; i < values.Length; i++)
            command.Parameters.AddWithValue($"${i + 1}", values[i]);
        command.ExecuteNonQuery();
    }
}
