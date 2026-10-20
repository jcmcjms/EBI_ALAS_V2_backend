---
feature: evaluator-org-wide-loan-visibility
status: delivered
specs:
  - docs/compose/specs/2026-10-20-evaluator-org-wide-loan-visibility-design.md
plans:
  - docs/compose/plans/2026-10-20-evaluator-org-wide-loan-visibility.md
branch: main
commits: 393e520..f10678a
---

# Evaluator Org-Wide Loan Visibility — Final Report

## What Was Built

Evaluators can now list, open, and act on loan applications from **every branch**, not only their home branch. That covers loan lists, detail, groups, revisions, review endpoints, document flags/checklists, evaluate/pushback workflow actions, the evaluation desk queue (across branch partitions), and dashboard aggregates.

Admin-only powers stay admin-only: partition picking on enqueue, role-check bypass, and submit-branch selection. Encoder create/submit home-branch rules and WebLoan CIS branch checks are unchanged. Encoder self-scope on the dashboard is unchanged.

## Architecture

A single loan-visibility predicate drives every loan read/act branch filter:

- `CallerContext.CanViewAllLoanBranches` (`src/Ebi.Alas.Api/Composition/Auth/CallerContext.cs`) is true for Admin, System, and **Evaluator**.
- `CallerContext.CanAccessAllBranches` remains Admin/System only (used by `WorkflowQueueService.ResolvePartition`, `EnsureRole` bypass, `SubmitLoanHandler`, `WebLoanAccess`).
- `BranchVisibility.GetReadableBranchIdsAsync` returns all branch codes for Evaluator (same early-return as Admin/System).

Loan path call sites use `CanViewAllLoanBranches` instead of `CanAccessAllBranches`: `LoanAccess`, `GetLoanHandler`, `ListLoansHandler`, `LoanWorkflowService.LoadScopedAsync`, `LoanGroupEndpoints`, `LoanGroupStatusHandler`, `LoanReviewEndpoints`, `LoanWorkflowEndpoints`, `ReviseLoanHandler`, `RevisionRequestEndpoints`, `DocumentFlagEndpoints`, `ChecklistDocumentEndpoints`, `DashboardService`.

`DeskQueueService` loads desk rows with a nullable partition filter (`null` = all partitions) for org-wide callers. Claim/reap maintenance iterates distinct queue partitions. Claims use the group’s `PartitionKey`, not the caller’s home branch. Desk scope label is `All branches` when org-wide.

### Design Decisions

- We added `CanViewAllLoanBranches` instead of expanding `CanAccessAllBranches` so evaluators do not inherit admin partition picking or `EnsureRole` bypass.
- Evaluator desk treats org-wide as **no partition filter** (not “all rows in `Branches`”) so claims still work if the branch catalog is incomplete.
- Optional `branchId` query on loan list is ignored for evaluators (always org-wide); only Admin/System may filter by branch.

## Usage

No configuration. Assign a user the `Evaluator` role. They will see and can evaluate/pushback applications from any branch. Lists and the Review Desk surface branch codes/names so cross-branch work is visible.

## Verification

**Backend** (`dotnet test tests/Ebi.Alas.Api.Tests`): **579 passed, 0 failed**, including:
- `BranchVisibilityTests` / `CallerContextTests` — Evaluator is org-wide; `CanAccessAllBranches` stays Admin/System
- `LoanBranchIsolationTests` — Evaluator loads/lists other-branch loans; Encoder stays home-branch
- `LoanGroupStatusHandlerTests` — Evaluator can touch other-branch bundles; Recommender cannot
- `DeskQueueServiceTests` — Evaluator desk shows/claims other-branch partitions; Recommender stays home partition
- `DashboardEncoderScopeTests` — Evaluator dashboard includes other-branch loans; Encoder self-scope intact

**Frontend** (`alas_v2`): `npm test` **317 passed**, `npm run typecheck` clean. No UI code change required — monitoring defaults `branchCode: 'all'`, already shows a Branch column, and desk cards show Branch; list APIs do not pin evaluator to home branch.

**No migration.** No JWT/RoleCatalog/DTO contract change.

## Journey Log

- [lesson] `dotnet ef --no-build` against a stale assembly can report “already up to date” while a migration is still unapplied — rebuild first.
- [lesson] Replacing multi-line C# with PowerShell `-replace` and literal `` `r`n `` corrupted source; use the Edit tool for structured replacements.
- [lesson] Evaluator org-wide via “all codes from `Branches`” broke desk tests that never seed `Branches`; nullable partition filter is more robust.
- [pivot] Frontend needed no changes once the backend gate was lifted — client already requested unscoped lists and labeled branch.

## Source Materials

| File | Role | Notes |
|------|------|-------|
| `docs/compose/specs/2026-10-20-evaluator-org-wide-loan-visibility-design.md` | Design | Scope S1–S8 |
| `docs/compose/plans/2026-10-20-evaluator-org-wide-loan-visibility.md` | Implementation plan | Tasks 1–7 |
