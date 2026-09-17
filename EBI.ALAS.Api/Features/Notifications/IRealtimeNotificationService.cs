namespace EBI.ALAS.Api.Features.Notifications;

/// <summary>
/// Abstraction over SignalR push delivery. Business logic
/// (<see cref="Loans.LoanSubmissionService"/>, workflow endpoints)
/// depends on this interface instead of <c>IHubContext</c> directly
/// so the notification transport can be swapped or mocked without
/// touching loan code.
/// </summary>
public interface IRealtimeNotificationService
{
    /// <summary>Push to a single user by their <c>User.Id</c>.</summary>
    Task NotifyUserAsync(int userId, string title, string description, string? link);

    /// <summary>Push to every officer in a branch (via <c>Branch_{id}</c> group).</summary>
    Task NotifyBranchAsync(string branchId, string title, string description, string? link);

    /// <summary>Push to every connected user (system-wide broadcast).</summary>
    Task NotifyAllAsync(string title, string description, string? link);

    /// <summary>
    /// Signal connected clients that dashboard data has changed and
    /// should be re-fetched. Sent to branch-scoped group so only
    /// affected officers see the refresh prompt.
    /// </summary>
    Task NotifyDashboardUpdateAsync(string? branchCode);
}
