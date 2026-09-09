namespace EBI.ALAS.Api.Features.Loans;

public interface ILamIdGenerator
{
    Task<string> GenerateLamIdAsync(CancellationToken ct = default);

    /// <summary>Allocates <paramref name="count"/> contiguous LAM sequences in one critical section.</summary>
    Task<IReadOnlyList<string>> GenerateLamIdsAsync(int count, CancellationToken ct = default);

    /// <summary>Group key tying the N loans of one submission together.</summary>
    Task<string> GenerateGroupNumberAsync(CancellationToken ct = default);
}
