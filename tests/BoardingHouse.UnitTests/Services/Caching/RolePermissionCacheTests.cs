using System.Text;
using System.Text.Json;
using BoardingHouse.Api.Services.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardingHouse.UnitTests.Services.Caching;

public class RolePermissionCacheTests
{
    private readonly Mock<IDistributedCache> _cache = new();

    private static IConfiguration ConfigurationWithTtl(int? ttlSeconds = null)
    {
        var values = new Dictionary<string, string?>();
        if (ttlSeconds is not null)
        {
            values["Redis:RolePermissionsCacheTtlSeconds"] = ttlSeconds.Value.ToString();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private RolePermissionCache CreateSut(IConfiguration? configuration = null) =>
        new(_cache.Object, configuration ?? ConfigurationWithTtl(), NullLogger<RolePermissionCache>.Instance);

    [Fact]
    public async Task GetAsync_CacheMiss_ReturnsNull()
    {
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await CreateSut().GetAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_CacheHit_ReturnsDeserializedPermissions()
    {
        var roleId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new List<string[]> { new[] { "user", "read" }, new[] { "user", "write" } });
        _cache.Setup(c => c.GetAsync($"role-permissions:{roleId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(json));

        var result = await CreateSut().GetAsync(roleId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Contains(("user", "read"), result);
        Assert.Contains(("user", "write"), result);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsPermissions_WhenResourceContainsColon()
    {
        var roleId = Guid.NewGuid();
        byte[]? capturedBytes = null;
        _cache.Setup(c => c.SetAsync(
                $"role-permissions:{roleId}",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, bytes, _, _) => capturedBytes = bytes)
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.GetAsync($"role-permissions:{roleId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => capturedBytes);

        var sut = CreateSut();
        await sut.SetAsync(roleId, [("tenant:billing", "read")]);
        var result = await sut.GetAsync(roleId);

        Assert.NotNull(result);
        Assert.Contains(("tenant:billing", "read"), result!);
    }

    [Fact]
    public async Task GetAsync_CorruptedJson_ReturnsNullAndEvictsEntry()
    {
        var roleId = Guid.NewGuid();
        _cache.Setup(c => c.GetAsync($"role-permissions:{roleId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("{ not-valid-json"));
        _cache.Setup(c => c.RemoveAsync($"role-permissions:{roleId}", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().GetAsync(roleId);

        Assert.Null(result);
        _cache.Verify(c => c.RemoveAsync($"role-permissions:{roleId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_UsesConfiguredTtl()
    {
        DistributedCacheEntryOptions? capturedOptions = null;
        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, _, options, _) => capturedOptions = options)
            .Returns(Task.CompletedTask);

        await CreateSut(ConfigurationWithTtl(120)).SetAsync(Guid.NewGuid(), [("user", "read")]);

        Assert.Equal(TimeSpan.FromSeconds(120), capturedOptions!.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task SetAsync_NoConfiguredTtl_FallsBackToDefault()
    {
        DistributedCacheEntryOptions? capturedOptions = null;
        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, _, options, _) => capturedOptions = options)
            .Returns(Task.CompletedTask);

        await CreateSut(ConfigurationWithTtl()).SetAsync(Guid.NewGuid(), [("user", "read")]);

        Assert.Equal(TimeSpan.FromSeconds(300), capturedOptions!.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsPermissions()
    {
        var roleId = Guid.NewGuid();
        byte[]? capturedBytes = null;
        _cache.Setup(c => c.SetAsync(
                $"role-permissions:{roleId}",
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, bytes, _, _) => capturedBytes = bytes)
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.GetAsync($"role-permissions:{roleId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => capturedBytes);

        var sut = CreateSut();
        await sut.SetAsync(roleId, [("user", "read"), ("user", "write")]);
        var result = await sut.GetAsync(roleId);

        Assert.NotNull(result);
        Assert.Contains(("user", "read"), result!);
        Assert.Contains(("user", "write"), result);
    }

    [Fact]
    public async Task InvalidateAsync_RemovesEntryByRoleIdKey()
    {
        var roleId = Guid.NewGuid();
        _cache.Setup(c => c.RemoveAsync($"role-permissions:{roleId}", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateSut().InvalidateAsync(roleId);

        _cache.Verify(c => c.RemoveAsync($"role-permissions:{roleId}", It.IsAny<CancellationToken>()), Times.Once);
    }
}
