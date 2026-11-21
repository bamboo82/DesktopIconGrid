using System.Text.Json;
using DesktopIconGrid.Core.Diagnostics;
using DesktopIconGrid.Core.Models;

namespace DesktopIconGrid.Core.Sync;

/// <summary>A background picture kept beside the profile copies in the sync place (0.15.0 WP5b).</summary>
/// <param name="Id">Where the place finds it: its file name in a folder, its file id on Drive.</param>
/// <param name="Name">Its remote name, <c>background-&lt;32 hex&gt;.jpg|png</c>.</param>
/// <param name="Size">Its length in bytes, when the listing says.</param>
public sealed record SyncPicture(string Id, string Name, long? Size);

/// <summary>
/// The background pictures in the sync place, beside the profile copies (0.15.0 WP5b): a chosen folder or the app's folder
/// on Google Drive. Only files named <c>background-&lt;stored name&gt;</c> are listed.
/// </summary>
public interface ISyncPictures
{
    Task<IReadOnlyList<SyncPicture>> ListPicturesAsync(CancellationToken cancellationToken);

    Task UploadPictureAsync(string remoteName, byte[] content, CancellationToken cancellationToken);

    /// <summary>The picture's bytes, of which at most <paramref name="maxBytes"/> are read: <see langword="null"/> when it is longer.</summary>
    Task<byte[]?> DownloadPictureAsync(SyncPicture picture, long maxBytes, CancellationToken cancellationToken);

    Task DeletePictureAsync(SyncPicture picture, CancellationToken cancellationToken);
}

/// <summary>How fetching a picture from the sync place ended (0.15.0 WP5b).</summary>
public enum PictureFetch
{
    /// <summary>Kept in <c>backgrounds</c> (or already there).</summary>
    Fetched,

    /// <summary>The sync place does not have it (yet).</summary>
    NotThere,

    /// <summary>Over <see cref="PictureSync.MaxBytes"/>, by the listing or while reading.</summary>
    TooLarge,

    /// <summary>Its bytes do not give the name the layout asks for: not the file a PC of this bar stored.</summary>
    WrongHash,

    /// <summary>Windows does not decode it as the JPEG or PNG its name says, or it has more pixels than a stored picture may.</summary>
    NotAPicture,
}

/// <summary>
/// The background picture file through sync (0.15.0 WP5b, plan 36 addition 6: the owner's "Yes, sync it"). The stored
/// file travels as it is, beside the profile copies, as <c>background-&lt;stored name&gt;</c>, once per picture; a received
/// one is kept only after every check, since others may write to the sync place:
/// <list type="number">
/// <item>at most <see cref="MaxBytes"/>, by the listing and while reading;</item>
/// <item>the SHA-256 of its bytes gives exactly the name the layout asks for (<see cref="BackgroundImage.NameFor"/>), so it
/// is the file a PC of this bar stored;</item>
/// <item>Windows decodes it as the JPEG or PNG its extension says, and nothing else, within the stored size (the caller's
/// check, which needs WPF);</item>
/// <item>written under a temporary name and moved into <c>backgrounds</c> only after all three.</item>
/// </list>
/// </summary>
public static class PictureSync
{
    /// <summary>The largest picture taken from the sync place.</summary>
    public const long MaxBytes = 20L * 1024 * 1024;

    /// <summary>What a picture's remote name starts with.</summary>
    public const string Prefix = "background-";

    /// <summary>The ending of a picture being written into <c>backgrounds</c>; never a stored name, so never pruned or drawn.</summary>
    public const string PartialEnding = ".partial";

    /// <summary>The remote name of the stored picture <paramref name="storedName"/>.</summary>
    public static string RemoteName(string storedName) => Prefix + storedName;

    /// <summary>The stored name a remote name stands for, or <see langword="null"/> when it is no picture's.</summary>
    public static string? StoredNameOf(string? remoteName) =>
        remoteName is not null && remoteName.StartsWith(Prefix, StringComparison.Ordinal) && BackgroundImage.IsStoredName(remoteName[Prefix.Length..])
            ? remoteName[Prefix.Length..]
            : null;

