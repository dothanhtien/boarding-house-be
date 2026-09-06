using Microsoft.AspNetCore.Mvc;

namespace BoardingHouse.Api.Exceptions;

public static class ValidationProblemDetailsFactory
{
    public static IActionResult Create(ActionContext context)
    {
        const int statusCode = StatusCodes.Status400BadRequest;

        var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString();

        var problemDetails = new ValidationProblemDetails(context.ModelState)
        {
            Status = statusCode,
            Title = "One or more validation errors occurred.",
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = context.HttpContext.Request.Path
        };

        problemDetails.Extensions["correlationId"] = correlationId;

        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(GlobalExceptionHandler));
        logger.LogWarning("Handled exception ({CorrelationId}): {Message}", correlationId, problemDetails.Title);

        return new BadRequestObjectResult(problemDetails);
    }
}
