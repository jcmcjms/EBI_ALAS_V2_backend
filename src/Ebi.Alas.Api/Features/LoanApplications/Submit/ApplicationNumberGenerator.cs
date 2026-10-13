using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanApplications.Submit;

/// <summary>
/// Allocates LAM / APP numbers as PREFIX-{PH yyyyMMdd}-{000001}, matching the
/// prior system's daily sequence format.
/// </summary>
public sealed class ApplicationNumberGenerator(AlasDbContext db)
{
    public const string LamPrefix = "LAM-";
    public const string GroupPrefix = "APP-";
    private const int SequenceWidth = 6;
    private static readonly TimeSpan PhilippinesOffset = TimeSpan.FromHours(8);

    public async Task<string> GenerateLamIdAsync(CancellationToken cancellationToken = default)
        => (await GenerateLamIdsAsync(1, cancellationToken))[0];

    public async Task<IReadOnlyList<string>> GenerateLamIdsAsync(int count, CancellationToken cancellationToken = default)
        => await AllocateAsync(LamPrefix, count, cancellationToken);

    public async Task<string> GenerateGroupNumberAsync(CancellationToken cancellationToken = default)
        => (await AllocateAsync(GroupPrefix, 1, cancellationToken))[0];

    private async Task<IReadOnlyList<string>> AllocateAsync(
        string prefix,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var now = DateTimeOffset.UtcNow.ToOffset(PhilippinesOffset);
        var fullPrefix = $"{prefix}{now:yyyyMMdd}-";

        var last = prefix == GroupPrefix
            ? await db.LoanApplications.AsNoTracking()
                .Where(l => l.ApplicationGroupNo.StartsWith(fullPrefix))
                .OrderByDescending(l => l.ApplicationGroupNo)
                .Select(l => l.ApplicationGroupNo)
                .FirstOrDefaultAsync(cancellationToken)
            : await db.LoanApplications.AsNoTracking()
                .Where(l => l.LamId.StartsWith(fullPrefix))
                .OrderByDescending(l => l.LamId)
                .Select(l => l.LamId)
                .FirstOrDefaultAsync(cancellationToken);

        var next = 1;
        if (!string.IsNullOrEmpty(last) && last.Length >= SequenceWidth)
        {
            var sequencePart = last[^SequenceWidth..];
            if (int.TryParse(sequencePart, out var lastSequence))
            {
                next = lastSequence + 1;
            }
        }

        var ids = new List<string>(count);
        for (var n = 0; n < count; n++)
        {
            ids.Add(fullPrefix + (next + n).ToString("D6"));
        }

        return ids;
    }
}
