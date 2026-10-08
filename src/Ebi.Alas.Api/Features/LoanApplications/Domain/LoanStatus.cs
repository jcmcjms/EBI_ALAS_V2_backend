namespace Ebi.Alas.Api.Features.LoanApplications.Domain;

public enum LoanStatus
{
    Draft = 0,
    ForRecommendation = 1,
    ForChecking = 2,
    ForApproval = 3,
    ForRevision = 4,
    ForIncompleteDocuments = 5,
    Approved = 6,
    Rejected = 7,
    Cancelled = 8,
    ForDisbursement = 9,
    Disbursed = 10,
    OnGoing = 11
}

public enum LoanType
{
    New = 1,
    Reloan = 2
}
