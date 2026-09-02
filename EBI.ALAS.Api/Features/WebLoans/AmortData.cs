using System.ComponentModel.DataAnnotations.Schema;

namespace EBI.ALAS.Api.Features.WebLoans;

// ─── amort_data ─────────────────────────────────────────────────────────────
// Per-loan amortization schedule rows in webloan. webloan stores one row per
// scheduled amortization installment; the `amort_no` column is the installment
// ordinal (1 = first scheduled payment, 2 = second, ...). For the outstanding-
// loans endpoint we only ever want the FIRST scheduled installment, filtered
// by `amort_no = 1` — that's the canonical "monthly amortization amount" the
// UI displays.
//
// Keyed by (bk, bch, acct_no, loan_no, amort_no) on the webloan side; we model
// it as keyless here because this context is read-only and we never fetch by
// the primary key directly — every query in this codebase joins to it from
// loan_data on (bk, bch, acct_no, loan_no) and then filters amort_no = 1.
[Table("amort_data", Schema = "dbo")]
public class AmortData
{
    // Composite-key parts (kept individually so EF can populate them and the
    // service layer can use them as JOIN targets — same pattern as
    // PreLoanData).
    [Column("bk")] public string BankCode { get; set; } = string.Empty;
    [Column("bch")] public string BranchCode { get; set; } = string.Empty;
    [Column("acct_no")] public string AccountNo { get; set; } = string.Empty;
    [Column("loan_no")] public string? LoanNo { get; set; }

    // Installment ordinal. The outstanding-loans query filters `amort_no = 1`
    // (the first scheduled payment); see WebLoanRepository.GetOutstandingLoansAsync.
    [Column("amort_no")] public int? AmortNo { get; set; }

    // The amortization amount the UI displays for this loan. Combined with
    // the CASE expression in the outstanding-loans SQL: when loan_product is
    // 'C35' or 'C23' we surface `loan_data.principal` instead, because those
    // products do not have a meaningful amort_data.total_amort row.
    [Column("total_amort")] public decimal? TotalAmort { get; set; }
}