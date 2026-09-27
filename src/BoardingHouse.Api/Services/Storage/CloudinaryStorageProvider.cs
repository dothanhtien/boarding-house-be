using BoardingHouse.Api.Entities.Enums;
using BoardingHouse.Api.Exceptions;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Options;

namespace BoardingHouse.Api.Services.Storage;

public class CloudinaryStorageProvider : IStorageProvider
{
    private readonly Cloudinary _cloudinary;
    private readonly string _rootFolder;
    private readonly ILogger<CloudinaryStorageProvider> _logger;

    public CloudinaryStorageProvider(IOptions<CloudinaryOptions> options, ILogger<CloudinaryStorageProvider> logger)
    {
        var value = options.Value;
        _cloudinary = new Cloudinary(new Account(value.CloudName, value.ApiKey, value.ApiSecret));
        _cloudinary.Api.Secure = true;
        _rootFolder = value.RootFolder.Trim('/');
        _logger = logger;
    }

    public StorageProvider Provider => StorageProvider.Cloudinary;

    public async Task<StoredFile> UploadAsync(
        Stream content,
        string fileName,
        string mimeType,
        string folder,
        CancellationToken cancellationToken = default)
    {
        EnsureImageResource(mimeType);

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(fileName, content),
            PublicId = $"{_rootFolder}/{folder.Trim('/')}/{Guid.NewGuid():N}",
            Overwrite = false,
            UseFilename = false,
            UniqueFilename = false
        };

        var result = await _cloudinary.UploadAsync(uploadParams, cancellationToken);
        if (result.Error is not null)
        {
            _logger.LogError("Cloudinary upload failed: {Error}", result.Error.Message);
            throw new AppInternalException("File upload failed");
        }

        return new StoredFile(result.PublicId, result.SecureUrl.ToString(), result.Bytes);
    }

    public async Task DeleteAsync(string storageKey, string mimeType, CancellationToken cancellationToken = default)
    {
        var result = await _cloudinary.DestroyAsync(new DeletionParams(storageKey)
        {
            ResourceType = ResolveResourceType(mimeType),
            Invalidate = true
        });

        if (result.Result is not ("ok" or "not found"))
        {
            _logger.LogError("Cloudinary delete failed ({StorageKey}): {Result} {Error}", storageKey, result.Result, result.Error?.Message);
            throw new AppInternalException("File delete failed");
        }
    }

    public string GetUrl(string storageKey, string mimeType)
    {
        EnsureImageResource(mimeType);
        return _cloudinary.Api.UrlImgUp.Secure(true).Format(ResolveFormat(mimeType)).BuildUrl(storageKey);
    }

    private static ResourceType ResolveResourceType(string mimeType) =>
        mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) || mimeType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            ? ResourceType.Image
            : ResourceType.Raw;

    private static void EnsureImageResource(string mimeType)
    {
        if (ResolveResourceType(mimeType) != ResourceType.Image)
        {
            throw new NotSupportedException($"MIME type '{mimeType}' is not supported by {nameof(CloudinaryStorageProvider)} yet");
        }
    }

    private static string ResolveFormat(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        "image/gif" => "gif",
        "application/pdf" => "pdf",
        _ => throw new NotSupportedException($"MIME type '{mimeType}' is not supported by {nameof(CloudinaryStorageProvider)} yet")
    };
}
