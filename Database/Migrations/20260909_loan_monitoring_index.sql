
BEGIN TRANSACTION;
GO

SET XACT_ABORT ON;
GO

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
            [ApplicationGroupNo],
            [FirstName],
            [LastName],
            [Product],
            [Purpose],
            [ProposedAmount],
            [LastActionDate],
            [CreatedById]
        )
        WITH (
            ONLINE = ON,
            SORT_IN_TEMPDB = ON,
            FILLFACTOR = 90,
            DATA_COMPRESSION = PAGE
        );

    PRINT 'Created IX_LoanApplications_BranchCode_Status_ApplicationDate.';
END
ELSE
BEGIN
    PRINT 'IX_LoanApplications_BranchCode_Status_ApplicationDate already exists — skipping.';
END
GO

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
