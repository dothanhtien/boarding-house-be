namespace BoardingHouse.Domain.Common;

public readonly struct Change<T>(T? value)
{
    public bool IsSet { get; } = true;
    public T? Value { get; } = value;

    public static implicit operator Change<T>(T? value) => new(value);

    public T? OrKeep(T? current) => IsSet ? Value : current;
}
