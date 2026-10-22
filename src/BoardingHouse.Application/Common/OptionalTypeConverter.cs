using System.ComponentModel;
using System.Globalization;

namespace BoardingHouse.Application.Common;

/// <summary>
/// String → <see cref="Optional{T}"/> converter, attached to <see cref="Optional{T}"/> via
/// <c>[TypeConverter]</c> and used by <see cref="OptionalModelBinder{T}"/> for form/query/route values.
/// Any string it is given counts as "sent", so the result is always set: an empty string becomes
/// <c>Optional&lt;T&gt;(null)</c>, anything else is parsed by <c>T</c>'s own converter.
/// TypeDescriptor passes the closed <c>Optional&lt;T&gt;</c> type to the constructor, which is how one
/// non-generic converter serves every <c>T</c>.
/// </summary>
public sealed class OptionalTypeConverter(Type optionalType) : TypeConverter
{
    private readonly Type _innerType = optionalType.GetGenericArguments()[0];

    /// <summary>
    /// Whether a value of <paramref name="innerType"/> can be parsed from a string. Also used by
    /// <see cref="OptionalModelBinderProvider"/> to skip <c>Optional&lt;T&gt;</c> types it couldn't bind.
    /// </summary>
    public static bool SupportsString(Type innerType) =>
        innerType == typeof(string) || TypeDescriptor.GetConverter(innerType).CanConvertFrom(typeof(string));

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) ? SupportsString(_innerType) : base.CanConvertFrom(context, sourceType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text)
        {
            return base.ConvertFrom(context, culture, value);
        }

        // Empty string = "clear the field". Only possible when T can hold null; for a non-nullable value type
        // (int, Guid, enum...) there is nothing to clear to, so it is a format error the binder reports as 400.
        object? inner;
        if (text.Length == 0)
        {
            if (_innerType.IsValueType && Nullable.GetUnderlyingType(_innerType) is null)
            {
                throw new FormatException($"An empty value is not valid for {_innerType.Name}.");
            }

            inner = null;
        }
        else
        {
            inner = TypeDescriptor.GetConverter(_innerType).ConvertFrom(context, culture, text);
        }

        // Goes through the Optional<T>(T? value) constructor, so IsSet is true even when inner is null.
        return Activator.CreateInstance(typeof(Optional<>).MakeGenericType(_innerType), inner);
    }
}
