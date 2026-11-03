using Dw3.Data;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary.Png;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>
/// Writes the battle models domain into a data pack: one manifest per decoded fighter under
/// <c>battle/models</c>, the decode report, and the <c>battle_models</c> / <c>battle_model_bones</c> index tables.
/// </summary>
public static class BattleModelDomainWriter
{
    private const string ModelsDirectory = "battle/models";

    /// <summary>Writes every fighter in entry order.</summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="fighters">The decoded fighter list; failed decodings are recorded as failed rows.</param>
    /// <param name="edition">The disc edition label written into the model JSON (e.g. <c>"SLES-03936"</c>).</param>
    public static void Write(DataPackWriteContext context, IReadOnlyList<BattleModelExtraction> fighters, string edition)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(fighters);
        Directory.CreateDirectory(context.GetImagePath(ModelsDirectory));
        for (int ordinal = 0; ordinal < fighters.Count; ordinal++)
        {
            BattleModelExtraction fighter = fighters[ordinal];
            if (fighter.Outcome.Reference is { } reference && fighter.Model is { } model)
            {
                WriteDecoded(context, ordinal, reference, model, fighter, edition);
            }
            else
            {
                WriteFailed(context, ordinal, fighter.Outcome);
            }
        }

        File.WriteAllText(
            context.GetImagePath($"{ModelsDirectory}/decode-report.json"),
            BattleModelJsonWriter.WriteReport(
                [.. fighters.Select(fighter => fighter.Outcome)],
                fighters.Count(fighter => fighter.Payload?.Metadata.Face.Parts.Count > 0)));
    }

    /// <summary>
    /// Writes the model manifest JSON, inserts a decoded row into <c>battle_models</c>, inserts bone rows,
    /// and writes texture and track assets when a full payload is available.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="ordinal">The zero-based position of this fighter in the fighter table.</param>
    /// <param name="reference">The resolved fighter model reference with file and archive entry numbers.</param>
    /// <param name="model">The decoded model structure with bones and mesh counts.</param>
    /// <param name="fighter">The full extraction result providing the disc path and optional payload.</param>
    /// <param name="edition">The disc edition label written into the model JSON.</param>
    private static void WriteDecoded(DataPackWriteContext context, int ordinal, FighterModelReference reference, BattleModelStructure model, BattleModelExtraction fighter, string edition)
    {
        string manifest = $"{ModelsDirectory}/fighter-{reference.FighterId}-model.json";
        File.WriteAllText(context.GetImagePath(manifest), fighter.Payload is { } payload
            ? BattleModelJsonWriter.WriteModel(reference, payload, edition)
            : BattleModelJsonWriter.WriteModel(reference, model, edition));
        Insert(
            context,
            "INSERT INTO battle_models (fighter_id,entry_ordinal,record_index,kind,status,reason,is_enemy,model_file,model_entry,motion_file,motion_entry,texture_entry,bone_count,mesh_count,disc_path,sha256,manifest_path) VALUES ($1,$2,$3,$4,'decoded',NULL,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15)",
            reference.FighterId,
            ordinal,
            reference.Index,
            reference.Kind,
            reference.IsEnemy ? 1 : 0,
            reference.ModelFile,
            reference.ModelEntry,
            reference.MotionFile,
            reference.MotionEntry,
            model.TextureEntry,
            model.BoneCount,
            model.Meshes.Count,
            (object?)fighter.DiscPath ?? DBNull.Value,
            (object?)fighter.Sha256 ?? DBNull.Value,
            manifest);
        foreach (BattleModelBone bone in model.Bones)
        {
            Insert(
                context,
                "INSERT INTO battle_model_bones (fighter_id,bone,parent,mesh_entry,key_entry) VALUES ($1,$2,$3,$4,$5)",
                reference.FighterId,
                bone.Index,
                (object?)bone.Parent ?? DBNull.Value,
                (object?)bone.MeshEntry ?? DBNull.Value,
                (object?)bone.KeyEntry ?? DBNull.Value);
        }

        if (fighter.Payload is { } full)
        {
            WriteAssets(context, reference.FighterId, full);
        }
    }

    /// <summary>
    /// Writes texture PNGs and inserts rows into <c>battle_model_textures</c>,
    /// <c>battle_model_track_keys</c>, and <c>battle_model_motion_steps</c> for a fully decoded payload.
    /// </summary>
    /// <param name="context">The data pack write context providing the database, transaction, and image paths.</param>
    /// <param name="fighterId">The fighter id used as the filename prefix and database key.</param>
    /// <param name="payload">The decoded model payload with textures, bone tracks, and motion steps.</param>
    private static void WriteAssets(DataPackWriteContext context, int fighterId, BattleModelPayload payload)
    {
        foreach (BattleTexture texture in payload.Textures.Textures)
        {
            string relative = $"{ModelsDirectory}/fighter-{fighterId}-texture-{texture.Key}.png";
            File.WriteAllBytes(context.GetImagePath(relative), PngEncoder.Encode(texture.Width, texture.Height, texture.Rgba));
            context.RegisterImage($"battle/models/fighter-{fighterId}-texture-{texture.Key}.png");
            Insert(
                context,
                "INSERT INTO battle_model_textures (fighter_id,texture_key,mode,page,clut_x,clut_y,transparent_pixels,stp_pixels,image_path) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)",
                fighterId,
                texture.Key,
                texture.Mode,
                texture.Page,
                (object?)texture.ClutX ?? DBNull.Value,
                (object?)texture.ClutY ?? DBNull.Value,
                texture.TransparentPixels,
                texture.StpPixels,
                relative);
        }

        foreach ((int bone, BattleBoneTracks tracks) in payload.Tracks)
            for (int track = 0; track < tracks.All.Count; track++)
            {
                for (int ordinal = 0; ordinal < tracks.All[track].Count; ordinal++)
                {
                    BattleTrackKey key = tracks.All[track][ordinal];
                    Insert(context, "INSERT INTO battle_model_track_keys (fighter_id,bone,track,ordinal,frame,x,y,z) VALUES ($1,$2,$3,$4,$5,$6,$7,$8)", fighterId, bone, track, ordinal, key.Frame, key.X, key.Y, key.Z);
                }
            }

        for (int motion = 0; motion < payload.Motions.Count; motion++)
        {
            for (int ordinal = 0; ordinal < payload.Motions[motion].Count; ordinal++)
            {
                BattleMotionStep step = payload.Motions[motion][ordinal];
                Insert(context, "INSERT INTO battle_model_motion_steps (fighter_id,motion,ordinal,step_index,count,frame,end_frame) VALUES ($1,$2,$3,$4,$5,$6,$7)", fighterId, motion, ordinal, step.Index, step.Count, step.Frame, step.EndFrame);
            }
        }
    }

    /// <summary>
    /// Inserts a failed row into <c>battle_models</c> for a fighter whose model could not be decoded.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="ordinal">The zero-based position of this fighter in the fighter table.</param>
    /// <param name="outcome">The failed outcome carrying the fighter entry and failure reason.</param>
    private static void WriteFailed(DataPackWriteContext context, int ordinal, BattleModelOutcome outcome) =>
        Insert(
            context,
            "INSERT INTO battle_models (fighter_id,entry_ordinal,record_index,kind,status,reason) VALUES ($1,$2,$3,$4,'failed',$5)",
            outcome.Entry.FighterId,
            ordinal,
            outcome.Entry.Index,
            outcome.Entry.Kind,
            outcome.Reason!);

    /// <summary>
    /// Executes a parameterised INSERT command against the current pack transaction.
    /// </summary>
    /// <param name="context">The data pack write context providing the database and transaction.</param>
    /// <param name="sql">The parameterised SQL text using positional parameters ($1, $2, …).</param>
    /// <param name="values">The parameter values in positional order.</param>
    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int i = 0; i < values.Length; i++)
        {
            command.Parameters.AddWithValue($"${i + 1}", values[i]);
        }

        command.ExecuteNonQuery();
    }
}
