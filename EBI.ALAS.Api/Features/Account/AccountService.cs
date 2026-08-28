namespace EBI.ALAS.Api.Features.Account;

public class AccountService : IAccountService
{
    private readonly IAccountRepository _repository;

    public AccountService(IAccountRepository repository)
    {
        _repository = repository;
    }

    public async Task<AccountProfileResponse?> GetProfileAsync(int userId) =>
        await _repository.GetProfileAsync(userId);

    public async Task<bool> UpdateProfileAsync(int userId, UpdateProfileRequest request) =>
        await _repository.UpdateProfileAsync(userId, request);

    public async Task<PagedSessionsResponse> GetActiveSessionsAsync(int userId, string currentJti, int pageNumber = 1, int pageSize = 10)
    {
        // For now, pass 0 as currentSessionId since we don't have direct JTI→RefreshToken mapping
        // TODO: Add Jti field to RefreshToken entity for proper current session detection
        return await _repository.GetActiveSessionsAsync(userId, 0, pageNumber, pageSize);
    }

    public async Task<bool> RevokeSessionAsync(int userId, int sessionId) =>
        await _repository.RevokeSessionAsync(userId, sessionId);

    public async Task<List<ActivityResponse>> GetRecentActivityAsync(int userId, int limit = 10) =>
        await _repository.GetRecentActivityAsync(userId, limit);

    public async Task<List<ProcessedLoanResponse>> GetProcessedLoansAsync(int userId, int limit = 10) =>
        await _repository.GetProcessedLoansAsync(userId, limit);

    public async Task<List<RecentClientResponse>> GetRecentClientsAsync(int userId, int limit = 5) =>
        await _repository.GetRecentClientsAsync(userId, limit);
}