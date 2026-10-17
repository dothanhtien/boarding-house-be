using BoardingHouse.Api.Services.Throttling;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace BoardingHouse.UnitTests.Services.Throttling;

public class DistributedAttemptLimiterTests
{
    private static DistributedAttemptLimiter CreateSut(IDistributedCache? cache = null) =>
        new(cache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<DistributedAttemptLimiter>.Instance);

    [Fact]
    public async Task TryAcquireAsync_UpToLimit_AllowsThenRejects()
    {
        var sut = CreateSut();

        Assert.True(await sut.TryAcquireAsync("k", 2, TimeSpan.FromMinutes(1)));
        Assert.True(await sut.TryAcquireAsync("k", 2, TimeSpan.FromMinutes(1)));
        Assert.False(await sut.TryAcquireAsync("k", 2, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_CountsKeysSeparately()
    {
        var sut = CreateSut();
        await sut.TryAcquireAsync("a", 1, TimeSpan.FromMinutes(1));

        Assert.True(await sut.TryAcquireAsync("b", 1, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task TryAcquireAsync_WindowElapsed_StartsNewWindow()
    {
        var sut = CreateSut();
        await sut.TryAcquireAsync("k", 1, TimeSpan.FromMilliseconds(50));

        await Task.Delay(100);

        Assert.True(await sut.TryAcquireAsync("k", 1, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public async Task TryAcquireAsync_CacheUnavailable_FailsOpen()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        Assert.True(await CreateSut(cache.Object).TryAcquireAsync("k", 1, TimeSpan.FromMinutes(1)));
    }
}
