-- ── WorkflowQueueItems backfill ──────────────────────────────────────────────
-- Run AFTER the EF migration creates the WorkflowQueueItems table.
-- This seeds existing in-flight loans into their correct queue partitions
-- so day-one queues are correct. Heads get promoted (and owners materialized)
-- by the QueueReconciliationHostedService on its first tick.

INSERT INTO WorkflowQueueItems (LoanApplicationId, Stage, PartitionKey, EnqueuedAt, [State])
SELECT
    l.Id,
    CASE l.Status
        WHEN 'ForRecommendation' THEN 'Recommendation'
        WHEN 'ForChecking'       THEN 'Evaluation'
        ELSE                          'Approval'
    END,
    CASE l.Status
        WHEN 'ForApproval'
            THEN CONCAT('APP:', l.BranchCode, ':', ISNULL(l.RequiredApprovalTier, 0))
        ELSE CONCAT(
            CASE l.Status
                WHEN 'ForRecommendation' THEN 'REC'
                ELSE                          'EVA'
            END,
            ':', l.BranchCode)
    END,
    l.ApplicationDate,
    'Queued'
FROM LoanApplications l
WHERE l.Status IN ('ForRecommendation', 'ForChecking', 'ForApproval');
