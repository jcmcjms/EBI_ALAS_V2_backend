using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace EBI.ALAS.Api.Features.Loans;
public class LoanProductRepository(AppDbContext context) : ILoanProductRepository
{
    public async Task<IReadOnlyList<LoanProduct>> GetAllAsync(CancellationToken ct = default)
    {
        return await context.LoanProducts
            .AsNoTracking()
            .OrderBy(p => p.Code)
            .ToListAsync(ct);
    }
    public async Task<LoanProduct?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return await context.LoanProducts
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code, ct);
    }
    public async Task<LoanProduct> UpsertAsync(
        LoanProduct product,
        bool preservePolicyFields,
        int? updatedByUserId,
        DateTime updatedDate,
        CancellationToken ct = default)
    {
        var existing = await context.LoanProducts
            .FirstOrDefaultAsync(p => p.Code == product.Code, ct);
        if (existing is null)
        {
            product.UpdatedDate = updatedDate;
            product.UpdatedById = updatedByUserId;
            await context.LoanProducts.AddAsync(product, ct);
        }
        else
        {
            existing.Description = product.Description;
            existing.IsRetired = product.IsRetired;
            existing.LastSyncedAt = product.LastSyncedAt;
            if (!preservePolicyFields)
            {
                existing.MinAmount = product.MinAmount;
                existing.MaxAmount = product.MaxAmount;
                existing.MinTermDays = product.MinTermDays;
                existing.MaxTermDays = product.MaxTermDays;
                existing.NotarialFee = product.NotarialFee;
                existing.DocStampFee = product.DocStampFee;
                existing.InsuranceFee = product.InsuranceFee;
                existing.AdvanceInterestRate = product.AdvanceInterestRate;
            }
            existing.UpdatedDate = updatedDate;
            existing.UpdatedById = updatedByUserId;
        }
        await context.SaveChangesAsync(ct);
        return existing ?? product;
    }
    public async Task<bool> DeleteAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var existing = await context.LoanProducts
            .FirstOrDefaultAsync(p => p.Code == code, ct);
        if (existing is null) return false;
        context.LoanProducts.Remove(existing);
        await context.SaveChangesAsync(ct);
        return true;
    }
    public async Task<bool> ExistsActiveByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        return await context.LoanProducts
            .AsNoTracking()
            .AnyAsync(p => p.Code == code && !p.IsRetired, ct);
    }
    public async Task<IReadOnlyList<LoanProduct>> GetByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default)
    {
        if (codes is null || codes.Count == 0) return [];

        return await context.LoanProducts
            .AsNoTracking()
            .Where(p => codes.Contains(p.Code))
            .ToListAsync(ct);
    }
}
