using Ebi.Alas.Api.Features.LoanApplications;
using Ebi.Alas.Api.Features.LoanApplications.Documents;
using Ebi.Alas.Api.Features.LoanApplications.Domain;
using Ebi.Alas.Api.Features.Notifications;
using Ebi.Alas.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ebi.Alas.Api.Features.Documents;

public sealed class DocumentRemarkService(
    AlasDbContext db,
    IRealtimeNotifier? realtime,
    TimeProvider timeProvider)
{
    public async Task<DocumentRemark> CreateAsync(
        LoanApplication loan,
        CreateDocumentRemarkRequest request,
        Guid authorId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Guid? parentAuthorId = null;
        if (request.ParentRemarkId is { } parentId)
        {
            parentAuthorId = await db.DocumentRemarks.AsNoTracking()
                .Where(r => r.Id == parentId && r.LoanApplicationId == loan.Id)
                .Select(r => (Guid?)r.AuthorId)
                .FirstOrDefaultAsync(cancellationToken);
            if (parentAuthorId is null)
            {
                throw new Composition.Errors.ValidationException("Parent remark was not found.");
            }
        }

        var remark = DocumentRemark.Create(
            loan.Id,
            request.ChecklistIdCode,
            request.DocId,
            request.ParentRemarkId,
            request.Body,
            authorId,
            timeProvider.GetUtcNow());
        db.DocumentRemarks.Add(remark);
        await db.SaveChangesAsync(cancellationToken);

        await NotifyParticipantsAsync(loan, remark, parentAuthorId, authorId, cancellationToken);
        return remark;
    }

    private async Task NotifyParticipantsAsync(
        LoanApplication loan,
        DocumentRemark remark,
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

        var threadAuthors = await db.DocumentRemarks.AsNoTracking()
            .Where(r => r.LoanApplicationId == loan.Id
                && r.ChecklistIdCode == remark.ChecklistIdCode
                && r.AuthorId != authorId)
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
        var documentLabel = await db.LoanProductDocumentRequirements.AsNoTracking()
            .Where(r => r.Code == remark.ChecklistIdCode)
            .Select(r => r.Description)
            .FirstOrDefaultAsync(cancellationToken)
            ?? remark.ChecklistIdCode;
        var title = "New document remark";
        var body = $"Loan {loan.LamId} · {documentLabel}: {preview}";
        var link = $"/loans/approval/{loan.Id}#files:{Uri.EscapeDataString(remark.ChecklistIdCode)}";

        foreach (var recipient in recipients)
        {
            var notification = Notification.Create(
                recipient,
                title,
                body,
                NotificationType.Action,
                now,
                link);
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
