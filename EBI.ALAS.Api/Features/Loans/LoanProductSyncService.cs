using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Features.WebLoans;

namespace EBI.ALAS.Api.Features.Loans;

// ─── Sync service contract ─────────────────────────────────────────────────
// Separated from ILoanProductService so the hosted background service
// (LoanProductSyncHostedService) can depend on JUST the sync contract,
// not the full read/write surface. Smaller surface = easier to mock in
// the hosted-service test, and the read paths stay read-only in spirit.
public interface ILoanProductSyncService
{
    // Pulls every webloan.loan_product row, derives retirement, and
    // upserts the ALAS mirror. Policy fields are preserved on update
    // (sync never overwrites ops-configured values).
    //
    // Returns the summary so the caller (manual endpoint, hosted
    // service, or unit test) can log or surface what happened.
    Task<LoanProductSyncResult> SyncAsync(CancellationToken ct = default);
}

public class LoanProductSyncService(
    IWebLoanRepository webLoanRepository,
    ILoanProductRepository loanProductRepository,
    ITimeProvider timeProvider,
    ILogger<LoanProductSyncService> logger) : ILoanProductSyncService
{
    // We need a list of ALL webloan products (active + retired) so the
    // sync can mark newly-retired rows. The existing
    // IWebLoanRepository.GetActiveLoanProductsAsync only returns active
    // rows — for the sync we add a new method
    // (GetAllLoanProductsWithExpirationAsync) on that interface.
    public async Task<LoanProductSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var syncedAt = timeProvider.UtcNow;

        // Fetch every webloan product, active and retired. The table
        // is small (~tens of rows); pulling the whole set is cheaper
        // than a delta query and keeps the upsert logic simple.
        var webloanProducts = await webLoanRepository.GetAllLoanProductsAsync(ct);

        var added = 0;
        var updated = 0;
        var preserved = 0;

        foreach (var wp in webloanProducts)
        {
            // Skip null/whitespace codes — defensive. webloan PK is
            // non-nullable in practice, but a code-level guarantee
            // would need a CHECK constraint we don't control.
            if (string.IsNullOrWhiteSpace(wp.IdCode)) continue;

            // Check the current mirror row to decide add vs update
            // vs preserved. We need this read anyway to honor
            // preservePolicyFields semantics — the upsert helper
            // would have to fetch it internally otherwise.
            var existing = await loanProductRepository.GetByCodeAsync(wp.IdCode, ct);

            // IsRetired is derived from webloan's `expiration IS NOT
            // NULL` at sync time. NULL → not retired. The sync is
            // the only place this column is computed.
            var isRetired = wp.Expiration is not null;

            if (existing is null)
            {
                // Brand-new row. Insert with default policy values
                // (zeros). The entity's IsRetired default is true, but
                // we set it explicitly here based on the webloan
                // signal — a brand-new webloan product is active by
                // construction.
                var newRow = new LoanProduct
                {
                    Code = wp.IdCode,
                    Description = wp.Description,
                    MinAmount = 0m,
                    MaxAmount = 0m,
                    MinTermMonths = 0,
                    MaxTermMonths = 0,
                    NotarialFee = 0m,
                    DocStampFee = 0m,
                    InsuranceFee = 0m,
                    AdvanceInterestRate = 0m,
                    IsRetired = isRetired,
                    LastSyncedAt = syncedAt
                };

                await loanProductRepository.UpsertAsync(
                    newRow, preservePolicyFields: true, ct);
                added++;
            }
            else
            {
                // Existing row. Compare the sync-owned fields to
                // decide if anything actually changed; if not, we
                // still want to bump LastSyncedAt to reflect the
                // successful refresh.
                var changed =
                    existing.Description != wp.Description ||
                    existing.IsRetired != isRetired;

                if (changed)
                {
                    existing.Description = wp.Description;
                    existing.IsRetired = isRetired;
                    existing.LastSyncedAt = syncedAt;

                    // preservePolicyFields=true: leave min/max/term/
                    // fees alone. Ops configured them; the sync must
                    // not silently revert them on every refresh.
                    await loanProductRepository.UpsertAsync(
                        existing, preservePolicyFields: true, ct);
                    updated++;
                }
                else
                {
                    // No semantic change, but still record the
                    // successful refresh so the row's LastSyncedAt
                    // stays accurate. Operators looking at the admin
                    // grid can see "synced 6 hours ago" instead of
                    // "synced 3 days ago" on a healthy mirror.
                    existing.LastSyncedAt = syncedAt;
                    await loanProductRepository.UpsertAsync(
                        existing, preservePolicyFields: true, ct);
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
