using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Deviations;

public sealed class DeviationRemarkService(
    AlasDbContext db,
    IRealtimeNotifier? realtime,
    TimeProvider timeProvider)
{
    public async Task<DeviationRemark> CreateAsync(
        LoanApplication loan,
        LoanDeviation deviation,
        string body,
        Guid? parentRemarkId,
        Guid authorId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        Guid? parentAuthorId = null;
        if (parentRemarkId is { } parentId)
        {
            parentAuthorId = await db.DeviationRemarks.AsNoTracking()
                .Where(r => r.Id == parentId && r.LoanDeviationId == deviation.Id)
                .Select(r => (Guid?)r.AuthorId)
                .FirstOrDefaultAsync(cancellationToken);
            if (parentAuthorId is null)
            {
                throw new Composition.Errors.ValidationException("Parent remark was not found.");
            }
        }

        var remark = DeviationRemark.Create(
            deviation.Id,
            parentRemarkId,
            body,
            authorId,
            timeProvider.GetUtcNow());
        db.DeviationRemarks.Add(remark);
        await db.SaveChangesAsync(cancellationToken);

        await NotifyParticipantsAsync(loan, deviation, remark, parentAuthorId, authorId, cancellationToken);
        return remark;
    }

    private async Task NotifyParticipantsAsync(
        LoanApplication loan,
        LoanDeviation deviation,
        DeviationRemark remark,
        Guid? parentAuthorId,
        Guid authorId,
        CancellationToken cancellationToken)
    {
        var recipients = new HashSet<Guid>();
        if (parentAuthorId is { } parentAuthor && parentAuthor != authorId)
        {
            recipients.Add(parentAuthor);
        }

        if (loan.CreatedBy != authorId)
        {
            recipients.Add(loan.CreatedBy);
        }

        var threadAuthors = await db.DeviationRemarks.AsNoTracking()
            .Where(r => r.LoanDeviationId == deviation.Id && r.AuthorId != authorId)
            .Select(r => r.AuthorId)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var threadAuthor in threadAuthors)
        {
            recipients.Add(threadAuthor);
        }

        if (recipients.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var preview = remark.Body.Length > 120
            ? remark.Body[..117] + "..."
            : remark.Body;
        var title = "New deviation remark";
        var body = $"Loan {loan.LamId} · {deviation.Description}: {preview}";
        var link = $"/loans/approval/{loan.Id}";

        foreach (var recipient in recipients)
        {
            var notification = Notification.Create(
                recipient,
                title,
                body,
                NotificationType.Action,
                now);
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(cancellationToken);

            if (realtime is null)
            {
                continue;
            }

            await realtime.NotifyUserAsync(
                notification.Id,
                recipient,
                title,
                body,
                link,
                notification.CreatedAt,
                cancellationToken);
        }
    }
}
