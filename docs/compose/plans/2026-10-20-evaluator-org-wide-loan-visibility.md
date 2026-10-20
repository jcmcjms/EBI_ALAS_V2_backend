# Evaluator Org-Wide Loan Visibility Implementation Plan

> [!NOTE]
> This document may not reflect the current implementation.
> See the final report for up-to-date state:
> [Final Report](../reports/evaluator-org-wide-loan-visibility.md)

> **For agentic workers:** REQUIRED SUB-SKILL: Use compose:subagent (recommended) or compose:execute to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Evaluators can list, open, and act on loan applications from every branch (org-wide), instead of home-branch-only.

**Architecture:** Add a shared loan-visibility predicate on `CallerContext` (`CanViewAllLoanBranches`) and use it in every loan read/act branch filter. Expand `BranchVisibility` so Evaluator resolves to all branch codes. Desk queue for evaluators reads/claims across all partitions. Admin-only powers (`CanAccessAllBranches` for partition picking, role bypass, submit branch choice) stay unchanged.

**Tech Stack:** .NET 10 / C# 14, EF Core, xUnit; React 19 + TanStack Query (frontend repo `alas_v2`).

## Global Constraints

- Spec: `docs/compose/specs/2026-10-20-evaluator-org-wide-loan-visibility-design.md`
- Keep `CallerContext.CanAccessAllBranches` as **Admin/System only** (do not add Evaluator to it).
- New property name is exactly `CanViewAllLoanBranches`.
- No EF migration. No JWT/RoleCatalog/DTO schema changes.
- `WorkflowQueueService.ResolvePartition` unchanged.
- Encoder create/submit home-branch rules unchanged. WebLoan CIS branch checks unchanged.
- Backend quality gate: `dotnet test tests/Ebi.Alas.Api.Tests` passes (0 failed).
- Frontend quality gate: Vitest + typecheck in `alas_v2` pass.
- Do **not** commit unrelated dirty files already in the backend working tree (document-requirements work). Only stage files this plan touches.
- Backend rules: `AGENTS.md` + `docs/engineering/rules/*` (esp. `11-ai-slop.md`). Frontend rules: `alas_v2/AGENTS.md` + `.ai/rules/*`.

---

### Task 1: Loan visibility predicate + BranchVisibility org-wide for Evaluator

**Covers:** [S2], [S3], [S4.1]

**Files:**
- Modify: `src/Ebi.Alas.Api/Composition/Auth/CallerContext.cs`
- Modify: `src/Ebi.Alas.Api/Features/Branches/BranchVisibility.cs`
- Test: `tests/Ebi.Alas.Api.Tests/Features/Branches/BranchVisibilityTests.cs`
- Test: `tests/Ebi.Alas.Api.Tests/Composition/CallerContextTests.cs` (create if missing; else extend nearest CallerContext/auth tests)

**Interfaces:**
- Consumes: `UserRole.Evaluator`, `CallerContext` record
- Produces: `CallerContext.CanViewAllLoanBranches` (`bool`); `BranchVisibility.GetReadableBranchIdsAsync` returns **all** branch codes when `role == UserRole.Evaluator`

- [ ] **Step 1: Write failing tests**

In `BranchVisibilityTests.cs` add:

```csharp
[Fact]
public async Task Evaluator_SeesAllBranches()
{
    await using var db = Db();
    db.Branches.Add(Branch.Create("001", "Main", "A1", DateTimeOffset.UtcNow));
    db.Branches.Add(Branch.Create("002", "Satellite", "A1", DateTimeOffset.UtcNow));
    db.Branches.Add(Branch.Create("003", "Other", "B9", DateTimeOffset.UtcNow));
    await db.SaveChangesAsync();

    var ids = await BranchVisibility.GetReadableBranchIdsAsync(
        db, Guid.NewGuid(), UserRole.Evaluator, "001", CancellationToken.None);

    Assert.Equal(3, ids.Count);
    Assert.Contains("001", ids);
    Assert.Contains("002", ids);
    Assert.Contains("003", ids);
}

[Fact]
public async Task Evaluator_IsHomeBranch_SeesAllAnyway()
{
    await using var db = Db();
    db.Branches.Add(Branch.Create("001", "Main", "A1", DateTimeOffset.UtcNow));
    db.Branches.Add(Branch.Create("009", "Remote", "Z9", DateTimeOffset.UtcNow));
    await db.SaveChangesAsync();

    var ids = await BranchVisibility.GetReadableBranchIdsAsync(
        db, Guid.NewGuid(), UserRole.Evaluator, "001", CancellationToken.None);

    Assert.Contains("009", ids);
}
```

