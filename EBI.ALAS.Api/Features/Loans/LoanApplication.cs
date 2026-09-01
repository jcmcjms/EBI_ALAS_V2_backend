using EBI.ALAS.Api.Features.Auth;
using System.Text.Json.Serialization;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Loan application entity with all client and loan parameter fields.
/// </summary>
public class LoanApplication
{
    public int Id { get; set; }
    public string FormNumber { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;

    // Client Information
    public string? CisId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? Agency { get; set; }
    public string? Position { get; set; }
    public string? EmployeeId { get; set; }
    public decimal? NetTakeHomePay { get; set; }

    // Additional manual-entry information (not sourced from CIS)
    public string? School { get; set; }
    public string? Referrer { get; set; }

    // Loan Parameters
    public string Product { get; set; } = string.Empty;
    public string? Purpose { get; set; }
    public decimal ProposedAmount { get; set; }
    public int TermMonths { get; set; }
    public decimal InterestRate { get; set; }
    public string? ModeOfPayment { get; set; }
    public DateTime? DateOfFirstRelease { get; set; }
    public string? CoMaker { get; set; }

    // Status & Dates
    public string Status { get; set; } = "Draft";
    public DateTime ApplicationDate { get; set; } = DateTime.UtcNow;
    public DateTime LastActionDate { get; set; } = DateTime.UtcNow;

    // Audit
    public int CreatedById { get; set; }
    public User CreatedBy { get; set; } = null!;

    // WebLoan Traceability (read-only legacy system references)
    public string? WebLoanCisNo { get; set; }
    public string? WebLoanBranchCode { get; set; }
    public List<string> WebLoanAccountNumbers { get; set; } = new();
    public List<string> WebLoanPnNumbers { get; set; } = new();
    public DateTime? WebLoanLastSyncedAt { get; set; }

    // Navigation Properties
    public ICollection<LoanAction> Actions { get; set; } = new List<LoanAction>();
    public ICollection<OutstandingLoan> OutstandingLoans { get; set; } = new List<OutstandingLoan>();
    public ICollection<BuyOut> BuyOuts { get; set; } = new List<BuyOut>();
}
