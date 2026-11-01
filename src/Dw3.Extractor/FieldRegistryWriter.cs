using Dw3.Data;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>Writes the disc-derived field-stage registry and exit slots to the data pack.</summary>
public static class FieldRegistryWriter
{
    /// <summary>Persists every classified field stage before resolving its exit destinations.</summary>
    public static void Write(DataPackWriteContext context, IReadOnlyList<ExtractedFieldStage> stages)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(stages);

        var byMode = new Dictionary<ushort, int>();
        foreach (ExtractedFieldStage stage in stages)
        {
            if (!byMode.TryAdd(stage.Mode, stage.StageId))
                throw new InvalidDataException($"Field mode 0x{stage.Mode:X4} is assigned to more than one stage.");
            InsertStage(context, stage);
        }

        foreach (ExtractedFieldStage stage in stages)
        {
            for (int slotIndex = 0; slotIndex < stage.Slots.Count; slotIndex++)
            {
                StageSlot slot = stage.Slots[slotIndex];
                if (slot.Type != 1)
                    continue;
                InsertWarp(context, stage.StageId, slotIndex, slot,
                    byMode.TryGetValue(slot.Argument, out int destination) ? destination : null);
            }
        }
    }

    private static void InsertStage(DataPackWriteContext context, ExtractedFieldStage stage)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            INSERT INTO field_stages (stage_id, mode, overlay_file_id, status, error)
            VALUES ($stage, $mode, $overlay, $status, $error)
            """;
        command.Parameters.AddWithValue("$stage", stage.StageId);
        command.Parameters.AddWithValue("$mode", stage.Mode);
        command.Parameters.AddWithValue("$overlay", stage.OverlayFileId);
        command.Parameters.AddWithValue("$status", stage.Status);
        command.Parameters.AddWithValue("$error", (object?)stage.Error ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void InsertWarp(
        DataPackWriteContext context,
        int sourceStage,
        int slotIndex,
        StageSlot slot,
        int? destinationStage)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = """
            INSERT INTO field_warps
                (source_stage_id, slot_index, destination_mode, destination_stage_id,
                 x, y, facing, condition, condition_status)
            VALUES
                ($source, $slot, $mode, $destination, $x, $y, $facing, $condition, $conditionStatus)
            """;
        command.Parameters.AddWithValue("$source", sourceStage);
        command.Parameters.AddWithValue("$slot", slotIndex);
        command.Parameters.AddWithValue("$mode", slot.Argument);
        command.Parameters.AddWithValue("$destination", destinationStage is int value ? value : DBNull.Value);
        command.Parameters.AddWithValue("$x", slot.X);
        command.Parameters.AddWithValue("$y", slot.Y);
        command.Parameters.AddWithValue("$facing", slot.Direction);
        command.Parameters.AddWithValue("$condition", slot.Condition);
        command.Parameters.AddWithValue("$conditionStatus", slot.Condition == ushort.MaxValue ? "unconditional" : "unevaluated");
        command.ExecuteNonQuery();
    }
}
