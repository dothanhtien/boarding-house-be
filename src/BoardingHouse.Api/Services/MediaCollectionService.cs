using Microsoft.EntityFrameworkCore;
using BoardingHouse.Api.Persistence;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Storage;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.Api.Services;

public class MediaCollectionService(
    IMediaAssetRepository mediaAssetRepository,
    IMediaUploader mediaUploader,
    IUnitOfWork unitOfWork,
    ILogger<MediaCollectionService> logger) : IMediaCollectionService
{
    private async Task<IReadOnlyList<MediaAsset>> UploadAsync(
        MediaAssetEntityType entityType,
        IReadOnlyList<IFormFile> files,
        string folder,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var buffers = new List<FileStream>(files.Count);
        try
        {
            var buffered = new List<IFormFile>(files.Count);
            foreach (var file in files)
            {
                var buffer = CreateTempBuffer();
                buffers.Add(buffer);
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
                    var asset = await mediaUploader.UploadAsync(file, folder, organizationId, CancellationToken.None);
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
        finally
        {
            foreach (var buffer in buffers)
            {
                await buffer.DisposeAsync();
            }
        }
    }

    private static FileStream CreateTempBuffer() =>
        new(
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

    public async Task<TResult> AddAsync<TResult>(
        MediaAssetEntityType entityType,
        IReadOnlyList<IFormFile> files,
        string folder,
        Guid? organizationId,
        Func<IReadOnlyList<MediaAsset>, CancellationToken, Task<TResult>> attachAsync,
        CancellationToken cancellationToken = default)
    {
        if (unitOfWork.HasActiveTransaction)
        {
            throw new InvalidOperationException($"{nameof(AddAsync)} must not run inside another transaction");
        }

        var uploaded = await UploadAsync(entityType, files, folder, organizationId, cancellationToken);

        var staged = false;
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var result = await attachAsync(uploaded, ct);
                await StageAddAsync(uploaded, ct);
                staged = true;
                return result;
            }, cancellationToken);
        }
        catch (Exception exception) when (!staged || exception is DbUpdateException)
        {
            await DiscardAsync(uploaded);
            throw;
        }
        catch
        {
            if (await IsSafeToDiscardAsync(uploaded))
            {
                await DiscardAsync(uploaded);
            }

            throw;
        }
    }

    private async Task<bool> IsSafeToDiscardAsync(IReadOnlyList<MediaAsset> uploaded)
    {
        if (uploaded.Count == 0)
        {
            return true;
        }

        try
        {
            return !await mediaAssetRepository.ExistsAsync(uploaded[0].Id, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Could not tell whether a failed commit persisted media {MediaAssetIds}; keeping the uploaded files",
                uploaded.Select(a => a.Id));
            return false;
        }
    }

    private async Task StageAddAsync(IEnumerable<MediaAsset> uploaded, CancellationToken cancellationToken)
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

    private async Task DiscardAsync(IEnumerable<MediaAsset> uploaded)
    {
        foreach (var asset in uploaded)
        {
            await mediaUploader.TryDeleteAsync(asset.StorageKey, asset.MimeType);
        }
    }
}
