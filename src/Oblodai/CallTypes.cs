namespace Oblodai;

/// <summary>A binary response (PDF/CSV documents).</summary>
/// <param name="Bytes">The document.</param>
/// <param name="ContentType">MIME type the gateway sent.</param>
/// <param name="Filename">
/// Name from <c>Content-Disposition</c>, when the gateway supplied one: a bare file name only (no
/// directories, no control characters, never <c>.</c> or <c>..</c>) — see <see cref="SafeFilename"/>.
/// </param>
public sealed record FileResult(byte[] Bytes, string ContentType, string? Filename)
{
    /// <summary>
    /// Save the document to a NEW file at <paramref name="path"/>, readable by the owner only (0600 on
    /// Unix). An existing file is never overwritten: that is an <see cref="IOException"/>.
    /// </summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task WriteToAsync(string path, CancellationToken cancellationToken = default)
        => WriteToAsync(path, overwrite: false, cancellationToken);

    /// <summary>Save the document, created owner-only (0600 on Unix).</summary>
    /// <param name="path">Where to write it.</param>
    /// <param name="overwrite">Whether an existing file may be replaced.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task WriteToAsync(string path, bool overwrite, CancellationToken cancellationToken = default)
    {
        if (overwrite)
        {
            File.Delete(path);
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(Bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The last path component of a server-supplied name, with control characters dropped; null for an
    /// empty name, <c>.</c> or <c>..</c>. A hostile <c>Content-Disposition</c> cannot steer a save outside
    /// the caller's directory.
    /// </summary>
    /// <param name="name">The name as the server sent it.</param>
    public static string? SafeFilename(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var cut = Math.Max(name.LastIndexOf('/'), name.LastIndexOf('\\'));
        var clean = new string(name[(cut + 1)..].Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean is "" or "." or ".." ? null : clean;
    }

    /// <inheritdoc />
    public override string ToString() => $"FileResult {{ ContentType = {ContentType}, Filename = {Filename}, Bytes = {Bytes.Length} }}";
}