In `CallerContextTests.cs` (create with in-memory claims helper if needed):

```csharp
[Theory]
[InlineData(UserRole.Admin, true)]
[InlineData(UserRole.System, true)]
[InlineData(UserRole.Evaluator, true)]
[InlineData(UserRole.Approver, false)]
[InlineData(UserRole.Recommender, false)]
[InlineData(UserRole.Encoder, false)]
public void CanViewAllLoanBranches_MatchesRole(UserRole role, bool expected)
{
    var caller = new CallerContext(Guid.NewGuid(), role, "001");
    Assert.Equal(expected, caller.CanViewAllLoanBranches);
}

[Theory]
[InlineData(UserRole.Evaluator, false)]
[InlineData(UserRole.Admin, true)]
[InlineData(UserRole.System, true)]
public void CanAccessAllBranches_DoesNotIncludeEvaluator(UserRole role, bool expected)
{
    var caller = new CallerContext(Guid.NewGuid(), role, "001");
    Assert.Equal(expected, caller.CanAccessAllBranches);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~BranchVisibility|FullyQualifiedName~CallerContext"`
Expected: FAIL (`CanViewAllLoanBranches` missing; Evaluator readable branches still `["001"]`)

- [ ] **Step 3: Implement**

`CallerContext.cs` — add property next to `CanAccessAllBranches`:

```csharp
public bool IsEvaluator => Role == UserRole.Evaluator;

/// Loan read/act scope: admins/system and evaluators may see every branch.
/// Does not grant admin partition picking or role bypass.
public bool CanViewAllLoanBranches => CanAccessAllBranches || IsEvaluator;
```

`BranchVisibility.GetReadableBranchIdsAsync` — include Evaluator in the all-branches early return:

```csharp
var home = (branchId ?? string.Empty).Trim();
if (role is UserRole.Admin or UserRole.System or UserRole.Evaluator)
{
    return await db.Branches.AsNoTracking()
        .Select(b => b.Code)
        .ToListAsync(cancellationToken);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~BranchVisibility|FullyQualifiedName~CallerContext"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Ebi.Alas.Api/Composition/Auth/CallerContext.cs src/Ebi.Alas.Api/Features/Branches/BranchVisibility.cs tests/Ebi.Alas.Api.Tests/Features/Branches/BranchVisibilityTests.cs tests/Ebi.Alas.Api.Tests/Composition/CallerContextTests.cs
git commit -m "feat(auth): grant evaluators org-wide loan visibility scope"
```

---

### Task 2: Apply loan-visibility predicate across loan read/act paths

**Covers:** [S3], [S4.2]

**Files:**
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/LoanAccess.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/GetLoan/GetLoanHandler.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/WorkflowActions/LoanWorkflowService.cs` (`LoadScopedAsync` only)
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/ListLoans/ListLoansHandler.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/LoanReviewEndpoints.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/LoanWorkflowEndpoints.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/LoanGroupEndpoints.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/LoanGroupStatusHandler.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/Revise/ReviseLoanHandler.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/RevisionRequestEndpoints.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/Documents/DocumentFlagEndpoints.cs`
- Modify: `src/Ebi.Alas.Api/Features/LoanApplications/Documents/ChecklistDocumentEndpoints.cs`
- Test: `tests/Ebi.Alas.Api.Tests/Features/LoanApplications/LoanBranchIsolationTests.cs` (extend)

**Interfaces:**
- Consumes: `CallerContext.CanViewAllLoanBranches` from Task 1
- Produces: same public endpoint contracts; evaluators receive other-branch loans

