using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

public class FormNumberGenerator : IFormNumberGenerator
{
    private const string LamPrefix = "LAM-";
    private const string GroupPrefix = "APP-";
    private const int SequenceWidth = 6;

    private readonly AppDbContext _context;
    private readonly ITimeProvider _timeProvider;

    // Serializes allocation within this process only. The unique indexes on
    // LoanApplications.FormNumber / ApplicationGroupNo are the cross-process
    // backstop; LoanSubmissionService retries on duplicate-key violations.
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public FormNumberGenerator(AppDbContext context, ITimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<string> GenerateFormNumberAsync(CancellationToken ct = default)
        => (await AllocateAsync(LamPrefix, 1, isGroup: false, ct))[0];

    public Task<IReadOnlyList<string>> GenerateFormNumbersAsync(int count, CancellationToken ct = default)
        => AllocateAsync(LamPrefix, count, isGroup: false, ct);

    public async Task<string> GenerateGroupNumberAsync(CancellationToken ct = default)
        => (await AllocateAsync(GroupPrefix, 1, isGroup: true, ct))[0];

    private async Task<IReadOnlyList<string>> AllocateAsync(
        string prefix, int count, bool isGroup, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var fullPrefix = $"{prefix}{_timeProvider.PhilippinesNow:yyyyMMdd}-";

        await _semaphore.WaitAsync(ct);
        try
        {
            // Fixed-width sequence ⇒ ordinal string ordering == numeric ordering.
            var last = isGroup
                ? await _context.LoanApplications
                    .Where(l => l.ApplicationGroupNo.StartsWith(fullPrefix))
                    .OrderByDescending(l => l.ApplicationGroupNo)
                    .Select(l => l.ApplicationGroupNo)
                    .FirstOrDefaultAsync(ct)
                : await _context.LoanApplications
                    .Where(l => l.FormNumber.StartsWith(fullPrefix))
                    .OrderByDescending(l => l.FormNumber)
                    .Select(l => l.FormNumber)
                    .FirstOrDefaultAsync(ct);

            var next = 1;
            if (!string.IsNullOrEmpty(last))
            {
                var sequencePart = last.Substring(last.Length - SequenceWidth);
                if (int.TryParse(sequencePart, out var lastSequence))
                {
                    next = lastSequence + 1;
                }
            }

            return Enumerable.Range(next, count)
                .Select(n => $"{fullPrefix}{n:D6}")
                .ToList();
        }
        finally
        {
            _semaphore.Release();
        }
    }
}