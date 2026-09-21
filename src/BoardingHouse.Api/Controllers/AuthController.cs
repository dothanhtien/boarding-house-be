using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, ICurrentUserAccessor currentUserAccessor) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<UserResponse>>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.RegisterAsync(request, cancellationToken);
        return Ok(new ApiResponse<UserResponse> { Data = response });
    }

    [HttpPost("login")]
    public async Task<ActionResult<ApiResponse<UserResponse>>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var (user, accessToken, accessTokenExpiresAt, refreshToken, refreshTokenExpiresAt) =
            await authService.LoginAsync(request, GetIpAddress(), GetUserAgent(), cancellationToken);

        Response.AppendAccessTokenCookie(accessToken, accessTokenExpiresAt);
        Response.AppendRefreshTokenCookie(refreshToken, refreshTokenExpiresAt);

        return Ok(new ApiResponse<UserResponse> { Data = user });
    }

    [HttpPost("refresh-token")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.TryGetRefreshTokenCookie(out var refreshToken))
        {
            return Unauthorized();
        }

        var refreshSucceeded = false;
        Response.OnStarting(() =>
        {
            if (!refreshSucceeded)
            {
                Response.DeleteAccessTokenCookie();
                Response.DeleteRefreshTokenCookie();
            }

            return Task.CompletedTask;
        });

        var (accessToken, accessTokenExpiresAt, newRefreshToken, refreshTokenExpiresAt) =
            await authService.RefreshTokenAsync(refreshToken, GetIpAddress(), GetUserAgent(), cancellationToken);

        refreshSucceeded = true;
        Response.AppendAccessTokenCookie(accessToken, accessTokenExpiresAt);
        Response.AppendRefreshTokenCookie(newRefreshToken, refreshTokenExpiresAt);

        return NoContent();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.TryGetRefreshTokenCookie(out var refreshToken))
        {
            await authService.LogoutAsync(refreshToken, cancellationToken);
        }

        Response.DeleteAccessTokenCookie();
        Response.DeleteRefreshTokenCookie();

        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserResponse>>> Me(CancellationToken cancellationToken)
    {
        var response = await authService.GetCurrentUserAsync(currentUserAccessor.RequiredUser.Id, cancellationToken);
        return Ok(new ApiResponse<UserResponse> { Data = response });
    }

    private string? GetIpAddress()
    {
        var ip = HttpContext.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return null;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        return ip.ToString();
    }
    private string? GetUserAgent() => Request.Headers.UserAgent.ToString();
}
