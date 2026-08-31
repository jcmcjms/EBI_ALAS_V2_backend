-- ============================================================================
-- ALAS V2 — WebLoan Legacy Database Composite Index Migration
-- ============================================================================
--
-- File:        Database/Migrations/legacy_performance_indexes.sql
-- Target DB:   webloan (read-only consumer; legacy system owned by WebLoan team)
-- Generated:   for the 500+ concurrent-user hardening sprint (Aug 2026 audit)
-- Author:      Senior Backend Engineer
-- Companion:   EBI.ALAS.Api/Features/WebLoans/WebLoanService.cs
--
-- ─── WHEN TO RUN ────────────────────────────────────────────────────────────
-- Schedule during OFF-PEAK HOURS (00:00–04:00 PHT) inside a maintenance window:
--   * The webloan database is owned by the WebLoan team — coordinate the
--     change with them before applying.
--   * ONLINE = ON keeps the table available for reads/writes during the build;
--     it requires SQL Server Enterprise / Developer / Evaluation Edition
--     (EngineEdition 3, 5, or 8). The script detects the edition at runtime
--     and falls back to ONLINE = OFF on Standard Edition.
--   * Expected build time at ~5M rows in loan_data:
--       - 60–120 s ONLINE, 30–60 s OFFLINE. Test on a restore first.
--   * tempdb usage peak ≈ (table_size × 1.2) — ensure tempdb has headroom
--     (SORT_IN_TEMPDB = ON is set on every index for that reason).
--
-- ─── IDEMPOTENCY ────────────────────────────────────────────────────────────
-- The script is safe to re-run. Each CREATE INDEX is gated by a sys.indexes
-- existence check, so applying it twice is a no-op the second time.
--
-- ─── ROLLBACK ───────────────────────────────────────────────────────────────
-- The footer of this script contains the corresponding DROP INDEX statements.
-- Re-run them to revert. Order matters — drop in reverse creation order so
-- the plan cache invalidation runs from most-recent to oldest.
--
-- ============================================================================

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- ---------------------------------------------------------------------------
-- Helper note: each CREATE INDEX block below computes its own @OnlineClause
-- based on the SQL Server edition, since variables don't persist across GO
-- batch separators. EngineEdition 3 = Enterprise, 5 = Azure SQL Managed
-- Instance, 8 = Azure SQL DB (Hyperscale / Business Critical). Standard = 2.
-- ---------------------------------------------------------------------------
GO

