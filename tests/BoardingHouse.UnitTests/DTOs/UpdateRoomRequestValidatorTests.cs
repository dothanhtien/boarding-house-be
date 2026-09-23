using System.Globalization;
using BoardingHouse.Api.DTOs.Rooms;
using BoardingHouse.Api.Entities.Enums;
using FluentValidation.TestHelper;

namespace BoardingHouse.UnitTests.DTOs;

public class UpdateRoomRequestValidatorTests
{
    private readonly UpdateRoomRequestValidator _validator = new();

    [Fact]
    public void Validate_EmptyRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyRoomNumber_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { RoomNumber = "" });

        result.ShouldHaveValidationErrorFor("RoomNumber");
    }

    [Fact]
    public void Validate_NullRoomNumber_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { RoomNumber = null });

        result.ShouldHaveValidationErrorFor("RoomNumber");
    }

    [Fact]
    public void Validate_RoomNumberExceeds20Characters_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { RoomNumber = new string('a', 21) });

        result.ShouldHaveValidationErrorFor("RoomNumber");
    }

    [Fact]
    public void Validate_RoomCategoryOutOfEnum_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { RoomCategory = (RoomCategory)999 });

        result.ShouldHaveValidationErrorFor("RoomCategory");
    }

    [Fact]
    public void Validate_RoomStatusOutOfEnum_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { RoomStatus = (RoomStatus)999 });

        result.ShouldHaveValidationErrorFor("RoomStatus");
    }

    [Fact]
    public void Validate_FloorNumberNegative_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { FloorNumber = -1 });

        result.ShouldHaveValidationErrorFor("FloorNumber");
    }

    [Fact]
    public void Validate_FloorNumberZero_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { FloorNumber = 0 });

        result.ShouldNotHaveValidationErrorFor("FloorNumber");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_AreaNotPositive_HasError(decimal area)
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { Area = area });

        result.ShouldHaveValidationErrorFor("Area");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_CapacityNotPositive_HasError(int capacity)
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { Capacity = capacity });

        result.ShouldHaveValidationErrorFor("Capacity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_MonthlyRentNotPositive_HasError(decimal monthlyRent)
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { MonthlyRent = monthlyRent });

        result.ShouldHaveValidationErrorFor("MonthlyRent");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_DepositAmountNotPositive_HasError(decimal depositAmount)
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { DepositAmount = depositAmount });

        result.ShouldHaveValidationErrorFor("DepositAmount");
    }

    [Theory]
    [InlineData("25.555")]
    [InlineData("100000000")]
    public void Validate_AreaExceedsPrecisionOrScale_HasError(string area)
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { Area = decimal.Parse(area, CultureInfo.InvariantCulture) });

        result.ShouldHaveValidationErrorFor("Area");
    }

    [Fact]
    public void Validate_AreaAtMaxPrecisionAndScale_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { Area = 99999999.99m });

        result.ShouldNotHaveValidationErrorFor("Area");
    }

    [Theory]
    [InlineData("3000000.555")]
    [InlineData("10000000000000000")]
    public void Validate_MonthlyRentExceedsPrecisionOrScale_HasError(string monthlyRent)
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { MonthlyRent = decimal.Parse(monthlyRent, CultureInfo.InvariantCulture) });

        result.ShouldHaveValidationErrorFor("MonthlyRent");
    }

    [Fact]
    public void Validate_MonthlyRentAtMaxPrecisionAndScale_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { MonthlyRent = 9999999999999999.99m });

        result.ShouldNotHaveValidationErrorFor("MonthlyRent");
    }

    [Theory]
    [InlineData("3000000.555")]
    [InlineData("10000000000000000")]
    public void Validate_DepositAmountExceedsPrecisionOrScale_HasError(string depositAmount)
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { DepositAmount = decimal.Parse(depositAmount, CultureInfo.InvariantCulture) });

        result.ShouldHaveValidationErrorFor("DepositAmount");
    }

    [Fact]
    public void Validate_DepositAmountAtMaxPrecisionAndScale_HasNoError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { DepositAmount = 9999999999999999.99m });

        result.ShouldNotHaveValidationErrorFor("DepositAmount");
    }

    [Fact]
    public void Validate_NullableFieldsExplicitlyCleared_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest
        {
            FloorNumber = null,
            Area = null,
            Capacity = null,
            MonthlyRent = null,
            DepositAmount = null,
            Note = null
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ValidRequest_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest
        {
            RoomNumber = "102",
            RoomCategory = RoomCategory.Studio,
            RoomStatus = RoomStatus.Maintenance,
            FloorNumber = 1,
            Area = 20,
            Capacity = 2,
            MonthlyRent = 3_000_000,
            DepositAmount = 3_000_000,
            Note = "Near the stairs"
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_AmenitiesSetToNull_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { Amenities = null });

        result.ShouldHaveValidationErrorFor("Amenities");
    }

    [Fact]
    public void Validate_AmenitiesSetToEmptyList_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest { Amenities = new List<UpdateRoomAmenityRequest>() });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_20AmenityChanges_HasNoErrors()
    {
        var amenities = Enumerable.Range(0, 10).Select(_ => new UpdateRoomAmenityRequest { Id = Guid.NewGuid(), IsDeleted = true })
            .Concat(Enumerable.Range(0, 10).Select(i => new UpdateRoomAmenityRequest { Name = $"Amenity {i}" }))
            .ToList();

        var result = _validator.TestValidate(new UpdateRoomRequest { Amenities = amenities });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_MoreThan20AmenityChanges_HasError()
    {
        var amenities = Enumerable.Range(0, 21).Select(i => new UpdateRoomAmenityRequest { Name = $"Amenity {i}" }).ToList();

        var result = _validator.TestValidate(new UpdateRoomRequest { Amenities = amenities });

        result.ShouldHaveValidationErrorFor("Amenities");
    }

    [Fact]
    public void Validate_NullAmenityItem_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest
        {
            Amenities = new List<UpdateRoomAmenityRequest> { null!, new() { Id = Guid.NewGuid(), Quantity = 2 } }
        });

        result.ShouldHaveValidationErrorFor("Amenities[0]");
    }

    [Fact]
    public void Validate_SameIdTwice_HasError()
    {
        var id = Guid.NewGuid();

        var result = _validator.TestValidate(new UpdateRoomRequest
        {
            Amenities = new List<UpdateRoomAmenityRequest> { new() { Id = id, Quantity = 2 }, new() { Id = id, IsDeleted = true } }
        });

        result.ShouldHaveValidationErrorFor("Amenities");
    }

    [Fact]
    public void Validate_DuplicateAmenityNames_HasNoErrors()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest
        {
            Amenities = new List<UpdateRoomAmenityRequest> { new() { Name = "WiFi" }, new() { Id = Guid.NewGuid(), Name = "WiFi" } }
        });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_InvalidAmenityItem_HasError()
    {
        var result = _validator.TestValidate(new UpdateRoomRequest
        {
            Amenities = new List<UpdateRoomAmenityRequest> { new() { Name = "WiFi", Quantity = 0 } }
        });

        Assert.False(result.IsValid);
    }
}
