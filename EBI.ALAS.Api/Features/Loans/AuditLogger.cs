using EBI.ALAS.Api.Common.Time;
using EBI.ALAS.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EBI.ALAS.Api.Features.Loans;
public class AuditLogger : IAuditLogger
{
    private readonly AppDbContext _context;
    private readonly ITimeProvider _timeProvider;

    public AuditLogger(AppDbContext context, ITimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
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
            ActionDate = _timeProvider.UtcNow
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
