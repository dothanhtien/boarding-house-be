using BoardingHouse.Api.Entities.Enums;

namespace BoardingHouse.Api.Services.Storage;

public interface IStorageProvider
{
    StorageProvider Provider { get; }
    Task<StoredFile> UploadAsync(
        Stream content,
        string fileName,
        string mimeType,
        string folder,
        CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, string mimeType, CancellationToken cancellationToken = default);
    string GetUrl(string storageKey, string mimeType);
}

public record StoredFile(string StorageKey, string Url, long SizeBytes);
