using FluentValidation.TestHelper;
using Microsoft.AspNetCore.Http;
using BoardingHouse.Api.DTOs.RoomAssetConditionHistories;
using BoardingHouse.Api.Services.Storage;
using BoardingHouse.Domain.Entities.Enums;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateRoomAssetConditionHistoryRequestValidatorTests
{
    private readonly CreateRoomAssetConditionHistoryRequestValidator _validator = new();

    private static IFormFile NewFile(string contentType = "image/png", long length = 1024) =>
        new FormFile(Stream.Null, 0, length, "files", "photo.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    [Fact]
    public void Validate_ConditionWithImages_HasNoErrors()
    {
        var result = _validator.TestValidate(new CreateRoomAssetConditionHistoryRequest
        {
            NewCondition = AssetCondition.Damaged,
            Files = [NewFile(), NewFile("image/jpeg")]
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NoFiles_HasNoErrors()
    {
        var result = _validator.TestValidate(new CreateRoomAssetConditionHistoryRequest { NewCondition = AssetCondition.Good });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_MissingCondition_HasError()
    {
        var result = _validator.TestValidate(new CreateRoomAssetConditionHistoryRequest { Files = [NewFile()] });

        result.ShouldHaveValidationErrorFor(x => x.NewCondition).WithErrorMessage("NewCondition is required");
    }

    [Fact]
    public void Validate_UndefinedCondition_HasError()
    {
        var result = _validator.TestValidate(new CreateRoomAssetConditionHistoryRequest { NewCondition = (AssetCondition)99 });

        result.ShouldHaveValidationErrorFor(x => x.NewCondition).WithErrorMessage("NewCondition is invalid");
    }

    [Fact]
    public void Validate_MaxFilesPerRequest_HasNoErrors()
    {
        var files = Enumerable.Range(0, MediaLimits.MaxFilesPerRequest).Select(_ => NewFile()).ToList();

        var result = _validator.TestValidate(new CreateRoomAssetConditionHistoryRequest
        {
            NewCondition = AssetCondition.Damaged,
            Files = files
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_MoreThanMaxFilesPerRequest_HasFilesError()
    {
        var files = Enumerable.Range(0, MediaLimits.MaxFilesPerRequest + 1).Select(_ => NewFile()).ToList();

        var result = _validator.TestValidate(new CreateRoomAssetConditionHistoryRequest
        {
            NewCondition = AssetCondition.Damaged,
            Files = files
        });

        result.ShouldHaveValidationErrorFor(x => x.Files);
    }

    [Fact]
    public void Validate_OneFileNotAnImage_HasErrorOnThatFile()
    {
        var result = _validator.TestValidate(new CreateRoomAssetConditionHistoryRequest
        {
            NewCondition = AssetCondition.Damaged,
            Files = [NewFile(), NewFile("application/pdf")]
        });

        result.ShouldHaveValidationErrorFor("Files[1]");
        result.ShouldNotHaveValidationErrorFor("Files[0]");
    }
}
