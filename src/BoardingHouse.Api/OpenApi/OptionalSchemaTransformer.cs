using System.Text.Json;
using System.Text.Json.Nodes;
using BoardingHouse.Api.Common;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BoardingHouse.Api.OpenApi;

/// <summary>
/// Without this, the generated schema for <see cref="Optional{T}"/> would expose its wrapper shape
/// (IsSet/Value) instead of the wrapped type, since it's an ordinary struct as far as reflection is
/// concerned. Rewrites it to look like a plain nullable, optional property of the wrapped type.
/// </summary>
public sealed class OptionalSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Optional<>))
        {
            return Task.CompletedTask;
        }

        // T itself may already be a nullable value type (e.g. Optional<int?>, used for DB-nullable
        // columns) — unwrap it so the switch below sees the underlying primitive/enum. A non-nullable
        // value type (e.g. Optional<int>) rejects a JSON null at deserialization time (see
        // OptionalJsonConverterFactory), so its schema must not advertise null as an accepted value.
        var genericArgument = type.GetGenericArguments()[0];
        var isNullable = !genericArgument.IsValueType || Nullable.GetUnderlyingType(genericArgument) is not null;
        var innerType = Nullable.GetUnderlyingType(genericArgument) ?? genericArgument;
        var nullFlag = isNullable ? JsonSchemaType.Null : default;

        schema.Properties?.Clear();
        schema.Required?.Clear();
        schema.AdditionalPropertiesAllowed = true;
        schema.AdditionalProperties = null;

        if (innerType.IsEnum)
        {
            schema.Type = JsonSchemaType.String | nullFlag;
            schema.Enum = Enum.GetNames(innerType)
                .Select(name => (JsonNode)JsonNamingPolicy.CamelCase.ConvertName(name))
                .ToList();
            return Task.CompletedTask;
        }

        if (innerType == typeof(string))
        {
            schema.Type = JsonSchemaType.String | nullFlag;
        }
        else if (innerType == typeof(bool))
        {
            schema.Type = JsonSchemaType.Boolean | nullFlag;
        }
        else if (innerType == typeof(int) || innerType == typeof(short) || innerType == typeof(byte))
        {
            schema.Type = JsonSchemaType.Integer | nullFlag;
        }
        else if (innerType == typeof(long))
        {
            schema.Type = JsonSchemaType.Integer | nullFlag;
            schema.Format = "int64";
        }
        else if (innerType == typeof(decimal) || innerType == typeof(double) || innerType == typeof(float))
        {
            schema.Type = JsonSchemaType.Number | nullFlag;
        }
        else if (innerType == typeof(Guid))
        {
            schema.Type = JsonSchemaType.String | nullFlag;
            schema.Format = "uuid";
        }
        else if (innerType == typeof(DateTime) || innerType == typeof(DateTimeOffset))
        {
            schema.Type = JsonSchemaType.String | nullFlag;
            schema.Format = "date-time";
        }
        else if (innerType == typeof(DateOnly))
        {
            schema.Type = JsonSchemaType.String | nullFlag;
            schema.Format = "date";
        }
        else if (innerType == typeof(TimeOnly))
        {
            schema.Type = JsonSchemaType.String | nullFlag;
            schema.Format = "time";
        }
        else
        {
            throw new NotSupportedException(
                $"{nameof(OptionalSchemaTransformer)} does not know how to render an OpenAPI schema for Optional<{innerType.Name}>. " +
                "Add a case for this type instead of letting it fall through silently.");
        }

        return Task.CompletedTask;
    }
}
