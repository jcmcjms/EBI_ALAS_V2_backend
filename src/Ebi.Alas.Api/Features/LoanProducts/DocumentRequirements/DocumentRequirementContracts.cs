namespace Ebi.Alas.Api.Features.LoanProducts.DocumentRequirements;

public sealed record DocumentRequirementItemDto(string Code, string Description, string? LoanType = null);

public sealed record ReplaceDocumentRequirementsRequest(
    IReadOnlyList<DocumentRequirementItemDto> Items);
