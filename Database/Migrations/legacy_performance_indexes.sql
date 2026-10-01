
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

GO

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

PRINT 'All composite indexes applied (or already present).';
GO
