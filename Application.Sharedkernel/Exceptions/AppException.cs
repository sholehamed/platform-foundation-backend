namespace Application.SharedKernel.Exceptions;

/// <summary>
/// Expected application failure. The error type is transport-independent;
/// the HTTP layer maps it to an appropriate status code.
/// Only use client-safe messages here.
/// </summary>
public class AppException : Exception
{
    public string Code { get; }
    public AppErrorType ErrorType { get; }

    public AppException(string code, string message, AppErrorType errorType)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        ErrorType = errorType;
    }

    public static AppException BadRequest(string code, string message) =>
        new(code, message, AppErrorType.BadRequest);

    public static AppException NotFound(string code, string message) =>
        new(code, message, AppErrorType.NotFound);

    public static AppException Conflict(string code, string message) =>
        new(code, message, AppErrorType.Conflict);

    public static AppException BusinessRule(string code, string message) =>
        new(code, message, AppErrorType.BusinessRule);

    public static AppException Unauthorized(string code = "authentication.required",
        string message = "Authentication is required.") =>
        new(code, message, AppErrorType.Unauthorized);

    public static AppException Forbidden(string code = "authorization.forbidden",
        string message = "Access is forbidden.") =>
        new(code, message, AppErrorType.Forbidden);

    public static AppException Unavailable(string code, string message) =>
        new(code, message, AppErrorType.ServiceUnavailable);
}

/// <summary>Categories, not HTTP status codes. Mapped at the transport boundary.</summary>
public enum AppErrorType
{
    BadRequest,
    NotFound,
    Conflict,
    BusinessRule,
    Unauthorized,
    Forbidden,
    ServiceUnavailable
}
