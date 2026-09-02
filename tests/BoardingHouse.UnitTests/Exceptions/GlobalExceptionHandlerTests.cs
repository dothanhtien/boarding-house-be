using System.Text.Json;
using BoardingHouse.Api.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BoardingHouse.UnitTests.Exceptions;

public class GlobalExceptionHandlerTests
{
    private static (DefaultHttpContext context, MemoryStream body) CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;
        return (context, body);
    }

    private static JsonElement ReadBodyAsJson(MemoryStream body)
    {
        body.Seek(0, SeekOrigin.Begin);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static GlobalExceptionHandler CreateHandler(string environmentName)
    {
        var env = new FakeHostEnvironment(environmentName);
        return new GlobalExceptionHandler(env, NullLogger<GlobalExceptionHandler>.Instance);
    }

    [Fact]
    public async Task TryHandleAsync_NotFoundAppException_Returns404WithMessageAndCorrelationId()
    {
        var handler = CreateHandler(Environments.Production);
        var (context, body) = CreateHttpContext();
        context.Items["CorrelationId"] = "test-correlation-id";

        var handled = await handler.TryHandleAsync(context, new NotFoundAppException("User not found"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);

        var json = ReadBodyAsJson(body);
        Assert.Equal("User not found", json.GetProperty("title").GetString());
        Assert.Equal("test-correlation-id", json.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_ConflictAppException_Returns409WithMessage()
    {
        var handler = CreateHandler(Environments.Production);
        var (context, body) = CreateHttpContext();

        var handled = await handler.TryHandleAsync(context, new ConflictAppException("Email already exists"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);

        var json = ReadBodyAsJson(body);
        Assert.Equal("Email already exists", json.GetProperty("title").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_UnknownException_Returns500WithGenericMessage()
    {
        var handler = CreateHandler(Environments.Production);
        var (context, body) = CreateHttpContext();

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("secret detail"), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        var json = ReadBodyAsJson(body);
        var title = json.GetProperty("title").GetString();
        Assert.NotEqual("secret detail", title);
        Assert.DoesNotContain("secret detail", title);
    }

    [Fact]
    public async Task TryHandleAsync_UnknownException_InDevelopment_IncludesExceptionField()
    {
        var handler = CreateHandler(Environments.Development);
        var (context, body) = CreateHttpContext();

        await handler.TryHandleAsync(context, new InvalidOperationException("secret detail"), CancellationToken.None);

        var json = ReadBodyAsJson(body);
        Assert.True(json.TryGetProperty("exception", out var exceptionField));
        Assert.Contains("secret detail", exceptionField.GetString());
    }

    [Fact]
    public async Task TryHandleAsync_UnknownException_InProduction_DoesNotIncludeExceptionField()
    {
        var handler = CreateHandler(Environments.Production);
        var (context, body) = CreateHttpContext();

        await handler.TryHandleAsync(context, new InvalidOperationException("secret detail"), CancellationToken.None);

        var json = ReadBodyAsJson(body);
        Assert.False(json.TryGetProperty("exception", out _));
    }

    private class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "BoardingHouse.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
