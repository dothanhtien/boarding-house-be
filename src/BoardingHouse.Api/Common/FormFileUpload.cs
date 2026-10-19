using BoardingHouse.Application.Services.Storage;

namespace BoardingHouse.Api.Common;

public sealed class FormFileUpload(IFormFile file) : IFileUpload
{
    public string FileName => file.FileName;
    public string ContentType => file.ContentType;
    public long Length => file.Length;
    public Stream OpenReadStream() => file.OpenReadStream();
}

public static class FormFileUploadExtensions
{
    public static IFileUpload ToFileUpload(this IFormFile file) => new FormFileUpload(file);

    // A missing optional "files" field binds as null; every handler treats that the same as "no files".
    public static IReadOnlyList<IFileUpload> ToFileUploads(this IEnumerable<IFormFile>? files) =>
        files?.Select(IFileUpload (f) => new FormFileUpload(f)).ToList() ?? [];
}