**Rule:** For **loan access/list/action** branch filters, replace `caller.CanAccessAllBranches` with `caller.CanViewAllLoanBranches`. Leave `CanAccessAllBranches` on `SubmitLoanHandler` branch choice, `EnsureRole` bypass, `WebLoanAccess`, and `WorkflowQueueService.ResolvePartition`.

- [ ] **Step 1: Write failing tests** (extend `LoanBranchIsolationTests.cs` or add `LoanEvaluatorOrgWideTests.cs`)

```csharp
[Fact]
public async Task Evaluator_CanLoadLoanFromOtherBranch()
{
    // seed caller Evaluator home 001, loan BranchId 002
    // LoanAccess.RequireAccessibleAsync succeeds
}

[Fact]
public async Task Evaluator_CanEvaluateLoanFromOtherBranch()
{
    // ForChecking loan on branch 002; evaluator home 001
    // LoanWorkflowService evaluate path succeeds
}

[Fact]
public async Task Encoder_StillHomeBranchOnly()
{
    // encoder home 001 cannot load loan on 002 (NotFoundException)
}
```

Use existing test host / in-memory DB patterns from `LoanBranchIsolationTests.cs` and `DeskQueueServiceTests.cs`. If those tests already assert evaluator isolation, **update them** to the new org-wide rule instead of adding parallel cases.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~BranchIsolation|FullyQualifiedName~EvaluatorOrgWide"`
Expected: evaluator cases FAIL; encoder case PASS (or all isolation tests red after assertion updates)

- [ ] **Step 3: Implement predicate swap**

Pattern (same in each handler):

```csharp
// before
if (!caller.CanAccessAllBranches)
{
    query = query.Where(l => l.BranchId == caller.BranchId);
}

// after
if (!caller.CanViewAllLoanBranches)
{
    query = query.Where(l => l.BranchId == caller.BranchId);
}
```

`ListLoansHandler.cs` — org-wide callers skip the readable-branch filter; **only Admin/System** may apply the optional `branchId` query param (evaluator ignores it):

```csharp
if (caller.CanViewAllLoanBranches)
{
    if (caller.CanAccessAllBranches && !string.IsNullOrWhiteSpace(branchId))
    {
        query = query.Where(l => l.BranchId == branchId);
    }
}
else
{
    var jobTitle = await db.Users.AsNoTracking()
        .Where(u => u.Id == caller.UserId)
        .Select(u => u.JobTitle)
        .FirstOrDefaultAsync(cancellationToken);
    var readable = await Branches.BranchVisibility.GetReadableBranchIdsAsync(
        db, caller.UserId, caller.Role, caller.BranchId, cancellationToken, jobTitle);
    query = query.Where(l => readable.Contains(l.BranchId));
}
```

Also update equality forms:

```csharp
// before
if (!caller.CanAccessAllBranches && loan.BranchId != caller.BranchId)

// after
if (!caller.CanViewAllLoanBranches && loan.BranchId != caller.BranchId)
```

and

```csharp
// before
.AnyAsync(l => l.Id == id && (caller.CanAccessAllBranches || l.BranchId == caller.BranchId), ...)

// after
.AnyAsync(l => l.Id == id && (caller.CanViewAllLoanBranches || l.BranchId == caller.BranchId), ...)
```

`GetLoanHandler` and `LoanAccess` both filter query results — change both.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~BranchIsolation|FullyQualifiedName~EvaluatorOrgWide|FullyQualifiedName~LoanAccess|FullyQualifiedName~ListLoans"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Ebi.Alas.Api/Features/LoanApplications tests/Ebi.Alas.Api.Tests/Features/LoanApplications
git commit -m "feat(loans): let evaluators open and act on applications from any branch"
```

---

### Task 3: Desk queue across branch partitions for evaluators

**Covers:** [S2], [S4.2], [S6]

**Files:**
- Modify: `src/Ebi.Alas.Api/Features/Workflow/DeskQueueService.cs`
- Modify: `src/Ebi.Alas.Api/Features/Workflow/WorkflowEndpoints.cs` (`ClaimNextAsync` call site only if still used)
- Test: `tests/Ebi.Alas.Api.Tests/Features/Workflow/DeskQueueServiceTests.cs`

**Interfaces:**
- Consumes: `CallerContext.IsEvaluator` / `CanViewAllLoanBranches`
- Produces: `GetMyDeskAsync` / `ClaimHeadAsync` see queued evaluation bundles from every `PartitionKey` for evaluators; non-evaluators unchanged (home partition)

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public async Task Evaluator_Desk_ShowsAndClaimsOtherBranchPartition()
{
    // Queue items: Evaluation stage, PartitionKey "002", Queued
    // Caller: Evaluator, BranchId "001"
    // GetMyDeskAsync includes the bundle
    // ClaimHeadAsync claims it
}

[Fact]
public async Task Recommender_Desk_RemainsHomePartition()
{
    // Same seed; Recommender BranchId "001" desk is empty
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~DeskQueue"`
Expected: evaluator cross-partition cases FAIL

