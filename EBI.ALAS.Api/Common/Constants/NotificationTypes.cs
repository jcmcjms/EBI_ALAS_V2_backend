namespace EBI.ALAS.Api.Common.Constants;

/// <summary>
/// Canonical notification type buckets used for SQL-based inbox filtering.
/// Written at creation time (by the dispatcher or the classifier fallback)
/// so the inbox query filters in SQL rather than keyword-guessing titles
/// client-side.
/// </summary>
public static class NotificationTypes
{
    public const string Application = "application";
    public const string Action = "action";
    public const string Message = "message";
    public const string System = "system";
}
