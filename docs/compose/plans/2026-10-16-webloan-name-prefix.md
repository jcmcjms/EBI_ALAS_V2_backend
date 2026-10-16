# WebLoan name-prefix access Implementation Plan

> [!NOTE]
> This document may not reflect the current implementation.
> See the final report for up-to-date state:
> [Final Report](../reports/webloan-name-prefix.md)

> **For agentic workers:** REQUIRED SUB-SKILL: Use compose:subagent (recommended) or compose:execute to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Query WebLoan tables through the ALAS SQL connection using a configurable name prefix (test: `webloan.dbo`, live: linked-server four-part prefix) and remove the dedicated WebLoan connection.

**Architecture:** `WebLoanReader` keeps the `IWebLoanReader` contract but executes parameterized SELECTs on `AlasDbContext` with table names qualified by `WebLoan:NamePrefix`. `WebLoanDbContext` and `ConnectionStrings:WebLoan` are removed. Checklist document `OPENQUERY` path is untouched.

**Tech Stack:** .NET 10, C# 14, EF Core (AlasDbContext / SqlQuery / ADO.NET), xUnit

## Global Constraints

- Target `net10.0`, C# 14, nullable enabled; production builds treat warnings as errors.
- Follow `docs/engineering/rules/` especially `11-ai-slop.md`, `04-efcore-sql.md`, `08-change-management.md`.
- `IWebLoanReader` public method signatures must not change.
- `WebLoan:NamePrefix` is config-only, validated at startup: `^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*$`.
- Table/function names in SQL are hard-coded C# literals (`cis_info`, `loan_data`, `is_loan`, …) — never taken from request input.
- Filter values are always SQL parameters (`FormattableString` / `DbParameter`).
- No `ConnectionStrings:WebLoan` after this change. No ALAS schema migrations.
- Checklist document `OPENQUERY(BPB_BINARY_SERVER, …)` remains unchanged.
- SELECT-only on the WebLoan path.
- Do not invent tables/columns; use only names already in `WebLoanDbContext` / `WebLoanReader`.
- Every task: run tests before claiming done; commit when the task is green.

---

### Task 1: WebLoanOptions + name qualifier + DI wiring

**Covers:** [S3], [S4] (config + qualifier + ServiceRegistration)

**Files:**
- Create: `src/Ebi.Alas.Api/Features/WebLoans/WebLoanOptions.cs`
- Create: `src/Ebi.Alas.Api/Features/WebLoans/WebLoanSql.cs`
- Modify: `src/Ebi.Alas.Api/Composition/ServiceRegistration.cs` (WebLoan registration block ~182-197; remove `ToReadOnlyIntent` if unused after Task 4)
- Test: `tests/Ebi.Alas.Api.Tests/Features/WebLoans/WebLoanOptionsTests.cs`

**Interfaces:**
- Consumes: existing `IWebLoanReader` / `NullWebLoanReader` in `WebLoanContracts.cs`
- Produces:
  - `sealed class WebLoanOptions` with `public const string SectionName = "WebLoan"` and `public string? NamePrefix { get; set; }`
  - `static class WebLoanSql` with `public static string Qualify(string namePrefix, string tableName)` → `"{namePrefix}.{tableName}"` when prefix non-empty, else `tableName`
  - DI: `IOptions<WebLoanOptions>` bound to `WebLoan`; `IWebLoanReader` → `WebLoanReader` when `NamePrefix` set, else `NullWebLoanReader`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Ebi.Alas.Api.Tests/Features/WebLoans/WebLoanOptionsTests.cs
using Ebi.Alas.Api.Features.WebLoans;

namespace Ebi.Alas.Api.Tests.Features.WebLoans;

public sealed class WebLoanOptionsTests
{
    [Theory]
    [InlineData("webloan.dbo", "cis_info", "webloan.dbo.cis_info")]
    [InlineData("WEBLOAN_LINKED.webloan.dbo", "loan_data", "WEBLOAN_LINKED.webloan.dbo.loan_data")]
    [InlineData("", "cis_info", "cis_info")]
    [InlineData(null, "cis_info", "cis_info")]
    public void Qualify_JoinsPrefixAndTable(string? prefix, string table, string expected)
    {
        Assert.Equal(expected, WebLoanSql.Qualify(prefix!, table));
    }

    [Theory]
    [InlineData("webloan.dbo")]
    [InlineData("WEBLOAN_LINKED.webloan.dbo")]
    [InlineData("webloan")]
    public void NamePrefix_Valid_identifiers_pass(string prefix)
    {
        Assert.True(WebLoanOptions.IsValidNamePrefix(prefix));
    }

