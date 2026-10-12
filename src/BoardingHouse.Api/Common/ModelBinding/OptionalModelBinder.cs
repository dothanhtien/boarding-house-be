using System.ComponentModel;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BoardingHouse.Api.Common.ModelBinding;

/// <summary>
/// Binds an <see cref="Optional{T}"/> from a non-JSON source (form, query, route) with the same PATCH semantics
/// as the JSON converter: a missing field stays unset, a field sent empty (<c>name=</c>) is set to <c>null</c>.
/// The built-in <c>SimpleTypeModelBinder</c> can't express this: it turns an empty value into a <c>null</c>
/// model without calling the type converter, which for a struct like <see cref="Optional{T}"/> becomes a
/// "value is invalid" error instead of "cleared".
/// </summary>
public sealed class OptionalModelBinder<T> : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        // Field not present at all → leave Result unset so the property keeps default(Optional<T>), i.e. unset.
        if (result == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);

        // Empty string means "clear the field": OptionalTypeConverter maps it to Optional<T>(null), or throws
        // FormatException when T is a non-nullable value type. Parsing is culture-invariant, matching
        // InvariantCultureFormValueProviderFactory and JSON bodies.
        var raw = result.FirstValue ?? string.Empty;
        try
        {
            var value = (Optional<T>)TypeDescriptor.GetConverter(typeof(Optional<T>)).ConvertFromInvariantString(raw)!;
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        catch (Exception exception) when (exception is FormatException or NotSupportedException or ArgumentException)
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName, $"The value '{raw}' is not valid for {bindingContext.ModelMetadata.Name}.");
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Picks <see cref="OptionalModelBinder{T}"/> for every <see cref="Optional{T}"/> whose inner type can be parsed
/// from a string. Inserted at index 0 in <c>AddApiControllers</c> so it wins over the built-in providers.
/// </summary>
public sealed class OptionalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var type = context.Metadata.ModelType;
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Optional<>))
        {
            return null;
        }

        var innerType = type.GetGenericArguments()[0];
        if (!OptionalTypeConverter.SupportsString(innerType))
        {
            return null;
        }

        // String-based sources only; [FromBody] goes through OptionalJsonConverterFactory instead. A null or
        // ModelBinding source (no explicit [From*] attribute) is accepted too.
        var source = context.BindingInfo.BindingSource;
        if (source is not null
            && source != BindingSource.Form
            && source != BindingSource.Query
            && source != BindingSource.Path
            && source != BindingSource.ModelBinding)
        {
            return null;
        }

        var binderType = typeof(OptionalModelBinder<>).MakeGenericType(innerType);
        return (IModelBinder)Activator.CreateInstance(binderType)!;
    }
}
