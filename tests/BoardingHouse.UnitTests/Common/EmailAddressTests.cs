using BoardingHouse.Application.Common;

namespace BoardingHouse.UnitTests.Common;

public class EmailAddressTests
{
    [Theory]
    [InlineData(" User@Test.COM ", "user@test.com")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_TrimsAndLowercases(string? input, string? expected)
    {
        Assert.Equal(expected, EmailAddress.Normalize(input));
    }

    [Theory]
    [InlineData("nguyenvana@gmail.com", "ng***@gmail.com")]
    [InlineData("ab@test.com", "a***@test.com")]
    [InlineData("a@test.com", "a***@test.com")]
    public void Mask_KeepsDomainAndFixedLengthPrefix(string input, string expected)
    {
        Assert.Equal(expected, EmailAddress.Mask(input));
    }
}
