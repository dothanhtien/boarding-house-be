using BoardingHouse.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace BoardingHouse.UnitTests.Middleware;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoCorrelationIdHeader_GeneratesNewGuidInResponseHeader()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var headerValue = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.True(Guid.TryParse(headerValue, out _));
    }

    [Fact]
    public async Task InvokeAsync_ExistingCorrelationIdHeader_ReusesSameValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc-123";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("abc-123", context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_BlankCorrelationIdHeader_GeneratesNewGuidInResponseHeader(string headerValue)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = headerValue;
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var responseHeaderValue = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.True(Guid.TryParse(responseHeaderValue, out _));
    }

    [Fact]
    public async Task InvokeAsync_SetsCorrelationIdInHttpContextItems()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc-123";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("abc-123", context.Items["CorrelationId"]);
    }

    [Fact]
    public async Task InvokeAsync_CallsNextDelegate()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;
        var middleware = new CorrelationIdMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
