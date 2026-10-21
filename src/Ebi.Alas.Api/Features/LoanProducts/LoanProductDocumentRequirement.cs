using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Features.LoanProducts;

public sealed class LoanProductDocumentRequirement
{
    private LoanProductDocumentRequirement()
    {
        ProductCode = string.Empty;
        Code = string.Empty;
        Description = string.Empty;
    }

    public Guid Id { get; private set; }

    public string ProductCode { get; private set; }

    public LoanType LoanType { get; private set; }

    public string Code { get; private set; }

    public string Description { get; private set; }

    public static LoanProductDocumentRequirement Create(
        string productCode,
        LoanType loanType,
        string code,
        string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return new LoanProductDocumentRequirement
        {
            Id = Guid.NewGuid(),
            ProductCode = productCode.Trim(),
            LoanType = loanType,
            Code = code.Trim(),
            Description = description.Trim()
        };
    }
}
