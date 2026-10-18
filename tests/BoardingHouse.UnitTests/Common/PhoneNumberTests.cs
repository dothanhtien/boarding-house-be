using BoardingHouse.Api.Common;

namespace BoardingHouse.UnitTests.Common;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0912345678", "0912345678")]
    [InlineData("+84912345678", "0912345678")]
    [InlineData("+84 912.345.678", "0912345678")]
    [InlineData(" 0912-345-678 ", "0912345678")]
    [InlineData("(028) 3822 1234", "02838221234")]
    [InlineData("+14155550123", "+14155550123")]
    public void Normalize_StripsSeparatorsAndConvertsVietnamPrefix(string input, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Blank_ReturnsNull(string? input)
    {
        Assert.Null(PhoneNumber.Normalize(input));
    }

    [Theory]
    [InlineData("0912345678", true)]
    [InlineData("+84 912.345.678", true)]
    [InlineData("0912abc678", false)]
    [InlineData("09+12345678", false)]
    [InlineData("+", false)]
    [InlineData("٠٩١٢٣٤٥٦٧٨", false)]
    [InlineData("012345678901234567890", false)]
    [InlineData("", false)]
    public void IsValid_ChecksNormalizedForm(string input, bool expected)
    {
        Assert.Equal(expected, PhoneNumber.IsValid(input));
    }

    [Theory]
    [InlineData("0912345678", "091****678")]
    [InlineData("02838221234", "028*****234")]
    [InlineData("123456", "****56")]
    [InlineData("12", "*2")]
    public void Mask_HidesMiddleDigitsAndKeepsLength(string input, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Mask(input));
    }
}
