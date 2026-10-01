namespace EBI.ALAS.Api.Features.Loans;
public sealed record SignatureSlotDto(
    int Order,
    string Action,
    string Role,
    string JobTitle,
    string? SignedByName,
    string? SignedByJobTitle,
    DateTime? SignedAt);
public interface ISignatureChainService
{
    IReadOnlyList<SignatureSlotDto> GetTemplate();
    Task<IReadOnlyList<SignatureSlotDto>?> ResolveForLoanAsync(int loanApplicationId, CancellationToken ct = default);
}
