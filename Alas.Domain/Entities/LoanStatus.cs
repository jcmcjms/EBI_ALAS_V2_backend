namespace Alas.Domain.Entities;

public enum LoanStatus
{
    Draft = 0,
    PendingReview = 1,
    PendingApproval = 2,
    Approved = 3,
    Disbursed = 4,
    Rejected = 5,
    Cancelled = 6
}
