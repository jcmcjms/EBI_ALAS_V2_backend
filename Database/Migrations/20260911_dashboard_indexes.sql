
BEGIN TRANSACTION;
GO

SET XACT_ABORT ON;
GO

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
            [LamId],
            [BranchCode]
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
