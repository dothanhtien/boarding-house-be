using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BoardingHouse.Api.Exceptions;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueViolation(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
