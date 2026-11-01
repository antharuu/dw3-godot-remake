using Dw3.Data;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Dialogue;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>The decoded field talk box: its frame sprites, panel texture and four layouts.</summary>
public sealed record TalkBoxDomain(
    IReadOnlyList<TalkBoxFrameImage> Frames,
    TalkBoxPanelImage Panel,
    IReadOnlyList<TalkBoxLayoutRecord> Layouts);

/// <summary>Persists the talk box as PNG images under dialogue/frame and three SQLite tables.</summary>
public static class TalkBoxDomainWriter
{
    private const string Directory = "dialogue/frame";

    /// <exception cref="ArgumentException">The domain has no frame or not exactly four layouts.</exception>
    public static void Write(DataPackWriteContext context, TalkBoxDomain domain)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domain);
        if (domain.Frames.Count == 0)
            throw new ArgumentException("The talk box needs at least one frame sprite.", nameof(domain));
        if (domain.Layouts.Count != TalkBoxFrameDecoder.LayoutCount)
            throw new ArgumentException($"The talk box needs {TalkBoxFrameDecoder.LayoutCount} layouts, got {domain.Layouts.Count}.", nameof(domain));

        foreach (TalkBoxFrameImage frame in domain.Frames.OrderBy(frame => frame.FrameId))
        {
            string relative = WriteImage(context, $"frame-{frame.FrameId}.png", frame.Width, frame.Height, frame.Rgba);
            Insert(context, "INSERT INTO talk_box_frames (frame_id,width,height,clut_frames,semi,image_path) VALUES ($1,$2,$3,$4,$5,$6)",
                frame.FrameId, frame.Width, frame.Height, frame.ClutFrames, frame.Semi ? 1 : 0, relative);
        }

        TalkBoxPanelImage panel = domain.Panel;
        string panelPath = WriteImage(context, "panel.png", panel.Width, panel.Height, panel.Rgba);
        Insert(context, "INSERT INTO talk_box_panel (red,green,blue,semi,texture_page,clut,width,height,image_path) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9)",
            panel.Red, panel.Green, panel.Blue, 1, panel.TexturePage, panel.Clut, panel.Width, panel.Height, panelPath);

        for (int index = 0; index < domain.Layouts.Count; index++)
        {
            TalkBoxLayoutRecord layout = domain.Layouts[index];
            Insert(context, """
                INSERT INTO talk_box_layouts (layout,sprite,sprite_x,sprite_y,sprite0_x,sprite0_y,panel_x,panel_y,sprite2_x,sprite2_y,name_x,name_y,text_x,text_y,arrow_x,arrow_y)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16)
                """,
                index, layout.Sprite, layout.SpriteX, layout.SpriteY, layout.Sprite0X, layout.Sprite0Y, layout.PanelX, layout.PanelY,
                layout.Sprite2X, layout.Sprite2Y, layout.NameX, layout.NameY, layout.TextX, layout.TextY, layout.ArrowX, layout.ArrowY);
        }
    }

    private static string WriteImage(DataPackWriteContext context, string name, int width, int height, byte[] rgba)
    {
        string relative = $"{Directory}/{name}";
        string path = context.GetImagePath(relative);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PngEncoder.Encode(width, height, rgba));
        context.RegisterImage(relative);
        return relative;
    }

    private static void Insert(DataPackWriteContext context, string sql, params object[] values)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = sql;
        for (int index = 0; index < values.Length; index++)
            command.Parameters.AddWithValue($"${index + 1}", values[index]);
        command.ExecuteNonQuery();
    }
}
