using BoardingHouse.Api.Common;

namespace BoardingHouse.UnitTests.Common;

public class PageRequestTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(5, 5)]
    public void Page_Clamped_ReturnsExpected(int input, int expected)
    {
        var request = new PageRequest { Page = input };

        Assert.Equal(expected, request.Page);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(101, 100)]
    [InlineData(50, 50)]
    public void PageSize_Clamped_ReturnsExpected(int input, int expected)
    {
        var request = new PageRequest { PageSize = input };

        Assert.Equal(expected, request.PageSize);
    }
}
