namespace Dw3.Extractor;

/// <summary>
/// Write-only <see cref="Stream"/> adapter that forwards UTF-8 bytes to a <see cref="TextWriter"/>.
/// Used to bridge <see cref="System.Text.Json.Utf8JsonWriter"/> (which writes to a <see cref="Stream"/>)
/// with the <see cref="TextWriter"/> passed to <see cref="ZoneManifestWriter.Write(TextWriter, ZoneManifestData)"/>.
/// </summary>
internal sealed class StreamWriterAdapter(TextWriter writer) : Stream
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
