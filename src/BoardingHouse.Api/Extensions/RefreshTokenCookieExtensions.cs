namespace BoardingHouse.Api.Extensions;

public static class RefreshTokenCookieExtensions
{
    private const string CookieName = "refreshToken";

    public static void AppendRefreshTokenCookie(this HttpResponse response, string refreshToken, DateTimeOffset expiresAtUtc)
    {
        response.Cookies.Append(CookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAtUtc,
            Path = "/api/auth"
        });
    }

    public static void DeleteRefreshTokenCookie(this HttpResponse response)
    {
        response.Cookies.Delete(CookieName, new CookieOptions { Path = "/api/auth" });
    }

    public static bool TryGetRefreshTokenCookie(this HttpRequest request, out string refreshToken) =>
        request.Cookies.TryGetValue(CookieName, out refreshToken!);
}
