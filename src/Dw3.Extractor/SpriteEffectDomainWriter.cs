using System.Text.Json;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;

namespace Dw3.Extractor;

/// <summary>Produces the isolated, versioned sprite domain from the complete overlay registry.</summary>
internal static class SpriteEffectDomainWriter
{
    /// <summary>Decodes every registered effect, keeping unsupported results distinct from assets.</summary>
    /// <param name="context">Atomic pack writer.</param>
    /// <param name="overlay">Supported edition battle overlay.</param>
    /// <param name="files">User-disc file resolver.</param>
    internal static void Write(DataPackWriteContext context, byte[] overlay, EngineFileReader files)
    {
        var source = new SpriteEffectSource(files);
        foreach (SpriteEffectReference reference in SpriteEffectRegistryDecoder.DecodeSpriteEffects(overlay))
        {
            WriteOne(context, overlay, source, reference);
        }
    }

    /// <summary>Persists a decoded asset or an explicit unsupported diagnostic at its source identity.</summary>
    /// <param name="context">Atomic pack writer.</param>
    /// <param name="overlay">Sheet registry bytes.</param>
    /// <param name="source">Cached archive resolver.</param>
    /// <param name="reference">Effect registry identity.</param>
    private static void WriteOne(DataPackWriteContext context, byte[] overlay, SpriteEffectSource source, SpriteEffectReference reference)
    {
        string status;
        string json;
        try
        {
            (json, bool available) = Decode(context, overlay, source, reference);
            status = available ? "available" : "unsupported";
        }
        catch (FormatException error)
        {
            status = error is SpriteEffectSourceAbsentException ? "source-absent" : "unsupported";
            json = JsonSerializer.Serialize(new { schema = "battle-sprites-v2", reference, reason = error.Message });
        }

        Execute(context, "INSERT INTO battle_sprites_v1 VALUES ($1,$2,$3)", reference.EffectId, status, json);
    }

    /// <summary>Resolves one full image and animation identity chain through the production frame writer.</summary>
    /// <param name="context">Atomic pack writer.</param>
    /// <param name="overlay">Sheet registry bytes.</param>
    /// <param name="source">Cached archive resolver.</param>
    /// <param name="reference">Effect identity and animation selector.</param>
    /// <returns>Versioned JSON metadata.</returns>
    private static (string Json, bool Available) Decode(DataPackWriteContext context, byte[] overlay, SpriteEffectSource source, SpriteEffectReference reference)
    {
        SpriteEffectSheetReference sheet = SpriteEffectRegistryDecoder.DecodeSheet(overlay, reference.SheetIndex);
        byte[] body = source.Read(sheet.SheetSelector);
        byte[] animation = source.Read(((uint)reference.AnimationFileId << 16) | (uint)reference.AnimationArchiveEntry);
        var animations = ModelArchive.ReadBounds(animation).Select(range => range.IsEmpty
            ? null : BattleSpriteAnimationDecoder.Decode(animation.AsSpan(range.Start, range.End - range.Start))).ToArray();
        IReadOnlyList<HudTexturePage> pages = source.Pages(sheet);
        var frameWriter = new SpriteEffectFrameWriter(body, pages, sheet.Placement);
        var frames = new List<object>();
        var unsupportedFrames = new List<object>();
        foreach (var key in animations.Where(a => a is not null).SelectMany(a => a!).Select(sample => (Frame: sample[0], Row: sample[1])).Distinct())
        {
            WriteFrame(context, reference.EffectId, frameWriter, key, frames, unsupportedFrames);
        }

        string json = JsonSerializer.Serialize(new
        {
            schema = "battle-sprites-v2",
            reference,
            sheet,
            sheetSha256 = DiscIdentity.Sha256Bytes(body),
            animationSha256 = DiscIdentity.Sha256Bytes(animation),
            textureSha256 = DiscIdentity.Sha256Bytes(source.TextureArchive(sheet)),
            paletteSelection = "frame-row-plus-animation-row",
            timingUnit = "source-animation-sample",
            animations,
            frames,
            unsupportedFrames,
            parts = frameWriter.Parts,
            sourceFrames = frameWriter.Definitions,
            textureWords = pages.Select(page => page.Rows),
            reason = unsupportedFrames.Count == 0 ? null : "Some frame CLUTs address source VRAM that is neither a loaded effect page nor qualified cleared VRAM. No palette fallback was applied.",
        });
        return (json, unsupportedFrames.Count == 0);
    }

    /// <summary>Preserves a frame failure alongside decoded payload, never replacing a palette.</summary>
    /// <param name="context">Atomic pack writer.</param>
    /// <param name="id">Script identity.</param>
    /// <param name="writer">Decoded sheet frame writer.</param>
    /// <param name="key">Frame and row identity.</param>
    /// <param name="frames">Successfully decoded frame metadata.</param>
    /// <param name="unsupported">Qualified failures retained in the contract.</param>
    private static void WriteFrame(DataPackWriteContext context, int id, SpriteEffectFrameWriter writer, (short Frame, short Row) key, List<object> frames, List<object> unsupported)
    {
        try
        {
            frames.Add(writer.Write(context, id, key));
        }
        catch (FormatException error)
        {
            unsupported.Add(new { frame = key.Frame, row = key.Row, reason = error.Message });
        }
    }

    /// <summary>Executes a parameterized statement inside the pack transaction.</summary>
    /// <param name="context">Atomic pack writer.</param>
    /// <param name="sql">Statement with numbered parameters.</param>
    /// <param name="values">Ordered parameter values.</param>
    private static void Execute(DataPackWriteContext context, string sql, params object[] values)
    {
        using var command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int i = 0; i < values.Length; i++)
        {
            command.Parameters.AddWithValue($"${i + 1}", values[i]);
        }

        command.ExecuteNonQuery();
    }
}
