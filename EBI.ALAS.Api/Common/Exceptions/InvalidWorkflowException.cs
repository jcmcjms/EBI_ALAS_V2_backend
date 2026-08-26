namespace EBI.ALAS.Api.Common.Exceptions;

/// <summary>
/// Exception thrown when an invalid workflow status transition is attempted.
/// Maps to HTTP 400 Bad Request by the GlobalExceptionHandler.
/// </summary>
public class InvalidWorkflowException : Exception
{
    /// <summary>
    /// The current status of the resource before the attempted transition.
    /// </summary>
    public string FromStatus { get; }

    /// <summary>
    /// The target status that was requested.
    /// </summary>
    public string ToStatus { get; }

    /// <summary>
    /// The role that was attempting the transition.
    /// </summary>
    public string? UserRole { get; }

    public InvalidWorkflowException(string fromStatus, string toStatus, string? userRole = null)
        : base(BuildMessage(fromStatus, toStatus, userRole))
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
        UserRole = userRole;
    }

    public InvalidWorkflowException(string fromStatus, string toStatus, string? userRole, Exception innerException)
        : base(BuildMessage(fromStatus, toStatus, userRole), innerException)
    {
        FromStatus = fromStatus;
        ToStatus = toStatus;
        UserRole = userRole;
    }

    private static string BuildMessage(string fromStatus, string toStatus, string? userRole)
    {
        if (!string.IsNullOrEmpty(userRole))
        {
            return $"Invalid status transition from '{fromStatus}' to '{toStatus}' for role '{userRole}'. " +
                   $"This transition is not allowed by the loan workflow rules.";
        }

        return $"Invalid status transition from '{fromStatus}' to '{toStatus}'. " +
               $"This transition is not allowed by the loan workflow rules.";
    }
}
