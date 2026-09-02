namespace BoardingHouse.Api.Common.Exceptions;

public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class NotFoundAppException(string message)
    : AppException(message, StatusCodes.Status404NotFound);

public class ConflictAppException(string message)
    : AppException(message, StatusCodes.Status409Conflict);

public class ValidationAppException(string message)
    : AppException(message, StatusCodes.Status400BadRequest);

public class UnauthorizedAppException(string message)
    : AppException(message, StatusCodes.Status401Unauthorized);

public class ForbiddenAppException(string message)
    : AppException(message, StatusCodes.Status403Forbidden);
