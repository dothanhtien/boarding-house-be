using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
            AppException appEx => (appEx.StatusCode, appEx.Message),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
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
}
