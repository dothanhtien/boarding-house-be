using System.Net;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Controllers;
using BoardingHouse.Api.DTOs.Auth;
using BoardingHouse.Api.DTOs.Users;
using BoardingHouse.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace BoardingHouse.UnitTests.Controllers;

public class AuthControllerTests
{
    private readonly Mock<IAuthService> _authService = new();
    private readonly Mock<ICurrentUserAccessor> _currentUserAccessor = new();
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        _controller = new AuthController(_authService.Object, _currentUserAccessor.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var user = new UserResponse
        {
            Id = Guid.NewGuid(),
            Email = "user@test.com",
            FullName = "Test User",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _authService
            .Setup(s => s.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((user, "access-token", DateTimeOffset.UtcNow.AddMinutes(15), "refresh-token", DateTimeOffset.UtcNow.AddDays(7)));
    }

    [Fact]
    public async Task Login_IPv4MappedToIPv6RemoteAddress_PassesUnmappedIPv4ToAuthService()
    {
        _controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.168.1.10");

        await _controller.Login(new LoginRequest { Email = "user@test.com", Password = "password" }, CancellationToken.None);

        _authService.Verify(s => s.LoginAsync(
            It.IsAny<LoginRequest>(),
            "192.168.1.10",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_PlainIPv4RemoteAddress_PassesAddressUnchanged()
    {
        _controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");

        await _controller.Login(new LoginRequest { Email = "user@test.com", Password = "password" }, CancellationToken.None);

        _authService.Verify(s => s.LoginAsync(
            It.IsAny<LoginRequest>(),
            "203.0.113.5",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_NoRemoteAddress_PassesNullIpAddress()
    {
        _controller.HttpContext.Connection.RemoteIpAddress = null;

        await _controller.Login(new LoginRequest { Email = "user@test.com", Password = "password" }, CancellationToken.None);

        _authService.Verify(s => s.LoginAsync(
            It.IsAny<LoginRequest>(),
            null,
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
