using Ebi.Alas.Api.Features.LoanApplications.Domain;

namespace Ebi.Alas.Api.Features.LoanApplications;

/// <summary>Default monitoring filter statuses (active pipeline only).</summary>
public static class QueueDefaults
{
    public static readonly IReadOnlyList<string> Statuses =
    [
        nameof(LoanStatus.Draft),
        nameof(LoanStatus.ForRecommendation),
        nameof(LoanStatus.ForChecking),
        nameof(LoanStatus.ForApproval),
        nameof(LoanStatus.ForRevision),
        nameof(LoanStatus.ForIncompleteDocuments),
        nameof(LoanStatus.ForDisbursement),
        nameof(LoanStatus.Approved),
    ];
}
