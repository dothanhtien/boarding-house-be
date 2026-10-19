using BoardingHouse.Domain.Entities;

namespace BoardingHouse.Application.Services.Storage;

public interface IMediaUploader
{
    Task<MediaAsset> UploadAsync(IFileUpload file, string folder, Guid? organizationId, CancellationToken cancellationToken = default);
    Task TryDeleteAsync(string storageKey, string mimeType);
}
