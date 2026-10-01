
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
