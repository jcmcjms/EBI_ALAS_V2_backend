-- ── Release orphan DocumentCompletion queue rows ───────────────────────────
-- ForIncompleteDocuments is now a tracking state (no queue row).
-- Document completion is parallel work — no FIFO desk, no head owner.
-- Run ONCE after deploying the code change that removes QueueStage.DocumentCompletion.
--
-- Idempotent: rows already marked Released are not touched (WHERE clause
-- filters on active states only).

UPDATE wq
SET DequeuedAt = SYSUTCDATETIME(),
    [State]    = 'Released'
FROM WorkflowQueueItems wq
INNER JOIN LoanApplications l ON l.Id = wq.LoanApplicationId
WHERE wq.[State] IN ('Queued', 'Active')
  AND wq.Stage = 3;   -- DocumentCompletion (old enum value, now removed)
