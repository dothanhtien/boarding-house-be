using System.IdentityModel.Tokens.Jwt;
using System.Text;
using BoardingHouse.Api.Common;
using BoardingHouse.Api.Repositories;
using BoardingHouse.Api.Services.Caching;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace BoardingHouse.Api.Extensions;

public static class JwtAuthenticationExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // JwtSecurityTokenHandler maps the "sub" claim to ClaimTypes.NameIdentifier (a long URI)
                // by default — disable that so FindFirst(JwtRegisteredClaimNames.Sub) in OnTokenValidatedAsync
                // reads back the original "sub" claim set by TokenService.GenerateAccessToken.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(configuration["Jwt:Secret"]!)),
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = OnTokenValidatedAsync,
                    OnChallenge = context =>
                    {
                        // Prevent the default handler from also writing the WWW-Authenticate header
                        // after we've already written a JSON body below.
                        context.HandleResponse();

                        return WriteProblemDetailsAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "Bearer token is missing or invalid");
                    },
                    OnForbidden = context => WriteProblemDetailsAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "You do not have permission to access this resource")
                };
            });

        return services;
    }

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var userIdClaim = context.Principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (userIdClaim is null || !Guid.TryParse(userIdClaim, out var userId))
        {
            context.Fail("Token is invalid");
            return;
        }

        var services = context.HttpContext.RequestServices;
        var userCache = services.GetRequiredService<IUserCache>();
        var cancellationToken = context.HttpContext.RequestAborted;

        var user = await userCache.GetAsync(userId, cancellationToken);

        if (user is null)
        {
            var userRepository = services.GetRequiredService<IUserRepository>();
            user = await userRepository.GetByIdAsync(userId, cancellationToken);

            if (user is null || !user.IsActive)
            {
                context.Fail("Account does not exist or has been disabled");
                return;
            }

            await userCache.SetAsync(user, cancellationToken);
        }

        services.GetRequiredService<ICurrentUserAccessor>().User = user;
    }

    private static async Task WriteProblemDetailsAsync(HttpContext httpContext, int statusCode, string title)
    {
        var correlationId = httpContext.Items["CorrelationId"]?.ToString();

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["correlationId"] = correlationId;

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        await httpContext.Response.WriteAsJsonAsync(problemDetails);
    }
}
