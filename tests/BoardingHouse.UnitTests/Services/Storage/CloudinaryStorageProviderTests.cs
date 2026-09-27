using BoardingHouse.Api.Services.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BoardingHouse.UnitTests.Services.Storage;

public class CloudinaryStorageProviderTests
{
    private readonly CloudinaryStorageProvider _provider = new(
        Options.Create(new CloudinaryOptions
        {
            CloudName = "demo",
            ApiKey = "key",
            ApiSecret = "secret",
            RootFolder = "boarding-house"
        }),
        NullLogger<CloudinaryStorageProvider>.Instance);

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("IMAGE/WEBP", "webp")]
    [InlineData("application/pdf", "pdf")]
    public void GetUrl_ImageOrPdf_ReturnsSecureImageUrlWithFormatExtension(string mimeType, string extension)
    {
        var url = _provider.GetUrl("boarding-house/avatars/abc", mimeType);

        Assert.StartsWith("https://res.cloudinary.com/demo/image/upload/", url);
        Assert.EndsWith($"boarding-house/avatars/abc.{extension}", url);
    }

    [Theory]
    [InlineData("application/zip")]
    [InlineData("text/plain")]
    public void GetUrl_OtherTypes_ThrowsNotSupported(string mimeType)
    {
        Assert.Throws<NotSupportedException>(() => _provider.GetUrl("boarding-house/files/abc", mimeType));
    }
}
