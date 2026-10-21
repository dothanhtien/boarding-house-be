using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using BoardingHouse.Application.Exceptions;

namespace BoardingHouse.Api.Exceptions;

public class GlobalExceptionHandler(IHostEnvironment env, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            AppException appEx => (ToStatusCode(appEx.Kind), appEx.Message),
            UniqueConstraintViolationException =>
                (StatusCodes.Status409Conflict, "A record with the same value already exists"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        var correlationId = httpContext.Items["CorrelationId"]?.ToString();

        if (statusCode >= 500)
        {
            logger.LogError(exception, "Unhandled exception ({CorrelationId})", correlationId);
        }
        else
        {
            logger.LogWarning("Handled exception ({CorrelationId}): {Message}", correlationId, exception.Message);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["correlationId"] = correlationId;

        if (env.IsDevelopment() && statusCode >= 500)
        {
            problemDetails.Extensions["exception"] = exception.ToString();
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static int ToStatusCode(AppErrorKind kind) => kind switch
    {
        AppErrorKind.NotFound => StatusCodes.Status404NotFound,
        AppErrorKind.Conflict => StatusCodes.Status409Conflict,
        AppErrorKind.Validation => StatusCodes.Status400BadRequest,
        AppErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        AppErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        AppErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError
    };
}
