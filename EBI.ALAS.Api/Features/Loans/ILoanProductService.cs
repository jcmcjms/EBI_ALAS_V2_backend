namespace EBI.ALAS.Api.Features.Loans;
public record LoanProductResponse(
    string Code,
    string Description,
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermDays,
    int MaxTermDays,
    decimal NotarialFee,
    decimal DocStampFee,
    decimal InsuranceFee,
    decimal AdvanceInterestRate,
    decimal ApplicationChargeRate,
    string AmortizationMode,
    bool ChargeAdvanceInterest,
    bool IsRetired,
    DateTime LastSyncedAt,
    DateTime UpdatedDate,
    int? UpdatedById,
    string? UpdatedByName);
public record UpdateLoanProductRequest(
    decimal MinAmount,
    decimal MaxAmount,
    int MinTermDays,
    int MaxTermDays,
    decimal NotarialFee,
    decimal DocStampFee,
    decimal InsuranceFee,
    decimal AdvanceInterestRate,
    decimal? ApplicationChargeRate = null,
    string? AmortizationMode = null,
    bool? ChargeAdvanceInterest = null);
public record LoanProductSyncResult(
    int Added,
    int Updated,
    int Preserved,
    DateTime SyncedAt);
public interface ILoanProductService
{
    Task<IReadOnlyList<LoanProductResponse>> GetAllAsync(CancellationToken ct = default);
    Task<LoanProductResponse?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<LoanProductResponse?> UpdateAsync(
        string code,
        UpdateLoanProductRequest request,
        int updatedByUserId,
        CancellationToken ct = default);
    Task<LoanProductSyncResult> SyncFromWebloanAsync(CancellationToken ct = default);
}
