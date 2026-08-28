using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.WebLoans;

/// <summary>
/// Assembles ALAS loan-application sections from the read-only WebLoan database.
/// Source tables: cis_info (client master), cis_info_misc_data (per-client attributes),
/// loan_acct_info (client→account), loan_data (PN records), loan_status / loan_product
/// (lookups), mis_group (multi-purpose hierarchy — group_no=2 holds requesting
/// officers, group_no=14 holds agency types).
/// </summary>
public class WebLoanService : IWebLoanService
{
    private readonly WebLoanDbContext _db;
    private readonly ILogger<WebLoanService> _logger;

    // loan_status codes considered CLOSED/PAYOFF — excluded from Outstanding Loans
    // per requirement "do not include accounts for payoff".
    private static readonly byte[] PayoffStatuses = [5, 6, 7, 8]; // TPL total, Write-Off, Total, Transfer Asset

    // mis_group.group_no that holds the requesting officer hierarchy.
    // loan_acct_info.solicitor is a path; the description of the matching row
    // (e.g. "ALDREX JOEY L. CEZAR") is what we display as the requesting officer.
    private const int RequestingOfficerGroupNo = 2;

    // mis_group.group_no that holds the agency type hierarchy. Reached via
    // cis_info_misc_data.value_str when cis_info_misc_data.id_code = 14
    // (see CisInfoMiscData.AgencyTypeIdCode).
    private const int AgencyTypeGroupNo = 14;

