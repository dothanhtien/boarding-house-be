using BoardingHouse.Application.DTOs.Auth;

namespace BoardingHouse.Api.Extensions;

public static class AuthTokensCookieExtensions
{
    public static void AppendAuthTokenCookies(this HttpResponse response, AuthTokens tokens)
    {
        response.AppendAccessTokenCookie(tokens.AccessToken, tokens.AccessTokenExpiresAt);
        response.AppendRefreshTokenCookie(tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
    }
}
