using BoardingHouse.Api.Entities;

namespace BoardingHouse.Api.Services.Storage;

public interface IMediaUploader
{
    Task<MediaAsset> UploadAsync(IFormFile file, string folder, Guid? organizationId, CancellationToken cancellationToken = default);
    Task TryDeleteAsync(string storageKey, string mimeType);
}
