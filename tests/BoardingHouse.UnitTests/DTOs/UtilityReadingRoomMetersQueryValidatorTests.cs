using BoardingHouse.Application.DTOs.UtilityReadings;

namespace BoardingHouse.UnitTests.DTOs;

public class UtilityReadingRoomMetersQueryValidatorTests
{
    private readonly UtilityReadingRoomMetersQueryValidator _validator = new();

    [Fact]
    public void Validate_FirstDayOfMonth_IsValid()
    {
        var result = _validator.Validate(new UtilityReadingRoomMetersQuery { ReadingMonth = new DateOnly(2026, 7, 1) });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_FarFutureMonth_IsValid()
    {
        var result = _validator.Validate(new UtilityReadingRoomMetersQuery { ReadingMonth = new DateOnly(2099, 1, 1) });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MissingReadingMonth_HasReadingMonthError()
    {
        var result = _validator.Validate(new UtilityReadingRoomMetersQuery());

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UtilityReadingRoomMetersQuery.ReadingMonth));
    }

    [Fact]
    public void Validate_NotFirstDayOfMonth_HasReadingMonthError()
    {
        var result = _validator.Validate(new UtilityReadingRoomMetersQuery { ReadingMonth = new DateOnly(2026, 7, 15) });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UtilityReadingRoomMetersQuery.ReadingMonth));
    }
}
