namespace Alas.Application.Common.Auditing;

public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken);
}