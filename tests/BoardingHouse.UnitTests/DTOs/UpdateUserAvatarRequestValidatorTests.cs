using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Services.Storage;
using Microsoft.AspNetCore.Http;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateUserAvatarRequestValidatorTests
{
    private readonly UpdateUserAvatarRequestValidator _validator = new();

    private static IFormFile NewFile(long length = 1024, string contentType = "image/png", string fileName = "me.png") =>
        new FormFile(Stream.Null, 0, length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void Validate_Image_IsValid(string contentType)
    {
        var result = _validator.Validate(new UpdateUserAvatarRequest { File = NewFile(contentType: contentType) });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NoFile_HasFileError()
    {
        var result = _validator.Validate(new UpdateUserAvatarRequest());

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserAvatarRequest.File) && e.ErrorMessage == "File is required");
    }

    [Fact]
    public void Validate_Pdf_HasFileError()
    {
        var result = _validator.Validate(new UpdateUserAvatarRequest { File = NewFile(contentType: "application/pdf") });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserAvatarRequest.File));
    }

    [Fact]
    public void Validate_EmptyFile_HasFileError()
    {
        var result = _validator.Validate(new UpdateUserAvatarRequest { File = NewFile(length: 0) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserAvatarRequest.File) && e.ErrorMessage == "File must not be empty");
    }

    [Fact]
    public void Validate_TooLarge_HasFileError()
    {
        var result = _validator.Validate(new UpdateUserAvatarRequest { File = NewFile(length: MediaLimits.MaxFileSizeBytes + 1) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserAvatarRequest.File));
    }

    [Fact]
    public void Validate_FileNameTooLong_HasFileError()
    {
        var fileName = new string('a', MediaLimits.MaxFileNameLength) + ".png";

        var result = _validator.Validate(new UpdateUserAvatarRequest { File = NewFile(fileName: fileName) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateUserAvatarRequest.File));
    }
}
