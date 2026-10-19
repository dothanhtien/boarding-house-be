using System.Net;

namespace BoardingHouse.Application.Exceptions;

public abstract class AppException(string message, HttpStatusCode statusCode) : Exception(message)
{
    public int StatusCode { get; } = (int)statusCode;
}

public class AppNotFoundException(string message) : AppException(message, HttpStatusCode.NotFound);

public class AppConflictException(string message) : AppException(message, HttpStatusCode.Conflict);

public class AppValidationException(string message) : AppException(message, HttpStatusCode.BadRequest);

public class AppUnauthorizedException(string message) : AppException(message, HttpStatusCode.Unauthorized);

public class AppForbiddenException(string message) : AppException(message, HttpStatusCode.Forbidden);

public class AppInternalException(string message) : AppException(message, HttpStatusCode.InternalServerError);

public class AppTooManyRequestsException(string message) : AppException(message, HttpStatusCode.TooManyRequests);