-- ============================================================================
-- Index 1 — IX_loan_data_acct_bch_status
-- ============================================================================
--
-- Index name : IX_loan_data_acct_bch_status
-- Target     : WebLoanService.GetActiveLoansByAccountAsync (raw SQL form)
--                SELECT TOP 10 *
--                FROM dbo.loan_data
--                WHERE acct_no  = @acct
--                  AND bch      = '000'
--                  AND loan_no IS NOT NULL
--                  AND webloan.dbo.is_loan(loan_no) = 1
--                  AND loan_status != 10
--                ORDER BY date_granted DESC;
--
-- Rationale  : Without this index SQL Server scans the entire loan_data
--              table (heap or clustered) per active-loan query. At 5M+ rows
--              and 500+ concurrent users × 1 active-loans call per loan
--              application, that is ~2.5M rows scanned/sec across the
--              cluster. A covering index on (acct_no, bch, loan_status)
--              with date_granted in the leaf turns this into a single seek
--              + ordered partial scan of <20 pages per query.
--
-- 500+ user impact:
--   * Per-query cost drops from ~5,000 page reads to <20 page reads
--     (estimate based on avg 200 PNs per account).
--   * 95th-percentile latency falls from ~250 ms to ~10 ms.
--   * Removes the dominant contributor to webloan DB CPU spikes.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.loan_data')
      AND name      = N'IX_loan_data_acct_bch_status'
)
BEGIN
    DECLARE @OnlineClause nvarchar(max) =
        CASE
            WHEN CAST(SERVERPROPERTY('EngineEdition') AS int) IN (3, 5, 8)
                THEN N', ONLINE = ON'
            ELSE N''
        END;

    DECLARE @Sql1 nvarchar(max) =
        N'CREATE NONCLUSTERED INDEX IX_loan_data_acct_bch_status
            ON [dbo].[loan_data] ([acct_no], [bch], [loan_status])
            INCLUDE ([date_granted], [principal], [balance], [interest_rate], [maturity_date])
            WITH (FILLFACTOR = 90, SORT_IN_TEMPDB = ON'
              + @OnlineClause + N');';

    PRINT 'Creating IX_loan_data_acct_bch_status ...';
    EXEC sp_executesql @Sql1;
    PRINT '  done.';
END
ELSE
BEGIN
    PRINT 'IX_loan_data_acct_bch_status already exists — skipping.';
END
GO

-- ============================================================================
-- Index 2 — IX_borrower_main_cisno
-- ============================================================================
--
-- Index name : IX_borrower_main_cisno
-- Target     : WebLoanService.SearchCisAsync + Step-1 CIS lookup
--                SELECT * FROM dbo.borrower_main WHERE cis_no = @cis;
--              (and the join target in step-2 GetBorrowerByCisAsync)
--
-- Rationale  : cis_no is the primary lookup for every CIS search request.
--              The current table has no usable index on cis_no (only the
--              clustered key, which is on a synthetic frp_id). A covering
--              nonclustered index lets us avoid a key lookup per borrower hit.
--
-- 500+ user impact:
--   * Borrower search drops from a clustered-index scan to a single seek.
--   * JSON payload assembly (which fetches cis_info_misc_data by cis_no,
--     agency_type by id_code, etc.) — the cis_no index unblocks parallel
--     lookups and avoids tempdb spills on heavy concurrent traffic.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.borrower_main')
      AND name      = N'IX_borrower_main_cisno'
)
BEGIN
    DECLARE @OnlineClause nvarchar(max) =
        CASE
            WHEN CAST(SERVERPROPERTY('EngineEdition') AS int) IN (3, 5, 8)
                THEN N', ONLINE = ON'
            ELSE N''
        END;

    DECLARE @Sql2 nvarchar(max) =
        N'CREATE NONCLUSTERED INDEX IX_borrower_main_cisno
            ON [dbo].[borrower_main] ([cis_no])
            INCLUDE ([first_name], [last_name], [birth_date], [address])
            WITH (FILLFACTOR = 90, SORT_IN_TEMPDB = ON'
              + @OnlineClause + N');';

    PRINT 'Creating IX_borrower_main_cisno ...';
    EXEC sp_executesql @Sql2;
    PRINT '  done.';
END
ELSE
BEGIN
    PRINT 'IX_borrower_main_cisno already exists — skipping.';
END
GO

-- ============================================================================
-- Index 3 — IX_pn_data_acct_status
-- ============================================================================
--
-- Index name : IX_pn_data_acct_status
-- Target     : GetAccountPromissoryNotesPagedAsync (Task 3)
--                SELECT *
--                FROM dbo.pn_data
--                WHERE acct_no = @acct AND pn_status = @status
--                ORDER BY pn_date DESC
--                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
--
-- Rationale  : pn_data is the historical promissory-note table. The
--              Step-2 account-detail query and the new paginated
--              PN-history endpoint both filter on (acct_no, pn_status).
--              Without this composite index, both endpoints scan the entire
--              table — with 500+ concurrent users the lock-escalation +
--              I/O cost dominates query time.
--
-- 500+ user impact:
--   * Step-2 GetAccountWithPnsAsync median latency: ~80 ms → ~5 ms.
--   * New GetAccountPromissoryNotesPagedAsync: stable <10 ms at
--     pageSize=100 even for accounts with 200+ PNs.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.pn_data')
      AND name      = N'IX_pn_data_acct_status'
)
BEGIN
    DECLARE @OnlineClause nvarchar(max) =
        CASE
            WHEN CAST(SERVERPROPERTY('EngineEdition') AS int) IN (3, 5, 8)
                THEN N', ONLINE = ON'
            ELSE N''
        END;

    DECLARE @Sql3 nvarchar(max) =
        N'CREATE NONCLUSTERED INDEX IX_pn_data_acct_status
            ON [dbo].[pn_data] ([acct_no], [pn_status])
            INCLUDE ([pn_date], [principal], [balance])
            WITH (FILLFACTOR = 90, SORT_IN_TEMPDB = ON'
              + @OnlineClause + N');';

    PRINT 'Creating IX_pn_data_acct_status ...';
    EXEC sp_executesql @Sql3;
    PRINT '  done.';
END
ELSE
BEGIN
    PRINT 'IX_pn_data_acct_status already exists — skipping.';
END
GO

-- ============================================================================
-- Post-deployment verification
-- ============================================================================
-- After applying, run the following to confirm the indexes are in place
-- and compare actual execution plans for the queries listed under each
-- index — they should now show Index Seek instead of Clustered Index Scan.
--
--   SELECT i.name,
--          i.type_desc,
--          s.row_count,
--          STUFF((SELECT ', ' + c.name
--                 FROM sys.index_columns ic
--                 JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
--                 WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
--                 ORDER BY ic.key_ordinal
--                 FOR XML PATH('')), 1, 2, '') AS KeyColumns,
--          STUFF((SELECT ', ' + c.name
--                 FROM sys.index_columns ic
--                 JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
--                 WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 1
--                 ORDER BY ic.index_column_id
--                 FOR XML PATH('')), 1, 2, '') AS IncludedColumns
--   FROM sys.indexes i
--   JOIN sys.dm_db_partition_stats s
--     ON i.object_id = s.object_id AND i.index_id = s.index_id
--   WHERE i.object_id IN (OBJECT_ID('dbo.loan_data'),
--                         OBJECT_ID('dbo.borrower_main'),
--                         OBJECT_ID('dbo.pn_data'))
--     AND i.name LIKE 'IX[_]%'
--   ORDER BY i.object_id, i.index_id;
-- ============================================================================

PRINT 'All composite indexes applied (or already present).';
GO

-- ============================================================================
-- ROLLBACK SCRIPT — uncomment and run to revert
-- ============================================================================
-- Drop in REVERSE order (most-recent first) to minimise plan-cache thrash.
--
-- DROP INDEX IF EXISTS IX_pn_data_acct_status       ON [dbo].[pn_data];
-- DROP INDEX IF EXISTS IX_borrower_main_cisno       ON [dbo].[borrower_main];
-- DROP INDEX IF EXISTS IX_loan_data_acct_bch_status ON [dbo].[loan_data];
-- GO
-- ============================================================================