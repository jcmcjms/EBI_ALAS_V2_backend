using System.Globalization;
namespace EBI.ALAS.Api.Features.WebLoans;
public class WebLoanService(IWebLoanRepository repository) : IWebLoanService
{
    public async Task<CisSearchResponse?> SearchByCisAsync(
        string cisNo,
        string? bch,
        CancellationToken ct = default)
    {
        var cisTask = repository.GetCisInfoAsync(cisNo, ct);
        var accountsTask = repository.GetAccountsByCisAsync(cisNo, bch, ct);
        var agencyTypeTask = repository.GetAgencyTypeAsync(cisNo, ct);
        var lengthOfServiceTask = repository.GetLengthOfServiceAsync(cisNo, ct);
        var cis = await cisTask;
        if (cis is null) return null;
        await Task.WhenAll(accountsTask, agencyTypeTask, lengthOfServiceTask);
        var accounts = await accountsTask;
        var agencyType = await agencyTypeTask;
        var lengthOfServiceRow = await lengthOfServiceTask;
        var lengthOfService = ComputeLengthOfService(lengthOfServiceRow?.Description);
        var group2Paths = accounts
            .Select(a => a.MisGroup2)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Cast<string>()
            .ToList();
        var solicitorPaths = accounts
            .Select(a => a.Solicitor)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Cast<string>()
            .ToList();
        var agencyDescTask = agencyType?.ValueStr is { Length: > 0 } agencyCode
            ? repository.GetMisGroupByIdCodeAsync(agencyCode, ct)
            : Task.FromResult<MisGroup?>(null);
        var misGroupsByPathTask = group2Paths.Count > 0
            ? repository.GetMisGroupsByPathsAsync(group2Paths, ct)
            : Task.FromResult<IReadOnlyList<MisGroup>>(Array.Empty<MisGroup>());
        var solicitorsByPathTask = solicitorPaths.Count > 0
            ? repository.GetSolicitorsByPathsAsync(solicitorPaths, ct)
            : Task.FromResult<IReadOnlyList<MisGroup>>(Array.Empty<MisGroup>());
        await Task.WhenAll(agencyDescTask, misGroupsByPathTask, solicitorsByPathTask);
        var agencyGroup = await agencyDescTask;
        var misGroupsByPath = await misGroupsByPathTask;
        var solicitorsByPath = await solicitorsByPathTask;
        var byPath = misGroupsByPath
            .Where(m => !string.IsNullOrEmpty(m.Path))
            .ToDictionary(m => m.Path!, m => m.Description);
        var bySolicitorPath = solicitorsByPath
            .Where(m => !string.IsNullOrEmpty(m.Path))
            .ToDictionary(m => m.Path!, m => m.Description);
        string? misAgencyDescription = null;
        if (accounts.Count > 0 &&
            !string.IsNullOrWhiteSpace(accounts[0].MisGroup2) &&
            byPath.TryGetValue(accounts[0].MisGroup2!, out var misAgencyDesc))
        {
            misAgencyDescription = misAgencyDesc;
        }
        string? solicitorDescription = null;
        if (accounts.Count > 0 &&
            !string.IsNullOrWhiteSpace(accounts[0].Solicitor) &&
            bySolicitorPath.TryGetValue(accounts[0].Solicitor!, out var solDesc))
        {
            solicitorDescription = solDesc;
        }
        var borrower = new BorrowerDto(
            CisNo: cis.CisNo,
            FirstName: cis.FirstName,
            MiddleName: cis.MiddleName,
            LastName: cis.LastName,
            Title: cis.Title,
            Appelation: cis.Appelation,
            BirthDate: ParseBirthDate(cis.BirthDateRaw),
            Address: BuildAddress(cis),
            AgencyType: agencyGroup?.Description,
            PositionTitle: cis.Occupation,
            Region: WebLoanRegions.Resolve(cis.RegionCode),
            RegionCode: cis.RegionCode,
            DivisionCode: cis.DivisionCode,
            StationCode: cis.StationCode,
            EmployeeNumber: cis.EmployeeNo,
            MisAgency: misAgencyDescription,
            RequestingOfficer: solicitorDescription,
            LengthOfService: lengthOfService);
        var accountDtos = accounts.Select(a => new AccountDto(
            BankCode: a.BankCode,
            BranchCode: a.BranchCode,
            AccountNo: a.AccountNo,
            AccountId: WebLoanAccountId.Format(a.BranchCode, a.AccountNo),
            Name: a.Name,
            CreditLimit: a.CreditLimit,
            UsedCredit: a.UsedCredit,
            BorrowerType: a.BorrowerType
        )).ToList();
        return new CisSearchResponse(borrower, accountDtos);
    }
    public async Task<OutstandingLoansResponse?> GetOutstandingLoansAsync(
        string cisNo,
        string accountId,
        int pageSize = 50,
        int pageNumber = 1,
        CancellationToken ct = default)
    {
        var (branchCode, accountNo) = WebLoanAccountId.Parse(accountId);
        var belongs = await repository.AccountBelongsToCisAsync(cisNo, branchCode, accountNo, ct);
        if (!belongs) return null;
        var rows = await repository.GetOutstandingLoansAsync(branchCode, accountNo, pageSize, pageNumber, ct);
        var loans = rows
            .OrderByDescending(r => r.DateGranted ?? DateTime.MinValue)
            .Select(r =>
            {
                var status = WebLoanRegions.ResolveLoanStatus(r.StatusCode);
                var statusLabel = WebLoanRegions.Label(status);
                var productCode = r.ProductCode ?? string.Empty;
                var productWithDesc = (r.ProductWithDescription ?? string.Empty).TrimEnd();
                if (productWithDesc.EndsWith(" - ", StringComparison.Ordinal))
                {
                    productWithDesc = productWithDesc[..^3];
                }
                return new OutstandingLoanDto(
                    LoanNo: r.LoanNo,
                    Principal: r.Principal,
                    PrincipalBalance: r.PrincipalBalance,
                    AmortAmount: r.ComputedAmortAmount,
                    DateGranted: r.DateGranted,
                    DateMaturity: r.DateMaturity,
                    ProductCode: productCode,
                    ProductStatus: $"{productCode} - {statusLabel}",
                    ProductWithDescription: productWithDesc);
            })
            .ToList();
        return new OutstandingLoansResponse(
            CisNo: cisNo,
            AccountId: accountId,
            BranchCode: branchCode,
            AccountNo: accountNo,
            Loans: loans);
    }
    public async Task<PendingLoanResponse?> GetPendingLoanAsync(
        string cisNo,
        string accountId,
        CancellationToken ct = default)
    {
        var (branchCode, accountNo) = WebLoanAccountId.Parse(accountId);
        var belongs = await repository.AccountBelongsToCisAsync(cisNo, branchCode, accountNo, ct);
        if (!belongs) return null;
        var rows = await repository.GetPendingLoansAsync(branchCode, accountNo, ct);
        var nthpRow = rows.FirstOrDefault();
        var dtos = rows
            .GroupBy(r => r.LoanNo)
            .Select(g => g.First())
            .Select(r =>
            {
                var productWithDesc = (r.ProductWithDescription ?? string.Empty).TrimEnd();
                if (productWithDesc.EndsWith(" - ", StringComparison.Ordinal))
                {
                    productWithDesc = productWithDesc[..^3];
                }
                return new PendingLoanDto(
                    LoanNo: r.LoanNo,
                    Principal: r.Principal,
                    GrantedRate: r.GrantedRate,
                    TotalTermDays: r.TotalTermDays,
                    PolicyTermMonths: r.TotalAmortization,
                    ProductWithDescription: productWithDesc,
                    LoanPurpose: r.LoanPurpose,
                    CreationType: r.CreationType,
                    CreationTypeLabel: string.IsNullOrEmpty(r.CreationTypeLabel)
                        ? WebLoanRegions.CreationTypeLabel(r.CreationType)
                        : r.CreationTypeLabel,
                    CDocStamp: r.CDocStamp);
            })
            .ToList();
        return new PendingLoanResponse(
            CisNo: cisNo,
            AccountId: accountId,
            BranchCode: branchCode,
            AccountNo: accountNo,
            Loans: dtos,
            Nthp: nthpRow?.Nthp,
            NthpDate: nthpRow?.NthpDate);
    }
    public async Task<IReadOnlyList<LoanProductDto>> GetActiveLoanProductsAsync(CancellationToken ct = default)
    {
        var rows = await repository.GetActiveLoanProductsAsync(ct);
        return rows
            .Select(p => new LoanProductDto(p.IdCode, p.Description))
            .ToList();
    }
    public async Task<CatLoanClassResponse?> GetCatLoanClassAsync(
        string bch,
        string loanNo,
        string loanProduct,
        CancellationToken ct = default)
    {
        var catLoanClass = await repository.GetCatLoanClassAsync(bch, loanNo, loanProduct, ct);
        if (catLoanClass is null) return null;
        return new CatLoanClassResponse(
            Bch: bch,
            LoanNo: loanNo,
            LoanProduct: loanProduct,
            CatLoanClass: string.IsNullOrEmpty(catLoanClass) ? null : catLoanClass);
    }
    public async Task<CocreeStatusResponse> GetCocreeStatusAsync(
        string cisNo,
        CancellationToken ct = default)
    {
        var rows = await repository.GetCocreeItemsAsync(cisNo, ct);
        var byItem = rows
            .Where(r => !string.IsNullOrEmpty(r.CheckListItem))
            .ToDictionary(r => r.CheckListItem, r => r);
        var items = CheckListData.CocreeItems
            .Select(code =>
            {
                byItem.TryGetValue(code, out var row);
                return new CocreeItemStatus(
                    ItemCode: code,
                    Submitted: row?.Submitted,
                    Description: row?.Description,
                    Expiration: row?.Expiration);
            })
            .ToList();
        var isComplete = items.All(i => i.Submitted.HasValue);
        return new CocreeStatusResponse(
            CisNo: cisNo,
            IsComplete: isComplete,
            Items: items);
    }
    private static DateTime? ParseBirthDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        string[] formats =
        {
            "yyyy-MM-dd",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "yyyy/MM/dd",
            "dd-MM-yyyy"
        };
        if (DateTime.TryParseExact(
                raw.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dt))
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }
        return DateTime.TryParse(
            raw.Trim(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var fallback)
            ? DateTime.SpecifyKind(fallback, DateTimeKind.Utc)
            : null;
    }
    private static string? ComputeLengthOfService(string? rawHireDate)
    {
        if (string.IsNullOrWhiteSpace(rawHireDate)) return null;
        var hireDate = ParseBirthDate(rawHireDate);
        if (hireDate is null) return null;
        var now = DateTime.UtcNow;
        var totalMonths = Math.Max(0, ((now.Year - hireDate.Value.Year) * 12) + (now.Month - hireDate.Value.Month));
        var years = totalMonths / 12;
        var months = totalMonths % 12;
        return $"{years} years, {months} months";
    }
    private static string? BuildAddress(CisInfo cis)
    {
        var parts = new List<string?>
        {
            cis.Zip,
            cis.HouseStreet,
            cis.City,
            cis.StateProvince,
            cis.Barangay,
            cis.Village
        }
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Select(p => p!.Trim())
        .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
