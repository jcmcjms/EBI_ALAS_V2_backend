using EBI.ALAS.Api.Common.Time;
namespace EBI.ALAS.Api.Features.Loans;
public class LoanProductService(
    ILoanProductRepository repository,
    ILoanProductSyncService syncService,
    ITimeProvider timeProvider) : ILoanProductService
{
    public const int AbsoluteMaxTermDays = 2617;
    public async Task<IReadOnlyList<LoanProductResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await repository.GetAllAsync(ct);
        return rows.Select(ToResponse).ToList();
    }
    public async Task<LoanProductResponse?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var row = await repository.GetByCodeAsync(code, ct);
        return row is null ? null : ToResponse(row);
    }
    public async Task<LoanProductResponse?> UpdateAsync(
        string code,
        UpdateLoanProductRequest request,
        int updatedByUserId,
        CancellationToken ct = default)
    {
        var existing = await repository.GetByCodeAsync(code, ct);
        if (existing is null) return null;
        ValidatePolicyFields(request);
        existing.MinAmount = request.MinAmount;
        existing.MaxAmount = request.MaxAmount;
        existing.MinTermDays = request.MinTermDays;
        existing.MaxTermDays = request.MaxTermDays;
        existing.NotarialFee = request.NotarialFee;
        existing.DocStampFee = request.DocStampFee;
        existing.InsuranceFee = request.InsuranceFee;
        existing.AdvanceInterestRate = request.AdvanceInterestRate;
        if (request.ApplicationChargeRate.HasValue)
            existing.ApplicationChargeRate = request.ApplicationChargeRate.Value;
        if (request.AmortizationMode is not null)
            existing.AmortizationMode = request.AmortizationMode;
        if (request.ChargeAdvanceInterest.HasValue)
            existing.ChargeAdvanceInterest = request.ChargeAdvanceInterest.Value;
        var updated = await repository.UpsertAsync(
            existing,
            preservePolicyFields: false,
            updatedByUserId: updatedByUserId,
            updatedDate: timeProvider.UtcNow,
            ct);
        return ToResponse(updated);
    }
    public async Task<LoanProductSyncResult> SyncFromWebloanAsync(CancellationToken ct = default)
    {
        return await syncService.SyncAsync(ct);
    }
    private static LoanProductResponse ToResponse(LoanProduct p) => new(
        p.Code,
        p.Description,
        p.MinAmount,
        p.MaxAmount,
        p.MinTermDays,
        p.MaxTermDays,
        p.NotarialFee,
        p.DocStampFee,
        p.InsuranceFee,
        p.AdvanceInterestRate,
        p.ApplicationChargeRate,
        p.AmortizationMode,
        p.ChargeAdvanceInterest,
        p.IsRetired,
        p.LastSyncedAt,
        p.UpdatedDate,
        p.UpdatedById,
        p.UpdatedBy is null
            ? null
            : $"{p.UpdatedBy.FirstName} {p.UpdatedBy.LastName}");
    private static void ValidatePolicyFields(UpdateLoanProductRequest r)
    {
        if (r.MinAmount < 0)
            throw new ArgumentException("MinAmount cannot be negative.", nameof(r));
        if (r.MaxAmount < r.MinAmount)
            throw new ArgumentException(
                $"MaxAmount ({r.MaxAmount}) must be >= MinAmount ({r.MinAmount}).", nameof(r));
        if (r.MinTermDays < 0)
            throw new ArgumentException("MinTermDays cannot be negative.", nameof(r));
        if (r.MaxTermDays < r.MinTermDays)
            throw new ArgumentException(
                $"MaxTermDays ({r.MaxTermDays}) must be >= MinTermDays ({r.MinTermDays}).", nameof(r));
        if (r.MaxTermDays > AbsoluteMaxTermDays)
            throw new ArgumentException(
                $"MaxTermDays ({r.MaxTermDays}) cannot exceed the absolute ceiling of {AbsoluteMaxTermDays} days (7 years).", nameof(r));
        if (r.NotarialFee < 0 || r.DocStampFee < 0 || r.InsuranceFee < 0)
            throw new ArgumentException("Fees cannot be negative.", nameof(r));
        if (r.AdvanceInterestRate < 0 || r.AdvanceInterestRate > 1m)
            throw new ArgumentException(
                "AdvanceInterestRate must be between 0 and 1 (e.g. 0.12 for 12% p.a.).", nameof(r));
        if (r.ApplicationChargeRate is < 0 or > 1m)
            throw new ArgumentException(
                "ApplicationChargeRate must be between 0 and 1 (e.g. 0.06 for 6%).", nameof(r));
        if (r.AmortizationMode is not null and not ("DIM" or "MIC"))
            throw new ArgumentException(
                "AmortizationMode must be 'DIM' or 'MIC'.", nameof(r));
    }
}
