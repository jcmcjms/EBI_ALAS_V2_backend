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
        => await AllocateAsync(LamPrefix, cancellationToken);

    public async Task<string> GenerateGroupNumberAsync(CancellationToken cancellationToken = default)
        => await AllocateAsync(GroupPrefix, cancellationToken);

    private async Task<string> AllocateAsync(string prefix, CancellationToken cancellationToken)
    {
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

        return fullPrefix + next.ToString("D6");
    }
}
