using EBI.ALAS.Api.Common.Models;

namespace EBI.ALAS.Api.Features.Account;

public record AccountProfileResponse(
    int Id,
    string Username,
    string FirstName,
    string? MiddleName,
    string LastName,
    string BranchId,
    string Role,
    string? Email,
    string? Phone,
    string? EmergencyContact,
    string? ProfilePhotoUrl,
    DateTime CreatedAt,
    DateTime? PasswordChangedAt,
    AccountStatsResponse Stats
);

public record AccountStatsResponse(
    int ProcessedLoans,
    int PendingLoans,
    int ApprovalRate
);

public record UpdateProfileRequest(
    string? Email,
    string? Phone,
    string? EmergencyContact
);

public record SessionResponse(
    int Id,
    string DeviceInfo,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    bool IsCurrent
);

public record PagedSessionsResponse(
    List<SessionResponse> Items,
    int CurrentPage,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage
);

public record ActivityResponse(
    int Id,
    string LoanFormNumber,
    string Action,
    string? FromStatus,
    string? ToStatus,
    string? Comments,
    DateTime ActionDate,
    string LoanClientName
);

public record ProcessedLoanResponse(
    int Id,
    string FormNumber,
    string ClientName,
    string Status,
    DateTime ApplicationDate,
    decimal ProposedAmount
);

public record RecentClientResponse(
    string CisId,
    string Name,
    string Agency,
    DateTime LastInteraction
);