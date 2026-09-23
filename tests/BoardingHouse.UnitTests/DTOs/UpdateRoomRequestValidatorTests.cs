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
}
