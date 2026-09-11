namespace BoardingHouse.Api.Extensions;

public static class AccessTokenCookieExtensions
{
    private const string CookieName = "accessToken";
    private const string Path = "/";

    public static void AppendAccessTokenCookie(this HttpResponse response, string accessToken, DateTimeOffset expiresAt)
    {
        response.Cookies.Append(CookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt,
            Path = Path
        });
    }

    public static void DeleteAccessTokenCookie(this HttpResponse response)
    {
        response.Cookies.Delete(CookieName, new CookieOptions { Path = Path });
    }

    public static bool TryGetAccessTokenCookie(this HttpRequest request, out string accessToken) =>
        request.Cookies.TryGetValue(CookieName, out accessToken!);
}
