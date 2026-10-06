using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Storage;

namespace BoardingHouse.Api.Services;

public class MediaCollectionService(
    IMediaAssetRepository mediaAssetRepository,
    IMediaUploader mediaUploader,
    ILogger<MediaCollectionService> logger) : IMediaCollectionService
{
    public async Task<IReadOnlyList<MediaAsset>> UploadAsync(
        MediaAssetEntityType entityType,
        IReadOnlyList<IFormFile> files,
        string folder,
        Guid? organizationId,
        CancellationToken cancellationToken = default)
    {
        var buffered = new List<IFormFile>(files.Count);
        foreach (var file in files)
        {
            var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            buffered.Add(new FormFile(buffer, 0, buffer.Length, file.Name, file.FileName)
            {
                Headers = file.Headers,
                ContentType = file.ContentType
            });
        }

        var uploads = buffered
            .Select(async file =>
            {
                var asset = await mediaUploader.UploadAsync(file, folder, organizationId, cancellationToken);
                asset.EntityType = entityType;
                return asset;
            })
            .ToList();

        try
        {
            return await Task.WhenAll(uploads);
        }
        catch
        {
            await DiscardAsync(uploads.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result));
            throw;
        }
    }

    public async Task StageAddAsync(IEnumerable<MediaAsset> uploaded, CancellationToken cancellationToken = default)
    {
        foreach (var asset in uploaded)
        {
            await mediaAssetRepository.AddAsync(asset, cancellationToken);
        }
    }

    public void StageRemove(IEnumerable<MediaAsset> assets)
    {
        foreach (var asset in assets)
        {
            mediaAssetRepository.SoftDelete(asset);
        }
    }

    public async Task CompleteAsync(IEnumerable<MediaAsset> removed)
    {
        foreach (var asset in removed)
        {
            logger.LogInformation("Media removed from collection ({EntityType}, {MediaAssetId})", asset.EntityType, asset.Id);
            await mediaUploader.TryDeleteAsync(asset.StorageKey, asset.MimeType);
        }
    }

    public async Task DiscardAsync(IEnumerable<MediaAsset> uploaded)
    {
        foreach (var asset in uploaded)
        {
            await mediaUploader.TryDeleteAsync(asset.StorageKey, asset.MimeType);
        }
    }
}
