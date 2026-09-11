using BoardingHouse.Api.Common;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Extensions;
using BoardingHouse.Api.Services;
using Mapster;
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
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var (response, refreshToken, expiresAt) = await authService.LoginAsync(request, GetIpAddress(), GetUserAgent(), cancellationToken);

        Response.AppendRefreshTokenCookie(refreshToken, expiresAt);

        return Ok(new ApiResponse<AuthResponse> { Data = response });
    }

    [HttpPost("refresh-token")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh(CancellationToken cancellationToken)
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
                Response.DeleteRefreshTokenCookie();
            }

            return Task.CompletedTask;
        });

        var (response, newRefreshToken, expiresAt) = await authService.RefreshTokenAsync(refreshToken, GetIpAddress(), GetUserAgent(), cancellationToken);

        refreshSucceeded = true;
        Response.AppendRefreshTokenCookie(newRefreshToken, expiresAt);

        return Ok(new ApiResponse<AuthResponse> { Data = response });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.TryGetRefreshTokenCookie(out var refreshToken))
        {
            await authService.LogoutAsync(refreshToken, cancellationToken);
        }

        Response.DeleteRefreshTokenCookie();

        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<ApiResponse<UserResponse>> Me()
    {
        return Ok(new ApiResponse<UserResponse> { Data = currentUserAccessor.RequiredUser.Adapt<UserResponse>() });
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
