using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.WebLoans;
namespace EBI.ALAS.Api.Features.Loans;
public interface ILoanProductSyncService
{
    Task<LoanProductSyncResult> SyncAsync(CancellationToken ct = default);
}
public class LoanProductSyncService(
    IWebLoanRepository webLoanRepository,
    ILoanProductRepository loanProductRepository,
    ITimeProvider timeProvider,
    ILogger<LoanProductSyncService> logger) : ILoanProductSyncService
{
    public async Task<LoanProductSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var syncedAt = timeProvider.UtcNow;
        var webloanProducts = await webLoanRepository.GetAllLoanProductsAsync(ct);
        var added = 0;
        var updated = 0;
        var preserved = 0;
        foreach (var wp in webloanProducts)
        {
            if (string.IsNullOrWhiteSpace(wp.IdCode)) continue;
            var existing = await loanProductRepository.GetByCodeAsync(wp.IdCode, ct);
            var isRetired = wp.Expiration is not null;
            if (existing is null)
            {
                var newRow = new LoanProduct
                {
                    Code = wp.IdCode,
                    Description = wp.Description,
                    MinAmount = 0m,
                    MaxAmount = 0m,
                    MinTermDays = 0,
                    MaxTermDays = 0,
                    NotarialFee = 0m,
                    DocStampFee = 0m,
                    InsuranceFee = 0m,
                    AdvanceInterestRate = 0m,
                    IsRetired = isRetired,
                    LastSyncedAt = syncedAt
                };
                await loanProductRepository.UpsertAsync(
                    newRow,
                    preservePolicyFields: true,
                    updatedByUserId: null,
                    updatedDate: syncedAt,
                    ct);
                added++;
            }
            else
            {
                var changed =
                    existing.Description != wp.Description ||
                    existing.IsRetired != isRetired;
                if (changed)
                {
                    existing.Description = wp.Description;
                    existing.IsRetired = isRetired;
                    existing.LastSyncedAt = syncedAt;
                    await loanProductRepository.UpsertAsync(
                        existing,
                        preservePolicyFields: true,
                        updatedByUserId: null,
                        updatedDate: syncedAt,
                        ct);
                    updated++;
                }
                else
                {
                    existing.LastSyncedAt = syncedAt;
                    await loanProductRepository.UpsertAsync(
                        existing,
                        preservePolicyFields: true,
                        updatedByUserId: null,
                        updatedDate: syncedAt,
                        ct);
                    preserved++;
                }
            }
        }
        logger.LogInformation(
            "LoanProduct sync completed: {Added} added, {Updated} updated, {Preserved} preserved",
            added, updated, preserved);
        return new LoanProductSyncResult(added, updated, preserved, syncedAt);
    }
}
