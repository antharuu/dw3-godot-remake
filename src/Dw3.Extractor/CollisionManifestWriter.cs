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

    /// <summary>Serializes collision actors to the given output writer as the EXP-800 JSON manifest.</summary>
    /// <param name="output">The text writer to receive the JSON output.</param>
    /// <param name="actors">The collision actor map keyed by stage then by actor name.</param>
    public static void WriteManifest(TextWriter output, IReadOnlyDictionary<string, Dictionary<string, FieldActor>> actors)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(actors);

        using var json = new Utf8JsonWriter(new TextWriterStream(output), new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        // Write actors before schema to match baseline alphabetical order.
        json.WriteStartObject("actors");
        foreach (var (stage, stageActors) in actors.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            json.WriteStartObject(stage);
            foreach (var (actorName, actor) in stageActors.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                json.WriteStartObject(actorName);
                json.WriteNumber("width", actor.Width);
                json.WriteNumber("x", actor.X);
                json.WriteNumber("y", actor.Y);
                json.WriteEndObject();
            }

            json.WriteEndObject();
        }

        json.WriteEndObject();
        json.WriteString("schema", SchemaId);
        json.WriteEndObject();
        json.Flush();
    }

    /// <summary>
    /// Bridges a <see cref="TextWriter"/> to the <see cref="Stream"/> interface required by
    /// <see cref="Utf8JsonWriter"/>. Named TextWriterStream (not StreamWriterAdapter) to
    /// avoid S3218 method-name shadowing of the outer WriteManifest method.
    /// </summary>
    private sealed class TextWriterStream(TextWriter writer) : Stream
    {
        /// <summary>Gets a value indicating whether this stream supports reading. Always false.</summary>
        public override bool CanRead => false;

        /// <summary>Gets a value indicating whether this stream supports seeking. Always false.</summary>
        public override bool CanSeek => false;

        /// <summary>Gets a value indicating whether this stream supports writing. Always true.</summary>
        public override bool CanWrite => true;

        /// <summary>Gets the stream length. Not supported.</summary>
        public override long Length => throw new NotSupportedException();

        /// <summary>Gets or sets the position. Not supported.</summary>
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        /// <summary>Flushes the underlying text writer.</summary>
        public override void Flush() => writer.Flush();

        /// <summary>Flushes the underlying text writer asynchronously.</summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the flush operation.</returns>
        public override Task FlushAsync(CancellationToken cancellationToken) => writer.FlushAsync(cancellationToken);

        /// <summary>Not supported on this write-only stream.</summary>
        /// <param name="value">The new length; ignored, always throws.</param>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>Seeking is not supported on this write-only stream.</summary>
        /// <param name="offset">The byte offset; ignored, always throws.</param>
        /// <param name="origin">The reference point; ignored, always throws.</param>
        /// <returns>Never returns.</returns>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>Reading is not supported on this write-only stream.</summary>
        /// <param name="buffer">The destination buffer; ignored, always throws.</param>
        /// <param name="offset">The byte offset into <paramref name="buffer"/>; ignored, always throws.</param>
        /// <param name="count">The number of bytes to read; ignored, always throws.</param>
        /// <returns>Never returns.</returns>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        /// <summary>Decodes <paramref name="buffer"/> as UTF-8 and forwards the text to the underlying writer.</summary>
        /// <param name="buffer">The bytes to decode and write.</param>
        /// <param name="offset">The byte offset in <paramref name="buffer"/> at which to begin.</param>
        /// <param name="count">The number of bytes to write.</param>
        public override void Write(byte[] buffer, int offset, int count) =>
            writer.Write(System.Text.Encoding.UTF8.GetString(buffer, offset, count));

        /// <summary>Decodes the span <paramref name="buffer"/> as UTF-8 and forwards the text to the underlying writer.</summary>
        /// <param name="buffer">The read-only byte span to decode and write.</param>
        public override void Write(ReadOnlySpan<byte> buffer) =>
            writer.Write(System.Text.Encoding.UTF8.GetString(buffer));
    }
}
