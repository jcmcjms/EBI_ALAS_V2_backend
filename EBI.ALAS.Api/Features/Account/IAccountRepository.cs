namespace EBI.ALAS.Api.Features.Account;

public interface IAccountRepository
{
    Task<AccountProfileResponse?> GetProfileAsync(int userId);
    Task<bool> UpdateProfileAsync(int userId, UpdateProfileRequest request);
    Task<PagedSessionsResponse> GetActiveSessionsAsync(int userId, int currentSessionId, int pageNumber = 1, int pageSize = 10);
    Task<bool> RevokeSessionAsync(int userId, int sessionId);
    Task<List<ActivityResponse>> GetRecentActivityAsync(int userId, int limit = 10);
    Task<List<ProcessedLoanResponse>> GetProcessedLoansAsync(int userId, int limit = 10);
    Task<List<RecentClientResponse>> GetRecentClientsAsync(int userId, int limit = 5);
    Task<AccountStatsResponse> GetStatsAsync(int userId);
}