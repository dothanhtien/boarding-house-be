using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BoardingHouse.Api.OpenApi;

public sealed class CamelCaseParameterNameTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        if (operation.Parameters is null) return Task.CompletedTask;

        foreach (var parameter in operation.Parameters)
        {
            if (parameter is OpenApiParameter { Name: { } name } openApiParameter)
            {
                openApiParameter.Name = JsonNamingPolicy.CamelCase.ConvertName(name);
            }
        }

        return Task.CompletedTask;
    }
}
