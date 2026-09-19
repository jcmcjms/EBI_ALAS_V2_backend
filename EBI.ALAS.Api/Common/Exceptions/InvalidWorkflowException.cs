namespace EBI.ALAS.Api.Common.Exceptions;

/// <summary>
/// Thrown when a workflow state transition is not allowed.
/// Carries from/to status and the user's role for structured error responses.
/// </summary>
public sealed class InvalidWorkflowException : Exception
{
    public string FromStatus { get; }
    public string ToStatus { get; }
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
        return string.IsNullOrEmpty(userRole)
            ? $"Invalid status transition from '{fromStatus}' to '{toStatus}'. This transition is not allowed by the loan workflow rules."
            : $"Invalid status transition from '{fromStatus}' to '{toStatus}' for role '{userRole}'. This transition is not allowed by the loan workflow rules.";
    }
}