    /// <summary>
    /// Up: puts the stored picture <paramref name="storedName"/> from <paramref name="folder"/> into the sync place unless it
    /// is there already (the same picture is never uploaded twice). False when nothing was uploaded: it is there, or this PC
    /// does not have it.
    /// </summary>
    public static async Task<bool> UploadIfMissingAsync(ISyncPictures place, string folder, string storedName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        if (!BackgroundImage.IsStoredName(storedName))
        {
            return false;
        }

        var path = Path.Combine(folder, storedName);
        if (!File.Exists(path))
        {
            return false;
        }

        if (new FileInfo(path).Length > MaxBytes)
        {
            // Every other PC would refuse it (a large picture with transparency is stored as PNG): it stays on this one.
            AppLog.Default.Info("Sync", $"Background picture {storedName} is larger than {MaxBytes / (1024 * 1024)} MB: not put beside the copies");
            return false;
        }

        var remote = RemoteName(storedName);
        var listed = await place.ListPicturesAsync(cancellationToken).ConfigureAwait(false);
        if (listed.Any(picture => string.Equals(picture.Name, remote, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        await place.UploadPictureAsync(remote, bytes, cancellationToken).ConfigureAwait(false);
        AppLog.Default.Info("Sync", $"Background picture {storedName} uploaded beside the copies ({bytes.Length:N0} bytes)");
        return true;
    }

    /// <summary>
    /// Down: fetches <paramref name="storedName"/> into <paramref name="folder"/> through the checks in the class summary;
    /// <paramref name="decodes"/> is the caller's (bytes, is PNG) check that Windows decodes them as that type within the
    /// stored size. Nothing is kept unless every check passes.
    /// </summary>
    public static async Task<PictureFetch> FetchAsync(
        ISyncPictures place, string folder, string storedName, Func<byte[], bool, bool> decodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(decodes);
        if (!BackgroundImage.IsStoredName(storedName))
        {
            return PictureFetch.NotThere;
        }

        var target = Path.Combine(folder, storedName);
        if (File.Exists(target))
        {
            return PictureFetch.Fetched;
        }

        var remote = RemoteName(storedName);
        var listed = await place.ListPicturesAsync(cancellationToken).ConfigureAwait(false);
        var picture = listed.FirstOrDefault(entry => string.Equals(entry.Name, remote, StringComparison.OrdinalIgnoreCase));
        if (picture is null)
        {
            return PictureFetch.NotThere;
        }

        if (picture.Size is > MaxBytes)
        {
            return PictureFetch.TooLarge;
        }

        var bytes = await place.DownloadPictureAsync(picture, MaxBytes, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length > MaxBytes)
        {
            return PictureFetch.TooLarge;
        }

        var png = storedName.EndsWith(".png", StringComparison.Ordinal);
        if (!string.Equals(BackgroundImage.NameFor(bytes, png), storedName, StringComparison.Ordinal))
        {
            return PictureFetch.WrongHash;
        }

        if (!decodes(bytes, png))
        {
            return PictureFetch.NotAPicture;
        }

        Directory.CreateDirectory(folder);
        var partial = target + PartialEnding;
        await File.WriteAllBytesAsync(partial, bytes, cancellationToken).ConfigureAwait(false);
        try
        {
            File.Move(partial, target, overwrite: false);
        }
        catch (IOException) when (File.Exists(target))
        {
            File.Delete(partial); // the same picture arrived another way meanwhile: its name is its content
        }

        AppLog.Default.Info("Sync", $"Background picture {storedName} received from the sync place ({bytes.Length:N0} bytes)");
        return PictureFetch.Fetched;
    }

    /// <summary>
    /// Tidy: deletes the remote picture <paramref name="storedName"/> unless a profile copy still names it.
    /// <paramref name="namedInCopies"/> are the pictures the copies this PC can see name, <see langword="null"/> when it is
    /// not sure (a copy could not be read): the picture then stays. True when it was deleted.
    /// </summary>
    public static async Task<bool> DeleteIfUnnamedAsync(ISyncPictures place, string storedName, IReadOnlyCollection<string>? namedInCopies, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        if (namedInCopies is null || namedInCopies.Contains(storedName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var remote = RemoteName(storedName);
        var listed = await place.ListPicturesAsync(cancellationToken).ConfigureAwait(false);
        var picture = listed.FirstOrDefault(entry => string.Equals(entry.Name, remote, StringComparison.OrdinalIgnoreCase));
        if (picture is null)
        {
            return false;
        }

        await place.DeletePictureAsync(picture, cancellationToken).ConfigureAwait(false);
        AppLog.Default.Info("Sync", $"Background picture {storedName} deleted from the sync place: no copy names it any more");
        return true;
    }

    /// <summary>
    /// The background picture a profile copy's text names, <see langword="null"/> for none; <see cref="JsonException"/> when
    /// the text is no JSON. Looked for as <c>backgroundImage.file</c> anywhere in it, so the copy's own shape does not matter.
    /// </summary>
    public static string? NamedIn(string copyJson)
    {
        using var document = JsonDocument.Parse(copyJson, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return Find(document.RootElement);

        static string? Find(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("backgroundImage") && property.Value.ValueKind == JsonValueKind.Object
                            && property.Value.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.String
                            && BackgroundImage.IsStoredName(file.GetString()))
                        {
                            return file.GetString();
                        }

                        if (Find(property.Value) is { } inside)
                        {
                            return inside;
                        }
                    }

                    return null;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        if (Find(item) is { } inside)
                        {
                            return inside;
                        }
                    }

                    return null;
                default:
                    return null;
            }
        }
    }
}

/// <summary>
/// A buffer that takes at most a set number of bytes (0.15.0 WP5b): a download into it stops with
/// <see cref="TooLongException"/> as soon as more come, so a picture over the limit is never held whole.
/// </summary>
internal sealed class BoundedBuffer(long limit) : Stream
{
    private readonly MemoryStream _bytes = new();

    /// <summary>Thrown by a write past the limit.</summary>
    internal sealed class TooLongException() : IOException("More bytes than the limit came.");

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _bytes.Length;

    public override long Position
    {
        get => _bytes.Position;
        set => throw new NotSupportedException();
    }

    public byte[] ToArray() => _bytes.ToArray();

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_bytes.Length + count > limit)
        {
            throw new TooLongException();
        }

        _bytes.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_bytes.Length + buffer.Length > limit)
        {
            throw new TooLongException();
        }

        _bytes.Write(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _bytes.Dispose();
        }

        base.Dispose(disposing);
    }
}