    public WebLoanService(WebLoanDbContext db, ILogger<WebLoanService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ─── Step 1: CIS Search ────────────────────────────────────────────────

    public async Task<CisSearchResult?> SearchCisAsync(string cisNo, CancellationToken ct = default)
    {
        var cis = await _db.CisInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CisNo == cisNo, ct);

        if (cis is null)
            return null;

        var accounts = await _db.LoanAcctInfos
            .AsNoTracking()
            .Where(a => a.CisNo == cisNo)
            .ToListAsync(ct);

        var accountNos = accounts.Select(a => a.AccountNo).ToList();

        // Get PN counts per account
        var pnCounts = accountNos.Count == 0
            ? new Dictionary<string, int>()
            : await _db.LoanDatas
                .AsNoTracking()
                .Where(l => accountNos.Contains(l.AccountNo) && l.LoanNo != null)
                .GroupBy(l => l.AccountNo)
                .Select(g => new { AccountNo = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AccountNo, x => x.Count, ct);

        return new CisSearchResult
        {
            CisNo = cis.CisNo,
            FullName = $"{cis.FirstName} {cis.MiddleName} {cis.LastName} {cis.Appelation}".Trim(),
            BranchCode = cis.BranchCode ?? string.Empty,
            Accounts = accounts.Select(a => new CisAccountSummary
            {
                AccountNo = a.AccountNo,
                AccountName = a.Name,
                AccountAddress = BuildAccountAddress(a),
                MisGroup = a.MisGroup,
                PnCount = pnCounts.GetValueOrDefault(a.AccountNo, 0)
            }).ToList()
        };
    }

    // ─── Step 2: Account Detail with PNs ──────────────────────────────────

    public async Task<AccountWithPnsResponse?> GetAccountWithPnsAsync(string cisNo, string accountNo, CancellationToken ct = default)
    {
        // Verify account belongs to this CIS
        var account = await _db.LoanAcctInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.CisNo == cisNo && a.AccountNo == accountNo, ct);

        if (account is null)
            return null;

        // Get active PN records for this account using is_loan(loan_no) = 1 and loan_status != 10
        // Matches the sample query: WHERE webloan.dbo.is_loan(ld.loan_no) = 1 AND loan_status != 10
        var loans = await _db.LoanDatas
            .FromSqlRaw<LoanData>(@"
                SELECT *
                FROM dbo.loan_data
                WHERE acct_no = {0}
                  AND loan_no IS NOT NULL
                  AND webloan.dbo.is_loan(loan_no) = 1
                  AND loan_status != 10
                ORDER BY date_granted DESC", accountNo)
            .AsNoTracking()
            .ToListAsync(ct);

        // Lookups
        var statuses = await _db.LoanStatuses.AsNoTracking().ToListAsync(ct);
        var products = await _db.LoanProducts.AsNoTracking().ToListAsync(ct);

        string? StatusDesc(byte? code) => code is null
            ? null
            : statuses.FirstOrDefault(s =>
                int.TryParse(s.IdCode, out var id) && id == code.Value)?.Description;

        string? ProductDesc(string? code) => code is null
            ? null
            : products.FirstOrDefault(p => p.IdCode == code)?.Description;

        return new AccountWithPnsResponse
        {
            AccountNo = account.AccountNo,
            AccountName = account.Name,
            AccountAddress = BuildAccountAddress(account),
            MisGroup = account.MisGroup,
            PnRecords = loans.Select(l => new PnRecord
            {
                PnNumber = l.LoanNo!,
                ProductCode = l.ProductCode,
                ProductDescription = ProductDesc(l.ProductCode),
                CreationType = l.CreationType,
                CreationTypeLabel = WebLoanCreationTypes.GetLabel(l.CreationType),
                Principal = l.Principal,
                AppliedPrincipal = l.AppliedPrincipal,
                PrincipalBalance = l.PrincipalBalance,
                AmortizationAmount = l.AmortizationAmount,
                OutstandingBalance = l.OutstandingBalance,
                DateGranted = l.DateGranted,
                DateMaturity = l.DateMaturity,
                StatusCode = l.StatusCode,
                StatusDescription = StatusDesc(l.StatusCode),
                CloseDate = l.CloseDate,
                GrantedRate = l.GrantedRate,
                EffectiveRate = l.EffectiveRate,
                Purpose = l.Purpose,
                PaymentInterval = l.PaymentInterval,
                TotalAmortization = l.TotalAmortization
            }).ToList()
        };
    }

    // ─── Original full profile (kept for backward compatibility) ────────────

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

        // Most recent non-closed PN drives both BranchAndType.Type and LoanInformation.
        var latestActiveLoan = loans
            .Where(l => l.CloseDate is null)
            .OrderByDescending(l => l.DateGranted)
            .FirstOrDefault();

        // ─── Mis-group lookups (requesting officer + MIS agency) ─────────
        // Pull the union of every distinct (solicitor path, mis_group2 path)
        // we need to resolve, in one round-trip.
        var solicitorPaths = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Solicitor))
            .Select(a => a.Solicitor!)
            .Distinct()
            .ToList();

        var misGroup2Paths = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.MisGroup2))
            .Select(a => a.MisGroup2!)
            .Distinct()
            .ToList();

        var allPaths = solicitorPaths.Union(misGroup2Paths).Distinct().ToList();

        var misGroupByPath = allPaths.Count == 0
            ? new Dictionary<string, MisGroup>(StringComparer.OrdinalIgnoreCase)
            : await _db.MisGroups
                .AsNoTracking()
                .Where(m => m.GroupNo == RequestingOfficerGroupNo && m.Path != null && allPaths.Contains(m.Path))
                .ToDictionaryAsync(m => m.Path!, m => m, ct);

        // Pull the row for each loan_acct_info.solicitor individually — group_no=2 is
        // the officer hierarchy, and within that the same path can appear at different
        // levels. We want the one with the highest grp_level (the most specific node).
        // Done client-side over the small result set.
        var officerByPath = new Dictionary<string, MisGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in solicitorPaths)
        {
            var candidates = await _db.MisGroups
                .AsNoTracking()
                .Where(m => m.GroupNo == RequestingOfficerGroupNo && m.Path == path)
                .ToListAsync(ct);
            // If the exact path is missing, fall back to the longest prefix that exists.
            // (Some accounts store a parent path rather than a leaf — rare but observed.)
            if (candidates.Count == 0)
            {
                MisGroup? best = null;
                foreach (var m in await _db.MisGroups
                    .AsNoTracking()
                    .Where(m => m.GroupNo == RequestingOfficerGroupNo && m.Path != null && path.StartsWith(m.Path))
                    .ToListAsync(ct))
                {
                    if (best is null || (m.Path!.Length > best.Path!.Length))
                        best = m;
                }
                if (best is not null) officerByPath[path] = best;
            }
            else
            {
                officerByPath[path] = candidates
                    .OrderByDescending(m => m.GrpLevel ?? 0)
                    .First();
            }
        }

        // ─── Agency type via cis_info_misc_data (id_code=14) ─────────────
        // cis_info_misc_data.value_str is a mis_group.id_code within the
        // agency-type group_no. We fetch the row directly and then resolve it.
        var agencyTypeRow = await _db.CisInfoMiscDatas
            .AsNoTracking()
            .Where(m => m.CisNo == cisNo && m.IdCode == CisInfoMiscData.AgencyTypeIdCode)
            .FirstOrDefaultAsync(ct);

        string? agencyTypeDescription = null;
        if (agencyTypeRow is { ValueStr: { Length: > 0 } idCode })
        {
            agencyTypeDescription = await _db.MisGroups
                .AsNoTracking()
                .Where(m => m.GroupNo == AgencyTypeGroupNo && m.IdCode == idCode)
                .Select(m => m.Description)
                .FirstOrDefaultAsync(ct);
        }

        // ─── Pick the primary account: the one whose most-recent PN is the
        //    "latest active loan". The requesting officer comes from that account.
        var primaryAccount = latestActiveLoan is null
            ? accounts.FirstOrDefault()
            : accounts.FirstOrDefault(a => a.AccountNo == latestActiveLoan.AccountNo) ?? accounts.FirstOrDefault();

        var primarySolicitor = primaryAccount?.Solicitor;
        var primarySolicitorDescription =
            primarySolicitor is not null && officerByPath.TryGetValue(primarySolicitor, out var mg)
                ? mg.Description
                : null;

        var primaryMisGroup2Description =
            primaryAccount?.MisGroup2 is { } mg2Path
                ? misGroupByPath.GetValueOrDefault(mg2Path)?.Description
                : null;

        var response = new WebLoanBorrowerResponse
        {
            // ─── Branch & Type ────────────────────────────────────────
            BranchAndType =
            {
                CisNo = cis.CisNo,
                BranchCode = cis.BranchCode,
                Type = WebLoanCreationTypes.GetLabel(latestActiveLoan?.CreationType),
                TypeCode = latestActiveLoan?.CreationType,
                // Pulled from loan_acct_info.solicitor on the primary account,
                // resolved through dbo.mis_group (group_no=2) for the human name.
                RequestingOfficer = primarySolicitorDescription,
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
                // Decoded from cis_info_misc_data (id_code=14) → mis_group
                // (group_no=14) description. Falls back to null if the
                // borrower has no misc row or the id_code is unknown.
                AgencyType = agencyTypeDescription,
                PositionTitle = cis.JobTitle,
                RegionCode = cis.RegionCode,
                DivisionCode = cis.DivisionCode,
                StationCode = cis.StationCode,
                EmployeeNo = cis.EmployeeNo,
                // Raw primary MIS path (e.g. "INDIV/SAL").
                MisAgency = accounts.FirstOrDefault()?.MisGroup,
                // Resolved secondary MIS path (e.g. "DEPED LIANGA") from
                // loan_acct_info.cat_mis_group2 → mis_group.path.
                MisAgencyName = primaryMisGroup2Description
            },

            // Loan Information — driven by the most recent non-closed PN
            LoanInformation = BuildLoanInformation(latestActiveLoan, ProductDesc),

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
        LoanData? latest,
        Func<string?, string?> productDesc)
    {
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

    private static string? BuildAccountAddress(LoanAcctInfo a)
    {
        var parts = new[] { a.Add1, a.Add2 }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(", ", parts);
        return joined.Length == 0 ? null : joined;
    }
}
