using Microsoft.EntityFrameworkCore;
using Npgsql;
using BoardingHouse.Application.Exceptions;

namespace BoardingHouse.Infrastructure.Persistence;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueViolation(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static bool IsUniqueViolation(this DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == constraintName;

    public static PersistenceException ToPersistenceException(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            ? new UniqueConstraintViolationException(pg.ConstraintName, ex)
            : new PersistenceException("Saving changes to the database failed", ex);
}
