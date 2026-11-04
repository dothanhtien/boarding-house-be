using FluentValidation.TestHelper;
using BoardingHouse.Application.Common.Listing;
using BoardingHouse.Application.DTOs.UtilityReadings;

namespace BoardingHouse.UnitTests.DTOs;

public class UtilityReadingRoomListQueryValidatorTests
{
    private readonly UtilityReadingRoomListQueryValidator _validator = new();

    [Fact]
    public void Validate_FirstDayOfMonth_HasNoErrors()
    {
        var result = _validator.TestValidate(new UtilityReadingRoomListQuery { ReadingMonth = new DateOnly(2026, 6, 1) });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_MissingReadingMonth_HasError()
    {
        var result = _validator.TestValidate(new UtilityReadingRoomListQuery());

        result.ShouldHaveValidationErrorFor(x => x.ReadingMonth);
    }

    [Fact]
    public void Validate_ReadingMonthNotFirstDay_HasError()
    {
        var result = _validator.TestValidate(new UtilityReadingRoomListQuery { ReadingMonth = new DateOnly(2026, 6, 15) });

        result.ShouldHaveValidationErrorFor(x => x.ReadingMonth);
    }

    [Fact]
    public void Default_SortOrder_IsAscending()
    {
        Assert.Equal(SortOrder.Asc, new UtilityReadingRoomListQuery().SortOrder);
    }
}
