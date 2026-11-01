using System.Text.Json;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Serializes collision actor records to the canonical JSON manifest format.
/// The output has the same structure as the Python EXP-800 collision manifest:
/// { "schema": "EXP-800", "actors": { "S200": { "actor1": { "x": ..., "y": ..., "width": ... } }, ... } }
/// Properties are written in alphabetical order to match the baseline sort.
/// </summary>
public static class CollisionManifestWriter
{
    private const string SchemaId = "EXP-800";

    /// <summary>Writes the collision manifest to the given output writer.</summary>
    public static void Write(TextWriter output, IReadOnlyDictionary<string, Dictionary<string, FieldActor>> actors)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(actors);

        using var writer = new Utf8JsonWriter(new CollisionStreamAdapter(output), new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        // Write actors before schema to match baseline alphabetical order.
        writer.WriteStartObject("actors");
        foreach (var (stage, stageActors) in actors.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            writer.WriteStartObject(stage);
            foreach (var (actorName, actor) in stageActors.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(actorName);
                writer.WriteNumber("width", actor.Width);
                writer.WriteNumber("x", actor.X);
                writer.WriteNumber("y", actor.Y);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteString("schema", SchemaId);
        writer.WriteEndObject();
        writer.Flush();
    }
}

/// <summary>
/// Write-only stream adapter that UTF-8 decodes bytes into the wrapped <see cref="TextWriter"/>.
/// Kept file-scoped (not nested in CollisionManifestWriter) to avoid S3218 method-name shadowing.
/// </summary>
file sealed class CollisionStreamAdapter(TextWriter writer) : Stream
{
    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <inheritdoc/>
    public override void Flush() => writer.Flush();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => writer.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => writer.Write(System.Text.Encoding.UTF8.GetString(buffer, offset, count));

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer) => writer.Write(System.Text.Encoding.UTF8.GetString(buffer));

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
