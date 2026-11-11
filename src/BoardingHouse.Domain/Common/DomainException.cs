namespace BoardingHouse.Domain.Common;

public enum DomainErrorKind
{
    Validation,
    Conflict,
    NotFound
}

public class DomainException(string message, DomainErrorKind kind = DomainErrorKind.Validation, string? field = null)
    : Exception(message)
{
    public DomainErrorKind Kind { get; } = kind;

    public string? Field { get; } = field;

    public static DomainException Conflict(string message) => new(message, DomainErrorKind.Conflict);

    public static DomainException NotFound(string message) => new(message, DomainErrorKind.NotFound);
}