    [Theory]
    [InlineData("")]
    [InlineData("webloan.dbo; DROP")]
    [InlineData("webloan..dbo")]
    [InlineData("webloan dbo")]
    [InlineData("webloan.dbo-")]
    public void NamePrefix_Invalid_rejected(string prefix)
    {
        Assert.False(WebLoanOptions.IsValidNamePrefix(prefix));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~WebLoanOptionsTests"`
Expected: FAIL (types not defined)

- [ ] **Step 3: Implement options + qualifier**

```csharp
// src/Ebi.Alas.Api/Features/WebLoans/WebLoanOptions.cs
using System.Text.RegularExpressions;

namespace Ebi.Alas.Api.Features.WebLoans;

public sealed partial class WebLoanOptions
{
    public const string SectionName = "WebLoan";

    public string? NamePrefix { get; set; }

    public static bool IsValidNamePrefix(string? namePrefix)
        => !string.IsNullOrWhiteSpace(namePrefix) && NamePrefixPattern().IsMatch(namePrefix);

    [GeneratedRegex("^[A-Za-z0-9_]+(\\.[A-Za-z0-9_]+)*$")]
    private static partial Regex NamePrefixPattern();
}
```

```csharp
// src/Ebi.Alas.Api/Features/WebLoans/WebLoanSql.cs
namespace Ebi.Alas.Api.Features.WebLoans;

public static class WebLoanSql
{
    public static string Qualify(string? namePrefix, string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return string.IsNullOrWhiteSpace(namePrefix)
            ? tableName
            : $"{namePrefix}.{tableName}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~WebLoanOptionsTests"`
Expected: PASS

- [ ] **Step 5: Wire DI (still using WebLoanDbContext until Task 2/4)**

In `ServiceRegistration.cs`, replace the WebLoan block so options are bound and validated when present:

```csharp
var webLoanSection = configuration.GetSection(WebLoanOptions.SectionName);
var webLoanNamePrefix = webLoanSection["NamePrefix"];
services.AddOptions<WebLoanOptions>()
    .Bind(webLoanSection)
    .Validate(o => o.NamePrefix is null || WebLoanOptions.IsValidNamePrefix(o.NamePrefix),
        "WebLoan:NamePrefix must be identifier-safe (letters, digits, underscore, dot-separated) when set.")
    .ValidateOnStart();

if (!string.IsNullOrWhiteSpace(webLoanNamePrefix))
{
    services.AddScoped<IWebLoanReader, Infrastructure.WebLoans.WebLoanReader>();
}
else
{
    services.AddScoped<IWebLoanReader, NullWebLoanReader>();
}
```

If `WebLoanReader` still needs `WebLoanDbContext` before Task 2 completes, leave that registration until Task 2 lands, then delete it in the same change set. End state after Task 2: **no** `AddDbContext<WebLoanDbContext>`.

- [ ] **Step 6: Run focused tests + build**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~WebLoan|FullyQualifiedName~ServiceRegistration"`
Expected: PASS (update `ServiceRegistrationConfigTests` / `TestHostSecrets` only if they fail on removed keys — fix in Task 4)

- [ ] **Step 7: Commit**

```bash
git add src/Ebi.Alas.Api/Features/WebLoans/WebLoanOptions.cs src/Ebi.Alas.Api/Features/WebLoans/WebLoanSql.cs src/Ebi.Alas.Api/Composition/ServiceRegistration.cs tests/Ebi.Alas.Api.Tests/Features/WebLoans/WebLoanOptionsTests.cs
git commit -m "feat(webloans): add NamePrefix options and table qualifier"
```

---

### Task 2: Rewrite WebLoanReader to ALAS connection + qualified SQL

**Covers:** [S2], [S4], [S5], [S6]

**Files:**
- Modify: `src/Ebi.Alas.Api/Infrastructure/WebLoans/WebLoanReader.cs` (full rewrite of data access; keep `IWebLoanReader` methods and DTO mapping behavior)
- Modify: `src/Ebi.Alas.Api/Composition/ServiceRegistration.cs` (constructor deps: `AlasDbContext` + `IOptions<WebLoanOptions>`)
- Test: `tests/Ebi.Alas.Api.Tests/Features/WebLoans/WebLoanCisLookupTests.cs` (adapt — see Step 4)

**Interfaces:**
- Consumes: `WebLoanSql.Qualify`, `WebLoanOptions.NamePrefix`, `AlasDbContext` (`Database.GetDbConnection()`, `Database.SqlQuery<T>`)
- Produces: `WebLoanReader(AlasDbContext db, IOptions<WebLoanOptions> options) : IWebLoanReader` — same public methods as today

**SQL Server object names already in the repo (do not invent others):**
`cis_info`, `loan_acct_info`, `cis_info_misc_data`, `check_list_data`, `check_list_all`, `mis_group`, `loan_product`, `loan_purpose`, `loan_data`, `amort_data`, `pre_loan_data`, function `is_loan`.

- [ ] **Step 1: Change `WebLoanReader` to take `AlasDbContext` + prefix; convert LINQ methods to SQL**

Constructor and helper:

```csharp
using System.Data;
using System.Globalization;
using Ebi.Alas.Api.Features.WebLoans;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ebi.Alas.Api.Infrastructure.WebLoans;

public sealed class WebLoanReader(AlasDbContext db, IOptions<WebLoanOptions> webLoanOptions) : IWebLoanReader
{
    private readonly string _prefix = webLoanOptions.Value.NamePrefix
        ?? throw new InvalidOperationException("WebLoan:NamePrefix is required for WebLoanReader.");

    private string T(string table) => WebLoanSql.Qualify(_prefix, table);
```

**SearchCisAsync** (portable SELECT + client name concat matching current shape):

```csharp
    public async Task<IReadOnlyList<CisSearchResult>> SearchCisAsync(
        string keyword, int max, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
        var limit = Math.Clamp(max, 1, 50);
        var term = keyword.Trim();
        FormattableString sql = $@"
            SELECT TOP ({limit}) cis_no, fname, mname, lname
            FROM {T("cis_info")}
            WHERE cis_no LIKE {"%" + term + "%"}
               OR fname LIKE {"%" + term + "%"}
               OR lname LIKE {"%" + term + "%"}
            ORDER BY lname";
        var rows = await db.Database.SqlQuery<CisSearchSqlRow>(sql).ToListAsync(cancellationToken);
        return rows.Select(r => new CisSearchResult(
            r.cis_no ?? string.Empty,
            ((r.fname ?? string.Empty) + " " + (r.mname ?? string.Empty) + " " + (r.lname ?? string.Empty)).Trim(),
            null)).ToList();
    }

    private sealed class CisSearchSqlRow
    {
        public string? cis_no { get; set; }
        public string? fname { get; set; }
        public string? mname { get; set; }
        public string? lname { get; set; }
    }
```

**GetChecklistItemsAsync / GetChecklistCatalogAsync / GetActiveProductsAsync / GetAllProductsAsync / GetOutstandingAsync** — same pattern: `SqlQuery<T>` with `T("table")`, parameters for filters, `TOP (limit)` / clamps unchanged from current code.

Example products:

```csharp
    public async Task<IReadOnlyList<LoanProductLookup>> GetActiveProductsAsync(int max, CancellationToken ct)
    {
        var limit = Math.Clamp(max, 1, 500);
        FormattableString sql = $@"
            SELECT TOP ({limit}) id_code, description
            FROM {T("loan_product")}
            WHERE expiration IS NULL
            ORDER BY description";
        var rows = await db.Database.SqlQuery<ProductSqlRow>(sql).ToListAsync(ct);
        return rows.Select(r => new LoanProductLookup(
            r.id_code ?? string.Empty, r.description ?? string.Empty, IsRetired: false, 0m)).ToList();
    }
```

(`GetAllProductsAsync`: drop the `WHERE`, set `IsRetired: r.expiration != null` via a row type that includes `expiration`.)

**GetCisDetailAsync** — one batch of parameterized queries against qualified tables (keep current assembly rules: agency id_code `14`, checklist items `CCR10` / `CCR07`, `mis_group` path lookups, `ComputeLengthOfService`, `ParseBirthDate`, `BuildAddress`). Prefer 2–3 queries over the old 7 when the same data can be projected; do not change the resulting `CisDetail` field mapping.

Sketch (implementer must keep field-level mapping identical to current `GetCisDetailAsync`):

```csharp
    public async Task<CisDetail?> GetCisDetailAsync(string cisNo, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cisNo);
        var term = cisNo.Trim();

        FormattableString cisSql = $@"
            SELECT TOP (1) cis_no, fname, mname, lname, title, appelation, p_bday,
                   h_sadd, h_barangay, h_village, h_city, h_state_prov, h_zip,
                   occupation, b_jtitle, b_region_code, b_division_code, b_station_code, b_employee_no, bk, bch
            FROM {T("cis_info")}
            WHERE cis_no = {term}";
        var cis = await db.Database.SqlQuery<CisInfoSqlRow>(cisSql).FirstOrDefaultAsync(ct);
        if (cis is null) return null;

        FormattableString acctSql = $@"
            SELECT TOP (50) bk, bch, acct_no, name, credit_limit, used_credit, borrower_type, cat_mis_group2, solicitor
            FROM {T("loan_acct_info")}
            WHERE cis_no = {term}
            ORDER BY bch, acct_no";

        FormattableString miscSql = $@"
            SELECT TOP (1) value_str
            FROM {T("cis_info_misc_data")}
            WHERE cis_no = {term} AND id_code = {AgencyTypeCode}";

        FormattableString checklistSql = $@"
            SELECT check_list_item, description, submitted, expiration
            FROM {T("check_list_data")}
            WHERE cis_no = {term} AND check_list_item IN ({LengthOfServiceItem}, {NthpItem})";

        // mis_group: resolve agency description from cis misc value_str, and
        // solicitor/mis paths from accounts.cat_mis_group2 / solicitor — same rules as the
        // current GetCisDetailAsync (agency path any group_no; solicitor path GroupNo == 2).
        FormattableString misGroupByCodeSql = $@"
            SELECT TOP (1) description FROM {T("mis_group")} WHERE id_code = {agencyCode}";
        FormattableString misAgencyByPathSql = $@"
            SELECT TOP (1) description FROM {T("mis_group")} WHERE path = {misPath}";
        FormattableString solicitorByPathSql = $@"
            SELECT TOP (1) description FROM {T("mis_group")} WHERE path = {solicitorPath} AND group_no = 2";
    }
```

Keep `AgencyTypeCode`, `LengthOfServiceItem`, `NthpItem`, `ComputeLengthOfService`, `ParseBirthDate`, `BuildAddress` as today (adjust `BuildAddress` to accept the SQL row type instead of `WebLoanCisInfo`).

**Account ownership + outstanding/pending** — replace `db.LoanAccounts.AnyAsync` with:

```csharp
        FormattableString belongsSql = $@"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM {T("loan_acct_info")}
                WHERE cis_no = {cisNo} AND bch = {branchCode} AND acct_no = {accountNo}
            ) THEN CAST(1 AS int) ELSE CAST(0 AS int) END";
        var belongs = await db.Database.SqlQuery<int>(belongsSql).SingleAsync(ct) == 1;
```

Rewrite `GetAccountOutstandingLoansAsync` / `GetAccountPendingLoanAsync` SQL already present in the file, changing `dbo.loan_data` → `{T("loan_data")}`, `dbo.amort_data` → `{T("amort_data")}`, `dbo.loan_product` → `{T("loan_product")}`, `dbo.pre_loan_data` → `{T("pre_loan_data")}`, `dbo.loan_purpose` → `{T("loan_purpose")}`, and `dbo.is_loan(...)` → `{_prefix}.is_loan(...)` (function is on the WebLoan database / linked server).

Keep `OFFSET/FETCH` SQL Server syntax and `OutstandingLoanSqlRow` / `PendingLoanSqlRow` private row types and DTO mapping exactly as today.

**GetLoanByNoAsync** — replace hard-coded `webloan.dbo.loan_data` with `{T("loan_data")}` (this is the live bug vs prefix config).

**GetCatLoanClassAsync** — same: `FROM {T("loan_data")}` with `@branchCode` / `@loanNo` / `@productCode` parameters (already ADO.NET).

- [ ] **Step 2: Build the API project**

Run: `dotnet build src/Ebi.Alas.Api`
Expected: 0 errors. Fix nullability/SQL row property names to match SELECT aliases.

- [ ] **Step 3: Point DI at the new constructor**

Ensure `ServiceRegistration` registers `WebLoanReader` with `AlasDbContext` (scoped) and `IOptions<WebLoanOptions>`; no `WebLoanDbContext` injection.

- [ ] **Step 4: Adapt WebLoanCisLookupTests**

Sqlite+EF against `WebLoanDbContext` cannot run qualified SQL Server SQL. Replace with tests that prove **assembly rules** on pure inputs (extract private static helpers to `internal` or keep `public static` test hooks only if already private static in the same class — prefer `InternalsVisibleTo` only if the project already uses it; otherwise test via `IWebLoanReader` fakes for consumers and unit-test `WebLoanSql` + options as in Task 1).

Minimum honest coverage in this task:
- Keep Task 1 `WebLoanOptionsTests`.
- Delete Sqlite `WebLoanDbContext` fixture usage from `WebLoanCisLookupTests` and replace with tests of `ComputeLengthOfService` / `ParseBirthDate` / address join **if** those helpers are made `internal static` (one-line visibility change is acceptable) **or** leave helpers private and drop those cases rather than adding a fake DB.

Do **not** add InMemory EF as relational proof (`11-ai-slop.md`).

- [ ] **Step 5: Run WebLoans tests**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~WebLoan"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/Ebi.Alas.Api/Infrastructure/WebLoans/WebLoanReader.cs src/Ebi.Alas.Api/Composition/ServiceRegistration.cs tests/Ebi.Alas.Api.Tests/Features/WebLoans/
git commit -m "refactor(webloans): query via ALAS connection and NamePrefix tables"
```

---

### Task 3: Remove WebLoanDbContext and WebLoan connection string

**Covers:** [S2], [S4], [S8] (cleanup of the old path)

**Files:**
- Delete: `src/Ebi.Alas.Api/Infrastructure/Persistence/WebLoanDbContext.cs` (includes entity classes in that file)
- Delete: `tests/Ebi.Alas.Api.Tests/Persistence/WebLoanDbContextReadOnlyTests.cs`
- Modify: `src/Ebi.Alas.Api/Composition/ServiceRegistration.cs` (remove `ConnectionStrings:WebLoan`, `ToReadOnlyIntent` if now unused)
- Modify: `tests/Ebi.Alas.Api.Tests/TestHostSecrets.cs` (remove `ConnectionStrings:WebLoan`)
- Modify: `tests/Ebi.Alas.Api.Tests/Composition/ServiceRegistrationConfigTests.cs` (remove WebLoan connection key)
- Modify: `README.md` WebLoan connection docs (lines mentioning `WebLoanConnection` / `ConnectionStrings:WebLoan`) — replace with `WebLoan:NamePrefix` examples for test vs live

**Interfaces:**
- Consumes: nothing from `WebLoanDbContext` after Task 2
- Produces: compile-clean tree with no `WebLoanDbContext` type

- [ ] **Step 1: Delete dead types and connection wiring**

Remove `WebLoanDbContext` and all entity types in that file (`WebLoanCisInfo`, `WebLoanLoanAccount`, …) if nothing references them. Remove:

```csharp
var webLoanConnectionString = configuration.GetConnectionString("WebLoan");
// AddDbContext<WebLoanDbContext>...
// ToReadOnlyIntent(webLoanConnectionString) for WebLoan only
```

Keep `NullWebLoanReader` when `NamePrefix` is absent.

- [ ] **Step 2: Fix tests and docs**

Update `TestHostSecrets` / `ServiceRegistrationConfigTests` to stop setting `ConnectionStrings:WebLoan`.  
README: document

```json
"WebLoan": { "NamePrefix": "webloan.dbo" }
```

and live `"NamePrefix": "YOUR_LINKED_SERVER.webloan.dbo"`.

- [ ] **Step 3: Full test suite**

Run: `dotnet test`
Expected: PASS (or only pre-existing unrelated failures — report honestly)

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(webloans): drop dedicated WebLoan connection in favor of NamePrefix"
```

---

### Task 4: Verification gate (no feature code)

**Covers:** [S7]

**Files:** none (commands only) unless a failure needs a fix

- [ ] **Step 1: Build + test + format check**

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

- [ ] **Step 2: AI-slop checklist**

Re-read the diff. Confirm: no placeholders, no swallowed exceptions, no secret config values, no `ConnectionStrings:WebLoan` left, `NamePrefix` validated, parameters used for all filters, `IWebLoanReader` unchanged, document `OPENQUERY` untouched.

- [ ] **Step 3: Report** (files changed, behavior, tests run, migration/config impact)

Config impact: replace `ConnectionStrings:WebLoan` with `WebLoan:NamePrefix` in every environment (test `webloan.dbo`, live `LINKED.webloan.dbo`). Linked server must exist on the ALAS SQL Server in live.

---

## Self-review notes

- Spec [S1] problem is the motivation; implementation covers [S2]–[S7]. [S8] is out of scope by design.
- `GetLoanByNoAsync` already hard-coded `webloan.dbo.loan_data` — Task 2 fixes that to use `NamePrefix`.
- Sqlite cannot execute four-part names or `is_loan`; do not fake relational coverage.
