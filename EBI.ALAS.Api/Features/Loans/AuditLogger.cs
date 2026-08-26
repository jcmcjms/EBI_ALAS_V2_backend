using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;

/// <summary>
/// Audit logger implementation for tracking loan actions.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly AppDbContext _context;

    public AuditLogger(AppDbContext context)
    {
        _context = context;
    }

    public async Task LogActionAsync(
        int loanApplicationId,
        int actionByUserId,
        string action,
        string? fromStatus,
        string? toStatus,
        string? comments = null)
    {
        var loanAction = new LoanAction
        {
            LoanApplicationId = loanApplicationId,
            ActionByUserId = actionByUserId,
            Action = action,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Comments = comments,
            ActionDate = DateTime.UtcNow
        };

        _context.LoanActions.Add(loanAction);
        await _context.SaveChangesAsync();
    }

    public async Task<List<LoanAction>> GetLoanActionsAsync(int loanApplicationId)
    {
        return await _context.LoanActions
            .Where(a => a.LoanApplicationId == loanApplicationId)
            .Include(a => a.ActionByUser)
            .OrderByDescending(a => a.ActionDate)
            .ToListAsync();
    }
}
