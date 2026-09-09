-- =====================================================================
-- IX_LoanApplications_BranchCode_Status_ApplicationDate
-- =====================================================================
-- Purpose: Keep GET /api/loans (the paginated loan-submissions list that
--         shares the POST /api/loans response envelope) O(log n) as the
--         table grows to hundreds of thousands of rows.
--
-- Query pattern this index covers:
--   WHERE BranchCode = @bch          [always — branch scoping]
--     AND (@status IS NULL OR Status IN (...))   [optional status filter]
--   ORDER BY ApplicationDate DESC     [default sort]
--
-- Note: a near-identical index already exists in the schema —
-- IX_LoanApplications_Status_BranchCode_Date, declared in AppDbContext.cs
-- with columns (Status, BranchCode, ApplicationDate DESC). It serves the
-- same queries because the SQL Server optimizer can also seek on
-- (BranchCode, Status) when Status is filtered. This migration adds an
-- *additional* index keyed on (BranchCode, Status, ApplicationDate DESC)
-- so the leading column is the always-present `BranchCode` — a stricter
-- leftmost-prefix match for branch-scoped monitoring queries.
--
-- Concretely, when a branch officer (non-admin) hits the monitoring
-- page, the optimizer can seek on BranchCode alone (the leftmost
-- prefix) and stream rows in ApplicationDate DESC order without an
-- extra sort — the existing composite index can't do that as cleanly
-- because its leading column is Status, which is unfiltered.
--
-- Idempotent: guards each step with sys.indexes lookup, so re-running
-- this script is a no-op. Safe to apply on production during a quiet
-- window — CREATE INDEX WITH (ONLINE = ON, SORT_IN_TEMPDB = ON)
-- prevents table locks during build.
--
-- Apply order: any time after multi_loan_submission migration.
-- =====================================================================

BEGIN TRANSACTION;
GO

SET XACT_ABORT ON;
GO

-- ── 1. Skip if already present ──────────────────────────────────────────────
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LoanApplications_BranchCode_Status_ApplicationDate'
      AND object_id = OBJECT_ID(N'[LoanApplications]')
)
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LoanApplications_BranchCode_Status_ApplicationDate]
        ON [dbo].[LoanApplications] (
            [BranchCode]      ASC,
            [Status]          ASC,
            [ApplicationDate] DESC
        )
        INCLUDE (
            [ApplicationGroupNo],   -- form number (search)
            [FirstName],            -- search
            [LastName],             -- search
            [Product],              -- list column
            [Purpose],              -- list column
            [ProposedAmount],       -- list column
            [LastActionDate],       -- list column
            [CreatedById]           -- join key for CreatedByName projection
        )
        WITH (
            ONLINE = ON,                -- no table lock during build
            SORT_IN_TEMPDB = ON,        -- reduces contention on the target filegroup
            FILLFACTOR = 90,            -- monitoring table sees heavy insert churn
            DATA_COMPRESSION = PAGE     -- compresses the large JSON columns elsewhere, marginal here but consistent
        );

    PRINT 'Created IX_LoanApplications_BranchCode_Status_ApplicationDate.';
END
ELSE
BEGIN
    PRINT 'IX_LoanApplications_BranchCode_Status_ApplicationDate already exists — skipping.';
END
GO

-- ── 2. Stats refresh ────────────────────────────────────────────────────────
-- New index = new histogram for the optimizer. Force an update with
-- FULLSCAN so the first post-deploy query gets accurate cardinality
-- estimates (otherwise the legacy stats on (Status, BranchCode, Date)
-- will mislead the plan choice until the next auto-update fires).
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_LoanApplications_BranchCode_Status_ApplicationDate'
      AND object_id = OBJECT_ID(N'[LoanApplications]')
)
BEGIN
    UPDATE STATISTICS [dbo].[LoanApplications]
        [IX_LoanApplications_BranchCode_Status_ApplicationDate]
        WITH FULLSCAN;
END
GO

COMMIT;
GO

-- Verify
SELECT
    i.name            AS IndexName,
    i.type_desc       AS IndexType,
    STUFF((
        SELECT ', ' + c.name + CASE WHEN ic.is_descending_key = 1 THEN ' DESC' ELSE ' ASC' END
        FROM sys.index_columns ic
        INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
        ORDER BY ic.key_ordinal
        FOR XML PATH(''), TYPE
    ).value('.', 'NVARCHAR(MAX)'), 1, 2, '') AS KeyColumns,
    i.fill_factor     AS FillFactor,
    i.is_padded       AS IsPadded
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID(N'[LoanApplications]')
  AND i.name = N'IX_LoanApplications_BranchCode_Status_ApplicationDate';
GO
