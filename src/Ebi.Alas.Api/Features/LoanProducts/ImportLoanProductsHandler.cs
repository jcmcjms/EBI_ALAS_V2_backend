using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.LoanProducts;

public sealed class ImportLoanProductsHandler(AlasDbContext db)
{
    public const int MaxImportRows = 1000;
    private const int AbsoluteMaxTermDays = 2617;

    public async Task<LoanProductImportResult> ImportAsync(Stream xlsx, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(xlsx);

        IReadOnlyList<LoanProductExcel.Row> rows;
        try
        {
            rows = LoanProductExcel.Parse(xlsx);
        }
        catch (Exception)
        {
            return new LoanProductImportResult(
                0,
                0,
                0,
                1,
                [new LoanProductImportValidationError(0, "File", "Excel file is empty or corrupted.")]);
        }

        var errors = new List<LoanProductImportValidationError>();
        var totalRows = 0;
        var created = 0;
        var updated = 0;

        foreach (var row in rows)
        {
            if (totalRows >= MaxImportRows)
            {
                errors.Add(new LoanProductImportValidationError(
                    row.RowNumber, "Row", $"Import is limited to {MaxImportRows} rows."));
                break;
            }

            totalRows++;
            var code = row.Code?.Trim();
            var existing = code is null
                ? null
                : await db.LoanProducts.FirstOrDefaultAsync(p => p.Code == code, cancellationToken);
            ValidateRow(row, isNew: existing is null, errors);
            if (errors.Any(e => e.RowNumber == row.RowNumber))
            {
                continue;
            }

            code = row.Code!.Trim();
            if (existing is null)
            {
                var product = LoanProduct.Create(
                    code,
                    row.Description!.Trim(),
                    row.AdvanceInterestRate ?? 0m,
                    row.MinTermDays!.Value,
                    row.MaxTermDays!.Value,
                    DateTimeOffset.UtcNow);
                product.UpdatePolicy(
                    row.MinAmount!.Value,
                    row.MaxAmount!.Value,
                    row.MinTermDays!.Value,
                    row.MaxTermDays!.Value,
                    row.NotarialFee ?? 0m,
                    row.DocStampFee ?? 0m,
                    row.InsuranceFee ?? 0m,
                    row.AdvanceInterestRate ?? 0m);
                product.ApplyPolicyOptions(
                    row.ApplicationChargeRate,
                    row.AmortizationMode,
                    row.ChargeAdvanceInterest);
                if (row.IsRetired is { } retiredOnCreate)
                {
                    product.SetRetired(retiredOnCreate);
                }

                db.LoanProducts.Add(product);
                created++;
                continue;
            }

            existing.ApplyCatalog(
                row.Description?.Trim() ?? existing.Name,
                row.AdvanceInterestRate ?? existing.InterestRatePerMonth,
                DateTimeOffset.UtcNow);
            existing.UpdatePolicy(
                row.MinAmount ?? existing.MinAmount,
                row.MaxAmount ?? existing.MaxAmount,
                row.MinTermDays ?? existing.MinTermDays,
                row.MaxTermDays ?? existing.MaxTermDays,
                row.NotarialFee ?? existing.NotarialFee,
                row.DocStampFee ?? existing.DocStampFee,
                row.InsuranceFee ?? existing.InsuranceFee,
                row.AdvanceInterestRate ?? existing.AdvanceInterestRate);
            existing.ApplyPolicyOptions(
                row.ApplicationChargeRate,
                row.AmortizationMode,
                row.ChargeAdvanceInterest);
            // Is Retired is ignored on update — retirement is owned by webloan sync.
            updated++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new LoanProductImportResult(
            totalRows,
            created,
            updated,
            errors.Count,
            errors);
    }

    private static void ValidateRow(
        LoanProductExcel.Row row,
        bool isNew,
        List<LoanProductImportValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(row.Code))
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Code", "Code is required."));
        }

        if (isNew && string.IsNullOrWhiteSpace(row.Description))
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Description", "Description is required."));
        }

        if (isNew && row.MinAmount is null)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Min Amount", "Min amount is required."));
        }
        else if (row.MinAmount is < 0)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Min Amount", "Min amount cannot be negative."));
        }

        if (isNew && row.MaxAmount is null)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Max Amount", "Max amount is required."));
        }
        else if (row.MaxAmount is < 0)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Max Amount", "Max amount cannot be negative."));
        }

        if (row.MinAmount is { } min && row.MaxAmount is { } max && max < min)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Max Amount", "Max amount must be greater than or equal to min amount."));
        }

        if (isNew && row.MinTermDays is null)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Min Term Days", "Min term is required."));
        }
        else if (row.MinTermDays is < 0)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Min Term Days", "Min term cannot be negative."));
        }

        if (isNew && row.MaxTermDays is null)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Max Term Days", "Max term is required."));
        }
        else if (row.MaxTermDays is < 0)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Max Term Days", "Max term cannot be negative."));
        }

        if (row.MinTermDays is { } minTerm && row.MaxTermDays is { } maxTerm && maxTerm < minTerm)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Max Term Days", "Max term must be greater than or equal to min term."));
        }

        if (row.MaxTermDays is > AbsoluteMaxTermDays)
        {
            errors.Add(new LoanProductImportValidationError(
                row.RowNumber,
                "Max Term Days",
                $"Max term cannot exceed {AbsoluteMaxTermDays} days."));
        }

        if (row.NotarialFee is < 0 || row.DocStampFee is < 0 || row.InsuranceFee is < 0)
        {
            errors.Add(new LoanProductImportValidationError(row.RowNumber, "Fees", "Fees cannot be negative."));
        }

        if (row.AdvanceInterestRate is < 0 or > 1)
        {
            errors.Add(new LoanProductImportValidationError(
                row.RowNumber,
                "Advance Interest Rate",
                "Advance interest rate must be between 0 and 1."));
        }

        if (row.ApplicationChargeRate is < 0 or > 1)
        {
            errors.Add(new LoanProductImportValidationError(
                row.RowNumber,
                "Application Charge Rate",
                "Application charge rate must be between 0 and 1."));
        }

        if (row.AmortizationMode is { Length: > 0 } mode
            && mode is not ("DIM" or "MIC"))
        {
            errors.Add(new LoanProductImportValidationError(
                row.RowNumber,
                "Amortization Mode",
                "Amortization mode must be DIM or MIC."));
        }
    }
}
