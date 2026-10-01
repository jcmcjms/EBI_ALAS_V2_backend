
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_LoanActions_Loan_ApplicationDate')
BEGIN
    CREATE NONCLUSTERED INDEX [IX_LoanActions_Loan_ApplicationDate]
        ON [dbo].[LoanActions]([LoanApplicationId] ASC, [ActionDate] DESC, [Id] DESC)
        INCLUDE ([ActionByUserId]);
    PRINT 'Created IX_LoanActions_Loan_ApplicationDate.';
END
ELSE
BEGIN
    PRINT 'IX_LoanActions_Loan_ApplicationDate already exists — skipping.';
END
GO
