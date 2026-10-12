using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.Services.Storage;
using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Http;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateRoomAssetConditionHistoryRequestValidatorTests
{
    private readonly UpdateRoomAssetConditionHistoryRequestValidator _validator = new();

    private static IFormFile NewFile(string contentType = "image/png", long length = 1024) =>
        new FormFile(Stream.Null, 0, length, "files", "photo.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    [Fact]
    public void Validate_Empty_HasNoErrors()
    {
        _validator.TestValidate(new UpdateRoomAssetConditionHistoryRequest()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NoteRemoveAndFiles_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomAssetConditionHistoryRequest
        {
            Note = "Broken leg",
            RemoveMediaIds = [Guid.NewGuid()],
            Files = [NewFile()]
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_DuplicateRemoveMediaIds_HasError()
    {
        var id = Guid.NewGuid();

        var result = _validator.TestValidate(new UpdateRoomAssetConditionHistoryRequest { RemoveMediaIds = [id, id] });

        result.ShouldHaveValidationErrorFor(x => x.RemoveMediaIds);
    }

    [Fact]
    public void Validate_MoreThanMaxFilesPerRequest_HasError()
    {
        var files = Enumerable.Range(0, MediaLimits.MaxFilesPerRequest + 1).Select(_ => NewFile()).ToList();

        var result = _validator.TestValidate(new UpdateRoomAssetConditionHistoryRequest { Files = files });

        result.ShouldHaveValidationErrorFor(x => x.Files);
    }

    [Fact]
    public void Validate_OneFileNotAnImage_HasErrorOnThatFile()
    {
        var result = _validator.TestValidate(new UpdateRoomAssetConditionHistoryRequest
        {
            Files = [NewFile(), NewFile("application/pdf")]
        });

        result.ShouldHaveValidationErrorFor("Files[1]");
        result.ShouldNotHaveValidationErrorFor("Files[0]");
    }
}
