namespace BoardingHouse.Application.Exceptions;

/// <summary>
/// A database write failed. Thrown by <see cref="Persistence.IUnitOfWork"/> in place of the EF Core exception so
/// handlers can react to write failures without referencing EF Core; the original exception is kept as
/// <see cref="Exception.InnerException"/>.
/// </summary>
public class PersistenceException(string message, Exception innerException) : Exception(message, innerException);

/// <summary>
/// A unique constraint or unique index was violated. <see cref="ConstraintName"/> is the database name of the
/// index (compare it against <see cref="Persistence.UniqueIndexNames"/>).
/// </summary>
public sealed class UniqueConstraintViolationException(string? constraintName, Exception innerException)
    : PersistenceException($"Unique constraint '{constraintName}' was violated", innerException)
{
    public string? ConstraintName { get; } = constraintName;
}
