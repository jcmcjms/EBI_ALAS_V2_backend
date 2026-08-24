using Alas.Application.Admin.Users;
using Alas.Application.Loans;
using Alas.Domain.Entities;
using Alas.Infrastructure.Identity;
using Alas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Alas.Infrastructure.Services;

public sealed class LoanService
{
    private readonly AlasDbContext _context;
    private readonly UserManager<AppUser> _userManager;

    public LoanService(
        AlasDbContext context,
        UserManager<AppUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<PagedResult<LoanListItemDto>> ListAsync(
        int page,
        int pageSize,
        string? search,
        LoanStatus? status,
        string? branchId,
        CancellationToken cancellationToken)
    {
        var query = _context.Loans.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(l =>
                l.LoanNumber.Contains(term) ||
                l.BorrowerName.Contains(term));
        }

        if (status.HasValue)
        {
            query = query.Where(l => l.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(branchId))
        {
            query = query.Where(l => l.BranchId == branchId);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var loans = await query
            .OrderByDescending(l => l.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LoanListItemDto(
                l.Id,
                l.LoanNumber,
                l.BorrowerName,
                l.PrincipalAmount,
                l.InterestRate,
                l.TermMonths,
                l.Status,
                l.BranchId,
                l.CreatedUtc))
            .ToListAsync(cancellationToken);

        return new PagedResult<LoanListItemDto>(
            loans, totalCount, page, pageSize);
    }

    public async Task<LoanDetailDto?> GetDetailAsync(
        Guid loanId,
        CancellationToken cancellationToken)
    {
        var loan = await _context.Loans
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);

        if (loan is null)
        {
            return null;
        }

        var createdByUser = await _userManager.FindByIdAsync(loan.CreatedByUserId.ToString());
        var approvedByUser = loan.ApprovedByUserId.HasValue
            ? await _userManager.FindByIdAsync(loan.ApprovedByUserId.Value.ToString())
            : null;

        return new LoanDetailDto(
            loan.Id,
            loan.LoanNumber,
            loan.BorrowerName,
            loan.BorrowerContact,
            loan.PrincipalAmount,
            loan.InterestRate,
            loan.TermMonths,
            loan.Purpose,
            loan.BranchId,
            loan.Status,
            createdByUser?.FullName ?? createdByUser?.UserName ?? "Unknown",
            approvedByUser?.FullName ?? approvedByUser?.UserName,
            loan.CreatedUtc,
            loan.ApprovedUtc,
            loan.DisbursedUtc,
            loan.Remarks,
            loan.RejectionReason);
    }

    public async Task<LoanDetailDto> CreateAsync(
        CreateLoanRequest request,
        Guid createdByUserId,
        CancellationToken cancellationToken)
    {
        var loanNumber = await GenerateLoanNumberAsync(cancellationToken);

        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            LoanNumber = loanNumber,
            BorrowerName = request.BorrowerName.Trim(),
            BorrowerContact = request.BorrowerContact?.Trim(),
            PrincipalAmount = request.PrincipalAmount,
            InterestRate = request.InterestRate,
            TermMonths = request.TermMonths,
            Purpose = request.Purpose?.Trim(),
            BranchId = request.BranchId?.Trim(),
            Status = LoanStatus.Draft,
            CreatedByUserId = createdByUserId,
            CreatedUtc = DateTimeOffset.UtcNow,
            Remarks = request.Remarks?.Trim()
        };

        _context.Loans.Add(loan);
        await _context.SaveChangesAsync(cancellationToken);

        return (await GetDetailAsync(loan.Id, cancellationToken))!;
    }

    public async Task<LoanDetailDto?> ApproveAsync(
        Guid loanId,
        Guid approvedByUserId,
        string? remarks,
        CancellationToken cancellationToken)
    {
        var loan = await _context.Loans
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);

        if (loan is null)
        {
            return null;
        }

