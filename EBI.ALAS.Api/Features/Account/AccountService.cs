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
        await _repository.UpdateProfileAsync(userId, request with
        {
            Email = Normalize(request.Email),
            Phone = Normalize(request.Phone),
            EmergencyContact = Normalize(request.EmergencyContact),
        });

    public async Task<PagedSessionsResponse> GetActiveSessionsAsync(int userId, int? currentSessionId, int pageNumber = 1, int pageSize = 10) =>
        await _repository.GetActiveSessionsAsync(userId, currentSessionId, pageNumber, pageSize);

    public async Task<SessionRevokeResult> RevokeSessionAsync(int userId, int sessionId, int? currentSessionId) =>
        await _repository.RevokeSessionAsync(userId, sessionId, currentSessionId);

    public async Task<int> RevokeOtherSessionsAsync(int userId, int? currentSessionId) =>
        await _repository.RevokeOtherSessionsAsync(userId, currentSessionId);

    public async Task<List<ActivityResponse>> GetRecentActivityAsync(int userId, int limit = 10) =>
        await _repository.GetRecentActivityAsync(userId, limit);

    public async Task<List<ProcessedLoanResponse>> GetProcessedLoansAsync(int userId, int limit = 10) =>
        await _repository.GetProcessedLoansAsync(userId, limit);

    public async Task<List<RecentClientResponse>> GetRecentClientsAsync(int userId, int limit = 5) =>
        await _repository.GetRecentClientsAsync(userId, limit);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
