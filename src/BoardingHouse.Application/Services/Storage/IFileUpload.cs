namespace BoardingHouse.Application.Services.Storage;

/// <summary>
/// A file sent by the client. Keeps the Application layer free of ASP.NET Core's IFormFile; the Api wraps form
/// files in an adapter.
/// </summary>
public interface IFileUpload
{
    string FileName { get; }
    string ContentType { get; }
    long Length { get; }
    Stream OpenReadStream();
}