- [ ] **Step 3: Implement multi-partition desk**

Replace single `var partition = caller.BranchId` in `GetMyDeskAsync` and `ClaimHeadAsync` with a partition set. Keep `ClaimByIdAsync` using `requested.PartitionKey` (already correct), but `LoadRowsAsync` must accept the set.

Add private helper and widen `LoadRowsAsync`:

```csharp
private async Task<IReadOnlyList<string>> ResolveDeskPartitionsAsync(
    CallerContext caller,
    CancellationToken cancellationToken)
{
    if (!caller.CanViewAllLoanBranches)
    {
        return [caller.BranchId];
    }

    return await db.Branches.AsNoTracking()
        .Select(b => b.Code)
        .ToListAsync(cancellationToken);
}
```

`GetMyDeskAsync`:

```csharp
var partitions = await ResolveDeskPartitionsAsync(caller, cancellationToken);
foreach (var partition in partitions)
{
    await queue.ReapExpiredAsync(stage, partition, cancellationToken);
    await ReleaseCancelledClaimsAsync(stage, partition, cancellationToken);
}

var rows = await LoadRowsAsync(stage, partitions, tracked: false, cancellationToken);
// summary label: home branch for non-eval; "All branches" when partitions.Count != 1
```

`ClaimHeadAsync` similarly uses `partitions` for reap/release/load; `ClaimGroupAsync` still receives the **loan’s** `PartitionKey` from the loaded rows (use the group’s partition, not `caller.BranchId`).

Change `LoadRowsAsync` signature from `string partition` to `IReadOnlyList<string> partitions` and filter `partitions.Contains(q.PartitionKey)` (or `Contains` in SQL).

Update the status string for multi-partition desks to `"All branches · N clients waiting"` instead of `"Branch {partition} · …"`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~DeskQueue"`
Expected: PASS (existing home-branch desk tests stay green)

- [ ] **Step 5: Commit**

```bash
git add src/Ebi.Alas.Api/Features/Workflow/DeskQueueService.cs src/Ebi.Alas.Api/Features/Workflow/WorkflowEndpoints.cs tests/Ebi.Alas.Api.Tests/Features/Workflow/DeskQueueServiceTests.cs
git commit -m "feat(workflow): serve evaluator desk across all branch partitions"
```

---

### Task 4: Dashboard org-wide for evaluators

**Covers:** [S4.2]

**Files:**
- Modify: `src/Ebi.Alas.Api/Features/Dashboard/DashboardService.cs`
- Test: `tests/Ebi.Alas.Api.Tests/Features/Dashboard/*` (existing dashboard scope tests)

**Interfaces:**
- Consumes: `CallerContext.CanViewAllLoanBranches`
- Produces: evaluator dashboard aggregates include all branches; encoder self-scope unchanged

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public async Task Evaluator_Dashboard_IncludesOtherBranchLoans()
{
    // loans on 001 and 002; evaluator home 001
    // pending/overview counts include both branches
}

