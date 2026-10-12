using System.Text.Json;
using System.Text.Json.Serialization;
using BoardingHouse.Api.Common.ModelBinding;
using BoardingHouse.Api.Exceptions;
using BoardingHouse.Api.OpenApi;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BoardingHouse.Api.Extensions;

public static class ApiServiceCollectionExtensions
{
    public const string FrontendCorsPolicy = "Frontend";

    public static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services.AddControllers(options =>
            {
                options.ModelBinderProviders.Insert(0, new OptionalModelBinderProvider());

                var formFactory = options.ValueProviderFactories.OfType<FormValueProviderFactory>().Single();
                options.ValueProviderFactories[options.ValueProviderFactories.IndexOf(formFactory)] =
                    new InvariantCultureFormValueProviderFactory();
            })
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = ValidationProblemDetailsFactory.Create;
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

        services.AddFluentValidationAutoValidation();

        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }

    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Boarding House API";
                document.Info.Version = "v1";
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<CamelCaseParameterNameTransformer>();
            options.AddSchemaTransformer<OptionalSchemaTransformer>();
        });

        return services;
    }

    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCors(options =>
        {
            options.AddPolicy(FrontendCorsPolicy, policy =>
            {
                policy.WithOrigins(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return services;
    }
}
