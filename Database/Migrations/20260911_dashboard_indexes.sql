-- =====================================================================
-- Dashboard overview indexes
-- =====================================================================
-- Purpose: Make every probe in DashboardService.ComputeAsync an index
--         seek + TOP-N. The dashboard polls a single cached aggregate
--         endpoint per branch; without these indexes that endpoint
--         degrades into a full table scan as the LoanApplications /
--         LoanActions tables grow into the hundreds of thousands of rows.
--
-- Query patterns this migration covers:
--   * Pending queue   — WHERE Status IN (...) ORDER BY LastActionDate
--                        (covered by IX_LoanApplications_Status_LastAction)
--   * Submission delta— WHERE BranchCode = @bch AND ApplicationDate ...
--                        (covered by IX_LoanApplications_Branch_ApplicationDate)
--   * Weekly decisions — WHERE ActionDate >= @week AND ToStatus IN (...)
--                        (covered by IX_LoanActions_ToStatus_ActionDate)
--   * Now serving      — WHERE ActionByUserId = @u AND ActionDate ...
--                        (covered by IX_LoanActions_ActionBy_ActionDate)
--
-- Idempotent: guards each step with sys.indexes lookup so re-running
-- this script is a no-op. Safe to apply during a quiet window —
-- CREATE INDEX WITH (ONLINE = ON, SORT_IN_TEMPDB = ON) prevents table
-- locks during build.
--
-- Apply order: any time after multi_loan_submission migration.
-- =====================================================================

BEGIN TRANSACTION;
GO

SET XACT_ABORT ON;
GO

-- ── 1. Pending queue (Status, LastActionDate) ──────────────────────────────
-- The existing IX_LoanApplications_Status_BranchCode_Date serves most
-- monitoring queries, but its key columns are Status then BranchCode
-- then ApplicationDate — never LastActionDate. The dashboard's pending
-- queue is "oldest waiting first" so it needs (Status, LastActionDate)
-- to stream rows in queue order without an extra sort. A narrow index
-- (no includes) keeps the seek cost minimal since the dashboard only
-- projects four small columns off it.
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LoanApplications_Status_LastAction'
      AND object_id = OBJECT_ID(N'[LoanApplications]')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LoanApplications_Status_LastAction]
        ON [dbo].[LoanApplications] (
            [Status]         ASC,
            [LastActionDate] ASC
        )
        INCLUDE (
            [LamId],         -- queue column
            [BranchCode]     -- queue column
        )
        WITH (
            ONLINE = ON,
            SORT_IN_TEMPDB = ON,
            FILLFACTOR = 95
        );

    PRINT 'Created IX_LoanApplications_Status_LastAction.';
END
ELSE
BEGIN
    PRINT 'IX_LoanApplications_Status_LastAction already exists — skipping.';
END
GO

-- ── 2. Submission delta (BranchCode, ApplicationDate) ──────────────────────
-- "How many loans came in today vs yesterday, scoped to my branch."
-- Branch-scoped scans on ApplicationDate are otherwise a clustered-index
-- scan + residual predicate — fine at 10k rows, brutal at 1M.
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LoanApplications_Branch_ApplicationDate'
      AND object_id = OBJECT_ID(N'[LoanApplications]')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LoanApplications_Branch_ApplicationDate]
        ON [dbo].[LoanApplications] (
            [BranchCode]      ASC,
            [ApplicationDate] DESC
        )
        WITH (
            ONLINE = ON,
            SORT_IN_TEMPDB = ON,
            FILLFACTOR = 95
        );

    PRINT 'Created IX_LoanApplications_Branch_ApplicationDate.';
END
ELSE
BEGIN
    PRINT 'IX_LoanApplications_Branch_ApplicationDate already exists — skipping.';
END
GO

-- ── 3. Weekly decisions (ToStatus, ActionDate) ─────────────────────────────
-- One indexed probe feeds three widgets (pushbacks-today, approved-today
-- + %vs avg, weekly trend chart). (ToStatus, ActionDate DESC) lets the
-- optimizer do a single range seek filtered on the two terminal-status
-- literals instead of a clustered-index scan over LoanActions.
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LoanActions_ToStatus_ActionDate'
      AND object_id = OBJECT_ID(N'[LoanActions]')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LoanActions_ToStatus_ActionDate]
        ON [dbo].[LoanActions] (
            [ToStatus]   ASC,
            [ActionDate] DESC
        )
        WITH (
            ONLINE = ON,
            SORT_IN_TEMPDB = ON,
            FILLFACTOR = 95
        );

    PRINT 'Created IX_LoanActions_ToStatus_ActionDate.';
END
ELSE
BEGIN
    PRINT 'IX_LoanActions_ToStatus_ActionDate already exists — skipping.';
END
GO

-- ── 4. Now-serving (ActionByUserId, ActionDate) ────────────────────────────
-- "Officers who acted in the last hour" — the dashboard caps the
-- in-memory grouping at 200 rows after a (UserId, ActionDate DESC)
-- seek. Without it the query reads the whole action log just to
-- discard most of it.
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LoanActions_ActionBy_ActionDate'
      AND object_id = OBJECT_ID(N'[LoanActions]')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LoanActions_ActionBy_ActionDate]
        ON [dbo].[LoanActions] (
            [ActionByUserId] ASC,
            [ActionDate]     DESC
        )
        INCLUDE (
            [LoanApplicationId]
        )
        WITH (
            ONLINE = ON,
            SORT_IN_TEMPDB = ON,
            FILLFACTOR = 95
        );

    PRINT 'Created IX_LoanActions_ActionBy_ActionDate.';
END
ELSE
BEGIN
    PRINT 'IX_LoanActions_ActionBy_ActionDate already exists — skipping.';
END
GO

-- ── 5. Stats refresh ───────────────────────────────────────────────────────
-- New indexes = new histograms. FULLSCAN so the first post-deploy
-- dashboard query gets accurate cardinality estimates (otherwise the
-- legacy stats on the table will mislead the plan choice until the
-- next auto-update fires).
DECLARE @ix NVARCHAR(200);
DECLARE ix_cur CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[LoanApplications]')
      AND name IN (
          N'IX_LoanApplications_Status_LastAction',
          N'IX_LoanApplications_Branch_ApplicationDate'
      )
    UNION ALL
    SELECT name FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[LoanActions]')
      AND name IN (
          N'IX_LoanActions_ToStatus_ActionDate',
          N'IX_LoanActions_ActionBy_ActionDate'
      );

OPEN ix_cur;
FETCH NEXT FROM ix_cur INTO @ix;

WHILE @@FETCH_STATUS = 0
BEGIN
    EXEC sp_executesql N'UPDATE STATISTICS [dbo].[LoanActions] [' + @ix + N'] WITH FULLSCAN;';
    FETCH NEXT FROM ix_cur INTO @ix;
END

CLOSE ix_cur;
DEALLOCATE ix_cur;
GO

COMMIT;
GO

-- Verify
SELECT
    OBJECT_NAME(i.object_id) AS TableName,
    i.name                   AS IndexName,
    i.type_desc              AS IndexType,
    STUFF((
        SELECT ', ' + c.name + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE ' ASC' END
        FROM sys.index_columns ic
        INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
        ORDER BY ic.key_ordinal
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS KeyColumns
FROM sys.indexes i
WHERE i.name IN (
    N'IX_LoanApplications_Status_LastAction',
    N'IX_LoanApplications_Branch_ApplicationDate',
    N'IX_LoanActions_ToStatus_ActionDate',
    N'IX_LoanActions_ActionBy_ActionDate'
)
ORDER BY TableName, IndexName;
GO