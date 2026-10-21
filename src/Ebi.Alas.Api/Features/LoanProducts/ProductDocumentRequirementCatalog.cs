using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Features.LoanProducts;

public static class ProductDocumentRequirementCatalog
{
    public static readonly IReadOnlyList<(string ProductCode, LoanType LoanType, string Code, string Description)> Defaults =
    [
        .. ForApds("A16"),
        .. ForApds("A17"),
        .. For("C21", LoanType.New,
        [
            ("A1004", "Duly Accomplished Application Form"),
            ("CCR38", "CASH FLOW"),
            ("A2008", "Proof of Income, Payslip, COE, Remittance Receipt"),
            ("PIC03", "CLIENT SELFIE WITH ID"),
            ("A2020", "CIBI Report"),
            ("A2023", "Bank Statement"),
            ("A2027", "Others Required Docs (Optional)"),
        ]),
        .. For("C21", LoanType.Reloan,
        [
            ("A1004", "Duly Accomplished Application Form"),
            ("CCR38", "CASH FLOW"),
            ("A2008", "Proof of Income, Payslip, COE, Remittance Receipt"),
            ("A2027", "Others Required Docs (Optional)"),
        ]),
        .. ForAtmGroup("C02"),
        .. ForAtmGroup("C23"),
        .. ForAtmGroup("C35"),
    ];

    private static IEnumerable<(string ProductCode, LoanType LoanType, string Code, string Description)> ForApds(string productCode) =>
    [
        .. For(productCode, LoanType.New,
        [
            ("A1004", "Duly Accomplished Application Form"),
            ("CCR38", "CASH FLOW"),
            ("A2016", "Confirmation From DEPED Verifier"),
            ("A2008", "Proof of Income, Payslip, COE, Remittance Receipt"),
            ("PIC03", "CLIENT SELFIE WITH ID"),
            ("A2020", "CIBI Report"),
        ]),
        .. For(productCode, LoanType.Reloan,
        [
            ("CCR38", "CASH FLOW"),
            ("A2016", "Confirmation From DEPED Verifier"),
            ("A2008", "Proof of Income, Payslip, COE, Remittance Receipt"),
        ]),
    ];

    private static IEnumerable<(string ProductCode, LoanType LoanType, string Code, string Description)> ForAtmGroup(string productCode) =>
        For(productCode, LoanType.New,
        [
            ("A1004", "Duly Accomplished Application Form"),
            ("CCR38", "CASH FLOW"),
            ("A2008", "Proof of Income, Payslip, COE, Remittance Receipt"),
        ]);

    private static IEnumerable<(string ProductCode, LoanType LoanType, string Code, string Description)> For(
        string productCode,
        LoanType loanType,
        (string Code, string Description)[] items) =>
        items.Select(i => (productCode, loanType, i.Code, i.Description));
}
