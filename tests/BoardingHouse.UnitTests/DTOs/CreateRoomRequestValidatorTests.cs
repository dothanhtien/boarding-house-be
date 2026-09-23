using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities.Enums;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class CreateRoomRequestValidatorTests
{
    private readonly CreateRoomRequestValidator _validator = new();

    private static CreateRoomRequest ValidRequest() => new()
    {
        PropertyId = Guid.NewGuid(),
        RoomNumber = "101"
    };

    [Fact]
    public void Validate_EmptyPropertyId_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { PropertyId = Guid.Empty });

        result.ShouldHaveValidationErrorFor(x => x.PropertyId);
    }

    [Fact]
    public void Validate_EmptyRoomNumber_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { RoomNumber = "" });

        result.ShouldHaveValidationErrorFor(x => x.RoomNumber);
    }

    [Fact]
    public void Validate_RoomNumberExceeds20Characters_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { RoomNumber = new string('a', 21) });

        result.ShouldHaveValidationErrorFor(x => x.RoomNumber);
    }

    [Fact]
    public void Validate_RoomCategoryOutOfRange_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { RoomCategory = (RoomCategory)99 });

        result.ShouldHaveValidationErrorFor(x => x.RoomCategory);
    }

    [Fact]
    public void Validate_FloorNumberNegative_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { FloorNumber = -1 });

        result.ShouldHaveValidationErrorFor(x => x.FloorNumber);
    }

    [Fact]
    public void Validate_FloorNumberZero_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { FloorNumber = 0 });

        result.ShouldNotHaveValidationErrorFor(x => x.FloorNumber);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_AreaNotPositive_HasError(decimal area)
    {
        var result = _validator.TestValidate(ValidRequest() with { Area = area });

        result.ShouldHaveValidationErrorFor(x => x.Area);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_CapacityNotPositive_HasError(int capacity)
    {
        var result = _validator.TestValidate(ValidRequest() with { Capacity = capacity });

        result.ShouldHaveValidationErrorFor(x => x.Capacity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MonthlyRentNotPositive_HasError(decimal monthlyRent)
    {
        var result = _validator.TestValidate(ValidRequest() with { MonthlyRent = monthlyRent });

        result.ShouldHaveValidationErrorFor(x => x.MonthlyRent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_DepositAmountNotPositive_HasError(decimal depositAmount)
    {
        var result = _validator.TestValidate(ValidRequest() with { DepositAmount = depositAmount });

        result.ShouldHaveValidationErrorFor(x => x.DepositAmount);
    }

    [Fact]
    public void Validate_ValidRequestWithAllOptionalFields_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest() with
        {
            FloorNumber = 1,
            Area = 25.5m,
            Capacity = 2,
            MonthlyRent = 3_000_000,
            DepositAmount = 3_000_000
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }
}
