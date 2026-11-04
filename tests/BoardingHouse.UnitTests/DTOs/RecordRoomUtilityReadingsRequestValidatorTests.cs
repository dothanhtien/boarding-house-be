using FluentValidation.TestHelper;
using BoardingHouse.Application.DTOs.UtilityReadings;
using BoardingHouse.Domain.Common;

namespace BoardingHouse.UnitTests.DTOs;

public class RecordRoomUtilityReadingsRequestValidatorTests
{
    private readonly RecordRoomUtilityReadingsRequestValidator _validator = new();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly ThisMonth => BillingMonth.Of(Today);
    private static DateOnly LastMonth => ThisMonth.AddMonths(-1);

    private static RecordRoomUtilityReadingsRequest Request(DateOnly? readingMonth) => new()
    {
        ReadingMonth = readingMonth,
        Items = [new RecordRoomUtilityReadingItemRequest { ServiceId = Guid.NewGuid(), CurrentValue = 120m }]
    };

    [Fact]
    public void Validate_WithReadingMonth_HasNoErrors()
    {
        _validator.TestValidate(Request(ThisMonth)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_WithoutReadingMonth_HasError()
    {
        _validator.TestValidate(Request(null)).ShouldHaveValidationErrorFor(x => x.ReadingMonth);
    }

    [Fact]
    public void Validate_ReadingMonthNotFirstDay_HasError()
    {
        _validator.TestValidate(Request(ThisMonth.AddDays(1))).ShouldHaveValidationErrorFor(x => x.ReadingMonth);
    }

    [Fact]
    public void Validate_ReadingMonthAfterNextMonth_HasError()
    {
        _validator.TestValidate(Request(ThisMonth.AddMonths(2))).ShouldHaveValidationErrorFor(x => x.ReadingMonth);
    }

    [Fact]
    public void Validate_ReadOnInTheFuture_HasError()
    {
        _validator.TestValidate(Request(ThisMonth) with { ReadOn = Today.AddDays(1) })
            .ShouldHaveValidationErrorFor(x => x.ReadOn);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, -1)]
    public void Validate_ReadOnWithinReadingMonthOrTheMonthBefore_HasNoErrors(int monthOffset, int dayOffset)
    {
        // (-1, 0) = first day of the month before, (1, -1) = last day of the reading month
        var readOn = LastMonth.AddMonths(monthOffset).AddDays(dayOffset);

        _validator.TestValidate(Request(LastMonth) with { ReadOn = readOn }).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-1, -1)]
    [InlineData(1, 0)]
    public void Validate_ReadOnOutsideReadingMonthAndTheMonthBefore_HasError(int monthOffset, int dayOffset)
    {
        // (-1, -1) = last day two months before, (1, 0) = first day of the month after
        var readOn = LastMonth.AddMonths(monthOffset).AddDays(dayOffset);

        _validator.TestValidate(Request(LastMonth) with { ReadOn = readOn }).ShouldHaveValidationErrorFor(x => x.ReadOn);
    }

    [Fact]
    public void Validate_WithoutReadOn_ChecksTodayAgainstTheReadingMonth()
    {
        // Today is in ThisMonth, so it fits ThisMonth and NextMonth but not a month two back
        _validator.TestValidate(Request(ThisMonth.AddMonths(1))).ShouldNotHaveAnyValidationErrors();
        _validator.TestValidate(Request(ThisMonth.AddMonths(-2))).ShouldHaveValidationErrorFor(x => x.ReadOn);
    }

    [Theory]
    [InlineData("9999-12-01")]
    [InlineData("0001-01-01")]
    public void Validate_ReadingMonthAtTheDateOnlyBounds_HasErrorsInsteadOfThrowing(string readingMonth)
    {
        var result = _validator.TestValidate(Request(DateOnly.Parse(readingMonth)) with { ReadOn = Today });

        result.ShouldHaveValidationErrorFor(x => x.ReadOn);
    }

    [Fact]
    public void Validate_DuplicateService_HasError()
    {
        var item = new RecordRoomUtilityReadingItemRequest { ServiceId = Guid.NewGuid(), CurrentValue = 1m };

        var result = _validator.TestValidate(Request(ThisMonth) with { Items = [item, item] });

        result.ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_TooManyItems_HasError()
    {
        var items = Enumerable.Range(0, RecordRoomUtilityReadingsRequest.MaxItems + 1)
            .Select(_ => new RecordRoomUtilityReadingItemRequest { ServiceId = Guid.NewGuid(), CurrentValue = 1m })
            .ToList();

        _validator.TestValidate(Request(ThisMonth) with { Items = items }).ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_EmptyItems_HasError()
    {
        _validator.TestValidate(Request(ThisMonth) with { Items = [] }).ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Fact]
    public void Validate_PreviousValueGreaterThanCurrentValue_LeavesItToTheService()
    {
        // The service ignores PreviousValue once the meter has a reading, so only it can tell whether this is wrong
        var item = new RecordRoomUtilityReadingItemRequest { ServiceId = Guid.NewGuid(), CurrentValue = 10m, PreviousValue = 11m };

        var result = _validator.TestValidate(Request(ThisMonth) with { Items = [item] });

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_NegativeCurrentValue_HasItemError()
    {
        var item = new RecordRoomUtilityReadingItemRequest { ServiceId = Guid.NewGuid(), CurrentValue = -1m };

        var result = _validator.TestValidate(Request(ThisMonth) with { Items = [item] });

        result.ShouldHaveValidationErrorFor("Items[0].CurrentValue");
    }
}
