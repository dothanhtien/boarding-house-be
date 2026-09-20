namespace BoardingHouse.Api.Exceptions;

public abstract class AppException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public class AppNotFoundException(string message)
    : AppException(message, StatusCodes.Status404NotFound);

public class AppConflictException(string message)
    : AppException(message, StatusCodes.Status409Conflict);

public class AppValidationException(string message)
    : AppException(message, StatusCodes.Status400BadRequest);

public class AppUnauthorizedException(string message)
    : AppException(message, StatusCodes.Status401Unauthorized);

public class AppForbiddenException(string message)
    : AppException(message, StatusCodes.Status403Forbidden);

public class AppInternalException(string message)
    : AppException(message, StatusCodes.Status500InternalServerError);
