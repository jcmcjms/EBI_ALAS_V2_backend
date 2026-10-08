namespace Ebi.Alas.Api.Features.LoanApplications.Domain;

public sealed class LoanApplication
{
    private LoanApplication()
    {
        LamId = string.Empty;
        ApplicationGroupNo = string.Empty;
        ClientName = string.Empty;
        BranchId = string.Empty;
    }

    public Guid Id { get; private set; }

    public string LamId { get; private set; }

    public string ApplicationGroupNo { get; private set; }

    public string ClientName { get; private set; }

    public string BranchId { get; private set; }

    public LoanType LoanType { get; private set; }

    public LoanStatus Status { get; private set; }

    public decimal Principal { get; private set; }

    public int TermDays { get; private set; }

    public decimal InterestRate { get; private set; }

    public decimal TotalInterest { get; private set; }

    public decimal TotalDeductions { get; private set; }

    public decimal NetProceeds { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static LoanApplication Create(
        string lamId,
        string applicationGroupNo,
        string clientName,
        string branchId,
        LoanType loanType,
        decimal principal,
        int termDays,
        decimal interestRate,
        decimal totalInterest,
        decimal totalDeductions,
        decimal netProceeds,
        Guid createdBy,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationGroupNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchId);
        ArgumentOutOfRangeException.ThrowIfLessThan(principal, 0.01m);
        ArgumentOutOfRangeException.ThrowIfLessThan(termDays, 1);

        return new LoanApplication
        {
            Id = Guid.NewGuid(),
            LamId = lamId,
            ApplicationGroupNo = applicationGroupNo,
            ClientName = clientName.Trim(),
            BranchId = branchId.Trim(),
            LoanType = loanType,
            Status = LoanStatus.ForRecommendation,
            Principal = principal,
            TermDays = termDays,
            InterestRate = interestRate,
            TotalInterest = totalInterest,
            TotalDeductions = totalDeductions,
            NetProceeds = netProceeds,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void TransitionTo(LoanStatus next, DateTimeOffset now)
    {
        if (!IsValidTransition(Status, next))
        {
            throw new InvalidOperationException($"Cannot transition from {Status} to {next}.");
        }

        Status = next;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is LoanStatus.Approved or LoanStatus.Rejected or LoanStatus.Cancelled
            or LoanStatus.ForDisbursement or LoanStatus.Disbursed or LoanStatus.OnGoing)
        {
            throw new InvalidOperationException($"Cannot cancel loan in status {Status}.");
        }

        Status = LoanStatus.Cancelled;
        UpdatedAt = now;
    }

    public static bool IsValidTransition(LoanStatus from, LoanStatus to) => (from, to) switch
    {
        (LoanStatus.ForRecommendation, LoanStatus.ForChecking) => true,
        (LoanStatus.ForRecommendation, LoanStatus.ForRevision) => true,
        (LoanStatus.ForChecking, LoanStatus.ForApproval) => true,
        (LoanStatus.ForChecking, LoanStatus.ForRevision) => true,
        (LoanStatus.ForChecking, LoanStatus.ForIncompleteDocuments) => true,
        (LoanStatus.ForIncompleteDocuments, LoanStatus.ForChecking) => true,
        (LoanStatus.ForApproval, LoanStatus.Approved) => true,
        (LoanStatus.ForApproval, LoanStatus.Rejected) => true,
        (LoanStatus.ForApproval, LoanStatus.ForRevision) => true,
        (LoanStatus.ForRevision, LoanStatus.ForRecommendation) => true,
        (LoanStatus.Approved, LoanStatus.ForDisbursement) => true,
        (LoanStatus.ForDisbursement, LoanStatus.Disbursed) => true,
        (LoanStatus.Disbursed, LoanStatus.OnGoing) => true,
        _ when from == to => true,
        _ => false
    };
}
