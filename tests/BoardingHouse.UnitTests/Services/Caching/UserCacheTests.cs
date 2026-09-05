using System.Text;
using System.Text.Json;
using BoardingHouse.Api.Entities;
using BoardingHouse.Api.Services.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BoardingHouse.UnitTests.Services.Caching;

public class UserCacheTests
{
    private readonly Mock<IDistributedCache> _cache = new();

    private static IConfiguration ConfigurationWithTtl(int? ttlSeconds = null)
    {
        var values = new Dictionary<string, string?>();
        if (ttlSeconds is not null)
        {
            values["Redis:UserCacheTtlSeconds"] = ttlSeconds.Value.ToString();
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private UserCache CreateSut(IConfiguration? configuration = null) =>
        new(_cache.Object, configuration ?? ConfigurationWithTtl(), NullLogger<UserCache>.Instance);

    private static User NewUser() => new()
    {
        Id = Guid.NewGuid(),
        Email = "user@test.com",
        PasswordHash = "super-secret-hash",
        FullName = "Test User",
        IsActive = true
    };

    [Fact]
    public async Task GetAsync_CacheMiss_ReturnsNull()
    {
        _cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await CreateSut().GetAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_CacheHit_ReturnsDeserializedUser()
    {
        var user = NewUser();
        var json = JsonSerializer.Serialize(user);
        _cache.Setup(c => c.GetAsync($"user:{user.Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes(json));

        var result = await CreateSut().GetAsync(user.Id);

        Assert.NotNull(result);
        Assert.Equal(user.Id, result!.Id);
        Assert.Equal(user.Email, result.Email);
    }

    [Fact]
    public async Task GetAsync_CorruptedJson_ReturnsNullAndEvictsEntry()
    {
        var userId = Guid.NewGuid();
        _cache.Setup(c => c.GetAsync($"user:{userId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("{ not-valid-json"));
        _cache.Setup(c => c.RemoveAsync($"user:{userId}", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateSut().GetAsync(userId);

        Assert.Null(result);
        _cache.Verify(c => c.RemoveAsync($"user:{userId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetAsync_NeverCachesPasswordHash()
    {
        byte[]? capturedBytes = null;
        _cache.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, bytes, _, _) => capturedBytes = bytes)
            .Returns(Task.CompletedTask);

        await CreateSut().SetAsync(NewUser());

        Assert.NotNull(capturedBytes);
        var cached = JsonSerializer.Deserialize<User>(Encoding.UTF8.GetString(capturedBytes!));
        Assert.Equal(string.Empty, cached!.PasswordHash);
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

        await CreateSut(ConfigurationWithTtl(120)).SetAsync(NewUser());

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

        await CreateSut(ConfigurationWithTtl()).SetAsync(NewUser());

        Assert.Equal(TimeSpan.FromSeconds(30), capturedOptions!.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task InvalidateAsync_RemovesEntryByUserIdKey()
    {
        var userId = Guid.NewGuid();
        _cache.Setup(c => c.RemoveAsync($"user:{userId}", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await CreateSut().InvalidateAsync(userId);

        _cache.Verify(c => c.RemoveAsync($"user:{userId}", It.IsAny<CancellationToken>()), Times.Once);
    }
}
