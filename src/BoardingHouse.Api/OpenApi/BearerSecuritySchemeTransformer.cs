using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BoardingHouse.Api.OpenApi;

public sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var schemes = await authenticationSchemeProvider.GetAllSchemesAsync();

        if (schemes.All(scheme => scheme.Name != JwtBearerDefaults.AuthenticationScheme)) return;

        const string schemeId = JwtBearerDefaults.AuthenticationScheme;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[schemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        };

        var securityRequirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(schemeId, document)] = []
        };

        var apiDescriptionsByKey = context.DescriptionGroups
            .SelectMany(group => group.Items)
            .ToLookup(description => (Path: NormalizePath(description.RelativePath), HttpMethod: description.HttpMethod));

        foreach (var (path, pathItem) in document.Paths)
        {
            foreach (var (httpMethod, operation) in pathItem.Operations!)
            {
                var apiDescription = apiDescriptionsByKey[(NormalizePath(path), httpMethod.Method)].FirstOrDefault();
                var requiresAuthorization = apiDescription?.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any() == true;

                operation.Security ??= [];

                if (requiresAuthorization)
                {
                    operation.Security.Add(securityRequirement);
                }
            }
        }
    }

    private static string NormalizePath(string? path) => path?.Trim('/').ToLowerInvariant() ?? string.Empty;
}
