using Dw3.Data;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Dialogue;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

/// <summary>Persists the talk box as PNG images under dialogue/frame and three SQLite tables.</summary>
public static class TalkBoxDomainWriter
{
    private const string Directory = "dialogue/frame";

    /// <summary>
    /// Writes all talk box frames as PNG images under <c>dialogue/frame/</c>, writes the panel image,
    /// and inserts rows into <c>talk_box_frames</c>, <c>talk_box_panel</c>, and <c>talk_box_layouts</c>.
    /// </summary>
    /// <param name="context">Write context providing the database, transaction, and image-path resolver.</param>
    /// <param name="domain">The decoded talk box domain; must have at least one frame and exactly <see cref="TalkBoxFrameDecoder.LayoutCount"/> layouts.</param>
    /// <exception cref="ArgumentException">The domain has no frame or not exactly four layouts.</exception>
    public static void Write(DataPackWriteContext context, TalkBoxDomain domain)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domain);
        if (domain.Frames.Count == 0)
        {
            throw new ArgumentException("The talk box needs at least one frame sprite.", nameof(domain));
        }

        if (domain.Layouts.Count != TalkBoxFrameDecoder.LayoutCount)
        {
            throw new ArgumentException($"The talk box needs {TalkBoxFrameDecoder.LayoutCount} layouts, got {domain.Layouts.Count}.", nameof(domain));
        }

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

    /// <summary>
    /// Encodes <paramref name="rgba"/> as a PNG, writes it to <c>dialogue/frame/<paramref name="name"/></c>
    /// in the data pack image tree, registers the path, and returns its relative path.
    /// </summary>
    /// <param name="context">Write context providing the image-path resolver and image registry.</param>
    /// <param name="name">Filename within <c>dialogue/frame/</c> (e.g. <c>frame-0.png</c>).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="rgba">Raw 32-bit RGBA pixel data, row-major, <c>width × height × 4</c> bytes.</param>
    /// <returns>The relative path stored in the pack (e.g. <c>dialogue/frame/frame-0.png</c>).</returns>
    private static string WriteImage(DataPackWriteContext context, string name, int width, int height, byte[] rgba)
    {
        string relative = $"{Directory}/{name}";
        string path = context.GetImagePath(relative);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PngEncoder.Encode(width, height, rgba));
        context.RegisterImage(relative);
        return relative;
    }

    /// <summary>
    /// Executes <paramref name="sql"/> against the destination database within the current transaction,
    /// binding <paramref name="values"/> to positional parameters <c>$1</c>, <c>$2</c>, and so on.
    /// </summary>
    /// <param name="context">Write context providing the database connection and active transaction.</param>
    /// <param name="sql">The SQL command text with positional parameters <c>$1</c>, <c>$2</c>, and so on.</param>
    /// <param name="values">Values bound to parameters in order; must match the number of <c>$N</c> placeholders.</param>
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
