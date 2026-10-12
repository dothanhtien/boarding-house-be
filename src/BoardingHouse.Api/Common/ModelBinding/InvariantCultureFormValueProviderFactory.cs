using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BoardingHouse.Api.Common.ModelBinding;

/// <summary>
/// Replaces the built-in <see cref="FormValueProviderFactory"/> (swapped in by <c>AddApiControllers</c>) so form
/// fields are parsed with <see cref="CultureInfo.InvariantCulture"/> instead of the server's current culture.
/// The built-in factory uses <see cref="CultureInfo.CurrentCulture"/>, so a multipart value like <c>"1.5"</c> could
/// bind differently depending on where the API runs (e.g. <c>vi-VN</c> uses <c>,</c> as the decimal separator) —
/// and JSON bodies are always culture-invariant, so form bodies should be too.
/// </summary>
public sealed class InvariantCultureFormValueProviderFactory : IValueProviderFactory
{
    public Task CreateValueProviderAsync(ValueProviderFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.ActionContext.HttpContext.Request.HasFormContentType
            ? AddValueProviderAsync(context)
            : Task.CompletedTask;
    }

    private static async Task AddValueProviderAsync(ValueProviderFactoryContext context)
    {
        var request = context.ActionContext.HttpContext.Request;

        // Same error handling as the built-in factory: a malformed or truncated body surfaces as a
        // ValueProviderException, which MVC turns into a 400 rather than an unhandled 500.
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(context.ActionContext.HttpContext.RequestAborted);
        }
        catch (InvalidDataException exception)
        {
            throw new ValueProviderException(exception.Message, exception);
        }
        catch (IOException exception)
        {
            throw new ValueProviderException(exception.Message, exception);
        }

        context.ValueProviders.Add(new FormValueProvider(BindingSource.Form, form, CultureInfo.InvariantCulture));
    }
}
