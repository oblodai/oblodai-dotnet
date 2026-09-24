namespace Oblodai;

/// <summary>A binary response (PDF/CSV documents).</summary>
/// <param name="Bytes">The document.</param>
/// <param name="ContentType">MIME type the gateway sent.</param>
/// <param name="Filename">Name from <c>Content-Disposition</c>, when the gateway supplied one.</param>
public sealed record FileResult(byte[] Bytes, string ContentType, string? Filename)
{
    /// <summary>Save the document to <paramref name="path"/>.</summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task WriteToAsync(string path, CancellationToken cancellationToken = default)
        => File.WriteAllBytesAsync(path, Bytes, cancellationToken);

    /// <inheritdoc />
    public override string ToString() => $"FileResult {{ ContentType = {ContentType}, Filename = {Filename}, Bytes = {Bytes.Length} }}";
}
