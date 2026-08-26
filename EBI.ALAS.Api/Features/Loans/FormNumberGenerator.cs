using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Form number generator that creates unique form numbers in format LAM-yyyyMMdd-XXXXXX.
/// </summary>
public class FormNumberGenerator : IFormNumberGenerator
{
    private readonly AppDbContext _context;
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public FormNumberGenerator(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string> GenerateFormNumberAsync()
    {
        var today = DateTime.UtcNow;
        var datePart = today.ToString("yyyyMMdd");
        var prefix = $"LAM-{datePart}-";

        // Async-compatible thread-safe sequence generation
        await _semaphore.WaitAsync();
        try
        {
            return await GenerateFormNumberInternal(prefix);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<string> GenerateFormNumberInternal(string prefix)
    {
        // Get the highest sequence number for today
        var lastFormNumber = await _context.LoanApplications
            .Where(l => l.FormNumber.StartsWith(prefix))
            .OrderByDescending(l => l.FormNumber)
            .Select(l => l.FormNumber)
            .FirstOrDefaultAsync();

        int sequenceNumber = 1;

        if (!string.IsNullOrEmpty(lastFormNumber))
        {
            // Extract the sequence part (last 6 digits)
            var sequencePart = lastFormNumber.Substring(lastFormNumber.Length - 6);
            if (int.TryParse(sequencePart, out int lastSequence))
            {
                sequenceNumber = lastSequence + 1;
            }
        }

        // Format as 6-digit sequence with leading zeros
        var sequence = sequenceNumber.ToString("D6");
        return $"{prefix}{sequence}";
    }
}
