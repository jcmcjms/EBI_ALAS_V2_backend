# Evaluator Org-Wide Loan Visibility — Design Spec

> [!NOTE]
> This document may not reflect the current implementation.
> See the final report for up-to-date state:
> [Final Report](../reports/evaluator-org-wide-loan-visibility.md)

# [S1] Problem

Evaluators are currently restricted to loan applications from their home branch. Branch filtering is applied inconsistently and mostly as `l.BranchId == caller.BranchId` across loan lists, detail access, workflow actions, groups, revisions, review endpoints, desk queues, and dashboards.

Product requirement: **evaluators must see and work applications org-wide** (every branch), not only their assigned branch. This is visibility and loan-workflow scope only — not admin authority.

# [S2] Solution overview

Introduce one shared branch-visibility rule used by every loan read/act path:

| Caller | Readable / actionable branches |
|---|---|
| Admin, System | All branches (unchanged) |
| **Evaluator** | **All branches (new)** |
| Area Head (job title) | Branches sharing home `AreaCode` (unchanged for list/notify) |
| Everyone else | Home branch only (unchanged) |

Desk queues for evaluators must include every branch partition (data model stays partitioned by `loan.BranchId`). Dashboard and list responses simply return a wider set. API contracts and database schema do not change.

Out of scope: RoleCatalog permission changes, JWT claim shape, approval routing, encoder create/submit branch rules, admin partition-picking (`ResolvePartition`), webloan CIS branch checks.

# [S3] Security model

- Evaluator gains **loan visibility and loan workflow actions** across branches (evaluate, pushback, list, detail, groups, revisions, review drafts, evaluation desk).
- Evaluator does **not** gain: user manage, branch manage, workflow config, role admin, or the Admin/System ability to choose arbitrary queue partitions on enqueue.
- Encoder self-scope on dashboard (`CreatedBy == caller.UserId`) is unchanged.
- Recommender / Approver remain home-branch (or Area Head multi-read on lists) unless they are also Admin/System.

# [S4] Backend design

## [S4.1] Visibility core

- Extend `Features/Branches/BranchVisibility.cs` (and/or `CallerContext`) with a single helper that returns whether a caller can access a loan branch, and the set of readable branch ids.
- Evaluator role is org-wide in that helper.
- `LoanAccess`, `LoanWorkflowService.LoadScopedAsync`, and list/group/revision/review handlers use the helper instead of `caller.BranchId` equality.

## [S4.2] Surfaces

| Surface | Change |
|---|---|
| `ListLoansHandler` | Evaluator sees all branches |
| `GetLoan` / `LoanAccess` | Helper-based access |
| `LoanWorkflowService` | Evaluate/pushback/recommend scoped by helper |
| `LoanGroup*`, `Revise`, `LoanReviewEndpoints`, `RevisionRequestEndpoints`, `LoanGroupStatusHandler` | Replace branch equality filters |
| `DeskQueueService` | Evaluator desk/claim reads all partitions (or filters by readable set = all) |
| `DashboardService` | Use helper / `CanAccessAllBranches` equivalent so evaluators are not home-pinned |
| `WorkflowQueueService.ResolvePartition` | **No change** |

No EF migration. Optional `branchId` query filters: Admin/System may filter by branch; for Evaluator the parameter is ignored (always org-wide).

# [S5] Frontend design

Repo: `alas_v2` (React 19, TanStack Router/Query, feature modules). Follow that repo’s `AGENTS.md` and `.ai/rules/*`.

- Loan monitoring / lists / review desk: do not pin evaluator queries to home branch; show branch as a column/badge on cross-branch rows.
- Dashboard: no evaluator home-branch pin (backend supplies org-wide aggregates).
- Loan creation / CIS matching stay encoder home-branch rules (unchanged).
- No API contract changes required beyond behavior of existing endpoints.

# [S6] Error handling and edge cases

- Unknown/missing evaluator branch claim still fails closed for non-evaluator rules; evaluator org-wide does not depend on `BranchId` being correct.
- Claim conflicts and desk lease behavior unchanged; only the visible/claimable set widens.
- Area Head non-evaluator behavior unchanged.

# [S7] Testing

## [S7.1] Backend

- `BranchVisibility` / isolation tests: evaluator sees and acts on other-branch loans; recommender/encoder still isolated.
- Desk queue: evaluator sees/claims evaluation work across branch partitions.
- Workflow: evaluate/pushback on a foreign-branch loan succeeds for evaluator; fails for a home-only role.

## [S7.2] Frontend

- List/desk render other-branch loans for evaluator; branch label present.
- Existing role-queue and auth tests remain green.

## [S7.3] Gates

- Backend: `dotnet test` (analyzers/warnings-as-errors).
- Frontend: Vitest, lint, typecheck per `alas_v2` rules.

# [S8] Compatibility

- No schema migration.
- No public JSON contract change (same DTO fields; more rows for evaluators).
- Authorization behavior **does** change for Evaluator — intentional and covered by tests. Call out in release notes if published.
