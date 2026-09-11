-- Index: IX_LoanActions_Loan_ApplicationDate
-- Purpose: Makes the per-row "latest action" probe (OUTER APPLY in
--          GET /api/loans) index-only on the LoanActions side.
--          Key order (LoanApplicationId, ActionDate DESC, Id DESC)
--          matches the ORDER BY used by the correlated sub-query.
--          INCLUDE (ActionByUserId) covers the projection so the
--          engine never touches the base table for this lookup.
--
-- Idempotent: checks sys.indexes before creating.

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
