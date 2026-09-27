namespace BoardingHouse.Api.Services.Storage;

public static class MediaLimits
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public const long MaxRequestBodyBytes = MaxFileSizeBytes + 1024 * 1024;

    public const int MaxFileNameLength = 255;

    public static readonly IReadOnlySet<string> AllowedImageMimeTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };
}
