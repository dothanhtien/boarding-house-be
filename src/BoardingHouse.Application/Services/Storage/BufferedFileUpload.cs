namespace BoardingHouse.Application.Services.Storage;

/// <summary>
/// A client file copied into a temporary buffer so several uploads can run in parallel after the request body
/// has been read. The buffer is owned (and disposed) by <see cref="MediaCollectionService"/>: each
/// <see cref="OpenReadStream"/> hands out a fresh, non-owning view, so a caller disposing it (as
/// <see cref="MediaUploader"/> does) leaves the buffer readable.
/// </summary>
internal sealed class BufferedFileUpload(Stream buffer, string fileName, string contentType) : IFileUpload
{
    public string FileName => fileName;
    public string ContentType => contentType;
    public long Length => buffer.Length;

    public Stream OpenReadStream()
    {
        buffer.Position = 0;
        return new NonOwningStream(buffer);
    }

    private sealed class NonOwningStream(Stream inner) : Stream
    {
        private bool _disposed;

        private Stream Inner => _disposed ? throw new ObjectDisposedException(nameof(NonOwningStream)) : inner;

        public override bool CanRead => !_disposed && inner.CanRead;
        public override bool CanSeek => !_disposed && inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => Inner.Length;

        public override long Position
        {
            get => Inner.Position;
            set => Inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => Inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => Inner.Seek(offset, origin);

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // Only marks this view closed; the shared buffer stays open for its owner.
        protected override void Dispose(bool disposing) => _disposed = true;

        public override ValueTask DisposeAsync()
        {
            _disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
