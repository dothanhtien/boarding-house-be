namespace BoardingHouse.Application.Services.Storage;

/// <summary>
/// A client file copied into a temporary buffer so several uploads can run in parallel after the request body
/// has been read. The buffer is owned (and disposed) by <see cref="MediaCollectionService"/>.
/// </summary>
internal sealed class BufferedFileUpload(Stream buffer, string fileName, string contentType) : IFileUpload
{
    public string FileName => fileName;
    public string ContentType => contentType;
    public long Length => buffer.Length;

    public Stream OpenReadStream()
    {
        buffer.Position = 0;
        return buffer;
    }
}
