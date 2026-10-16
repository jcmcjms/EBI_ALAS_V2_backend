# WebLoan access via ALAS connection + name prefix

> [!NOTE]
> This document may not reflect the current implementation.
> See the final report for up-to-date state:
> [Final Report](../reports/webloan-name-prefix.md)

## [S1] Problem

WebLoan data is fetched today through a **separate** read-only SQL Server connection (`ConnectionStrings:WebLoan` → `WebLoanDbContext`). That forces a second connection string and a second SQL Server login in every environment.

In **test**, the `webloan` database is on the **same SQL Server instance as the ALAS database**. A second connection is unnecessary. In **live**, WebLoan may sit behind a **linked server**.

Checklist document binaries already leave the ALAS connection via `OPENQUERY(BPB_BINARY_SERVER, …)`. Core WebLoan tables should not need a dedicated app connection.

## [S2] Solution overview

Drop the dedicated WebLoan connection. Query WebLoan tables on the **ALAS SQL connection**, qualified by a configurable **name prefix**:

| Environment | `WebLoan:NamePrefix` | Example table reference |
| --- | --- | --- |
| Test (co-located) | `webloan.dbo` | `webloan.dbo.cis_info` |
| Live (linked server) | `WEBLOAN_LINKED.webloan.dbo` | `WEBLOAN_LINKED.webloan.dbo.cis_info` |

- Table names stay **hard-coded literals** in C# (`cis_info`, `loan_data`, …).
- Only the prefix comes from configuration.
- Four-part names (not `OPENQUERY`) so filters stay **parameterized**.
- Checklist binary `OPENQUERY` path is **unchanged**.

## [S3] Configuration

```json
"WebLoan": {
  "NamePrefix": "webloan.dbo"
}
```

- Startup validation (`ValidateOnStart`): when WebLoan is enabled, `NamePrefix` must be non-empty and match identifier-safe characters only (`[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)*`).
- Invalid prefix → fail at startup (not at first request).
- Unset / empty prefix → `NullWebLoanReader` (empty results), same as today’s missing connection-string behavior.
- Replaces `ConnectionStrings:WebLoan` as the WebLoan wiring key (ops/deploy note).

## [S4] Components

| Component | Change |
| --- | --- |
| `WebLoanOptions` | New. Holds `NamePrefix`. |
| Qualifier helper | New, tiny. `Qualify("cis_info")` → `{prefix}.cis_info`. Prefix validated; table names are literals. |
| `WebLoanReader` | Rewrite queries to parameterized SELECTs via ALAS connection (`AlasDbContext` / `GetDbConnection`, `SqlQuery<T>` where useful). Public `IWebLoanReader` surface **unchanged**. |
| `ServiceRegistration` | Wire `WebLoanOptions`, drop `WebLoan` connection string / `WebLoanDbContext` registration, drop WebLoan `ToReadOnlyIntent` path. |
| `WebLoanDbContext` | Remove once unused (entities were only for this reader). |
| Endpoints / sync hosts / `ChecklistDocumentReader` | Unchanged consumers of `IWebLoanReader`. |

WebLoan access remains **SELECT-only** by construction (no write APIs on this path).

## [S5] Data flow

```
HTTP / hosted service
  → IWebLoanReader (unchanged contracts)
    → WebLoanReader
      → ALAS SqlConnection (AlasDbContext connection)
        → SELECT … FROM {NamePrefix}.{table} WHERE … @p
```

Test: prefix `webloan.dbo` resolves inside the ALAS server.  
Live: prefix `LINKED.webloan.dbo` routes through the linked server. Same code.

## [S6] Error handling

- Invalid `NamePrefix` → startup failure.
- Missing prefix → `NullWebLoanReader` (no throw).
- Linked-server / SQL failures propagate as query errors. Do not swallow into empty success.
- `CancellationToken` flows through all async I/O.
- Result bounds/pagination remain as today (existing clamps on `max`/`pageSize`).

## [S7] Testing

- Options/qualifier: valid prefix, invalid characters, empty → null reader.
- Reader: SQL qualification uses `NamePrefix`; parameters used for filters (no string-concat of values).
- Existing `IWebLoanReader` behavior tests (CIS lookup, access filtering, auth) stay green or are adapted off `WebLoanDbContext`/Sqlite EF fixtures.
- Build + analyzers; no ALAS migrations.

## [S8] Out of scope

- Checklist document `OPENQUERY(BPB_BINARY_SERVER, …)` binary path.
- ALAS schema changes.
- Performance tuning of linked-server latency beyond keeping queries bounded and projected.
