namespace Ebi.Alas.Api.Features.LoanProducts;

public static class ProductDocumentRequirementCatalog
{
    public static readonly IReadOnlyList<(string ProductCode, string Code)> Defaults =
    [
        .. For("A16",
            "A1004", "A2008", "A2016", "A2017", "A2018", "A2019", "A2020", "A2021", "A2035", "A4001", "CCR38", "PIC02", "PIC03"),
        .. For("A17",
            "A1004", "A2008", "A2016", "A2017", "A2018", "A2019", "A2020", "A2021", "A2022", "A2027", "A2035", "A4001", "CCR38", "PIC02", "PIC03"),
        .. For("C02",
            "A1004", "A2008", "A2018", "A2019", "A2027", "A4001", "CCR38", "PIC02", "PIC03"),
        .. For("C21",
            "A1004", "A2008", "A2018", "A2019", "A2020", "A2021", "A2022", "A2023", "A2027", "A4001", "CCR38", "PIC02", "PIC03"),
        .. For("C23",
            "A1004", "A2008", "A2018", "A2019", "A2027", "A4001", "CCR38", "PIC02", "PIC03"),
        .. For("C35",
            "A1004", "A2008", "A2018", "A2019", "A2027", "A4001", "CCR38", "PIC02", "PIC03"),
    ];

    private static IEnumerable<(string ProductCode, string Code)> For(string productCode, params string[] codes) =>
        codes.Select(code => (productCode, code));
}
