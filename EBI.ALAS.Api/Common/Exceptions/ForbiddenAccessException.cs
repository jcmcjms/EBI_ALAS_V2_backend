namespace EBI.ALAS.Api.Common.Exceptions;

/// <summary>
/// Exception thrown when the authenticated user lacks permission to perform the requested action.
/// Mapped to HTTP 403 by the GlobalExceptionHandler.
/// </summary>
public class ForbiddenAccessException : Exception
{
    /// <summary>
    /// The permission key that was required but not held by the user.
    /// </summary>
    public string? RequiredPermission { get; }

    public ForbiddenAccessException()
        : base("You do not have permission to perform this action.")
    {
    }

    public ForbiddenAccessException(string message)
        : base(message)
    {
    }

    public ForbiddenAccessException(string message, string requiredPermission)
        : base(message)
    {
        RequiredPermission = requiredPermission;
    }

    public ForbiddenAccessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
