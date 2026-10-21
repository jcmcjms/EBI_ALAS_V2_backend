using Ebi.Alas.Api.Composition.Errors;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanProducts.DocumentRequirements;

public sealed class GetDocumentRequirementsHandler(AlasDbContext db)
{
    public async Task<IReadOnlyList<DocumentRequirementItemDto>> HandleAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeCode(code);
        var exists = await db.LoanProducts.AsNoTracking()
            .AnyAsync(p => p.Code == normalized, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Loan product", normalized);
        }

        return await db.LoanProductDocumentRequirements.AsNoTracking()
            .Where(r => r.ProductCode == normalized)
            .OrderBy(r => r.LoanType)
            .ThenBy(r => r.Code)
            .Select(r => new DocumentRequirementItemDto(r.Code, r.Description, r.LoanType.ToString()))
            .ToListAsync(cancellationToken);
    }

    private static string NormalizeCode(string code) =>
        string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim();
}

public sealed class ReplaceDocumentRequirementsHandler(AlasDbContext db)
{
    public const int MaxItems = 50;

    public async Task<IReadOnlyList<DocumentRequirementItemDto>> HandleAsync(
        string code,
        ReplaceDocumentRequirementsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = string.IsNullOrWhiteSpace(code) ? string.Empty : code.Trim();
        var product = await db.LoanProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == normalized, cancellationToken)
            ?? throw new NotFoundException("Loan product", normalized);

        var items = request.Items ?? [];
        if (items.Count > MaxItems)
        {
            throw new ArgumentException($"At most {MaxItems} document requirements are allowed.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parsed = new List<(string Code, string Description, LoanType LoanType)>(items.Count);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Code) || item.Code.Trim().Length > 50)
            {
                throw new ArgumentException(
                    "Each document requirement code is required and must be 50 characters or fewer.");
            }
            if (string.IsNullOrWhiteSpace(item.Description) || item.Description.Trim().Length > 200)
            {
                throw new ArgumentException(
                    "Each document requirement description is required and must be 200 characters or fewer.");
            }

            var loanType = ParseLoanType(item.LoanType);
            if (!seen.Add($"{item.Code.Trim()}\n{loanType}"))
            {
                throw new ArgumentException(
                    $"Duplicate document requirement code '{item.Code.Trim()}' for loan type '{loanType}'.");
            }

            parsed.Add((item.Code.Trim(), item.Description.Trim(), loanType));
        }

        var existing = await db.LoanProductDocumentRequirements
            .Where(r => r.ProductCode == product.Code)
            .ToListAsync(cancellationToken);
        db.LoanProductDocumentRequirements.RemoveRange(existing);

        foreach (var (itemCode, description, loanType) in parsed)
        {
            db.LoanProductDocumentRequirements.Add(
                LoanProductDocumentRequirement.Create(product.Code, loanType, itemCode, description));
        }

        await db.SaveChangesAsync(cancellationToken);

        return parsed
            .Select(i => new DocumentRequirementItemDto(i.Code, i.Description, i.LoanType.ToString()))
            .OrderBy(i => i.LoanType, StringComparer.Ordinal)
            .ThenBy(i => i.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static LoanType ParseLoanType(string? value) => value?.Trim() switch
    {
        null or "" => LoanType.New,
        nameof(LoanType.New) => LoanType.New,
        nameof(LoanType.Reloan) => LoanType.Reloan,
        _ => throw new ArgumentException("LoanType must be 'New' or 'Reloan'.")
    };
}
