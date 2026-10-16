---
feature: webloan-name-prefix
status: delivered
specs:
  - docs/compose/specs/2026-10-16-webloan-name-prefix-design.md
plans:
  - docs/compose/plans/2026-10-16-webloan-name-prefix.md
branch: main
commits: 01e9f1a..e16af47
---

# WebLoan name-prefix access — Final Report

## What Was Built

WebLoan (legacy core banking) data is now read through the **ALAS SQL connection** using a configurable **name prefix**, instead of a dedicated `ConnectionStrings:WebLoan` / `WebLoanDbContext` connection.

- **Test:** `WebLoan:NamePrefix` = `webloan.dbo` (webloan database on the same SQL Server as ALAS)
- **Live:** `WebLoan:NamePrefix` = `YOUR_LINKED_SERVER.webloan.dbo` (four-part linked-server name)

The `IWebLoanReader` API surface is unchanged. Endpoints, loan-product sync, disbursement sync, and checklist document loading keep working against the same contracts. Checklist **binary** documents still use `OPENQUERY(BPB_BINARY_SERVER, …)` and were not changed.

## Architecture

```
HTTP / hosted service
  → IWebLoanReader
    → WebLoanReader
      → AlasDbContext (ALAS SQL connection)
        → SELECT … FROM {NamePrefix}.{table} WHERE … @parameters
```

| Piece | Location | Role |
|---|---|---|
| `WebLoanOptions` | `Features/WebLoans/WebLoanOptions.cs` | `NamePrefix` config + identifier validation |
| `WebLoanSql.Qualify` | `Features/WebLoans/WebLoanSql.cs` | `{prefix}.{table}` (or bare table when prefix empty) |
| `WebLoanReader` | `Infrastructure/WebLoans/WebLoanReader.cs` | Parameterized SELECTs via ALAS connection |
| DI wiring | `Composition/ServiceRegistration.cs` | `NamePrefix` set → `WebLoanReader`; else `NullWebLoanReader` |

Removed: `WebLoanDbContext`, `ConnectionStrings:WebLoan`, WebLoan `ToReadOnlyIntent`.

### Design Decisions

- **Four-part names, not OPENQUERY**, for WebLoan tables — OPENQUERY cannot take parameters; four-part names keep filters as SQL parameters and work for both same-server (`webloan.dbo.t`) and linked-server (`LINKED.webloan.dbo.t`).
- **Name prefix only** (no mode enum) — test vs live is one config string.
- **Prefix is config-only and regex-validated** (`^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*$`); table/function names stay hard-coded C# literals.
- **Unset prefix → `NullWebLoanReader`** — same “no WebLoan” behavior as the old missing connection string.

## Usage

```json
"WebLoan": {
  "NamePrefix": "webloan.dbo"
}
```

Live:

```json
"WebLoan": {
  "NamePrefix": "YOUR_LINKED_SERVER.webloan.dbo"
}
```

```powershell
dotnet user-secrets set "WebLoan:NamePrefix" "webloan.dbo"
```

**Ops:** replace every environment’s `ConnectionStrings:WebLoan` with `WebLoan:NamePrefix`. In live, the linked server must exist on the ALAS SQL Server.

## Verification

| Check | Result |
|---|---|
| `dotnet build` | 0 warnings, 0 errors |
| `dotnet test` | **496 passed**, 0 failed |
| `dotnet format --verify-no-changes` | clean |
| Grep `WebLoanDbContext` / `ConnectionStrings:WebLoan` / `TODO` | none in `*.cs` |

Covered by tests: `NamePrefix` validation, `WebLoanSql.Qualify`, CIS date/length helpers, account-id parsing, DTO shapes, authorization/access tests (unchanged contracts).

**Not covered (honest gap):** end-to-end SQL against a real SQL Server / linked server. Sqlite cannot run `TOP`, four-part names, or `webloan.dbo.is_loan`. Integration verification must happen in test (co-located `webloan`) and live (linked server) environments.

## Journey Log

- [lesson] EF Core `ToTable` cannot cleanly map four-part linked-server names; raw/`SqlQuery` with a validated prefix is the honest path.
- [dead end] Sqlite+EF fixtures for `WebLoanDbContext` cannot prove SQL Server / linked-server behavior after the rewrite — replaced with pure mapping/helper tests.
- [pivot] Intermediate DI that pointed `WebLoanDbContext` at the ALAS connection was reverted; `WebLoanDbContext` was removed in the same feature once the reader moved to SQL.

## Source Materials

| File | Role | Notes |
|------|------|-------|
| `docs/compose/specs/2026-10-16-webloan-name-prefix-design.md` | Design | See [S3] config and [S2] prefix table |
| `docs/compose/plans/2026-10-16-webloan-name-prefix.md` | Implementation plan | Complete |