        if (loan.Status != LoanStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"Cannot approve loan in {loan.Status} status. Must be PendingApproval.");
        }

        loan.Status = LoanStatus.Approved;
        loan.ApprovedByUserId = approvedByUserId;
        loan.ApprovedUtc = DateTimeOffset.UtcNow;
        loan.Remarks = remarks?.Trim();

        await _context.SaveChangesAsync(cancellationToken);

        return await GetDetailAsync(loan.Id, cancellationToken);
    }

    public async Task<LoanDetailDto?> RejectAsync(
        Guid loanId,
        Guid rejectedByUserId,
        string rejectionReason,
        CancellationToken cancellationToken)
    {
        var loan = await _context.Loans
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);

        if (loan is null)
        {
            return null;
        }

        if (loan.Status != LoanStatus.PendingApproval &&
            loan.Status != LoanStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"Cannot reject loan in {loan.Status} status.");
        }

        loan.Status = LoanStatus.Rejected;
        loan.ApprovedByUserId = rejectedByUserId;
        loan.ApprovedUtc = DateTimeOffset.UtcNow;
        loan.RejectionReason = rejectionReason.Trim();

        await _context.SaveChangesAsync(cancellationToken);

        return await GetDetailAsync(loan.Id, cancellationToken);
    }

    public async Task<LoanDetailDto?> SubmitForReviewAsync(
        Guid loanId,
        CancellationToken cancellationToken)
    {
        var loan = await _context.Loans
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);

        if (loan is null)
        {
            return null;
        }

        if (loan.Status != LoanStatus.Draft)
        {
            throw new InvalidOperationException(
                $"Cannot submit loan in {loan.Status} status. Must be Draft.");
        }

        loan.Status = LoanStatus.PendingReview;
        await _context.SaveChangesAsync(cancellationToken);

        return await GetDetailAsync(loan.Id, cancellationToken);
    }

    public async Task<LoanDetailDto?> SubmitForApprovalAsync(
        Guid loanId,
        CancellationToken cancellationToken)
    {
        var loan = await _context.Loans
            .FirstOrDefaultAsync(l => l.Id == loanId, cancellationToken);

        if (loan is null)
        {
            return null;
        }

        if (loan.Status != LoanStatus.PendingReview)
        {
            throw new InvalidOperationException(
                $"Cannot submit for approval in {loan.Status} status. Must be PendingReview.");
        }

        loan.Status = LoanStatus.PendingApproval;
        await _context.SaveChangesAsync(cancellationToken);

        return await GetDetailAsync(loan.Id, cancellationToken);
    }

    public async Task<LoanMonitorDto> GetMonitorAsync(
        CancellationToken cancellationToken)
    {
        var loans = await _context.Loans
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new LoanMonitorDto(
            TotalLoans: loans.Count,
            DraftCount: loans.Count(l => l.Status == LoanStatus.Draft),
            PendingReviewCount: loans.Count(l => l.Status == LoanStatus.PendingReview),
            PendingApprovalCount: loans.Count(l => l.Status == LoanStatus.PendingApproval),
            ApprovedCount: loans.Count(l => l.Status == LoanStatus.Approved),
            DisbursedCount: loans.Count(l => l.Status == LoanStatus.Disbursed),
            RejectedCount: loans.Count(l => l.Status == LoanStatus.Rejected),
            CancelledCount: loans.Count(l => l.Status == LoanStatus.Cancelled),
            TotalPrincipal: loans.Sum(l => l.PrincipalAmount),
            DisbursedPrincipal: loans
                .Where(l => l.Status == LoanStatus.Disbursed)
                .Sum(l => l.PrincipalAmount));
    }

    private async Task<string> GenerateLoanNumberAsync(CancellationToken cancellationToken)
    {
        var date = DateTime.UtcNow;
        var prefix = $"LN-{date:yyyyMMdd}-";

        var lastLoan = await _context.Loans
            .AsNoTracking()
            .Where(l => l.LoanNumber.StartsWith(prefix))
            .OrderByDescending(l => l.LoanNumber)
            .Select(l => l.LoanNumber)
            .FirstOrDefaultAsync(cancellationToken);

        var sequence = 1;

        if (lastLoan is not null)
        {
            var lastSequence = lastLoan.Substring(lastLoan.LastIndexOf('-') + 1);
            if (int.TryParse(lastSequence, out var parsed))
            {
                sequence = parsed + 1;
            }
        }

        return $"{prefix}{sequence:D4}";
    }
}
