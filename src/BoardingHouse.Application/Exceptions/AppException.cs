namespace BoardingHouse.Application.Exceptions;

public enum AppErrorKind
{
    NotFound,
    Conflict,
    Validation,
    Unauthorized,
    Forbidden,
    Internal,
    TooManyRequests
}

public abstract class AppException(string message, AppErrorKind kind) : Exception(message)
{
    public AppErrorKind Kind { get; } = kind;
}

public class AppNotFoundException(string message) : AppException(message, AppErrorKind.NotFound);

public class AppConflictException(string message) : AppException(message, AppErrorKind.Conflict);

public class AppValidationException(string message) : AppException(message, AppErrorKind.Validation);

public class AppUnauthorizedException(string message) : AppException(message, AppErrorKind.Unauthorized);

public class AppForbiddenException(string message) : AppException(message, AppErrorKind.Forbidden);

public class AppInternalException(string message) : AppException(message, AppErrorKind.Internal);

public class AppTooManyRequestsException(string message) : AppException(message, AppErrorKind.TooManyRequests);
