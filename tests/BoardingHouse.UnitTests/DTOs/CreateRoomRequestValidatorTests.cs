using System.Globalization;
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

    [Theory]
    [InlineData("25.555")]
    [InlineData("100000000")]
    public void Validate_AreaExceedsPrecisionOrScale_HasError(string area)
    {
        var result = _validator.TestValidate(ValidRequest() with { Area = decimal.Parse(area, CultureInfo.InvariantCulture) });

        result.ShouldHaveValidationErrorFor(x => x.Area);
    }

    [Fact]
    public void Validate_AreaAtMaxPrecisionAndScale_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Area = 99999999.99m });

        result.ShouldNotHaveValidationErrorFor(x => x.Area);
    }

    [Theory]
    [InlineData("3000000.555")]
    [InlineData("10000000000000000")]
    public void Validate_MonthlyRentExceedsPrecisionOrScale_HasError(string monthlyRent)
    {
        var result = _validator.TestValidate(ValidRequest() with { MonthlyRent = decimal.Parse(monthlyRent, CultureInfo.InvariantCulture) });

        result.ShouldHaveValidationErrorFor(x => x.MonthlyRent);
    }

    [Fact]
    public void Validate_MonthlyRentAtMaxPrecisionAndScale_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { MonthlyRent = 9999999999999999.99m });

        result.ShouldNotHaveValidationErrorFor(x => x.MonthlyRent);
    }

    [Theory]
    [InlineData("3000000.555")]
    [InlineData("10000000000000000")]
    public void Validate_DepositAmountExceedsPrecisionOrScale_HasError(string depositAmount)
    {
        var result = _validator.TestValidate(ValidRequest() with { DepositAmount = decimal.Parse(depositAmount, CultureInfo.InvariantCulture) });

        result.ShouldHaveValidationErrorFor(x => x.DepositAmount);
    }

    [Fact]
    public void Validate_DepositAmountAtMaxPrecisionAndScale_HasNoError()
    {
        var result = _validator.TestValidate(ValidRequest() with { DepositAmount = 9999999999999999.99m });

        result.ShouldNotHaveValidationErrorFor(x => x.DepositAmount);
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

    [Fact]
    public void Validate_AmenitiesNullOrEmpty_HasNoErrors()
    {
        _validator.TestValidate(ValidRequest() with { Amenities = null }).ShouldNotHaveAnyValidationErrors();
        _validator.TestValidate(ValidRequest() with { Amenities = [] }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_MoreThan20Amenities_HasError()
    {
        var amenities = Enumerable.Range(0, 21).Select(i => new CreateRoomAmenityRequest { Name = $"Amenity {i}" }).ToList();

        var result = _validator.TestValidate(ValidRequest() with { Amenities = amenities });

        result.ShouldHaveValidationErrorFor(x => x.Amenities);
    }

    [Fact]
    public void Validate_NullAmenityItem_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Amenities = [null!] });

        result.ShouldHaveValidationErrorFor("Amenities[0]");
    }

    [Fact]
    public void Validate_DuplicateAmenityNames_HasNoErrors()
    {
        var result = _validator.TestValidate(ValidRequest() with
        {
            Amenities = [new CreateRoomAmenityRequest { Name = "WiFi" }, new CreateRoomAmenityRequest { Name = "WiFi", Quantity = 2 }]
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_AmenityWithEmptyName_HasError()
    {
        var result = _validator.TestValidate(ValidRequest() with { Amenities = [new CreateRoomAmenityRequest { Name = "" }] });

        result.ShouldHaveValidationErrorFor("Amenities[0].Name");
    }
}
