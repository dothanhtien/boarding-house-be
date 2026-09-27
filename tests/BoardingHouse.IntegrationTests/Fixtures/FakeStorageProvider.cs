using System.Collections.Concurrent;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Services.Storage;

namespace BoardingHouse.IntegrationTests.Fixtures;

public class FakeStorageProvider : IStorageProvider
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();
    public bool FailOnDelete { get; set; }

    public StorageProvider Provider => StorageProvider.Cloudinary;

    public async Task<StoredFile> UploadAsync(
        Stream content,
        string fileName,
        string mimeType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var key = $"test/{folder}/{Guid.NewGuid():N}";
        Files[key] = buffer.ToArray();
        return new StoredFile(key, GetUrl(key, mimeType), buffer.Length);
    }

    public Task DeleteAsync(string storageKey, string mimeType, CancellationToken cancellationToken = default)
    {
        if (FailOnDelete) throw new InvalidOperationException("Simulated storage failure");
        Files.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }

    public string GetUrl(string storageKey, string mimeType) => $"https://storage.test/{storageKey}";
}
