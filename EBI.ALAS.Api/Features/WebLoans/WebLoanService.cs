using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Assembles ALAS loan-application sections from the read-only WebLoan database.
/// Source tables: cis_info (client master), loan_acct_info (client→account),
/// loan_data (PN records), loan_status / loan_product (lookups).
/// </summary>
public class WebLoanService : IWebLoanService
{
    private readonly WebLoanDbContext _db;
    private readonly ILogger<WebLoanService> _logger;

    // loan_status codes considered CLOSED/PAYOFF — excluded from Outstanding Loans
    // per requirement "do not include accounts for payoff".
    private static readonly byte[] PayoffStatuses = [5, 6, 7, 8]; // TPL total, Write-Off, Total, Transfer Asset

    public WebLoanService(WebLoanDbContext db, ILogger<WebLoanService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<WebLoanBorrowerResponse?> GetBorrowerByCisAsync(string cisNo, CancellationToken ct = default)
    {
        // ─── Client master ───────────────────────────────────────────────
        var cis = await _db.CisInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CisNo == cisNo, ct);

        if (cis is null)
            return null;

        // ─── Accounts owned by client (LAI) ──────────────────────────────
        var accounts = await _db.LoanAcctInfos
            .AsNoTracking()
            .Where(a => a.CisNo == cisNo)
            .ToListAsync(ct);

        var accountNos = accounts.Select(a => a.AccountNo).ToList();

        // ─── All PN records for those accounts (ledger rows have no PN — excluded) ──
        var loans = accountNos.Count == 0
            ? new List<LoanData>()
            : await _db.LoanDatas
                .AsNoTracking()
                .Where(l => accountNos.Contains(l.AccountNo) && l.LoanNo != null)
                .ToListAsync(ct);

        // ─── Lookups (small tables) ──────────────────────────────────────
        var statuses = await _db.LoanStatuses.AsNoTracking().ToListAsync(ct);
        var products = await _db.LoanProducts.AsNoTracking().ToListAsync(ct);

        string? StatusDesc(byte? code) => code is null
            ? null
            : statuses.FirstOrDefault(s =>
                  int.TryParse(s.IdCode, out var id) && id == code.Value)?.Description;

        string? ProductDesc(string? code) => code is null
            ? null
            : products.FirstOrDefault(p => p.IdCode == code)?.Description;

        var response = new WebLoanBorrowerResponse
        {
            // ─── Branch & Type ────────────────────────────────────────
            BranchAndType =
            {
                CisNo = cis.CisNo,
                BranchCode = cis.BranchCode,
                Type = accounts.FirstOrDefault()?.BorrowerType,
                Lai = accounts.Select(a => a.AccountNo).ToList()
            },

            // ─── Personal Information ─────────────────────────────────
            PersonalInformation =
            {
                FirstName = cis.FirstName,
                MiddleName = cis.MiddleName,
                LastName = cis.LastName,
                Suffix = cis.Appelation,
                Birthdate = ParseBirthDate(cis.BirthDateRaw),
                Address = BuildAddress(cis),
                AgencyName = cis.Company,
                AgencyTypeCode = cis.CompanyTypeCode,
                PositionTitle = cis.JobTitle,
                RegionCode = cis.RegionCode,
                DivisionCode = cis.DivisionCode,
                StationCode = cis.StationCode,
                EmployeeNo = cis.EmployeeNo,
                MisAgency = accounts.FirstOrDefault()?.MisGroup
            },

            // Loan Information — driven by the most recent non-closed PN
            LoanInformation = BuildLoanInformation(loans, StatusDesc, ProductDesc),

            // Outstanding Loans — exclude closed & payoff/write-off accounts
            OutstandingLoans = loans
                .Where(l => l.CloseDate is null
                            && (l.StatusCode is null || !PayoffStatuses.Contains(l.StatusCode.Value)))
                .OrderByDescending(l => l.DateGranted)
                .Select(l => new OutstandingLoanItem
                {
                    Pn = l.LoanNo!,
                    AccountNo = l.AccountNo,
                    PrincipalBalance = l.PrincipalBalance,
                    Amortization = l.AmortizationAmount,
                    OutstandingBalance = l.OutstandingBalance,
                    DateGranted = l.DateGranted,
                    DateMaturity = l.DateMaturity,
                    Status = StatusDesc(l.StatusCode)
                })
                .ToList(),

            // EBI reloan accounts — all existing accounts incl. recently closed,
            // excluding write-offs. Deductions/PayToClose computed later in ALAS.
            EbiReloanAccounts = loans
                .Where(l => l.StatusCode is null || !PayoffStatuses.Contains(l.StatusCode.Value))
                .OrderByDescending(l => l.DateGranted)
                .Select(l => new EbiReloanAccountItem
                {
                    Pn = l.LoanNo!,
                    Name = accounts.FirstOrDefault(a => a.AccountNo == l.AccountNo)?.Name,
                    PrincipalBalance = l.PrincipalBalance,
                    Status = StatusDesc(l.StatusCode)
                })
                .ToList()

            // BuyOutAccounts and IncomingLoans intentionally left empty:
            // no source table exists in webloan; these are captured in ALAS.
        };

        return response;
    }

    private static LoanInformationSection BuildLoanInformation(
        List<LoanData> loans,
        Func<byte?, string?> statusDesc,
        Func<string?, string?> productDesc)
    {
        // Most recent active (non-closed) loan represents current loan parameters.
        var latest = loans
            .Where(l => l.CloseDate is null)
            .OrderByDescending(l => l.DateGranted)
            .FirstOrDefault();

        if (latest is null)
            return new LoanInformationSection();

        return new LoanInformationSection
        {
            ProductCode = latest.ProductCode,
            ProductDescription = productDesc(latest.ProductCode),
            TermMonths = latest.TotalAmortization,
            PaymentIntervalMonths = latest.PaymentInterval,
            InterestRate = latest.GrantedRate ?? latest.EffectiveRate,
            Purpose = latest.Purpose,
            ProposedAmount = latest.AppliedPrincipal ?? latest.Principal
        };
    }

    // webloan stores p_bday as varchar; formats observed: MM/dd/yyyy, yyyy-MM-dd.
    private static readonly string[] BirthDateFormats = ["MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss"];

    private static DateTime? ParseBirthDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return DateTime.TryParseExact(
            raw, BirthDateFormats, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.TryParse(raw, out dt) ? dt : null;
    }

    private static string? BuildAddress(CisInfo c)
    {
        var parts = new[] { c.HouseStreet, c.Village, c.Barangay, c.City, c.StateProvince, c.Zip }
            .Where(p => !string.IsNullOrWhiteSpace(p));

        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined;
    }
}
