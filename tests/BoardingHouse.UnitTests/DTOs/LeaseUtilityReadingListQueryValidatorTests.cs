using BoardingHouse.Application.DTOs.UtilityReadings;

namespace BoardingHouse.UnitTests.DTOs;

public class LeaseUtilityReadingListQueryValidatorTests
{
    private readonly LeaseUtilityReadingListQueryValidator _validator = new();

    [Fact]
    public void Validate_NoFilters_IsValid()
    {
        var result = _validator.Validate(new LeaseUtilityReadingListQuery());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_FirstDaysOfMonth_IsValid()
    {
        var result = _validator.Validate(new LeaseUtilityReadingListQuery
        {
            From = new DateOnly(2026, 7, 1),
            To = new DateOnly(2026, 9, 1)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_SameFromAndTo_IsValid()
    {
        var result = _validator.Validate(new LeaseUtilityReadingListQuery
        {
            From = new DateOnly(2026, 7, 1),
            To = new DateOnly(2026, 7, 1)
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_OnlyTo_IsValid()
    {
        var result = _validator.Validate(new LeaseUtilityReadingListQuery { To = new DateOnly(2026, 7, 1) });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_FromNotFirstDay_HasFromError()
    {
        var result = _validator.Validate(new LeaseUtilityReadingListQuery { From = new DateOnly(2026, 7, 2) });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(LeaseUtilityReadingListQuery.From), error.PropertyName);
        Assert.Equal("From must be the first day of a month", error.ErrorMessage);
    }

    [Fact]
    public void Validate_ToNotFirstDay_HasToError()
    {
        var result = _validator.Validate(new LeaseUtilityReadingListQuery { To = new DateOnly(2026, 7, 15) });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(LeaseUtilityReadingListQuery.To), error.PropertyName);
        Assert.Equal("To must be the first day of a month", error.ErrorMessage);
    }

    [Fact]
    public void Validate_ToBeforeFrom_HasToError()
    {
        var result = _validator.Validate(new LeaseUtilityReadingListQuery
        {
            From = new DateOnly(2026, 8, 1),
            To = new DateOnly(2026, 7, 1)
        });

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(LeaseUtilityReadingListQuery.To), error.PropertyName);
        Assert.Equal("To must not be before From", error.ErrorMessage);
    }
}
