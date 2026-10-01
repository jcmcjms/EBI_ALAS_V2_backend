namespace EBI.ALAS.Api.Features.Loans;
public interface ILoanProductRepository
{
    Task<IReadOnlyList<LoanProduct>> GetAllAsync(CancellationToken ct = default);
    Task<LoanProduct?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<LoanProduct> UpsertAsync(
        LoanProduct product,
        bool preservePolicyFields,
        int? updatedByUserId,
        DateTime updatedDate,
        CancellationToken ct = default);
    Task<bool> DeleteAsync(string code, CancellationToken ct = default);
    Task<bool> ExistsActiveByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<LoanProduct>> GetByCodesAsync(IReadOnlyCollection<string> codes, CancellationToken ct = default);
}
