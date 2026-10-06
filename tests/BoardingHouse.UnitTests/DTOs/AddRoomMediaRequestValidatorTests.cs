using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Services.Storage;
using Microsoft.AspNetCore.Http;

namespace BoardingHouse.UnitTests.DTOs;

public class AddRoomMediaRequestValidatorTests
{
    private readonly AddRoomMediaRequestValidator _validator = new();

    private static IFormFile NewFile(string contentType = "image/png", long length = 1024) =>
        new FormFile(Stream.Null, 0, length, "files", "photo.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    [Fact]
    public void Validate_ImagesWithinLimit_IsValid()
    {
        var result = _validator.Validate(new AddRoomMediaRequest { Files = [NewFile(), NewFile("image/jpeg")] });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NoFiles_HasFilesError()
    {
        var result = _validator.Validate(new AddRoomMediaRequest());

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRoomMediaRequest.Files) && e.ErrorMessage == "At least one file is required");
    }

    [Fact]
    public void Validate_EmptyList_HasFilesError()
    {
        var result = _validator.Validate(new AddRoomMediaRequest { Files = [] });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRoomMediaRequest.Files));
    }

    [Fact]
    public void Validate_MoreThanMaxFilesPerRequest_HasFilesError()
    {
        var files = Enumerable.Range(0, MediaLimits.MaxFilesPerRequest + 1).Select(_ => NewFile()).ToList();

        var result = _validator.Validate(new AddRoomMediaRequest { Files = files });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddRoomMediaRequest.Files));
    }

    [Fact]
    public void Validate_OneFileNotAnImage_HasErrorOnThatFile()
    {
        var result = _validator.Validate(new AddRoomMediaRequest { Files = [NewFile(), NewFile("application/pdf")] });

        Assert.Contains(result.Errors, e => e.PropertyName == "Files[1]");
        Assert.DoesNotContain(result.Errors, e => e.PropertyName == "Files[0]");
    }

    [Fact]
    public void Validate_EmptyFile_HasErrorOnThatFile()
    {
        var result = _validator.Validate(new AddRoomMediaRequest { Files = [NewFile(length: 0)] });

        Assert.Contains(result.Errors, e => e.PropertyName == "Files[0]" && e.ErrorMessage == "File must not be empty");
    }
}
