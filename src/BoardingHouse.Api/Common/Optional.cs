using System.Text.Json.Serialization;
using BoardingHouse.Api.Common.Json;

namespace BoardingHouse.Api.Common;

/// <summary>
/// Distinguishes "the client sent this field" (possibly as <c>null</c>) from "the client omitted this
/// field entirely", so PATCH requests can update a field to <c>null</c> without disturbing fields the
/// caller never mentioned. The converter is attached here (rather than registered on a specific
/// JsonSerializerOptions) so it applies wherever an Optional&lt;T&gt; is (de)serialized — including
/// HttpClient calls in tests that use the default options.
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>(T? value)
{
    public bool IsSet { get; } = true;
    public T? Value { get; } = value;

    /// <summary>
    /// Lets callers write <c>Name = "foo"</c> or <c>Name = null</c> in object initializers (tests,
    /// in-process code) the same way a client would send the field in JSON — both count as "set".
    /// A field left out of the initializer stays <c>default(Optional&lt;T&gt;)</c>, i.e. unset.
    /// </summary>
    public static implicit operator Optional<T>(T? value) => new(value);

    /// <summary>
    /// Applies <paramref name="apply"/> to <see cref="Value"/> only when the field was actually sent
    /// by the caller — the standard "if set, update the field" idiom for PATCH endpoints.
    /// </summary>
    public void ApplyIfSet(Action<T?> apply)
    {
        if (IsSet)
        {
            apply(Value);
        }
    }
}
