
UPDATE wq
SET DequeuedAt = SYSUTCDATETIME(),
    [State]    = 'Released'
FROM WorkflowQueueItems wq
INNER JOIN LoanApplications l ON l.Id = wq.LoanApplicationId
WHERE wq.[State] IN ('Queued', 'Active')
  AND wq.Stage = 3;
