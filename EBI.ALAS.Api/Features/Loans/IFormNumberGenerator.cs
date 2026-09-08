namespace EBI.ALAS.Api.Features.Loans;

public interface IFormNumberGenerator
{
    Task<string> GenerateFormNumberAsync(CancellationToken ct = default);

    /// <summary>Allocates <paramref name="count"/> contiguous LAM sequences in one critical section.</summary>
    Task<IReadOnlyList<string>> GenerateFormNumbersAsync(int count, CancellationToken ct = default);

    /// <summary>Group key tying the N loans of one submission together.</summary>
    Task<string> GenerateGroupNumberAsync(CancellationToken ct = default);
}