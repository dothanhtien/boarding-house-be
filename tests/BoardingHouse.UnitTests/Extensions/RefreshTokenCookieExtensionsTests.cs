using BoardingHouse.Api.Extensions;
using Microsoft.AspNetCore.Http;

namespace BoardingHouse.UnitTests.Extensions;

public class RefreshTokenCookieExtensionsTests
{
    [Fact]
    public void AppendRefreshTokenCookie_SetsCookieWithExpectedNameAndAttributes()
    {
        var context = new DefaultHttpContext();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        context.Response.AppendRefreshTokenCookie("the-refresh-token", expiresAt);

        var setCookieHeader = context.Response.Headers.SetCookie.Single();
        Assert.StartsWith("refreshToken=the-refresh-token", setCookieHeader, StringComparison.Ordinal);
        Assert.Contains("path=/api/auth", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expiresAt.ToString("R"), setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeleteRefreshTokenCookie_SetsExpiredCookieForSamePath()
    {
        var context = new DefaultHttpContext();

        context.Response.DeleteRefreshTokenCookie();

        var setCookieHeader = context.Response.Headers.SetCookie.Single();
        Assert.StartsWith("refreshToken=", setCookieHeader, StringComparison.Ordinal);
        Assert.Contains("path=/api/auth", setCookieHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("01 Jan 1970", setCookieHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryGetRefreshTokenCookie_CookiePresent_ReturnsTrueAndValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Append("Cookie", "refreshToken=some-value");

        var found = context.Request.TryGetRefreshTokenCookie(out var refreshToken);

        Assert.True(found);
        Assert.Equal("some-value", refreshToken);
    }

    [Fact]
    public void TryGetRefreshTokenCookie_CookieMissing_ReturnsFalse()
    {
        var context = new DefaultHttpContext();

        var found = context.Request.TryGetRefreshTokenCookie(out var refreshToken);

        Assert.False(found);
        Assert.Null(refreshToken);
    }
}