[Fact]
public async Task Encoder_Dashboard_StillSelfScoped()
{
    // unchanged encoder CreatedBy filter still applies
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~Dashboard"`
Expected: evaluator case FAIL

- [ ] **Step 3: Implement**

In `DashboardService.cs`, replace every loan branch gate:

```csharp
// before
if (!caller.CanAccessAllBranches)
{
    query = query.Where(l => l.BranchId == caller.BranchId);
}
// and
where (caller.CanAccessAllBranches || l.BranchId == caller.BranchId)

// after
if (!caller.CanViewAllLoanBranches)
{
    query = query.Where(l => l.BranchId == caller.BranchId);
}
// and
where (caller.CanViewAllLoanBranches || l.BranchId == caller.BranchId)
```

Do not remove the encoder `CreatedBy == caller.UserId` extra filter.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --filter "FullyQualifiedName~Dashboard"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/Ebi.Alas.Api/Features/Dashboard/DashboardService.cs tests/Ebi.Alas.Api.Tests/Features/Dashboard
git commit -m "feat(dashboard): include all branches for evaluator dashboards"
```

---

### Task 5: Backend full suite

**Covers:** [S7.1], [S7.3]

**Files:**
- None (verification only)

- [ ] **Step 1: Full test run**

Run: `dotnet test tests/Ebi.Alas.Api.Tests --nologo`
Expected: Failed: 0

If failures are pre-existing dirty-tree issues in document-requirements tests, **do not “fix” them here** — note them and ensure the tests this plan owns are green.

- [ ] **Step 2: Commit only if fixes were needed for this feature**

---

### Task 6: Frontend — evaluator org-wide lists and branch labels

**Covers:** [S5], [S7.2]

**Repo:** `C:\Users\L-185-DEV\OneDrive - ENTERPRISE BANK INC\Desktop\Code\test\alas_v2`

**Files:**
- Inspect/modify: `src/features/loans/api/loans.ts` (only if callers force `branchId` for evaluators)
- Inspect/modify: loan monitoring / list pages under `src/features/loans/pages/**` and `src/features/loans/hooks/use-loan-monitoring.ts`
- Inspect/modify: `src/features/loans/pages/queue/review-desk.tsx` (no home-branch pin)
- Inspect/modify: `src/features/dashboard/**` if client pins evaluator branch
- Test: co-located `*.spec.ts(x)` for list/desk rendering

**Interfaces:**
- Consumes: existing `/api/loans`, desk, dashboard APIs (contracts unchanged)
- Produces: evaluator UI shows other-branch applications with a visible branch label

**Rules:** `alas_v2/AGENTS.md` + `.ai/rules/20-ai-slop-prevention.md`. Feature modules must not import other features. No new top-level catch-all folders.

- [ ] **Step 1: Read** `.ai/rules/00-governance.md` and `20-ai-slop-prevention.md` before UI edits.

- [ ] **Step 2: Write failing UI tests**

Cover: given role `Evaluator`, list includes a loan with `branchId` ≠ user’s home branch and renders a branch label. Existing role-queue tests stay green.

- [ ] **Step 3: Implement**

- Do not send `branchId: user.branchId` on list/desk/dashboard queries for evaluators (omit the param).
- If a list already shows `branchId`, ensure it is visible in the evaluator row/card.
- Leave loan **creation** / CIS home-branch checks untouched.

- [ ] **Step 4: Run frontend gates**

Run: `npm test` / `npx vitest run` (match repo scripts) and `npm run typecheck` (or `tsc --noEmit`)
Expected: PASS

- [ ] **Step 5: Commit** in `alas_v2` repo

```bash
git add <touched frontend files>
git commit -m "feat(loans): show evaluators applications from all branches"
```

---

### Task 7: Spec coverage self-check

**Covers:** [S8]

- [ ] Confirm every spec section S1–S8 is implemented or explicitly out of scope (S8 = no migration/contract change; document in PR/release notes).
- [ ] Run backend tests once more.
- [ ] Summarize files changed, behavior change, and residual risks.

---

## Self-review notes

- Spec anchors: S2/S3/S4.1 → Task 1; S3/S4.2 → Task 2–4; S6 → Task 3; S7 → Tasks 5–6; S8 → Task 7. S1 is the problem statement (no task required).
- `CanViewAllLoanBranches` is defined in Task 1 and used in Tasks 2–4 — name is consistent.
- `LoadRowsAsync` signature change is confined to Task 3.
